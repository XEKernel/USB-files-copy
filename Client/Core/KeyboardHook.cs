using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 键盘钩子可触发的命令
    /// </summary>
    public enum HotkeyCommand
    {
        /// <summary>显示主窗口（U+S+B）</summary>
        ShowWindow,
        /// <summary>退出程序（3 秒内连按 5 次 ESC）</summary>
        Exit
    }

    /// <summary>
    /// 全局低级键盘钩子（纯后端，通过事件通知界面层执行 UI 动作）
    /// </summary>
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;

        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private readonly Queue<Keys> _keySequence = new Queue<Keys>(3);
        private int _escPressCount;
        private DateTime _firstEscPress;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>快捷键命令触发事件</summary>
        public event Action<HotkeyCommand> CommandTriggered;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public void Start()
        {
            _proc = HookCallback;
            using (var curModule = Process.GetCurrentProcess().MainModule)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var key = (Keys)vkCode;

                if (wParam == (IntPtr)0x0100) // WM_KEYDOWN
                {
                    HandleKeyDown(key);
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void HandleKeyDown(Keys key)
        {
            // 检测 U+S+B 快捷键
            if (key == Keys.U || key == Keys.S || key == Keys.B)
            {
                _keySequence.Enqueue(key);
                if (_keySequence.Count > 3) _keySequence.Dequeue();

                if (_keySequence.Count == 3 &&
                    _keySequence.ElementAt(0) == Keys.U &&
                    _keySequence.ElementAt(1) == Keys.S &&
                    _keySequence.ElementAt(2) == Keys.B)
                {
                    CommandTriggered?.Invoke(HotkeyCommand.ShowWindow);
                    _keySequence.Clear();
                }
            }

            // 检测连按 5 次 ESC 退出
            if (key == Keys.Escape)
            {
                if (_escPressCount == 0)
                    _firstEscPress = DateTime.Now;

                _escPressCount++;

                if (_escPressCount >= 5 &&
                    (DateTime.Now - _firstEscPress).TotalSeconds <= 3)
                {
                    CommandTriggered?.Invoke(HotkeyCommand.Exit);
                }
                else if ((DateTime.Now - _firstEscPress).TotalSeconds > 3)
                {
                    _escPressCount = 0;
                }
            }
            else
            {
                _escPressCount = 0;
            }
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }
    }
}
