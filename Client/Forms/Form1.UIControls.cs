using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using U盘文件复制.Core;

namespace U盘文件复制
{
    partial class Form1
    {
        /// <summary>
        /// 设置 UI 控件
        /// </summary>
        private void SetupControls()
        {
            chkAllFiles.CheckedChanged += (_, __) =>
            {
                txtCustomExtensions.Enabled = !chkAllFiles.Checked;
                ToggleAllCheckboxes(chkAllFiles.Checked);
            };
            txtCustomExtensions.Enabled = !chkAllFiles.Checked;

            // 服务器配置界面事件
            SetupServerConfigControls();
        }

        /// <summary>
        /// 设置服务器配置相关控件
        /// </summary>
        private void SetupServerConfigControls()
        {
            // 绑定"浏览远程目录"按钮事件
            _btnBrowseRemote.Click += async (s, e) => await BrowseRemoteDirectory();

            // 文件保存位置切换：启用/禁用服务器配置组
            rdoLocalSave.CheckedChanged += (s, e) =>
            {
                grpServerConfig.Enabled = false;
                btnTestConn.Enabled = false;
                _btnBrowseRemote.Enabled = false;
                SaveSettings();
                // 切换后重新创建目标
                _currentDestination = CreateFileDestination();
            };
            rdoServerSave.CheckedChanged += (s, e) =>
            {
                grpServerConfig.Enabled = true;
                btnTestConn.Enabled = true;
                _btnBrowseRemote.Enabled = true;
                SaveSettings();
                _currentDestination = CreateFileDestination();
            };
            // 初始状态
            grpServerConfig.Enabled = rdoServerSave.Checked;
            btnTestConn.Enabled = rdoServerSave.Checked;
            _btnBrowseRemote.Enabled = rdoServerSave.Checked;

            // 测试连接按钮
            btnTestConn.Click += async (s, e) => await TestServerConnection();

            // 任何配置项改变时自动保存并重建目标
            txtServerAddress.TextChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            txtServerPassword.TextChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            txtServerToken.TextChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            txtServerPort.TextChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            chkUseHttps.CheckedChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            chkChunkedUpload.CheckedChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            txtChunkSize.TextChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
            cmbChunkUnit.SelectedIndexChanged += (s, e) => { SaveSettings(); _currentDestination = CreateFileDestination(); };
        }

        /// <summary>
        /// 测试服务器连接
        /// </summary>
        private async Task TestServerConnection()
        {
            if (!rdoServerSave.Checked)
            {
                lblConnStatus.Text = "未启用服务器模式";
                return;
            }

            // 构建配置
            var config = BuildServerConfigFromUi();
            if (config == null)
            {
                lblConnStatus.Text = "配置无效";
                return;
            }

            btnTestConn.Enabled = false;
            lblConnStatus.Text = "测试中...";
            try
            {
                bool success = await NetworkHelper.TestConnectionAsync(config);
                lblConnStatus.Text = success ? "连接成功" : "连接失败，请检查配置";
            }
            catch (Exception ex)
            {
                lblConnStatus.Text = $"错误: {ex.Message}";
            }
            finally
            {
                btnTestConn.Enabled = true;
            }
        }

        /// <summary>
        /// 从 UI 控件构建 ServerConfig 对象
        /// </summary>
        private ServerConfig BuildServerConfigFromUi()
        {
            if (string.IsNullOrWhiteSpace(txtServerAddress.Text))
            {
                LogMessage("服务器地址不能为空", true);
                return null;
            }
            if (!int.TryParse(txtServerPort.Text, out int port) || port < 1 || port > 65535)
                port = 443;

            return new ServerConfig
            {
                ServerAddress = txtServerAddress.Text.Trim(),
                Port = port,
                UseHttps = chkUseHttps.Checked,
                Password = txtServerPassword.Text,
                ApiToken = txtServerToken.Text,
                RemoteRootPath = "/",
                ValidateCertificate = true,
                TimeoutSeconds = 30,
                ChunkSizeBytes = ParseChunkSize(),
                MaxRetries = 3
            };
        }

        /// <summary>
        /// 根据当前 UI 选择创建对应的文件存储目标
        /// </summary>
        private IFileDestination CreateFileDestination()
        {
            if (rdoServerSave.Checked) // 服务器
            {
                var config = BuildServerConfigFromUi();
                if (config == null)
                {
                    LogMessage("服务器配置无效，回退到本地存储。", true);
                    return new LocalFileDestination(GetDefaultLocalPath());
                }
                bool useChunked = chkChunkedUpload.Checked;
                return new HttpFileDestination(config, useChunked);
            }
            else
            {
                // 本地模式：如果目标目录为空，使用默认路径
                string localPath = string.IsNullOrWhiteSpace(txtTargetDir.Text)
                    ? GetDefaultLocalPath()
                    : txtTargetDir.Text;
                return new LocalFileDestination(localPath);
            }
        }

        private string GetDefaultLocalPath()
        {
            // 默认保存到桌面下的 "U盘文件备份" 文件夹
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return Path.Combine(desktop, "U盘文件备份");
        }

        /// <summary>
        /// 设置复选框事件（扩展名全选联动）
        /// </summary>
        private void SetupCheckboxEvents()
        {
            var checkBoxes = new[] { chkPpt, chkWord, chkExcel, chkPdf, chkImage, chkVideo, chkCustomExt, chkCompressed, chkAudio };
            foreach (var cb in checkBoxes)
            {
                cb.CheckedChanged += SyncCheckBoxStates;
            }
        }

        private void SyncCheckBoxStates(object sender, EventArgs e)
        {
            var checkBoxes = new[] { chkPpt, chkWord, chkExcel, chkPdf, chkImage, chkVideo, chkCustomExt, chkCompressed, chkAudio };
            chkAllFiles.Checked = System.Linq.Enumerable.All(checkBoxes, cb => cb.Checked);
        }

        private void ToggleAllCheckboxes(bool state)
        {
            var checkBoxes = new[] { chkPpt, chkWord, chkExcel, chkPdf, chkImage, chkVideo, chkCustomExt, chkCompressed, chkAudio };

            foreach (var cb in checkBoxes)
            {
                cb.CheckedChanged -= SyncCheckBoxStates;
            }

            foreach (var cb in checkBoxes)
            {
                cb.Checked = state;
            }

            foreach (var cb in checkBoxes)
            {
                cb.CheckedChanged += SyncCheckBoxStates;
            }

            txtCustomExtensions.Enabled = !state;
        }

        private void SetupDuplicateFileHandling()
        {
            rdoSkip.Checked = true;
        }

        /// <summary>
        /// 限速控件
        /// </summary>
        private void SetupSpeedLimitControls()
        {
            chkSpeedLimit.CheckedChanged += (s, e) =>
            {
                numSpeedMinutes.Enabled = chkSpeedLimit.Checked;
                cmbSpeedLimit.Enabled = chkSpeedLimit.Checked;
                if (!chkSpeedLimit.Checked)
                {
                    numSpeedMinutes.Value = 5;
                    cmbSpeedLimit.SelectedIndex = 1;
                }
            };

            cmbSpeedLimit.SelectedIndex = 1;
            cmbSpeedLimit.Enabled = chkSpeedLimit.Checked;
            numSpeedMinutes.Enabled = chkSpeedLimit.Checked;

            cmbSpeedLimit.SelectedIndexChanged += (s, e) => UpdateSpeedLimit();
        }

        private void UpdateSpeedLimit()
        {
            if (cmbSpeedLimit.SelectedItem == null) return;

            string selectedSpeed = cmbSpeedLimit.SelectedItem.ToString();

            switch (selectedSpeed)
            {
                case "1 MB/秒":
                    _currentSpeedLimit = 1 * 1024 * 1024;
                    break;
                case "2 MB/秒":
                    _currentSpeedLimit = 2 * 1024 * 1024;
                    break;
                case "5 MB/秒":
                    _currentSpeedLimit = 5 * 1024 * 1024;
                    break;
                case "10 MB/秒":
                    _currentSpeedLimit = 10 * 1024 * 1024;
                    break;
                default:
                    _currentSpeedLimit = 2 * 1024 * 1024;
                    break;
            }
        }

        /// <summary>
        /// 文件大小限制控件
        /// </summary>
        private void SetupFileSizeLimitControls()
        {
            chkSizeLimit.CheckedChanged += (s, e) =>
            {
                txtMaxSizeMB.Enabled = chkSizeLimit.Checked;
                if (!chkSizeLimit.Checked) txtMaxSizeMB.Text = string.Empty;
            };
            txtMaxSizeMB.Enabled = chkSizeLimit.Checked;
        }

        /// <summary>
        /// 文件名过滤控件
        /// </summary>
        private void SetupFileNameFilterControls()
        {
            chkFileNameFilter.CheckedChanged += (s, e) =>
            {
                txtFileNameKeywords.Enabled = chkFileNameFilter.Checked;
                if (!chkFileNameFilter.Checked) txtFileNameKeywords.Text = string.Empty;
            };
            txtFileNameKeywords.Enabled = chkFileNameFilter.Checked;
        }

        /// <summary>
        /// 文件夹名过滤控件
        /// </summary>
        private void SetupFolderNameFilterControls()
        {
            chkFolderFilter.CheckedChanged += (s, e) =>
            {
                txtFolderKeywords.Enabled = chkFolderFilter.Checked;
                if (!chkFolderFilter.Checked) txtFolderKeywords.Text = string.Empty;
            };
            txtFolderKeywords.Enabled = chkFolderFilter.Checked;
        }

        /// <summary>
        /// 目录深度控件
        /// </summary>
        private void SetupDirectoryDepthControls()
        {
            chkDepthLimit.CheckedChanged += (s, e) =>
            {
                numMaxDepth.Enabled = chkDepthLimit.Checked;
            };
            numMaxDepth.Enabled = chkDepthLimit.Checked;
        }

        /// <summary>
        /// U盘特殊文件控件
        /// </summary>
        private void SetupUsbSpecialSettingsControls()
        {
            chkStopCopy.CheckedChanged += (s, e) =>
            {
                txtStopCopyFile.Enabled = chkStopCopy.Checked;
            };
            chkReverseCopy.CheckedChanged += (s, e) =>
            {
                txtReverseCopyFile.Enabled = chkReverseCopy.Checked;
            };
            txtStopCopyFile.Enabled = chkStopCopy.Checked;
            txtReverseCopyFile.Enabled = chkReverseCopy.Checked;
        }

        /// <summary>
        /// U盘白名单控件
        /// </summary>
        private void SetupWhitelistControls()
        {
            chkWhitelist.CheckedChanged += (s, e) => txtWhitelist.Enabled = chkWhitelist.Checked;
            txtWhitelist.Enabled = chkWhitelist.Checked;
        }

        /// <summary>
        /// 日志设置控件（同步到后端日志引擎）
        /// </summary>
        private void SetupLogSettingsControls()
        {
            chkLogSuccess.CheckedChanged += (s, e) => _logEngine.LogSuccess = chkLogSuccess.Checked;
            chkLogErrors.CheckedChanged += (s, e) => _logEngine.LogErrors = chkLogErrors.Checked;
            chkLogNeutral.CheckedChanged += (s, e) => _logEngine.LogNeutral = chkLogNeutral.Checked;
            chkLogToFile.CheckedChanged += (s, e) => _logEngine.SaveToFile = chkLogToFile.Checked;
            chkLogWindow.CheckedChanged += (s, e) => _logEngine.ShowInWindow = chkLogWindow.Checked;
        }

        /// <summary>
        /// 开机自启动控件（委托 AutoStartManager）
        /// </summary>
        private void SetupAutoStartControls()
        {
            // 检查注册表中是否已设置开机自启动，同步 checkBox 状态
            chkAutoStart.Checked = AutoStartManager.IsEnabled();

            // 绑定事件
            chkAutoStart.CheckedChanged += (s, e) =>
            {
                try
                {
                    if (chkAutoStart.Checked)
                    {
                        AutoStartManager.Enable(chkAutoStartHidden.Checked);
                        LogMessage("开机自启动已启用", true);
                    }
                    else
                    {
                        AutoStartManager.Disable();
                        LogMessage("开机自启动已禁用", true);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"设置开机自启动失败: {ex.Message}", true);
                    chkAutoStart.Checked = false;
                }
            };

            // 隐藏模式状态改变时，如果已启用自启动则更新注册表
            chkAutoStartHidden.CheckedChanged += (s, e) =>
            {
                if (chkAutoStart.Checked)
                {
                    try
                    {
                        AutoStartManager.Enable(chkAutoStartHidden.Checked);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"更新开机自启动失败: {ex.Message}", true);
                    }
                }
            };
        }

        private void SetDefaultValues()
        {
            // 设置默认本地保存目录（桌面/U盘文件备份）
            if (string.IsNullOrWhiteSpace(txtTargetDir.Text))
            {
                string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "U盘文件备份");
                txtTargetDir.Text = defaultPath;
            }

            // 目录深度设置
            chkDirectoryTree.Checked = true;
            chkDepthLimit.Checked = false;
            numMaxDepth.Value = 3;
            numMaxDepth.Enabled = false;

            // 日志设置
            chkLogSuccess.Checked = true;
            chkLogErrors.Checked = true;
            chkLogNeutral.Checked = true;
            chkLogToFile.Checked = true;
            chkLogWindow.Checked = true;

            // U盘特殊设置
            chkStopCopy.Checked = false;
            chkReverseCopy.Checked = false;
            txtStopCopyFile.Text = "stop.copy";
            txtReverseCopyFile.Text = "reverse.copy";
            txtStopCopyFile.Enabled = false;
            txtReverseCopyFile.Enabled = false;

            // 文件夹名称过滤
            chkFolderFilter.Checked = false;
            txtFolderKeywords.Enabled = false;

            // 托盘与通知（默认开启）
            chkTrayIcon.Checked = true;
            chkNotify.Checked = true;

            // 设备白名单（默认关闭）
            chkWhitelist.Checked = false;
            txtWhitelist.Enabled = false;

            // 服务器配置默认值
            if (txtServerPort != null) txtServerPort.Text = "443";
            if (chkUseHttps != null) chkUseHttps.Checked = true;
            if (chkChunkedUpload != null) chkChunkedUpload.Checked = false;   // 默认不分块
            if (txtChunkSize != null) txtChunkSize.Text = "1";
            if (cmbChunkUnit != null) cmbChunkUnit.SelectedItem = "MB";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            _cts?.Cancel();
            _ = CleanupResourcesAsync();
            base.OnFormClosing(e);
        }

        private async Task CleanupResourcesAsync()
        {
            try
            {
                await Task.Delay(500);
                _usbMonitor.Dispose();
                _keyboardHook.Dispose();
                DisposeTrayIcon();
                _cts?.Cancel();
                _cts?.Dispose();
                _copyLock?.Dispose();
            }
            catch { }
        }

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtTargetDir.Text = fbd.SelectedPath;
                    // 本地目录变化时，如果是本地模式需要重建目标
                    if (rdoLocalSave.Checked)
                        _currentDestination = CreateFileDestination();
                }
            }
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/XEKernel/USB-files-copy",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/XEKernel/USB-files-copy/blob/main/README.md",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        /// <summary>
        /// 浏览远程服务器目录
        /// </summary>
        private async Task BrowseRemoteDirectory()
        {
            if (!rdoServerSave.Checked)
            {
                MessageBox.Show("请先切换到服务器模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = BuildServerConfigFromUi();
            if (config == null) return;

            // 先测试连接
            lblConnStatus.Text = "测试连接中...";
            bool connected = false;
            try
            {
                connected = await NetworkHelper.TestConnectionAsync(config);
            }
            catch { }

            if (!connected)
            {
                lblConnStatus.Text = "连接失败";
                MessageBox.Show("服务器连接失败，请检查配置。", "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            lblConnStatus.Text = "已连接";
            try
            {
                var destination = new HttpFileDestination(config, chkChunkedUpload.Checked);
                using (var browser = new RemoteBrowserForm(destination))
                {
                    browser.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"浏览远程目录出错: {ex.Message}", true);
                lblConnStatus.Text = $"错误: {ex.Message}";
            }
        }
    }
}
