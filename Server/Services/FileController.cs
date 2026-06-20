using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
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
        /// </summary>
        [HttpPut("file")]
        [RequestSizeLimit(1_073_741_824)] // 1GB，可调整
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
        [RequestSizeLimit(100_000_000)] // 单个分块最大 100MB
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
                var (fileStream, fileSize, lastModifiedUtc) = await _fileStore.OpenFileForReadAsync(path);

                // 设置响应头
                Response.Headers.Append("Content-Length", fileSize.ToString());
                Response.Headers.Append("Last-Modified", lastModifiedUtc.ToString("r"));
                Response.Headers.Append("Accept-Ranges", "bytes");

                var fileName = Path.GetFileName(path);
                var contentType = "application/octet-stream";

                // 处理 Range 请求（断点续传下载）
                if (Request.Headers.ContainsKey("Range"))
                {
                    var rangeHeader = Request.Headers["Range"].ToString();
                    if (rangeHeader.StartsWith("bytes="))
                    {
                        var range = rangeHeader.Substring("bytes=".Length);
                        var parts = range.Split('-');
                        long start = long.Parse(parts[0]);
                        long end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                            ? long.Parse(parts[1])
                            : fileSize - 1;

                        if (start >= fileSize || end >= fileSize)
                        {
                            return StatusCode(416, new { error = "请求范围不满足" });
                        }

                        var length = end - start + 1;
                        fileStream.Seek(start, SeekOrigin.Begin);

                        Response.StatusCode = 206;
                        Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileSize}");
                        Response.Headers.Append("Content-Length", length.ToString());

                        return File(fileStream, contentType, enableRangeProcessing: true);
                    }
                }

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
    }
}
