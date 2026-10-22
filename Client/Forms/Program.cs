using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace U盘文件复制
{
    static class Program
    {
        // 添加Win32 API声明
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        private const string WindowTitlePrefix = "U盘文件复制器";

        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            // 创建唯一互斥体（使用GUID确保唯一性）
            bool createdNew;
            _mutex = new Mutex(true, "{B7E9A614-5D4B-4A3B-9A5E-3C9D8D7F6C5A}", out createdNew);

            if (!createdNew)
            {
                // 已存在运行实例
                ActivateExistingInstance();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            finally
            {
                _mutex?.ReleaseMutex();
            }
        }

        /// <summary>
        /// 唤起已有实例。
        /// 原实现用 Process.MainWindowHandle，但窗口隐藏到托盘时该值为 IntPtr.Zero，
        /// 用户再次双击程序只会看到"已经在运行中"的弹框、窗口永远出不来，
        /// 于是只能去任务管理器结束进程 —— 这正是最后一次设置丢失的常见原因。
        /// 现改为枚举该进程的所有顶层窗口（含隐藏窗口）并按标题匹配。
        /// </summary>
        private static void ActivateExistingInstance()
        {
            Process current = Process.GetCurrentProcess();
            IntPtr handle = IntPtr.Zero;

            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id == current.Id) continue;
                handle = FindMainWindow(process.Id);
                if (handle != IntPtr.Zero) break;
            }

            if (handle != IntPtr.Zero)
            {
                ShowWindowAsync(handle, SW_SHOW);      // 隐藏到托盘时先显示
                ShowWindowAsync(handle, SW_RESTORE);   // 最小化时还原
                SetForegroundWindow(handle);
                return;
            }

            MessageBox.Show("程序已经在运行中，但未能定位到它的窗口。\n请双击系统托盘中的图标打开。",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Environment.Exit(0);
        }

        private static IntPtr FindMainWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != (uint)processId)
                    return true;

                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString().StartsWith(WindowTitlePrefix, StringComparison.Ordinal))
                {
                    found = hwnd;
                    return false;   // 找到即停止枚举
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }
    }
}
