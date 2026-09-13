using System;
using System.Linq;
using System.Windows.Forms;
using U盘文件复制.Core;

namespace U盘文件复制
{
    partial class Form1
    {
        // ===== 设置持久化状态 =====
        // 构造期间（SetupXxx → SetDefaultValues）控件事件会被触发，若此时允许写盘，
        // 就会在 LoadSettings 读取之前用「默认值」覆盖 settings.xml，
        // 导致用户设置每次启动都被重置（用户反馈的「设置无法保存」根因）。
        // 因此 _settingsLoaded 为 false 时所有保存请求一律忽略。
        private bool _settingsLoaded;
        private readonly Timer _settingsSaveTimer = new Timer { Interval = 600 };
        private bool _settingsSaveErrorShown;
        private bool _settingsLoadErrorShown;

        /// <summary>
        /// 立即保存设置（初始化阶段无效）。仅在退出、以及需要立刻落盘的场景调用。
        /// </summary>
        private void SaveSettings()
        {
            if (!_settingsLoaded) return;
            _settingsSaveTimer.Stop();
            SaveSettingsCore();
        }

        /// <summary>
        /// 请求延迟保存：合并连续输入（如逐字符填令牌），避免每次按键都写盘并重建连接对象。
        /// </summary>
        private void RequestDelayedSave()
        {
            if (!_settingsLoaded) return;
            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        }

        private void OnSettingsSaveTimerTick(object sender, EventArgs e)
        {
            _settingsSaveTimer.Stop();
            if (!_settingsLoaded) return;
            SaveSettingsCore();
            // 服务器/存储相关配置变化后重建存储目标
            _currentDestination = CreateFileDestination();
        }

        /// <summary>
        /// 实际写盘（含失败提示：用户可见，避免「设置悄悄没保存」）
        /// </summary>
        private void SaveSettingsCore()
        {
            try
            {
                SettingsStore.Save(BuildAppSettings());
                _settingsSaveErrorShown = false;
            }
            catch (Exception ex)
            {
                LogMessage($"保存设置失败: {ex.Message}（配置文件：{SettingsStore.FilePath}）", true);

                if (_settingsSaveErrorShown) return;
                _settingsSaveErrorShown = true;
                MessageBox.Show(
                    $"设置保存失败，关闭程序后这些设置会丢失。\n\n" +
                    $"配置文件：{SettingsStore.FilePath}\n" +
                    $"失败原因：{ex.Message}\n\n" +
                    "若程序安装在 Program Files 等受保护目录，请把程序移动到有写入权限的目录" +
                    "（如 D:\\U盘文件复制器），或以管理员身份运行。",
                    "无法保存设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 控件状态 → AppSettings
        /// </summary>
        private AppSettings BuildAppSettings()
        {
            return new AppSettings
            {
                TargetDirectory = txtTargetDir.Text,

                // 文件类型
                CopyPpt = chkPpt.Checked,
                CopyWord = chkWord.Checked,
                CopyExcel = chkExcel.Checked,
                CopyPdf = chkPdf.Checked,
                CopyImage = chkImage.Checked,
                CopyVideo = chkVideo.Checked,
                CopyAudio = chkAudio.Checked,
                CopyCompressed = chkCompressed.Checked,
                CopyAllFiles = chkAllFiles.Checked,
                UseCustomExtensions = chkCustomExt.Checked,
                CustomExtensions = txtCustomExtensions.Text,

                // 重复文件处理
                DuplicateFileAction = rdoSkip.Checked ? 0 :
                                      rdoOverwrite.Checked ? 1 :
                                      rdoKeepBoth.Checked ? 2 : 3,

                // 文件大小限制
                EnableFileSizeLimit = chkSizeLimit.Checked,
                MaxFileSizeMB = long.TryParse(txtMaxSizeMB.Text, out var size) ? size : 100,

                // 文件名过滤
                EnableFileNameFilter = chkFileNameFilter.Checked,
                FileNameKeywords = txtFileNameKeywords.Text,

                // 文件夹过滤
                EnableFolderNameFilter = chkFolderFilter.Checked,
                FolderNameKeywords = txtFolderKeywords.Text,

                // 目录深度
                CreateDirectoryTree = chkDirectoryTree.Checked,
                LimitDirectoryDepth = chkDepthLimit.Checked,
                MaxDirectoryDepth = (int)numMaxDepth.Value,

                // 日志设置
                LogSuccess = chkLogSuccess.Checked,
                LogErrors = chkLogErrors.Checked,
                LogNeutral = chkLogNeutral.Checked,
                SaveLogToFile = chkLogToFile.Checked,
                ShowLogInWindow = chkLogWindow.Checked,

                // USB特殊文件
                EnableStopCopyFile = chkStopCopy.Checked,
                StopCopyFileName = txtStopCopyFile.Text,
                EnableReverseCopyFile = chkReverseCopy.Checked,
                ReverseCopyFileName = txtReverseCopyFile.Text,

                // 速度限制
                EnableSpeedLimit = chkSpeedLimit.Checked,
                SpeedLimitMinutes = (int)numSpeedMinutes.Value,
                SpeedLimitIndex = cmbSpeedLimit.SelectedIndex,

                // 开机自启动
                AutoStart = chkAutoStart.Checked,
                AutoStartHidden = chkAutoStartHidden.Checked,

                // 服务器相关
                SaveLocation = rdoLocalSave.Checked ? 0 : 1,
                UseChunkedUpload = chkChunkedUpload?.Checked ?? true,
                ShowTrayIcon = chkTrayIcon.Checked,
                ShowCompletionNotify = chkNotify.Checked,
                EnableWhitelist = chkWhitelist.Checked,
                WhitelistDriveIds = txtWhitelist.Text,

                Server = new ServerConfig
                {
                    ServerAddress = txtServerAddress.Text,
                    Port = int.TryParse(txtServerPort?.Text, out int port) ? port : 443,
                    UseHttps = chkUseHttps?.Checked ?? true,
                    Password = SettingsStore.Encrypt(txtServerPassword.Text),     // 加密存储
                    ApiToken = SettingsStore.Encrypt(txtServerToken.Text),    // 加密存储
                    RemoteRootPath = "/",
                    ValidateCertificate = true,
                    TimeoutSeconds = 30,
                    ChunkSizeBytes = ParseChunkSize(),
                    MaxRetries = 3
                }
            };
        }

        /// <summary>
        /// 加载设置（SettingsStore → 控件状态）
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                var settings = SettingsStore.Load();
                if (settings == null)
                {
                    // 首次运行，使用控件默认值
                    return;
                }

                // 目标目录
                txtTargetDir.Text = settings.TargetDirectory ?? "";

                // 文件类型
                chkPpt.Checked = settings.CopyPpt;
                chkWord.Checked = settings.CopyWord;
                chkExcel.Checked = settings.CopyExcel;
                chkPdf.Checked = settings.CopyPdf;
                chkImage.Checked = settings.CopyImage;
                chkVideo.Checked = settings.CopyVideo;
                chkAudio.Checked = settings.CopyAudio;
                chkCompressed.Checked = settings.CopyCompressed;
                chkAllFiles.Checked = settings.CopyAllFiles;
                chkCustomExt.Checked = settings.UseCustomExtensions;
                txtCustomExtensions.Text = settings.CustomExtensions ?? "";

                // 重复文件处理
                switch (settings.DuplicateFileAction)
                {
                    case 0: rdoSkip.Checked = true; break;
                    case 1: rdoOverwrite.Checked = true; break;
                    case 2: rdoKeepBoth.Checked = true; break;
                    case 3: rdoReplaceNewer.Checked = true; break;
                }

                // 文件大小限制
                chkSizeLimit.Checked = settings.EnableFileSizeLimit;
                txtMaxSizeMB.Text = settings.MaxFileSizeMB.ToString();

                // 文件名过滤
                chkFileNameFilter.Checked = settings.EnableFileNameFilter;
                txtFileNameKeywords.Text = settings.FileNameKeywords ?? "";

                // 文件夹过滤
                chkFolderFilter.Checked = settings.EnableFolderNameFilter;
                txtFolderKeywords.Text = settings.FolderNameKeywords ?? "";

                // 目录深度（越界值会抛异常导致整个配置读取失败，故做钳制）
                chkDirectoryTree.Checked = settings.CreateDirectoryTree;
                chkDepthLimit.Checked = settings.LimitDirectoryDepth;
                numMaxDepth.Value = ClampToRange(settings.MaxDirectoryDepth, numMaxDepth);

                // 日志设置
                chkLogSuccess.Checked = settings.LogSuccess;
                chkLogErrors.Checked = settings.LogErrors;
                chkLogNeutral.Checked = settings.LogNeutral;
                chkLogToFile.Checked = settings.SaveLogToFile;
                chkLogWindow.Checked = settings.ShowLogInWindow;

                // USB特殊文件
                chkStopCopy.Checked = settings.EnableStopCopyFile;
                txtStopCopyFile.Text = settings.StopCopyFileName;
                chkReverseCopy.Checked = settings.EnableReverseCopyFile;
                txtReverseCopyFile.Text = settings.ReverseCopyFileName;

                // 速度限制
                chkSpeedLimit.Checked = settings.EnableSpeedLimit;
                numSpeedMinutes.Value = ClampToRange(settings.SpeedLimitMinutes, numSpeedMinutes);
                cmbSpeedLimit.SelectedIndex = settings.SpeedLimitIndex >= 0 && settings.SpeedLimitIndex < cmbSpeedLimit.Items.Count
                    ? settings.SpeedLimitIndex
                    : 1;

                // 开机自启动
                chkAutoStart.Checked = settings.AutoStart;
                chkAutoStartHidden.Checked = settings.AutoStartHidden;

                // 服务器相关
                if (settings.Server != null)
                {
                    txtServerAddress.Text = settings.Server.ServerAddress ?? "";
                    if (txtServerPort != null) txtServerPort.Text = settings.Server.Port.ToString();
                    if (chkUseHttps != null) chkUseHttps.Checked = settings.Server.UseHttps;
                    txtServerPassword.Text = SettingsStore.Decrypt(settings.Server.Password);
                    txtServerToken.Text = SettingsStore.Decrypt(settings.Server.ApiToken);
                    RestoreChunkSizeUI(settings.Server.ChunkSizeBytes);
                }

                // 保存位置
                if (settings.SaveLocation == 0)
                    rdoLocalSave.Checked = true;
                else
                    rdoServerSave.Checked = true;

                // 分块上传开关
                if (chkChunkedUpload != null)
                    chkChunkedUpload.Checked = settings.UseChunkedUpload;

                // 托盘与通知开关
                chkTrayIcon.Checked = settings.ShowTrayIcon;
                chkNotify.Checked = settings.ShowCompletionNotify;

                // 设备白名单
                chkWhitelist.Checked = settings.EnableWhitelist;
                txtWhitelist.Text = settings.WhitelistDriveIds ?? "";
            }
            catch (Exception ex)
            {
                LogMessage($"加载设置失败: {ex.Message}（配置文件：{SettingsStore.FilePath}）", true);

                if (!_settingsLoadErrorShown)
                {
                    _settingsLoadErrorShown = true;
                    MessageBox.Show(
                        $"读取设置失败，本次以默认值启动。\n\n" +
                        $"配置文件：{SettingsStore.FilePath}\n" +
                        $"失败原因：{ex.Message}",
                        "读取设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                // 无论成功与否都放开写盘：否则用户在本会话的修改无法保存
                _settingsLoaded = true;
            }
        }

        /// <summary>
        /// 从界面控件构建复制任务配置（传递给后端 CopyEngine）
        /// </summary>
        private CopyOptions BuildCopyOptions()
        {
            var extensions = GetSelectedExtensions().ToList();

            return new CopyOptions
            {
                TargetDirectory = txtTargetDir.Text,
                SearchPatterns = extensions.Contains(FileCategories.AllFilesPattern)
                    ? new[] { FileCategories.AllFilesPattern }
                    : extensions.Distinct().ToArray(),
                CreateDirectoryTree = chkDirectoryTree.Checked,
                LimitDirectoryDepth = chkDepthLimit.Checked,
                MaxDirectoryDepth = chkDepthLimit.Checked ? (int)numMaxDepth.Value : int.MaxValue,
                FilterByFolderName = chkFolderFilter.Checked,
                FolderNameKeywords = ParseKeywords(txtFolderKeywords.Text),
                FilterByFileName = chkFileNameFilter.Checked,
                FileNameKeywords = ParseKeywords(txtFileNameKeywords.Text),
                EnableSizeLimit = chkSizeLimit.Checked,
                MaxSizeBytes = long.TryParse(txtMaxSizeMB.Text, out var sizeMB) ? sizeMB * 1024 * 1024 : 0,
                EnableSpeedLimit = chkSpeedLimit.Checked,
                SpeedLimitMinutes = (int)numSpeedMinutes.Value,
                SpeedLimitBytesPerSecond = _currentSpeedLimit,
                StopCopyWhenFileExists = chkStopCopy.Checked,
                StopCopyFileName = txtStopCopyFile.Text.Trim(),
                ReverseCopyWhenFileExists = chkReverseCopy.Checked,
                ReverseCopyFileName = txtReverseCopyFile.Text.Trim(),
                DuplicateAction = GetDuplicateFileActionFromUi(),
                EnableWhitelist = chkWhitelist.Checked,
                WhitelistedDriveIds = ParseKeywords(txtWhitelist.Text),
            };
        }

        private DuplicateFileAction GetDuplicateFileActionFromUi()
        {
            if (rdoSkip.Checked) return DuplicateFileAction.Skip;
            if (rdoOverwrite.Checked) return DuplicateFileAction.Overwrite;
            if (rdoKeepBoth.Checked) return DuplicateFileAction.KeepBoth;
            if (rdoReplaceNewer.Checked) return DuplicateFileAction.ReplaceWithNewer;
            return DuplicateFileAction.Skip;
        }

        // ===== 分块大小序列化辅助 =====
        /// <summary>把设置值钳制到数字控件的合法区间（越界赋值会抛异常）</summary>
        private static decimal ClampToRange(decimal value, NumericUpDown control)
        {
            if (value < control.Minimum) return control.Minimum;
            if (value > control.Maximum) return control.Maximum;
            return value;
        }

        /// <summary>
        /// 解析分块大小（字节）。输入范围钳制，避免大数值乘积溢出为负数导致分块上传异常。
        /// </summary>
        private int ParseChunkSize()
        {
            if (!int.TryParse(txtChunkSize?.Text, out int val) || val <= 0) val = 1;

            string unit = cmbChunkUnit?.SelectedItem?.ToString() ?? "MB";
            long bytes = unit == "MB" ? (long)val * 1024 * 1024 : (long)val * 1024;

            const long minChunk = 64 * 1024;                 // 64KB
            const long maxChunk = 512L * 1024 * 1024;        // 512MB
            if (bytes < minChunk) bytes = minChunk;
            if (bytes > maxChunk) bytes = maxChunk;

            return (int)bytes;
        }

        private void RestoreChunkSizeUI(int chunkSizeBytes)
        {
            if (txtChunkSize == null || cmbChunkUnit == null) return;
            if (chunkSizeBytes >= 1024 * 1024)
            {
                txtChunkSize.Text = (chunkSizeBytes / (1024 * 1024)).ToString();
                cmbChunkUnit.SelectedItem = "MB";
            }
            else
            {
                txtChunkSize.Text = (chunkSizeBytes / 1024).ToString();
                cmbChunkUnit.SelectedItem = "KB";
            }
        }
    }
}
