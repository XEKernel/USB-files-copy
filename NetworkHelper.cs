// NetworkHelper.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制
{
    /// <summary>
    /// HTTP 辅助类，封装带认证的请求、文件上传等
    /// 使用 ApiBaseUrl 构造正确的 API 请求路径，与服务器端路由匹配
    /// </summary>
    public static class NetworkHelper
    {
        /// <summary>
        /// 创建并配置一个 HttpClient 实例（基于 ServerConfig）
        /// </summary>
        private static HttpClient CreateClient(ServerConfig config)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseProxy = true,
            };

            // 配置服务器证书验证
            if (config.ValidateCertificate)
            {
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors)
                    => sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
            }
            else
            {
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors)
                    => true;
            }

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            // 认证头：优先使用令牌，否则使用密码（基本认证）
            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                var byteArray = Encoding.ASCII.GetBytes($"usercopy:{config.Password}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }

            return client;
        }

        /// <summary>
        /// 测试服务器连接（发送 GET 请求到健康检查端点）
        /// </summary>
        public static async Task<bool> TestConnectionAsync(ServerConfig config)
        {
            using (var client = CreateClient(config))
            {
                try
                {
                    // 测试健康检查端点（无需认证）
                    var healthUrl = $"{config.BaseUrl}/api/health";
                    var response = await client.GetAsync(healthUrl);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 发送 GET 请求，返回字符串内容
        /// </summary>
        public static async Task<string> GetStringAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            using (var client = CreateClient(config))
            {
                var url = $"{config.BaseUrl}/{relativePath.TrimStart('/')}";
                var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        /// <summary>
        /// 发送 HEAD 请求，检查资源是否存在并获取 Last-Modified
        /// 对应服务器端：HEAD api/file/file?path=...
        /// </summary>
        public static async Task<(bool exists, DateTime? lastModifiedUtc)> HeadAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            using (var client = CreateClient(config))
            {
                var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    var response = await client.SendAsync(request, ct);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return (false, null);
                    response.EnsureSuccessStatusCode();

                    DateTime? lastModified = null;
                    if (response.Content.Headers.LastModified.HasValue)
                        lastModified = response.Content.Headers.LastModified.Value.UtcDateTime;
                    return (true, lastModified);
                }
            }
        }

        /// <summary>
        /// 删除远程文件
        /// 对应服务器端：DELETE api/file/file?path=...
        /// </summary>
        public static async Task DeleteAsync(ServerConfig config, string relativePath, CancellationToken ct)
        {
            using (var client = CreateClient(config))
            {
                var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
                var response = await client.DeleteAsync(url, ct);
                // 如果文件不存在，也视为成功
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// 上传文件数据（完整文件）
        /// 对应服务器端：PUT api/file/file?path=...
        /// </summary>
        public static async Task UploadAsync(ServerConfig config, string relativePath, Stream fileData, string contentType, CancellationToken ct)
        {
            using (var client = CreateClient(config))
            {
                var url = $"{config.ApiBaseUrl}/file?path={Uri.EscapeDataString(relativePath)}";
                using (var content = new StreamContent(fileData))
                {
                    if (!string.IsNullOrEmpty(contentType))
                        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    var response = await client.PutAsync(url, content, ct);
                    response.EnsureSuccessStatusCode();
                }
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

            long totalLength = fileStream.Length;
            int chunksCount = (int)Math.Ceiling((double)totalLength / chunkSize);

            using (var client = CreateClient(config))
            {
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
                    await fileStream.ReadAsync(buffer, 0, currentChunkSize, ct);

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
                    var indices = content.Trim('[', ']').Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var set = new HashSet<int>();
                    foreach (var idx in indices)
                        if (int.TryParse(idx.Trim(), out int i))
                            set.Add(i);
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
            using (var client = CreateClient(config))
            {
                var url = $"{config.ApiBaseUrl}/search?keyword={Uri.EscapeDataString(keyword ?? "")}" +
                          $"&extension={Uri.EscapeDataString(extension ?? "")}&recursive={(recursive ? "true" : "false")}" +
                          $"&page={page}&pageSize={pageSize}";
                if (startDate.HasValue)
                    url += $"&startDate={Uri.EscapeDataString(startDate.Value.ToString("yyyy-MM-dd"))}";
                if (endDate.HasValue)
                    url += $"&endDate={Uri.EscapeDataString(endDate.Value.ToString("yyyy-MM-dd"))}";

                var response = await client.GetAsync(url, ct);
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
                        LastWriteTimeUtc = DateTime.Parse((string)item["lastWriteTimeUtc"] ?? DateTime.MinValue.ToString()),
                        IsDirectory = (bool)item["isDirectory"]
                    });
                }
                return result;
            }
        }

        /// <summary>
        /// 从远程服务器下载文件到本地
        /// 对应服务器端：GET api/file/download?path=...
        /// </summary>
        public static async Task DownloadFileAsync(ServerConfig config, string remotePath, string localPath, CancellationToken ct)
        {
            using (var client = CreateClient(config))
            {
                var url = $"{config.ApiBaseUrl}/download?path={Uri.EscapeDataString(remotePath)}";
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
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
}
