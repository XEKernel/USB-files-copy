// ServerConfig.cs
using System;

namespace U盘文件复制
{
    /// <summary>
    /// 服务器配置信息（用于保存到 settings.xml）
    /// </summary>
    [Serializable]
    public class ServerConfig
    {
        /// <summary> 服务器地址（IP 或域名，不含 http:// ） </summary>
        public string ServerAddress { get; set; } = "";

        /// <summary> 端口（默认 443 用于 HTTPS，也可指定 8080 等） </summary>
        public int Port { get; set; } = 443;

        /// <summary> 是否使用 HTTPS（强烈建议 true） </summary>
        public bool UseHttps { get; set; } = true;

        /// <summary> 密码（与令牌二选一，优先使用令牌） </summary>
        public string Password { get; set; } = "";

        /// <summary> API 令牌（优先于密码） </summary>
        public string ApiToken { get; set; } = "";

        /// <summary> 远程存储根路径（例如 /uploads/ 或 /usb-files/） </summary>
        public string RemoteRootPath { get; set; } = "/";

        /// <summary> API 基础路径（例如 /api/file，需与服务器端路由匹配） </summary>
        public string ApiBasePath { get; set; } = "/api/file";

        /// <summary> 是否验证服务器证书（生产环境必须 true，测试时可设为 false 并加警告） </summary>
        public bool ValidateCertificate { get; set; } = true;

        /// <summary> 超时时间（秒） </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary> 分块上传大小（字节，默认 1MB） </summary>
        public int ChunkSizeBytes { get; set; } = 1024 * 1024;

        /// <summary> 最大重试次数 </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary> 获取服务器根 URL（包含 RemoteRootPath，不含 API 路径） </summary>
        public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{ServerAddress}:{Port}{RemoteRootPath.TrimEnd('/')}";

        /// <summary> 获取 API 基础 URL（BaseUrl + ApiBasePath） </summary>
        public string ApiBaseUrl => $"{BaseUrl}{ApiBasePath}";
    }
}