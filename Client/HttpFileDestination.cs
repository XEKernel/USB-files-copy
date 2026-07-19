// HttpFileDestination.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace U盘文件复制
{
    /// <summary>
    /// 服务器上传实现（基于 HTTPS）
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
                using (var client = CreateClient(_config))
                {
                    var url = $"{_config.ApiBaseUrl}/list?path={Uri.EscapeDataString(relativePath)}&recursive={(recursive ? "true" : "false")}&pageSize=10000";
                    var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();

                    var doc = JObject.Parse(json);
                    var result = new List<FileMetadataInfo>();
                    var items = (JArray)doc["items"];
                    foreach (var item in items)
                    {
                        result.Add(new FileMetadataInfo
                        {
                            Path = (string)item["path"] ?? "",
                            Name = (string)item["name"] ?? "",
                            SizeBytes = (long)item["sizeBytes"],
                            LastWriteTimeUtc = DateTime.Parse((string)item["lastWriteTimeUtc"] ?? DateTime.MinValue.ToString()),
                            IsDirectory = (bool)item["isDirectory"]
                        });
                    }
                    return result;
                }
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
                using (var client = CreateClient(_config))
                {
                    var url = $"{_config.ApiBaseUrl}/stats";
                    var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();

                    var doc = JObject.Parse(json);
                    return new StorageStatsInfo
                    {
                        TotalFiles = (long)doc["totalFiles"],
                        TotalSizeBytes = (long)doc["totalSizeBytes"],
                        AvailableDiskBytes = doc["availableDiskMB"] != null
                            ? (long)((double)doc["availableDiskMB"] * 1024 * 1024) : 0,
                        TotalDiskBytes = doc["totalDiskMB"] != null
                            ? (long)((double)doc["totalDiskMB"] * 1024 * 1024) : 0,
                        PendingChunks = doc["pendingChunks"] != null ? (int)doc["pendingChunks"] : 0
                    };
                }
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

        /// <summary>
        /// 创建 HTTP 客户端（专用于元数据查询，复用共享 Handler）
        /// </summary>
        private static HttpClient CreateClient(ServerConfig config)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseProxy = true,
            };
            // 证书验证策略
            if (config.ValidateCertificate)
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
            else
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
            };

            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiToken);
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                var byteArray = System.Text.Encoding.ASCII.GetBytes($"usercopy:{config.Password}");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }

            return client;
        }
    }
}
