using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 应用程序设置模型
    /// </summary>
    public class AppSettings
    {
        public string TargetDirectory { get; set; }

        public bool CopyPpt { get; set; } = true;
        public bool CopyWord { get; set; } = true;
        public bool CopyExcel { get; set; } = true;
        public bool CopyPdf { get; set; } = true;
        public bool CopyImage { get; set; } = true;
        public bool CopyVideo { get; set; } = true;
        public bool CopyAudio { get; set; } = true;
        public bool CopyCompressed { get; set; } = true;
        public bool CopyAllFiles { get; set; }
        public bool UseCustomExtensions { get; set; }
        public string CustomExtensions { get; set; }

        /// <summary>重复文件处理：0=跳过 1=覆盖 2=保留两者 3=保留较新</summary>
        public int DuplicateFileAction { get; set; }

        public bool EnableFileSizeLimit { get; set; }
        public long MaxFileSizeMB { get; set; } = 100;

        public bool EnableFileNameFilter { get; set; }
        public string FileNameKeywords { get; set; }

        public bool EnableFolderNameFilter { get; set; }
        public string FolderNameKeywords { get; set; }

        public bool CreateDirectoryTree { get; set; } = true;
        public bool LimitDirectoryDepth { get; set; }
        public int MaxDirectoryDepth { get; set; } = 3;

        public bool LogSuccess { get; set; } = true;
        public bool LogErrors { get; set; } = true;
        public bool LogNeutral { get; set; } = true;
        public bool SaveLogToFile { get; set; } = true;
        public bool ShowLogInWindow { get; set; } = true;

        public bool EnableStopCopyFile { get; set; }
        public string StopCopyFileName { get; set; } = "stop.copy";
        public bool EnableReverseCopyFile { get; set; }
        public string ReverseCopyFileName { get; set; } = "reverse.copy";

        public bool EnableSpeedLimit { get; set; }
        public int SpeedLimitMinutes { get; set; } = 5;
        public int SpeedLimitIndex { get; set; } = 1;

        public bool AutoStart { get; set; }
        public bool AutoStartHidden { get; set; }

        /// <summary>服务器配置（密码/令牌加密存储）</summary>
        public U盘文件复制.ServerConfig Server { get; set; } = new U盘文件复制.ServerConfig();

        /// <summary>保存位置：0=本地，1=服务器</summary>
        public int SaveLocation { get; set; }

        /// <summary>是否启用分块上传</summary>
        public bool UseChunkedUpload { get; set; } = true;

        /// <summary>是否显示系统托盘图标</summary>
        public bool ShowTrayIcon { get; set; } = true;

        /// <summary>复制完成时是否弹出托盘通知</summary>
        public bool ShowCompletionNotify { get; set; } = true;

        /// <summary>是否启用设备白名单（仅复制指定卷序列号的 U 盘）</summary>
        public bool EnableWhitelist { get; set; }

        /// <summary>白名单卷序列号列表（逗号分隔）</summary>
        public string WhitelistDriveIds { get; set; }
    }

    /// <summary>
    /// 设置持久化（XML 序列化 + DPAPI 加密敏感字段）
    /// 路径策略：程序目录可写时用程序目录（绿色版/便携），否则回退到 %APPDATA%\U盘文件复制器
    /// （安装到 Program Files、希沃教学机等受保护目录时，程序目录不可写会导致设置无法保存）
    /// </summary>
    public static class SettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("U盘文件复制器_SALT_2024");

        private static readonly object FileLock = new object();
        private static string _filePath;
        private static bool _pathResolved;

        /// <summary>设置文件完整路径（首次访问时解析并缓存）</summary>
        public static string FilePath
        {
            get
            {
                if (!_pathResolved)
                {
                    _filePath = ResolveFilePath();
                    _pathResolved = true;
                }
                return _filePath;
            }
        }

        /// <summary>当前使用的设置目录（用于界面提示 / 日志）</summary>
        public static string DirectoryPath
        {
            get
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                return string.IsNullOrEmpty(dir) ? Application.StartupPath : dir;
            }
        }

        /// <summary>
        /// 解析设置文件位置：
        /// 1) 已存在的程序目录配置优先（兼容既有便携部署，避免升级后设置「丢失」）
        /// 2) 已存在的 %APPDATA% 配置次之
        /// 3) 都没有时：程序目录可写则用程序目录，否则用 %APPDATA%
        /// </summary>
        private static string ResolveFilePath()
        {
            string portable = System.IO.Path.Combine(Application.StartupPath, "settings.xml");
            string roaming = System.IO.Path.Combine(GetRoamingDirectory(), "settings.xml");

            try
            {
                if (File.Exists(portable)) return portable;
                if (File.Exists(roaming)) return roaming;
                return IsDirectoryWritable(Application.StartupPath) ? portable : roaming;
            }
            catch
            {
                return roaming;
            }
        }

        private static string GetRoamingDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = System.IO.Path.GetTempPath();
            return System.IO.Path.Combine(appData, "U盘文件复制器");
        }

        /// <summary>探测目录是否可写（不留下残留文件）</summary>
        public static bool IsDirectoryWritable(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            string probe = System.IO.Path.Combine(directory, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    fs.WriteByte(0);
                }
                return true;
            }
            catch
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
                return false;
            }
        }

        /// <summary>保存设置（原子写入：先写临时文件再替换，避免写入中断导致配置损坏）</summary>
        public static void Save(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            lock (FileLock)
            {
                var target = FilePath;
                var directory = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var serializer = new XmlSerializer(typeof(AppSettings));
                string tempPath = target + ".tmp";

                using (var writer = new StreamWriter(tempPath, false, Encoding.UTF8))
                {
                    serializer.Serialize(writer, settings);
                }

                try
                {
                    if (File.Exists(target))
                        File.Replace(tempPath, target, null);
                    else
                        File.Move(tempPath, target);
                }
                catch (Exception)
                {
                    // 极端情况下（如目标被占用）退化为覆盖写入
                    File.Copy(tempPath, target, true);
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        public static AppSettings Load()
        {
            var target = FilePath;
            if (!File.Exists(target))
                return null;

            lock (FileLock)
            {
                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var reader = new StreamReader(target, Encoding.UTF8))
                {
                    return serializer.Deserialize(reader) as AppSettings;
                }
            }
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }

        public static string Decrypt(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return "";
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
