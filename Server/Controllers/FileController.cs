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
        private readonly IFileStore _fileStore;

        public FileController(IFileStore fileStore)
        {
            _fileStore = fileStore;
        }

        /// <summary>
        /// 检查文件是否存在并获取最后修改时间
        /// </summary>
        [HttpHead("file")]
        public async Task<IActionResult> HeadFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            var exists = await _fileStore.FileExistsAsync(path);
            if (!exists)
                return NotFound();

            var lastModified = await _fileStore.GetLastWriteTimeUtcAsync(path);
            if (lastModified.HasValue)
                Response.Headers.Append("Last-Modified", lastModified.Value.ToString("r"));
            return Ok();
        }

        /// <summary>
        /// 上传完整文件（PUT 方式）
        /// 大小限制由 Kestrel 全局 MaxRequestBodySize（appsettings 的 MaxFileSizeBytes）控制
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
                return StatusCode(500, new { error = ex.Message });
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

            await _fileStore.DeleteFileAsync(path);
            return Ok(new { message = "文件已删除", path });
        }

        /// <summary>
        /// 获取已上传的分块索引（断点续传查询）
        /// </summary>
        [HttpGet("chunk-status")]
        public async Task<IActionResult> GetChunkStatus([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("path 参数不能为空");

            var indices = await _fileStore.GetUploadedChunksAsync(path);
            return Ok(indices);
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

            try
            {
                await _fileStore.UploadChunkAsync(path, index, total, Request.Body);
                return Ok(new { message = $"分块 {index} 上传成功" });
            }
            catch (IOException ex)
            {
                return StatusCode(500, new { error = ex.Message });
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

            try
            {
                await _fileStore.MergeChunksAsync(path, total);
                return Ok(new { message = "文件合并成功", path });
            }
            catch (FileNotFoundException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
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
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        f.IsDirectory
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
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
                return StatusCode(500, new { error = ex.Message });
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
                return StatusCode(500, new { error = ex.Message });
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
                return StatusCode(500, new { error = ex.Message });
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
                    result.Page,
                    result.PageSize,
                    items = result.Items.Select(f => new
                    {
                        f.Path,
                        f.Name,
                        f.SizeBytes,
                        SizeKB = Math.Round(f.SizeBytes / 1024.0, 1),
                        SizeMB = Math.Round(f.SizeBytes / (1024.0 * 1024.0), 2),
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        f.IsDirectory
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 批量下载：将多个文件打包为 ZIP
        /// </summary>
        [HttpPost("download-zip")]
        public async Task<IActionResult> DownloadZip([FromBody] DownloadZipRequest request)
        {
            if (request?.Paths == null || request.Paths.Length == 0)
                return BadRequest("paths 参数不能为空");

            try
            {
                var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var p in request.Paths.Distinct())
                    {
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        try
                        {
                            var (stream, _, _) = await _fileStore.OpenFileForReadAsync(p);
                            var entry = zip.CreateEntry(p.TrimStart('/').Replace('\\', '/'));
                            using (var entryStream = entry.Open())
                            using (stream)
                            {
                                await stream.CopyToAsync(entryStream);
                            }
                        }
                        catch (FileNotFoundException) { /* 单个文件丢失则跳过，不中断打包 */ }
                    }
                }
                ms.Position = 0;
                return File(ms, "application/zip", $"批量下载_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
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
                        LastWriteTimeUtc = f.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
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
                return StatusCode(500, new { error = ex.Message });
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
                return StatusCode(500, new { error = ex.Message });
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
