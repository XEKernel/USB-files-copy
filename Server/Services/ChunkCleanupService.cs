using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Services
{
    /// <summary>
    /// 临时分块自动清理服务：按配置周期清理超时的未完成分块上传，
    /// 替代手动调用 POST /api/file/cleanup
    /// </summary>
    public class ChunkCleanupService : BackgroundService
    {
        private readonly IFileStore _fileStore;
        private readonly ILogger<ChunkCleanupService> _logger;
        private readonly ChunkCleanupOptions _options;

        public ChunkCleanupService(IFileStore fileStore, ILogger<ChunkCleanupService> logger, IOptions<ChunkCleanupOptions> options)
        {
            _fileStore = fileStore;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("分块清理服务已启动（周期 {IntervalHours} 小时，清理早于 {MaxAgeHours} 小时的分块）",
                _options.IntervalHours, _options.MaxAgeHours);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 等待一个周期（首次启动也先等待，避免与启动争抢 IO）
                    await Task.Delay(TimeSpan.FromHours(_options.IntervalHours), stoppingToken);

                    var cleaned = await _fileStore.CleanupStaleChunksAsync(TimeSpan.FromHours(_options.MaxAgeHours));
                    if (cleaned > 0)
                        _logger.LogInformation("自动清理过期分块：{Count} 个", cleaned);

                    // 顺带清理 30 天前的回收站文件
                    var trashCleared = await _fileStore.ClearTrashAsync(TimeSpan.FromDays(30));
                    if (trashCleared > 0)
                        _logger.LogInformation("自动清理过期回收站文件：{Count} 个", trashCleared);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自动清理分块失败，将在下个周期重试");
                }
            }
        }
    }

    /// <summary>
    /// 分块清理配置（appsettings 的 Cleanup 节）
    /// </summary>
    public class ChunkCleanupOptions
    {
        /// <summary>清理周期（小时），默认 24</summary>
        public int IntervalHours { get; set; } = 24;

        /// <summary>分块最大保留时长（小时），默认 24</summary>
        public int MaxAgeHours { get; set; } = 24;
    }
}
