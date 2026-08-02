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
    /// </summary>
    public static class SettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("U盘文件复制器_SALT_2024");

        /// <summary>设置文件路径（程序目录下 settings.xml）</summary>
        public static string FilePath => Path.Combine(Application.StartupPath, "settings.xml");

        public static void Save(AppSettings settings)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var writer = new StreamWriter(FilePath, false, Encoding.UTF8))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public static AppSettings Load()
        {
            if (!File.Exists(FilePath))
                return null;

            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var reader = new StreamReader(FilePath, Encoding.UTF8))
            {
                return serializer.Deserialize(reader) as AppSettings;
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
