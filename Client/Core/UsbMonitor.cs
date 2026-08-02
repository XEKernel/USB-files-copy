using System;
using System.Management;
using System.Threading.Tasks;

namespace U盘文件复制.Core
{
    /// <summary>
    /// USB 插入监听器（纯后端，通过事件通知界面层）
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
