using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using U盘文件复制.Core;

namespace U盘文件复制
{
    /// <summary>
    /// U盘文件复制器主窗体（界面层）。
    /// 仅负责：控件读写、事件绑定、界面编排。
    /// 业务逻辑全部委托给 Core 命名空间下的后端类。
    /// </summary>
    public partial class Form1 : Form
    {
        // 当前使用的文件存储目标（本地或服务器）
        private IFileDestination _currentDestination;

        // 标记是否以隐藏模式启动
        private bool _startHidden;

        // ===== 后端对象（纯功能，零 UI 依赖） =====
        private readonly LogEngine _logEngine = new LogEngine();
        private readonly UsbMonitor _usbMonitor = new UsbMonitor();
        private readonly KeyboardHook _keyboardHook = new KeyboardHook();
        private readonly CopyEngine _copyEngine = new CopyEngine();
        private readonly SemaphoreSlim _copyLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts = new CancellationTokenSource();

        // 限速值（由界面选择，构建 CopyOptions 时传入后端）
        private int _currentSpeedLimit = 2 * 1024 * 1024; // 默认 2 MB/秒

        public Form1()
        {
            InitializeComponent();

            // 检测是否以隐藏模式启动
            string[] args = Environment.GetCommandLineArgs();
            _startHidden = args.Contains("/hidden", StringComparer.OrdinalIgnoreCase);

            // 订阅后端事件 + 启动监听
            InitializeBackend();
            InitializeUsbWatcher();
            InitializeKeyboardHook();

            // 设置 UI 控件
            SetupControls();
            SetupCheckboxEvents();
            SetupFileSizeLimitControls();
            SetupDuplicateFileHandling();
            SetupFileNameFilterControls();
            SetupSpeedLimitControls();
            SetupDirectoryDepthControls();
            SetupLogSettingsControls();
            SetupUsbSpecialSettingsControls();
            SetupFolderNameFilterControls();
            SetupAutoStartControls();

            this.KeyPreview = true;

            // 设置默认值 + 加载保存的设置
            SetDefaultValues();
            LoadSettings();

            // 根据用户选择的保存位置创建对应的文件存储目标（本地或服务器）
            _currentDestination = CreateFileDestination();

            // 如果是隐藏模式启动，在窗口加载后隐藏
            if (_startHidden)
            {
                this.Load += (s, e) =>
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false;
                    this.Hide();
                };
            }
        }

        /// <summary>
        /// 订阅后端事件（日志显示、USB 插入、快捷键）
        /// </summary>
        private void InitializeBackend()
        {
            _logEngine.LineAppended += entry => AppendLogToWindow(entry);
            _usbMonitor.UsbInserted += OnUsbInserted;
            _keyboardHook.CommandTriggered += command =>
            {
                if (command == HotkeyCommand.ShowWindow)
                    ShowMainWindow();
                else
                    ExitApplication();
            };
            _copyEngine.Log += (message, isError) => _logEngine.Write(message, isError);
            _copyEngine.CountsChanged += (total, success, failure) => UpdateCountDisplay(total, success, failure);
        }

        /// <summary>
        /// 启动 USB 监听器
        /// </summary>
        private void InitializeUsbWatcher()
        {
            try
            {
                _usbMonitor.Start();
            }
            catch (Exception ex)
            {
                _logEngine.Write($"初始化失败: {ex.Message}", true);
            }
        }

        /// <summary>
        /// 启动键盘钩子
        /// </summary>
        private void InitializeKeyboardHook()
        {
            try
            {
                _keyboardHook.Start();
            }
            catch (Exception ex)
            {
                _logEngine.Write($"键盘钩子初始化失败: {ex.Message}", true);
            }
        }

        /// <summary>
        /// 处理 USB 插入事件（界面层编排：构建配置 → 调用复制引擎）
        /// </summary>
        private async void OnUsbInserted()
        {
            if (!await _copyLock.WaitAsync(0)) return;

            try
            {
                await SafeInvokeAsync(() => this.Hide());
                _cts = new CancellationTokenSource();

                // 根据当前用户选择的保存位置，创建对应的文件存储目标
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

                _copyEngine.ResetCounters();

                foreach (var drive in _copyEngine.GetRemovableDrives())
                {
                    _copyEngine.RecordDriveInsertion(drive);
                    string driveId = _copyEngine.GetDriveId(drive);
                    _logEngine.Write($"检测到U盘插入: {drive.Name} (ID: {driveId})", true);

                    if (_copyEngine.ContainsSpecialFile(drive, options, out var actionType))
                    {
                        switch (actionType)
                        {
                            case SpecialFileAction.StopCopy:
                                _logEngine.Write($"检测到停止复制文件，跳过该U盘：{drive.Name}", true);
                                continue;
                            case SpecialFileAction.ReverseCopy:
                                await _copyEngine.ReverseCopyAsync(drive, options, _cts.Token);
                                continue;
                        }
                    }

                    await _copyEngine.CopyDriveAsync(drive, destination, options, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logEngine.Write("操作已取消", true);
            }
            catch (Exception ex)
            {
                // async void 事件处理器未捕获异常会导致进程崩溃，必须兜底
                _logEngine.Write($"处理 USB 事件出错：{ex.Message}", true);
            }
            finally
            {
                _copyLock.Release();
            }
        }

        /// <summary>
        /// 记录日志（转发到后端日志引擎）
        /// </summary>
        private void LogMessage(string message, bool isError = false)
        {
            _logEngine.Write(message, isError);
        }

        /// <summary>
        /// 追加日志到窗口（跨线程安全）
        /// </summary>
        private void AppendLogToWindow(string entry)
        {
            if (txtLogView.InvokeRequired)
            {
                txtLogView.BeginInvoke((Action)(() =>
                {
                    txtLogView.AppendText(entry + Environment.NewLine);
                    txtLogView.ScrollToCaret();
                }));
                return;
            }

            txtLogView.AppendText(entry + Environment.NewLine);
            txtLogView.ScrollToCaret();
        }

        /// <summary>
        /// 更新复制统计显示（跨线程安全）
        /// </summary>
        private void UpdateCountDisplay(int total, int success, int failure)
        {
            if (lblCount.InvokeRequired)
            {
                lblCount.BeginInvoke((Action)(() =>
                {
                    lblCount.Text = $"一共复制{total}文件，成功{success}个，失败{failure}个";
                }));
            }
            else
            {
                lblCount.Text = $"一共复制{total}文件，成功{success}个，失败{failure}个";
            }
        }

        private Task SafeInvokeAsync(Action action)
        {
            if (InvokeRequired)
                return Task.Factory.FromAsync(BeginInvoke(action), _ => { });
            else
            {
                action();
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 根据存储目标类型决定日志文件位置（本地=目标目录，远程=程序目录）
        /// </summary>
        private string GetLogFilePath(IFileDestination destination)
        {
            if (destination is LocalFileDestination)
                return Path.Combine(txtTargetDir.Text, "CopyLog.txt");

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CopyLog.txt");
        }

        /// <summary>
        /// 验证本地目标目录
        /// </summary>
        private bool ValidateTargetDirectory()
        {
            if (string.IsNullOrWhiteSpace(txtTargetDir.Text))
            {
                LogMessage("错误：未选择目标目录", true);
                return false;
            }

            try
            {
                if (!Path.IsPathRooted(txtTargetDir.Text))
                {
                    LogMessage("错误：目标路径必须是绝对路径", true);
                    return false;
                }

                var fullPath = Path.GetFullPath(txtTargetDir.Text);
                if (fullPath.StartsWith(@"\\?\"))
                {
                    LogMessage("警告：长路径格式可能需要系统支持", true);
                }

                Directory.CreateDirectory(fullPath);
                txtTargetDir.Text = fullPath;
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"目录验证失败：{ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 显示主窗口（快捷键 U+S+B）
        /// </summary>
        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.BringToFront();
        }

        /// <summary>
        /// 退出应用程序（连按 5 次 ESC）
        /// </summary>
        private void ExitApplication()
        {
            _keyboardHook.Dispose();
            Application.Exit();
        }
    }
}
