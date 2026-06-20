using System;
using System.Linq;
using System.Windows.Forms;

namespace U盘文件复制
{
    /// <summary>
    /// U盘文件复制器主窗体
    /// 代码已按功能模块拆分到以下文件：
    /// - Form1.Constants.cs   : 常量和静态字段定义
    /// - Form1.AutoStart.cs   : 开机自启动功能
    /// - Form1.KeyboardHook.cs: 键盘钩子和快捷键
    /// - Form1.UsbWatcher.cs  : USB监听和事件处理
    /// - Form1.FileCopy.cs    : 文件复制核心逻辑
    /// - Form1.FileFilter.cs  : 文件过滤功能
    /// - Form1.Log.cs         : 日志记录
    /// - Form1.UIControls.cs  : UI控件设置和事件
    /// - Form1.Extensions.cs  : 文件扩展名处理
    /// - Form1.Settings.cs    : 设置保存和加载
    /// </summary>
    public partial class Form1 : Form
    {
        // 当前使用的文件存储目标（本地或服务器）
        private IFileDestination _currentDestination;

        public Form1()
        {
            InitializeComponent();

            // 检测是否以隐藏模式启动
            string[] args = Environment.GetCommandLineArgs();
            _startHidden = args.Contains("/hidden", StringComparer.OrdinalIgnoreCase);

            // 初始化各模块
            InitializeUsbWatcher();
            InitializeKeyboardHook();

            // 设置UI控件
            SetupControls();
            SetupCheckboxEvents();
            SetupFileSizeLimitControls();
            SetupDuplicateFileHandling();
            SetupFileNameFilterControls();
            SetupSpeedLimitControls();

            // 新增的事件设置
            SetupDirectoryDepthControls();
            SetupLogSettingsControls();
            SetupUsbSpecialSettingsControls();
            SetupFolderNameFilterControls();
            SetupAutoStartControls();

            this.KeyPreview = true;

            // 设置默认值
            SetDefaultValues();

            // 加载保存的设置
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

        // 标记是否以隐藏模式启动
        private bool _startHidden;
    }
}