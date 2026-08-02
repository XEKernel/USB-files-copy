using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using U盘文件复制.Core;

namespace U盘文件复制
{
    partial class Form1
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;

        /// <summary>
        /// 绑定托盘/通知开关控件事件
        /// </summary>
        private void SetupNotifyControls()
        {
            chkTrayIcon.CheckedChanged += (s, e) =>
            {
                if (_notifyIcon != null)
                    _notifyIcon.Visible = chkTrayIcon.Checked;
            };
        }

        /// <summary>
        /// 初始化系统托盘图标（含右键菜单：显示主窗口 / 立即复制 / 退出）
        /// </summary>
        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
            _trayMenu.Items.Add("立即复制所有U盘", null, async (s, e) => await ManualCopyAllAsync());
            _trayMenu.Items.Add("检查更新", null, async (s, e) => await CheckForUpdatesAsync());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出", null, (s, e) => ExitApplication());

            _notifyIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "U盘文件复制器",
                Visible = chkTrayIcon.Checked,
                ContextMenuStrip = _trayMenu
            };
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        /// <summary>
        /// 托盘"立即复制"：复制当前所有可移动驱动器
        /// </summary>
        private async Task ManualCopyAllAsync()
        {
            if (!await _copyLock.WaitAsync(0))
            {
                _logEngine.Write("已有复制任务进行中，忽略本次请求", true);
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();
                var destination = CreateFileDestination();
                if (destination == null)
                {
                    _logEngine.Write("错误：未设置文件存储目标", true);
                    return;
                }

                var options = BuildCopyOptions();
                _logEngine.LogFilePath = GetLogFilePath(destination);

                // 本地存储需要验证目录
                if (destination is LocalFileDestination && !ValidateTargetDirectory()) return;

                _logEngine.Write($"==== 手动复制开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====", true);
                await _copyEngine.CopyAllRemovableAsync(destination, options, _cts.Token);
                _logEngine.Write($"==== 手动复制完成 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====\n", true);
            }
            catch (OperationCanceledException)
            {
                _logEngine.Write("操作已取消", true);
            }
            catch (Exception ex)
            {
                _logEngine.Write($"手动复制出错：{ex.Message}", true);
            }
            finally
            {
                _copyLock.Release();
            }
        }

        /// <summary>
        /// 复制完成通知（BalloonTip，可开关）
        /// </summary>
        private void ShowCompletionNotification(int total, int success, int failure)
        {
            if (!chkNotify.Checked) return;
            if (_notifyIcon == null) return;

            _notifyIcon.ShowBalloonTip(
                5000,
                "复制完成",
                $"一共复制 {total} 个文件，成功 {success} 个，失败 {failure} 个",
                ToolTipIcon.Info);
        }

        private void DisposeTrayIcon()
        {
            try
            {
                _notifyIcon?.Dispose();
                _trayMenu?.Dispose();
            }
            catch { }
            _notifyIcon = null;
            _trayMenu = null;
        }

        /// <summary>
        /// 检查 GitHub Releases 是否有新版本（后台执行，不阻塞启动）
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
                var latest = await UpdateChecker.GetLatestReleaseAsync();
                if (latest == null)
                    return;

                if (UpdateChecker.IsNewerThanCurrent(latest.Value.tag, current))
                {
                    _logEngine.Write($"发现新版本 {latest.Value.tag}（当前 {current}），可从 GitHub 下载", false);
                    if (chkNotify.Checked && _notifyIcon != null)
                    {
                        string url = latest.Value.htmlUrl;
                        _notifyIcon.BalloonTipClicked += (s, e) => OpenUrl(url);
                        _notifyIcon.ShowBalloonTip(
                            8000,
                            "发现新版本",
                            $"U盘文件复制器 {latest.Value.tag} 已发布，点击打开下载页",
                            ToolTipIcon.Info);
                    }
                }
            }
            catch { /* 更新检查失败不影响使用 */ }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }
    }
}
