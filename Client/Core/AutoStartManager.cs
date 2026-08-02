using System;
using Microsoft.Win32;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 开机自启动管理（纯后端，注册表 HKCU\...\Run）
    /// </summary>
    public static class AutoStartManager
    {
        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "U盘文件复制器";

        /// <summary>
        /// 检查是否已启用开机自启动
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(AppName) != null;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 启用开机自启动
        /// </summary>
        /// <param name="hidden">是否以隐藏模式启动</param>
        public static void Enable(bool hidden)
        {
            string exePath = System.Windows.Forms.Application.ExecutablePath;
            string arguments = hidden ? "/hidden" : "";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
            {
                if (key != null)
                {
                    key.SetValue(AppName, $"\"{exePath}\" {arguments}".TrimEnd());
                }
            }
        }

        /// <summary>
        /// 禁用开机自启动
        /// </summary>
        public static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
            {
                if (key != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
    }
}
