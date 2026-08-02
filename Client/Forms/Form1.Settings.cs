using System;
using System.Linq;
using U盘文件复制.Core;

namespace U盘文件复制
{
    partial class Form1
    {
        /// <summary>
        /// 保存设置到文件（控件状态 → AppSettings → SettingsStore）
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
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

                SettingsStore.Save(settings);
            }
            catch (Exception ex)
            {
                LogMessage($"保存设置失败: {ex.Message}", true);
            }
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

                // 目录深度
                chkDirectoryTree.Checked = settings.CreateDirectoryTree;
                chkDepthLimit.Checked = settings.LimitDirectoryDepth;
                numMaxDepth.Value = settings.MaxDirectoryDepth;

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
                numSpeedMinutes.Value = settings.SpeedLimitMinutes;
                cmbSpeedLimit.SelectedIndex = settings.SpeedLimitIndex >= 0 ? settings.SpeedLimitIndex : 1;

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
                LogMessage($"加载设置失败: {ex.Message}", true);
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
        private int ParseChunkSize()
        {
            if (!int.TryParse(txtChunkSize?.Text, out int val) || val <= 0) val = 1;
            string unit = cmbChunkUnit?.SelectedItem?.ToString() ?? "MB";
            if (unit == "MB")
                return val * 1024 * 1024;
            else
                return val * 1024;
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
