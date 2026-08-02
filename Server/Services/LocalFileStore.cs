using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Services
{
    /// <summary>
    /// 本地文件系统实现（支持分块上传、断点续传、回收站软删除）
    /// </summary>
    public class LocalFileStore : IFileStore
    {
        private readonly string _rootPath;
        private readonly string _tempChunkFolder;
        private readonly long _maxFileSizeBytes;

        /// <summary>回收站目录名（软删除文件存放处）</summary>
        public const string TrashFolderName = ".trash";

        /// <summary>SQLite 文件索引（搜索加速）</summary>
        private readonly FileIndex _index;

        public LocalFileStore(string rootPath, string tempChunkFolder, long maxFileSizeBytes)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _tempChunkFolder = tempChunkFolder ?? "_chunks";
            _maxFileSizeBytes = maxFileSizeBytes;

            // 确保存储根目录存在
            Directory.CreateDirectory(_rootPath);

            // 初始化文件索引并后台全量构建（不阻塞启动）
            _index = new FileIndex(Path.Combine(_rootPath, "fileindex.db"));
            _index.Open();
            Task.Run(() => _index.Rebuild(_rootPath, IsExcludedPath, null));
        }

        private string GetSafeFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("路径不能为空", nameof(relativePath));

            // 防止路径遍历攻击
            relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                       .Replace('/', Path.DirectorySeparatorChar)
                                       .TrimStart(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("路径遍历攻击");
            return fullPath;
        }

        private string GetChunkDirectory(string relativePath)
        {
            // 临时分块目录：_rootPath/_tempChunkFolder/相对路径的目录部分
            var chunkRoot = Path.Combine(_rootPath, _tempChunkFolder);
            var relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            var chunkDir = Path.Combine(chunkRoot, relativeDir);
            Directory.CreateDirectory(chunkDir);
            return chunkDir;
        }

        private string GetChunkFilePath(string relativePath, int chunkIndex)
        {
            var chunkDir = GetChunkDirectory(relativePath);
            var fileName = $"{Path.GetFileName(relativePath)}.part_{chunkIndex}";
            return Path.Combine(chunkDir, fileName);
        }

        public Task<bool> FileExistsAsync(string relativePath)
        {
            try
            {
                var fullPath = GetSafeFullPath(relativePath);
                return Task.FromResult(File.Exists(fullPath));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<DateTime?> GetLastWriteTimeUtcAsync(string relativePath)
        {
            try
            {
                var fullPath = GetSafeFullPath(relativePath);
                if (!File.Exists(fullPath))
                    return Task.FromResult<DateTime?>(null);
                var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                return Task.FromResult<DateTime?>(lastWrite);
            }
            catch
            {
                return Task.FromResult<DateTime?>(null);
            }
        }

        public async Task WriteFileAsync(string relativePath, Stream fileStream)
        {
            // 尝试获取流长度（有些流不支持，如 HttpRequestStream）
            long? fileLength = null;
            try
            {
                fileLength = fileStream.Length;
            }
            catch (NotSupportedException) { /* 忽略，流不支持 Length */ }

            if (fileLength.HasValue && fileLength.Value > _maxFileSizeBytes)
                throw new IOException($"文件大小超过限制 ({_maxFileSizeBytes} 字节)");

            var fullPath = GetSafeFullPath(relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var destStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await fileStream.CopyToAsync(destStream);
            }

            // 增量更新索引
            var fi = new FileInfo(fullPath);
            _index.Upsert(relativePath.Replace('\\', '/'), fi.Name, fi.Length, fi.LastWriteTimeUtc);
        }

        public async Task DeleteFileAsync(string relativePath)
        {
            var fullPath = GetSafeFullPath(relativePath);
            if (!File.Exists(fullPath))
            {
                await Task.CompletedTask;
                return;
            }

            // 回收站内的文件：彻底删除（避免嵌套软删除）
            if (relativePath.StartsWith(TrashFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(fullPath);
                TryDeleteEmptyDirectories(Path.GetDirectoryName(fullPath));
                _index.Remove(relativePath.Replace('\\', '/'));
                await Task.CompletedTask;
                return;
            }

            // 普通文件：软删除，移动到回收站 .trash/ 目录，可恢复
            var trashPath = GetTrashPathFor(relativePath);
            var trashDir = Path.GetDirectoryName(trashPath);
            if (!string.IsNullOrEmpty(trashDir))
                Directory.CreateDirectory(trashDir);

            if (File.Exists(trashPath))
                File.Delete(trashPath);   // 回收站内同名则覆盖（保留最新）
            File.Move(fullPath, trashPath);
            _index.Remove(relativePath.Replace('\\', '/'));
            await Task.CompletedTask;
        }

        /// <summary>
        /// 计算回收站路径：.trash/原始相对路径；若已存在则文件名追加时间戳
        /// </summary>
        private string GetTrashPathFor(string relativePath)
        {
            var trashRoot = Path.Combine(_rootPath, TrashFolderName);
            var trashFull = Path.Combine(trashRoot, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(trashFull))
                return trashFull;

            // 重名冲突：在文件名后追加时间戳
            var dir = Path.GetDirectoryName(trashFull);
            var name = Path.GetFileNameWithoutExtension(trashFull);
            var ext = Path.GetExtension(trashFull);
            return Path.Combine(dir ?? trashRoot, $"{name}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
        }

        public Task<List<FileMetadata>> ListTrashAsync()
        {
            return Task.Run(() =>
            {
                var results = new List<FileMetadata>();
                var trashRoot = Path.Combine(_rootPath, TrashFolderName);
                var dir = new DirectoryInfo(trashRoot);
                if (!dir.Exists)
                    return results;

                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    results.Add(new FileMetadata
                    {
                        // Path 为相对存储根的回收站路径（如 ".trash/a/b.txt"），用于恢复
                        Path = file.FullName.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                        Name = file.Name,
                        SizeBytes = file.Length,
                        LastWriteTimeUtc = file.LastWriteTimeUtc,
                        IsDirectory = false
                    });
                }
                return results.OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            });
        }

        public Task RestoreFromTrashAsync(string trashRelativePath)
        {
            if (string.IsNullOrWhiteSpace(trashRelativePath))
                throw new ArgumentException("路径不能为空", nameof(trashRelativePath));

            // 仅允许恢复 .trash/ 下的文件，防止路径穿越
            trashRelativePath = trashRelativePath.Replace('\\', '/').TrimStart('/');
            if (!trashRelativePath.StartsWith(TrashFolderName + "/", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("只能恢复回收站中的文件");

            var trashFull = Path.GetFullPath(Path.Combine(_rootPath, trashRelativePath));
            if (!trashFull.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("路径遍历攻击");
            if (!File.Exists(trashFull))
                throw new FileNotFoundException("回收站中不存在该文件");

            // 还原到原始路径（去掉 .trash/ 前缀）
            var originalRelative = trashRelativePath.Substring(TrashFolderName.Length + 1);
            var targetFull = GetSafeFullPath(originalRelative);
            var targetDir = Path.GetDirectoryName(targetFull);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            if (File.Exists(targetFull))
                File.Delete(targetFull);   // 目标已存在则覆盖（保留恢复的最新版本）

            File.Move(trashFull, targetFull);

            // 索引：移除回收站记录，更新原位置记录
            _index.Remove(trashRelativePath.Replace('\\', '/'));
            if (File.Exists(targetFull))
            {
                var fi = new FileInfo(targetFull);
                _index.Upsert(originalRelative.Replace('\\', '/'), fi.Name, fi.Length, fi.LastWriteTimeUtc);
            }

            // 尝试清理回收站中的空目录
            TryDeleteEmptyDirectories(Path.GetDirectoryName(trashFull));
            return Task.CompletedTask;
        }

        public Task<int> ClearTrashAsync(TimeSpan olderThan)
        {
            return Task.Run(() =>
            {
                int cleared = 0;
                var trashRoot = Path.Combine(_rootPath, TrashFolderName);
                if (!Directory.Exists(trashRoot)) return 0;

                var cutoffTime = DateTime.UtcNow - olderThan;
                foreach (var file in Directory.EnumerateFiles(trashRoot, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoffTime)
                        {
                            File.Delete(file);
                            cleared++;
                        }
                    }
                    catch { }
                }
                TryDeleteEmptyDirectories(trashRoot);
                return cleared;
            });
        }

        /// <summary>
        /// 递归删除空目录（自底向上）
        /// </summary>
        private static void TryDeleteEmptyDirectories(string? startDir)
        {
            try
            {
                if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir)) return;
                foreach (var sub in Directory.EnumerateDirectories(startDir))
                    TryDeleteEmptyDirectories(sub);
                if (!Directory.EnumerateFileSystemEntries(startDir).Any())
                    Directory.Delete(startDir);
            }
            catch { }
        }

        /// <summary>
        /// 排除临时分块目录与回收站目录
        /// </summary>
        private bool IsExcludedPath(string fullPath)
        {
            var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string chunkMarker = Path.DirectorySeparatorChar + _tempChunkFolder + Path.DirectorySeparatorChar;
            string trashMarker = Path.DirectorySeparatorChar + TrashFolderName + Path.DirectorySeparatorChar;

            return normalized.IndexOf(chunkMarker, StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf(trashMarker, StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.EndsWith(Path.DirectorySeparatorChar + _tempChunkFolder, StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(Path.DirectorySeparatorChar + TrashFolderName, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<HashSet<int>> GetUploadedChunksAsync(string relativePath)
        {
            var chunkDir = GetChunkDirectory(relativePath);
            var baseName = Path.GetFileName(relativePath);
            var pattern = $"{baseName}.part_*";
            var chunkFiles = Directory.EnumerateFiles(chunkDir, pattern, SearchOption.TopDirectoryOnly);
            var indices = new HashSet<int>();
            foreach (var file in chunkFiles)
            {
                var fileName = Path.GetFileName(file);
                var suffix = fileName.Substring(fileName.LastIndexOf(".part_") + 6);
                if (int.TryParse(suffix, out int idx))
                    indices.Add(idx);
            }
            return await Task.FromResult(indices);
        }

        public async Task UploadChunkAsync(string relativePath, int chunkIndex, int totalChunks, Stream chunkStream)
        {
            // 大小限制由 [RequestSizeLimit] 在控制器层校验，此处不检查 stream.Length（HttpRequestStream 不支持 .Length）

            var chunkFilePath = GetChunkFilePath(relativePath, chunkIndex);
            using (var destStream = new FileStream(chunkFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await chunkStream.CopyToAsync(destStream);
            }
        }

        public async Task MergeChunksAsync(string relativePath, int totalChunks)
        {
            var fullTargetPath = GetSafeFullPath(relativePath);
            var directory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var targetStream = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var chunkFilePath = GetChunkFilePath(relativePath, i);
                    if (!File.Exists(chunkFilePath))
                        throw new FileNotFoundException($"分块 {i} 不存在，无法合并");

                    using (var chunkStream = new FileStream(chunkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
                    {
                        await chunkStream.CopyToAsync(targetStream);
                    }
                }
            }

            // 验证合并后文件大小不超过限制
            var finalInfo = new FileInfo(fullTargetPath);
            if (finalInfo.Exists && finalInfo.Length > _maxFileSizeBytes)
            {
                File.Delete(fullTargetPath);
                throw new IOException($"合并后文件大小 ({finalInfo.Length} 字节) 超过限制 ({_maxFileSizeBytes} 字节)");
            }

            // 合并完成：更新索引
            if (finalInfo.Exists)
                _index.Upsert(relativePath.Replace('\\', '/'), finalInfo.Name, finalInfo.Length, finalInfo.LastWriteTimeUtc);

            // 合并完成后删除临时分块文件
            for (int i = 0; i < totalChunks; i++)
            {
                try
                {
                    var chunkFilePath = GetChunkFilePath(relativePath, i);
                    if (File.Exists(chunkFilePath))
                        File.Delete(chunkFilePath);
                }
                catch { }
            }

            // 尝试删除空的临时目录（非必要）
            try
            {
                var chunkRoot = Path.Combine(_rootPath, _tempChunkFolder);
                if (Directory.Exists(chunkRoot) && !Directory.EnumerateFileSystemEntries(chunkRoot).Any())
                    Directory.Delete(chunkRoot);
            }
            catch { }
        }

        public Task<List<FileMetadata>> ListFilesAsync(string relativePath = "", bool recursive = false)
        {
            return Task.Run(() =>
            {
                var results = new List<FileMetadata>();
                try
                {
                    var searchPath = string.IsNullOrWhiteSpace(relativePath)
                        ? _rootPath
                        : GetSafeFullPath(relativePath);
                    var dir = new DirectoryInfo(searchPath);

                    if (!dir.Exists) return results;

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    foreach (var file in dir.EnumerateFiles("*", searchOption))
                    {
                        if (IsExcludedPath(file.FullName))
                            continue;
                        results.Add(new FileMetadata
                        {
                            Path = file.FullName.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                            Name = file.Name, SizeBytes = file.Length,
                            LastWriteTimeUtc = file.LastWriteTimeUtc, IsDirectory = false
                        });
                    }

                    if (!recursive)
                    {
                        foreach (var subDir in dir.EnumerateDirectories())
                            results.Add(new FileMetadata
                            {
                                Path = subDir.FullName.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/') + "/",
                                Name = subDir.Name, SizeBytes = 0,
                                LastWriteTimeUtc = subDir.LastWriteTimeUtc, IsDirectory = true
                            });
                    }
                }
                catch (UnauthorizedAccessException) { }

                return results
                    .OrderByDescending(f => f.IsDirectory)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
        }

        public Task<(Stream fileStream, long fileSize, DateTime lastModifiedUtc)> OpenFileForReadAsync(string relativePath)
        {
            var fullPath = GetSafeFullPath(relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"文件不存在: {relativePath}");

            var fileInfo = new FileInfo(fullPath);
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return Task.FromResult(((Stream)stream, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        public Task<StorageStats> GetStatsAsync()
        {
            return Task.Run(() =>
            {
                var stats = new StorageStats();
                try
                {
                    var rootDir = new DirectoryInfo(_rootPath);
                    if (rootDir.Exists)
                    {
                        foreach (var file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            if (IsExcludedPath(file.FullName))
                                continue;
                            stats.TotalFiles++;
                            stats.TotalSizeBytes += file.Length;
                        }
                    }

                    var driveRoot = Path.GetPathRoot(_rootPath);
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        var rootDrive = new DriveInfo(driveRoot);
                        stats.AvailableDiskBytes = rootDrive.AvailableFreeSpace;
                        stats.TotalDiskBytes = rootDrive.TotalSize;
                    }

                    var chunkRoot = Path.Combine(_rootPath, _tempChunkFolder);
                    if (Directory.Exists(chunkRoot))
                        stats.PendingChunks = Directory.EnumerateFiles(chunkRoot, "*.part_*", SearchOption.AllDirectories).Count();
                }
                catch { }
                return stats;
            });
        }

        public Task<int> CleanupStaleChunksAsync(TimeSpan olderThan)
        {
            return Task.Run(() =>
            {
                int cleaned = 0;
                try
                {
                    var chunkRoot = Path.Combine(_rootPath, _tempChunkFolder);
                    if (!Directory.Exists(chunkRoot)) return 0;

                    var cutoffTime = DateTime.UtcNow - olderThan;
                    var chunkFiles = Directory.EnumerateFiles(chunkRoot, "*.part_*", SearchOption.AllDirectories);

                    foreach (var file in chunkFiles)
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(file) < cutoffTime) { File.Delete(file); cleaned++; }
                        }
                        catch { }
                    }

                    foreach (var dir in Directory.EnumerateDirectories(chunkRoot, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                    {
                        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                        catch { }
                    }
                }
                catch { }
                return cleaned;
            });
        }

        public Task<SearchResult> SearchFilesAsync(
            string keyword = "", string extension = "",
            DateTime? startDate = null, DateTime? endDate = null,
            bool recursive = true, int page = 1, int pageSize = 100)
        {
            // 索引就绪时优先使用 SQLite 索引查询（性能远高于全盘扫描）
            if (_index.IsReady)
            {
                var result = _index.Search(keyword, extension, startDate, endDate, page, pageSize);
                if (result.HasValue)
                {
                    return Task.FromResult(new SearchResult
                    {
                        Total = result.Value.total,
                        Page = page,
                        PageSize = pageSize,
                        Items = result.Value.items
                    });
                }
            }

            // 索引不可用时回退文件系统扫描
            return Task.Run(() =>
            {
                var allResults = new List<FileMetadata>();
                try
                {
                    var rootDir = new DirectoryInfo(_rootPath);
                    if (!rootDir.Exists) return new SearchResult();

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    foreach (var file in rootDir.EnumerateFiles("*", searchOption))
                    {
                        if (IsExcludedPath(file.FullName))
                            continue;
                        if (!string.IsNullOrWhiteSpace(keyword) && file.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (!string.IsNullOrWhiteSpace(extension))
                        {
                            var ext = extension.StartsWith(".") ? extension : "." + extension;
                            if (!file.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        if (startDate.HasValue && file.LastWriteTimeUtc < startDate.Value) continue;
                        if (endDate.HasValue && file.LastWriteTimeUtc > endDate.Value) continue;

                        allResults.Add(new FileMetadata
                        {
                            Path = file.FullName.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                            Name = file.Name, SizeBytes = file.Length,
                            LastWriteTimeUtc = file.LastWriteTimeUtc, IsDirectory = false
                        });
                    }

                    allResults = allResults.OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                    var total = allResults.Count;
                    return new SearchResult
                    {
                        Total = total, Page = page, PageSize = pageSize,
                        Items = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList()
                    };
                }
                catch (UnauthorizedAccessException) { }
                return new SearchResult();
            });
        }
    }
}