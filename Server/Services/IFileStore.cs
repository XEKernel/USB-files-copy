using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Services
{
    /// <summary>
    /// 文件元数据信息
    /// </summary>
    public class FileMetadata
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
    public class StorageStats
    {
        public long TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public long AvailableDiskBytes { get; set; }
        public long TotalDiskBytes { get; set; }
        public int PendingChunks { get; set; }
    }

    /// <summary>
    /// 文件存储接口（定义服务器端文件操作）
    /// </summary>
    public interface IFileStore
    {
        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        Task<bool> FileExistsAsync(string relativePath);

        /// <summary>
        /// 获取文件的最后写入时间（UTC）
        /// </summary>
        Task<DateTime?> GetLastWriteTimeUtcAsync(string relativePath);

        /// <summary>
        /// 写入完整文件（覆盖或新建）
        /// </summary>
        Task WriteFileAsync(string relativePath, Stream fileStream);

        /// <summary>
        /// 删除文件
        /// </summary>
        Task DeleteFileAsync(string relativePath);

        /// <summary>
        /// 获取已上传的分块索引集合（用于断点续传）
        /// </summary>
        Task<HashSet<int>> GetUploadedChunksAsync(string relativePath);

        /// <summary>
        /// 上传一个分块（临时存储）
        /// </summary>
        Task UploadChunkAsync(string relativePath, int chunkIndex, int totalChunks, Stream chunkStream);

        /// <summary>
        /// 合并所有分块为完整文件
        /// </summary>
        Task MergeChunksAsync(string relativePath, int totalChunks);

        /// <summary>
        /// 列出指定目录下的所有文件和子目录
        /// </summary>
        /// <param name="relativePath">相对目录路径（空字符串表示根目录）</param>
        /// <param name="recursive">是否递归列出</param>
        Task<List<FileMetadata>> ListFilesAsync(string relativePath = "", bool recursive = false);

        /// <summary>
        /// 打开文件流用于下载
        /// </summary>
        /// <param name="relativePath">相对文件路径</param>
        /// <returns>文件流、文件大小、最后修改时间</returns>
        Task<(Stream fileStream, long fileSize, DateTime lastModifiedUtc)> OpenFileForReadAsync(string relativePath);

        /// <summary>
        /// 获取存储统计信息
        /// </summary>
        Task<StorageStats> GetStatsAsync();

        /// <summary>
        /// 清理超时的未完成分块上传
        /// </summary>
        /// <param name="olderThan">清理早于此时间的临时分块</param>
        Task<int> CleanupStaleChunksAsync(TimeSpan olderThan);

        /// <summary>
        /// 搜索文件（支持文件名、扩展名、日期范围过滤）
        /// </summary>
        /// <param name="keyword">搜索关键词（匹配文件名）</param>
        /// <param name="extension">文件扩展名过滤（如 ".txt"）</param>
        /// <param name="startDate">文件修改时间起始（UTC）</param>
        /// <param name="endDate">文件修改时间截止（UTC）</param>
        /// <param name="recursive">是否递归搜索子目录</param>
        /// <param name="page">页码（从1开始）</param>
        /// <param name="pageSize">每页条数</param>
        Task<SearchResult> SearchFilesAsync(
            string keyword = "",
            string extension = "",
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool recursive = true,
            int page = 1,
            int pageSize = 100);
    }

    /// <summary>
    /// 搜索结果
    /// </summary>
    public class SearchResult
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<FileMetadata> Items { get; set; } = new List<FileMetadata>();
    }
}
