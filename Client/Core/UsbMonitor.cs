using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Threading.Tasks;

namespace U盘文件复制.Core
{
    /// <summary>
    /// USB 插入监听器（纯后端，通过事件通知界面层）
    ///
    /// 注意：WMI 的 Win32_VolumeChangeEvent 只报告「监听开始之后」发生的卷变更。
    /// 因此软件启动前就已经插好的 U 盘不会产生任何事件，调用方必须在启动时用
    /// <see cref="GetPresentRemovableDriveNames"/> 补扫一次，否则会漏掉这类设备。
    /// </summary>
    public class UsbMonitor : IDisposable
    {
        private ManagementEventWatcher _watcher;

        /// <summary>U 盘插入事件（异步触发，界面层需自行捕获异常）</summary>
        public event Action UsbInserted;

        public void Start()
        {
            try
            {
                var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2");
                _watcher = new ManagementEventWatcher(query);
                _watcher.EventArrived += (sender, e) =>
                {
                    var handler = UsbInserted;
                    if (handler != null)
                    {
                        // WMI 事件线程上触发，异步执行避免阻塞
                        Task.Run(() => handler());
                    }
                };
                _watcher.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("初始化 USB 监听失败: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// 获取当前已存在的可移动驱动器盘符（如 "E:\"）。
        /// 不要求卷已就绪（开机早期卷可能仍在挂载过程中），只要设备类型是可移动即计入。
        /// 用于补扫「软件启动前就已插入」的 U 盘——这类设备不会再产生 WMI 插入事件。
        /// </summary>
        public static List<string> GetPresentRemovableDriveNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType == DriveType.Removable)
                            names.Add(drive.Name);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return names;
        }

        public void Stop()
        {
            try { _watcher?.Stop(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
        }
    }
}
