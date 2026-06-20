// IFileDestination.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制
{
    /// <summary>
    /// 文件元数据信息（用于文件列表）
    /// </summary>
    public class FileMetadataInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public bool IsDirectory { get; set; }
    }

    /// <summary>
    /// 存储统计信息
    /// </summary>
    public class StorageStatsInfo
    {
        public long TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public double TotalSizeMB => Math.Round(TotalSizeBytes / (1024.0 * 1024.0), 2);
        public long AvailableDiskBytes { get; set; }
        public long TotalDiskBytes { get; set; }
        public int PendingChunks { get; set; }
    }

    /// <summary>
    /// 搜索结果信息
    /// </summary>
    public class SearchResultInfo
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<FileMetadataInfo> Items { get; set; } = new List<FileMetadataInfo>();
    }

    /// <summary>
    /// 文件存储目标接口（本地或远程服务器）
    /// </summary>
    public interface IFileDestination
    {
        /// <summary>
        /// 写入文件（若目录不存在则自动创建）
        /// </summary>
        Task WriteFileAsync(string relativePath, Stream fileStream, CancellationToken cancellationToken);

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// 获取文件的最后写入时间（UTC）
        /// </summary>
        Task<DateTime> GetFileLastWriteTimeUtcAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// 删除文件
        /// </summary>
        Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken);

        /// <summary>
        /// 测试连接是否可用
        /// </summary>
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 列出指定目录下的文件和子目录
        /// </summary>
        Task<List<FileMetadataInfo>> ListFilesAsync(string relativePath, bool recursive, CancellationToken cancellationToken);

        /// <summary>
        /// 获取存储统计信息
        /// </summary>
        Task<StorageStatsInfo> GetStatsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 搜索文件（支持关键词、扩展名、日期范围过滤）
        /// </summary>
        Task<SearchResultInfo> SearchFilesAsync(string keyword, string extension,
            DateTime? startDate, DateTime? endDate, bool recursive,
            int page, int pageSize, CancellationToken cancellationToken);

        /// <summary>
        /// 获取目标类型描述
        /// </summary>
        string DestinationType { get; }

        /// <summary>
        /// 获取/设置进度回调（用于报告上传进度）
        /// </summary>
        IProgress<(string filePath, long bytesTransferred, long totalBytes)> Progress { get; set; }
    }
}
