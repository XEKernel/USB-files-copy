// NetworkHelper.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace U盘文件复制
{
    /// <summary>
    /// HTTP 辅助类，封装带认证的请求、文件上传等
    /// 使用 ApiBaseUrl 构造正确的 API 请求路径，与服务器端路由匹配
    /// </summary>
    public static class NetworkHelper
    {
        /// <summary>
        /// 按配置缓存 HttpClient（认证头已内置于客户端），避免重复创建导致的 Socket 耗尽。
        /// 缓存 key 含令牌/密码，故配置变化时旧客户端必须释放，否则每次改动配置都会泄漏一个连接池。
        /// </summary>
        private static readonly ConcurrentDictionary<string, HttpClient> _clientCache =
            new ConcurrentDictionary<string, HttpClient>(StringComparer.Ordinal);

        private static readonly object _clientCacheLock = new object();

        private static string BuildClientKey(ServerConfig config) =>
            $"{config.UseHttps}|{config.ServerAddress}|{config.Port}|{config.ValidateCertificate}|{config.ApiToken}|{config.Password}";

        private static HttpClient GetClient(ServerConfig config)
        {
            string key = BuildClientKey(config);

            if (_clientCache.TryGetValue(key, out var cached))
                return cached;

            lock (_clientCacheLock)
            {
                if (_clientCache.TryGetValue(key, out cached))
                    return cached;

                // 服务器配置（地址/令牌/密码）变更：释放并丢弃全部旧客户端，避免无限累积
                foreach (var pair in _clientCache)
                {
                    if (pair.Key == key) continue;
                    try { pair.Value.Dispose(); } catch { }
                }
                _clientCache.Clear();

                var client = CreateClient(config);
                _clientCache[key] = client;
                return client;
            }
        }

        private static HttpClient CreateClient(ServerConfig config)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseProxy = true,
            };

            // 证书验证策略：默认验证（安全）；仅当用户明确关闭时才跳过（测试环境）
            if (!config.ValidateCertificate)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (sender, cert, chain, sslPolicyErrors) => true;
            }

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
            };

            // 认证头：优先使用令牌（Bearer），否则使用密码（Basic 基本认证）
            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.ApiToken);
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                var byteArray = Encoding.ASCII.GetBytes($"usercopy:{config.Password}");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }

            return client;
        }

        /// <summary>
        /// 测试服务器连接（发送 GET 请求到健康检查端点）
        /// 注意：健康检查免认证，令牌错误同样返回 200，仅适合判断「服务是否在线」
        /// </summary>
        public static async Task<bool> TestConnectionAsync(ServerConfig config)
        {
            try
            {
                var healthUrl = $"{config.BaseUrl}/api/health";
                var response = await GetClient(config).GetAsync(healthUrl);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 校验服务器是否真的可用（含认证）：访问受保护接口确认令牌/密码有效。
        /// 用于「测试连接」，避免令牌填错却显示“连接成功”，直到上传时才失败。
        /// </summary>
        public static async Task<(bool ok, string message)> VerifyConnectionAsync(ServerConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ServerAddress))
                return (false, "服务器地址不能为空");

            try
            {
                var url = $"{config.ApiBaseUrl}/stats";
                var response = await GetClient(config).GetAsync(url);

                if (response.IsSuccessStatusCode)
                    return (true, "连接成功");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return (false, "认证失败：令牌或密码不正确");

                return (false, $"服务器返回 {response.StatusCode:D} {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                return (false, $"连接失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 发送 GET 请求，返回字符串内容
        /// </summary>
        public static async Task<string> GetStringAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            var url = $"{config.BaseUrl}/{relativePath.TrimStart('/')}";
            var response = await GetClient(config).GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// 发送 HEAD 请求，检查资源是否存在并获取 Last-Modified
        /// 对应服务器端：HEAD api/file/file?path=...
        /// </summary>
        public static async Task<(bool exists, DateTime? lastModifiedUtc)> HeadAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
            using (var request = new HttpRequestMessage(HttpMethod.Head, url))
            {
                var response = await GetClient(config).SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return (false, null);
                response.EnsureSuccessStatusCode();

                DateTime? lastModified = null;
                if (response.Content.Headers.LastModified.HasValue)
                    lastModified = response.Content.Headers.LastModified.Value.UtcDateTime;
                return (true, lastModified);
            }
        }

        /// <summary>
        /// 删除远程文件
        /// 对应服务器端：DELETE api/file/file?path=...
        /// </summary>
        public static async Task DeleteAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
            var response = await GetClient(config).DeleteAsync(url, ct);
            // 如果文件不存在，也视为成功
            if (response.StatusCode == HttpStatusCode.NotFound)
                return;
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// 上传文件数据（完整文件）
        /// 对应服务器端：PUT api/file/file?path=...
        /// </summary>
        public static async Task UploadAsync(ServerConfig config, string relativePath, Stream fileData, string contentType, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
            using (var content = new StreamContent(fileData))
            {
                if (!string.IsNullOrEmpty(contentType))
                    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                var response = await GetClient(config).PutAsync(url, content, ct);
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// 分块上传（支持断点续传）
        /// 对应服务器端：GET chunk-status、PUT chunk、POST merge
        /// </summary>
        public static async Task UploadChunkedAsync(ServerConfig config, string relativePath, Stream fileStream, int chunkSize, CancellationToken ct)
        {
            if (!fileStream.CanSeek)
                throw new InvalidOperationException("文件流必须支持 Seek 操作才能分块上传");
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "分块大小必须大于 0");

            long totalLength = fileStream.Length;
            int chunksCount = (int)Math.Ceiling((double)totalLength / chunkSize);
            if (chunksCount <= 0)
                throw new InvalidOperationException("文件长度为 0，无需分块上传");

            var client = GetClient(config);

            // 询问服务器已经上传了哪些分块（断点续传）
            HashSet<int> uploadedChunks = await GetUploadedChunksAsync(client, config, relativePath, ct);

            for (int i = 0; i < chunksCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (uploadedChunks.Contains(i))
                    continue;

                long offset = i * (long)chunkSize;
                int currentChunkSize = (int)Math.Min(chunkSize, totalLength - offset);
                byte[] buffer = new byte[currentChunkSize];
                fileStream.Seek(offset, SeekOrigin.Begin);

                // 必须循环读满：单次 ReadAsync 不保证返回请求的字节数，
                // 直接上传会导致远端分块残缺、合并后文件损坏
                int filled = 0;
                while (filled < currentChunkSize)
                {
                    int read = await fileStream.ReadAsync(buffer, filled, currentChunkSize - filled, ct);
                    if (read <= 0) break;
                    filled += read;
                }
                if (filled != currentChunkSize)
                    throw new IOException($"读取分块 {i} 失败（期望 {currentChunkSize} 字节，实际 {filled} 字节）");

                using (var chunkStream = new MemoryStream(buffer))
                {
                    await UploadChunkAsync(client, config, relativePath, chunkStream, i, chunksCount, ct);
                }
            }

            // 所有分块上传完成后，发送合并请求
            var mergeUrl = $"{config.ApiBaseUrl}/merge?path={Uri.EscapeDataString(relativePath)}&total={chunksCount}";
            var mergeResponse = await client.PostAsync(mergeUrl, null, ct);
            mergeResponse.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// 查询服务器已上传的分块索引（用于断点续传）
        /// 对应服务器端：GET api/file/chunk-status?path=...
        /// </summary>
        private static async Task<HashSet<int>> GetUploadedChunksAsync(HttpClient client, ServerConfig config, string remotePath, CancellationToken ct)
        {
            var statusUrl = $"{config.ApiBaseUrl}/chunk-status?path={Uri.EscapeDataString(remotePath)}";
            try
            {
                var response = await client.GetAsync(statusUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // 服务器返回 JSON 数组格式，例如 [0,2,5]
                    var indicesArray = JArray.Parse(content);
                    var set = new HashSet<int>();
                    foreach (var idx in indicesArray)
                        set.Add((int)idx);
                    return set;
                }
            }
            catch { /* 忽略，当作没有已上传块 */ }
            return new HashSet<int>();
        }

        /// <summary>
        /// 上传单个分块到服务器
        /// 对应服务器端：PUT api/file/chunk?path=...&index=...&total=...
        /// </summary>
        private static async Task UploadChunkAsync(HttpClient client, ServerConfig config, string originalPath, Stream chunkStream, int index, int total, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/chunk?path={Uri.EscapeDataString(originalPath)}&index={index}&total={total}";
            using (var content = new StreamContent(chunkStream))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var response = await client.PutAsync(url, content, ct);
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// 列出服务器目录内容
        /// 对应服务器端：GET api/file/list?path=...&recursive=...
        /// </summary>
        public static async Task<List<FileMetadataInfo>> ListFilesAsync(ServerConfig config, string relativePath, bool recursive, CancellationToken ct)
        {
            // pageSize 与服务端上限（5000）保持一致，避免被截断后前端无感
            var url = $"{config.ApiBaseUrl}/list?path={Uri.EscapeDataString(relativePath ?? "")}&recursive={(recursive ? "true" : "false")}&pageSize=5000";
            var response = await GetClient(config).GetAsync(url, ct);
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
                    LastWriteTimeUtc = ParseUtcTime((string)item["lastWriteTimeUtc"]),
                    IsDirectory = (bool)item["isDirectory"]
                });
            }
            return result;
        }

        /// <summary>
        /// 获取服务器存储统计
        /// 对应服务器端：GET api/file/stats
        /// </summary>
        public static async Task<StorageStatsInfo> GetStatsAsync(ServerConfig config, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/stats";
            var response = await GetClient(config).GetAsync(url, ct);
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

        /// <summary>
        /// 搜索远程文件
        /// 对应服务器端：GET api/file/search?keyword=...&extension=...&startDate=...&endDate=...
        /// </summary>
        public static async Task<SearchResultInfo> SearchFilesAsync(
            ServerConfig config,
            string keyword = "",
            string extension = "",
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool recursive = true,
            int page = 1,
            int pageSize = 100,
            CancellationToken ct = default)
        {
            var url = $"{config.ApiBaseUrl}/search?keyword={Uri.EscapeDataString(keyword ?? "")}" +
                      $"&extension={Uri.EscapeDataString(extension ?? "")}&recursive={(recursive ? "true" : "false")}" +
                      $"&page={page}&pageSize={pageSize}";
            if (startDate.HasValue)
                url += $"&startDate={Uri.EscapeDataString(startDate.Value.ToString("yyyy-MM-dd"))}";
            if (endDate.HasValue)
                url += $"&endDate={Uri.EscapeDataString(endDate.Value.ToString("yyyy-MM-dd"))}";

            var response = await GetClient(config).GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
            var result = new SearchResultInfo
            {
                Total = (int)doc["total"],
                Page = (int)doc["page"],
                PageSize = (int)doc["pageSize"]
            };
            var items = (Newtonsoft.Json.Linq.JArray)doc["items"];
            foreach (var item in items)
            {
                result.Items.Add(new FileMetadataInfo
                {
                    Path = (string)item["path"] ?? "",
                    Name = (string)item["name"] ?? "",
                    SizeBytes = (long)item["sizeBytes"],
                    LastWriteTimeUtc = ParseUtcTime((string)item["lastWriteTimeUtc"]),
                    IsDirectory = (bool)item["isDirectory"]
                });
            }
            return result;
        }

        /// <summary>
        /// 解析服务端返回的时间（服务端以 UTC 下发）。
        /// 新版格式为 ISO 8601 带 Z（如 2026-09-13T05:27:34Z）；旧版无时区标记，
        /// 按 UTC 解释——若按本地时间解析会让「保留较新」比较整体偏移时区差（中国为 8 小时）。
        /// </summary>
        private static DateTime ParseUtcTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DateTime.MinValue;

            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return DateTime.MinValue;

            switch (dt.Kind)
            {
                case DateTimeKind.Utc:
                    return dt;
                case DateTimeKind.Local:
                    return dt.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 从远程服务器下载文件到本地
        /// 对应服务器端：GET api/file/download?path=...
        /// </summary>
        public static async Task DownloadFileAsync(ServerConfig config, string remotePath, string localPath, CancellationToken ct)
        {
            var url = $"{config.ApiBaseUrl}/download?path={Uri.EscapeDataString(remotePath)}";
            var response = await GetClient(config).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            using (var responseStream = await response.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(fileStream, 81920, ct);
            }
        }
    }
}
