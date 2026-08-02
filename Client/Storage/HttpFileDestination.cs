// HttpFileDestination.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制
{
    /// <summary>
    /// 服务器上传实现（基于 HTTPS）
    /// 所有 HTTP 细节统一委托给 NetworkHelper，避免重复的客户端工厂与认证逻辑
    /// </summary>
    public class HttpFileDestination : IFileDestination
    {
        private readonly ServerConfig _config;
        private readonly bool _useChunkedUpload;

        /// <summary>公开服务器配置，供 RemoteBrowserForm 等使用</summary>
        public ServerConfig Config => _config;

        public HttpFileDestination(ServerConfig config, bool useChunkedUpload = true)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _useChunkedUpload = useChunkedUpload;
        }

        public string DestinationType => $"服务器 ({_config.ServerAddress}:{_config.Port})";

        public IProgress<(string filePath, long bytesTransferred, long totalBytes)> Progress { get; set; }

        public async Task WriteFileAsync(string relativePath, Stream fileStream, CancellationToken cancellationToken)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');

            // 报告开始
            long totalSize = 0;
            try { totalSize = fileStream.Length; } catch { }
            Progress?.Report((relativePath, 0, totalSize));

            int retryCount = 0;
            while (retryCount <= _config.MaxRetries)
            {
                try
                {
                    if (_useChunkedUpload && fileStream.Length > _config.ChunkSizeBytes)
                    {
                        await NetworkHelper.UploadChunkedAsync(_config, relativePath, fileStream, _config.ChunkSizeBytes, cancellationToken);
                    }
                    else
                    {
                        await NetworkHelper.UploadAsync(_config, relativePath, fileStream, null, cancellationToken);
                    }
                    // 报告完成
                    Progress?.Report((relativePath, totalSize, totalSize));
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception) when (retryCount < _config.MaxRetries)
                {
                    retryCount++;
                    await Task.Delay(1000 * retryCount, cancellationToken);
                    if (fileStream.CanSeek)
                        fileStream.Seek(0, SeekOrigin.Begin);
                }
            }
            throw new IOException($"上传文件失败，已重试 {_config.MaxRetries} 次: {relativePath}");
        }

        public async Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            var (exists, _) = await NetworkHelper.HeadAsync(_config, relativePath, cancellationToken);
            return exists;
        }

        public async Task<DateTime> GetFileLastWriteTimeUtcAsync(string relativePath, CancellationToken cancellationToken)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            var (exists, lastModified) = await NetworkHelper.HeadAsync(_config, relativePath, cancellationToken);
            if (!exists)
                throw new FileNotFoundException($"远程文件不存在: {relativePath}");
            if (!lastModified.HasValue)
                throw new NotSupportedException("服务器未提供 Last-Modified 头");
            return lastModified.Value;
        }

        public async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            await NetworkHelper.DeleteAsync(_config, relativePath, cancellationToken);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            return await NetworkHelper.TestConnectionAsync(_config);
        }

        public async Task<List<FileMetadataInfo>> ListFilesAsync(string relativePath, bool recursive, CancellationToken cancellationToken)
        {
            relativePath = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
            try
            {
                return await NetworkHelper.ListFilesAsync(_config, relativePath, recursive, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new IOException($"获取文件列表失败: {ex.Message}", ex);
            }
        }

        public async Task<StorageStatsInfo> GetStatsAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await NetworkHelper.GetStatsAsync(_config, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new IOException($"获取统计信息失败: {ex.Message}", ex);
            }
        }

        public async Task<SearchResultInfo> SearchFilesAsync(
            string keyword, string extension,
            DateTime? startDate, DateTime? endDate,
            bool recursive, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            return await NetworkHelper.SearchFilesAsync(_config, keyword, extension,
                startDate, endDate, recursive, page, pageSize, cancellationToken);
        }
    }
}
