using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using U盘文件复制.Server.Services;

namespace U盘文件复制.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        /// <summary>对外统一下发的 UTC 时间格式（ISO 8601，带 Z 标记），避免客户端按时区误解</summary>
        private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        /// <summary>分页上限，避免 pageSize 过大导致内存与 IO 压力</summary>
        private const int MaxPageSize = 5000;

        /// <summary>自身产生的、可安全回显给调用方的 IO 错误前缀（其余 IO 异常可能含服务端路径）</summary>
        private static readonly string[] SafeIoMessagePrefixes =
        {
            "文件大小超过限制", "单个分块超过限制", "合并后文件大小", "分块 "
        };

        /// <summary>属于「超出容量限制」的错误前缀，对外返回 413 而非 500</summary>
        private static readonly string[] SizeLimitPrefixes =
        {
            "文件大小超过限制", "单个分块超过限制", "合并后文件大小"
        };

        private readonly IFileStore _fileStore;
        private readonly ILogger<FileController> _logger;
        private readonly int _maxChunkCount;
        private readonly int _maxZipEntries;
        private readonly long _maxZipSizeBytes;

        public FileController(IFileStore fileStore, ILogger<FileController> logger, IConfiguration configuration)
        {
            _fileStore = fileStore;
            _logger = logger;

            var storage = configuration.GetSection("FileStorage");
            _maxChunkCount = storage.GetValue("MaxChunkCount", 10000);
            _maxZipEntries = storage.GetValue("MaxZipEntries", 200);
            _maxZipSizeBytes = storage.GetValue("MaxZipSizeBytes", 1073741824L);
        }

        // ===== 错误响应（不向调用方泄露内部细节）=====

        private ObjectResult ServerError(Exception ex, string operation)
        {
            _logger.LogError(ex, "{Operation} 失败", operation);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "服务器内部错误" });
        }

        private ObjectResult IoError(IOException ex, string operation)
        {
            _logger.LogWarning(ex, "{Operation} IO 失败", operation);

            // 超出容量限制：返回 413，便于客户端区分「重试无用」
            if (SizeLimitPrefixes.Any(p => ex.Message.StartsWith(p, StringComparison.Ordinal)))
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = ex.Message });

            bool safe = SafeIoMessagePrefixes.Any(p => ex.Message.StartsWith(p, StringComparison.Ordinal));
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = safe ? ex.Message : "文件操作失败，请查看服务端日志" });
        }

        private IActionResult RequestFailed(Exception ex, string operation)
        {
            return ex switch
            {
                UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { error = "无权访问该路径" }),
                ArgumentException => BadRequest(new { error = "参数不合法" }),
                IOException ioEx => IoError(ioEx, operation),
                _ => ServerError(ex, operation)
            };
        }

        private static int ClampPage(int page) => page < 1 ? 1 : page;

        private static int ClampPageSize(int pageSize)
            => pageSize < 1 ? 1 : (pageSize > MaxPageSize ? MaxPageSize : pageSize);

        /// <summary>
        /// 检查文件是否存在并获取最后修改时间
        /// </summary>
        [HttpHead("file")]
        public async Task<IActionResult> HeadFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                var exists = await _fileStore.FileExistsAsync(path);
                if (!exists)
                    return NotFound();

                var lastModified = await _fileStore.GetLastWriteTimeUtcAsync(path);
                if (lastModified.HasValue)
                    Response.Headers.Append("Last-Modified", lastModified.Value.ToString("r"));
                return Ok();
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "检查文件");
            }
        }

        /// <summary>
        /// 上传完整文件（PUT 方式）
        /// 大小限制由 Kestrel 全局 MaxRequestBodySize 与存储层的累计校验共同控制
        /// </summary>
        [HttpPut("file")]
        public async Task<IActionResult> UploadFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                await _fileStore.WriteFileAsync(path, Request.Body);
                return Ok(new { message = "文件上传成功", path });
            }
            catch (IOException ex)
            {
                return IoError(ex, "上传文件");
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "上传文件");
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        [HttpDelete("file")]
        public async Task<IActionResult> DeleteFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                await _fileStore.DeleteFileAsync(path);
                return Ok(new { message = "文件已删除", path });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "删除文件");
            }
        }

        /// <summary>
        /// 获取已上传的分块索引（断点续传查询）
        /// </summary>
        [HttpGet("chunk-status")]
        public async Task<IActionResult> GetChunkStatus([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                var indices = await _fileStore.GetUploadedChunksAsync(path);
                return Ok(indices);
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "查询分块状态");
            }
        }

        /// <summary>
        /// 上传分块
        /// </summary>
        [HttpPut("chunk")]
        public async Task<IActionResult> UploadChunk([FromQuery] string path, [FromQuery] int index, [FromQuery] int total)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");
            if (index < 0 || total <= 0 || index >= total)
                return BadRequest("index 或 total 参数无效");
            if (total > _maxChunkCount)
                return BadRequest($"分块总数超过限制（{_maxChunkCount}）");

            try
            {
                await _fileStore.UploadChunkAsync(path, index, total, Request.Body);
                return Ok(new { message = $"分块 {index} 上传成功" });
            }
            catch (IOException ex)
            {
                return IoError(ex, "上传分块");
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "上传分块");
            }
        }

        /// <summary>
        /// 合并分块为完整文件
        /// </summary>
        [HttpPost("merge")]
        public async Task<IActionResult> MergeChunks([FromQuery] string path, [FromQuery] int total)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");
            if (total <= 0)
                return BadRequest("total 参数无效");
            if (total > _maxChunkCount)
                return BadRequest($"分块总数超过限制（{_maxChunkCount}）");

            try
            {
                await _fileStore.MergeChunksAsync(path, total);
                return Ok(new { message = "文件合并成功", path });
            }
            catch (FileNotFoundException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (IOException ex)
            {
                return IoError(ex, "合并分块");
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "合并分块");
            }
        }

        /// <summary>
        /// 列出文件（支持分页）
        /// </summary>
        [HttpGet("list")]
        public async Task<IActionResult> ListFiles(
            [FromQuery] string path = "",
            [FromQuery] bool recursive = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            try
            {
                path = path ?? "";
                page = ClampPage(page);
                pageSize = ClampPageSize(pageSize);

                var allFiles = await _fileStore.ListFilesAsync(path, recursive);

                var total = allFiles.Count;
                var paged = allFiles
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new
                {
                    total,
                    page,
                    pageSize,
                    items = paged.Select(f => new
                    {
                        f.Path,
                        f.Name,
                        f.SizeBytes,
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString(UtcFormat),
                        f.IsDirectory
                    })
                });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "列出文件");
            }
        }

        /// <summary>
        /// 下载文件（支持断点续传 / Range 请求）
        /// </summary>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                var (fileStream, _, lastModifiedUtc) = await _fileStore.OpenFileForReadAsync(path);

                // 设置响应头
                Response.Headers.Append("Last-Modified", lastModifiedUtc.ToString("r"));

                var fileName = Path.GetFileName(path);
                var contentType = "application/octet-stream";

                // File() 重载默认 enableRangeProcessing: true，自动处理 Range 断点续传，
                // 无需手动解析 Range 头（避免双重处理冲突）
                return File(fileStream, contentType, fileName);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "下载文件");
            }
        }

        /// <summary>
        /// 获取存储统计信息
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var stats = await _fileStore.GetStatsAsync();
                return Ok(new
                {
                    stats.TotalFiles,
                    TotalSizeMB = Math.Round(stats.TotalSizeBytes / (1024.0 * 1024.0), 2),
                    stats.TotalSizeBytes,
                    AvailableDiskMB = Math.Round(stats.AvailableDiskBytes / (1024.0 * 1024.0), 2),
                    TotalDiskMB = Math.Round(stats.TotalDiskBytes / (1024.0 * 1024.0), 2),
                    stats.PendingChunks
                });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "获取统计");
            }
        }

        /// <summary>
        /// 清理超时的临时分块文件
        /// </summary>
        [HttpPost("cleanup")]
        public async Task<IActionResult> CleanupStaleChunks([FromQuery] int hoursOld = 24)
        {
            try
            {
                var cleaned = await _fileStore.CleanupStaleChunksAsync(TimeSpan.FromHours(hoursOld));
                return Ok(new { message = "清理完成", cleanedChunks = cleaned });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "清理分块");
            }
        }

        /// <summary>
        /// 搜索文件（支持关键词、扩展名、日期范围过滤）
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> SearchFiles(
            [FromQuery] string keyword = "",
            [FromQuery] string extension = "",
            [FromQuery] string startDate = "",
            [FromQuery] string endDate = "",
            [FromQuery] bool recursive = true,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            try
            {
                page = ClampPage(page);
                pageSize = ClampPageSize(pageSize);

                DateTime? start = null;
                DateTime? end = null;

                if (!string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, out var sd))
                    start = sd;
                if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var ed))
                    end = ed;

                var result = await _fileStore.SearchFilesAsync(
                    keyword: keyword ?? "",
                    extension: extension ?? "",
                    startDate: start,
                    endDate: end,
                    recursive: recursive,
                    page: page,
                    pageSize: pageSize);

                return Ok(new
                {
                    result.Total,
                    Page = page,
                    PageSize = pageSize,
                    items = result.Items.Select(f => new
                    {
                        f.Path,
                        f.Name,
                        f.SizeBytes,
                        SizeKB = Math.Round(f.SizeBytes / 1024.0, 1),
                        SizeMB = Math.Round(f.SizeBytes / (1024.0 * 1024.0), 2),
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString(UtcFormat),
                        f.IsDirectory
                    })
                });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "搜索文件");
            }
        }

        /// <summary>
        /// 批量下载：将多个文件打包为 ZIP
        /// 以临时文件流式产出，避免整包驻留内存
        /// </summary>
        [HttpPost("download-zip")]
        public async Task<IActionResult> DownloadZip([FromBody] DownloadZipRequest request)
        {
            if (request?.Paths == null || request.Paths.Length == 0)
                return BadRequest("paths 参数不能为空");

            var paths = request.Paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToArray();

            if (paths.Length == 0)
                return BadRequest("paths 参数不能为空");
            if (paths.Length > _maxZipEntries)
                return BadRequest($"一次最多打包 {_maxZipEntries} 个文件");

            string tempPath = Path.Combine(Path.GetTempPath(), $"usbfiles_{Guid.NewGuid():N}.zip");
            FileStream? output = null;

            try
            {
                // DeleteOnClose：交给框架在响应结束后释放并删除临时文件
                output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

                long projectedBytes = 0;
                bool sizeExceeded = false;

                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var p in paths)
                    {
                        try
                        {
                            var (stream, size, _) = await _fileStore.OpenFileForReadAsync(p);
                            using (stream)
                            {
                                projectedBytes += size;
                                if (projectedBytes > _maxZipSizeBytes)
                                {
                                    // 不能在此处直接释放流并 return：ZipArchive 仍持有该流，
                                    // 提前释放会让 using 结束时写中央目录失败
                                    sizeExceeded = true;
                                    break;
                                }

                                var entry = zip.CreateEntry(MakeZipEntryName(p), CompressionLevel.Optimal);
                                using (var entryStream = entry.Open())
                                {
                                    await stream.CopyToAsync(entryStream);
                                }
                            }
                        }
                        catch (FileNotFoundException) { /* 单个文件丢失则跳过，不中断打包 */ }
                        catch (UnauthorizedAccessException) { /* 不可访问的项目直接跳过 */ }
                    }
                }

                if (sizeExceeded)
                {
                    output.Dispose();
                    output = null;
                    return BadRequest($"打包总大小超过上限（{_maxZipSizeBytes / (1024 * 1024)} MB），请分批下载");
                }

                output.Position = 0;
                var result = File(output, "application/zip", $"批量下载_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                output = null;   // 由框架负责释放（触发临时文件删除）
                return result;
            }
            catch (Exception ex)
            {
                output?.Dispose();
                return ServerError(ex, "批量打包下载");
            }
        }

        /// <summary>
        /// 生成 ZIP 内的条目名：去掉绝对路径与 ".." 段，防止解压时目录穿越（zip-slip）
        /// </summary>
        private static string MakeZipEntryName(string relativePath)
        {
            var segments = (relativePath ?? string.Empty)
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s != "." && s != ".." && s.IndexOf(':') < 0)
                .ToArray();

            return segments.Length == 0 ? "file" : string.Join("/", segments);
        }

        /// <summary>
        /// 列出回收站文件
        /// </summary>
        [HttpGet("trash")]
        public async Task<IActionResult> ListTrash()
        {
            try
            {
                var items = await _fileStore.ListTrashAsync();
                return Ok(new
                {
                    total = items.Count,
                    items = items.Select(f => new
                    {
                        f.Path,
                        f.Name,
                        f.SizeBytes,
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString(UtcFormat)
                    })
                });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "列出回收站");
            }
        }

        /// <summary>
        /// 从回收站恢复文件
        /// </summary>
        [HttpPost("restore")]
        public async Task<IActionResult> RestoreFromTrash([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            try
            {
                await _fileStore.RestoreFromTrashAsync(path);
                return Ok(new { message = "文件已恢复", path });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "恢复文件");
            }
        }

        /// <summary>
        /// 清空回收站（可选 hoursOld：仅清理早于 N 小时的文件，默认全部）
        /// </summary>
        [HttpPost("trash-clear")]
        public async Task<IActionResult> ClearTrash([FromQuery] int hoursOld = 0)
        {
            try
            {
                var cleared = await _fileStore.ClearTrashAsync(TimeSpan.FromHours(hoursOld));
                return Ok(new { message = "回收站清理完成", clearedFiles = cleared });
            }
            catch (Exception ex)
            {
                return RequestFailed(ex, "清空回收站");
            }
        }
    }

    /// <summary>
    /// 批量下载请求体
    /// </summary>
    public class DownloadZipRequest
    {
        public string[] Paths { get; set; } = Array.Empty<string>();
    }
}
