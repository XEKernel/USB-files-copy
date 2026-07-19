using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Services
{
    /// <summary>
    /// 本地文件系统实现（支持分块上传、断点续传）
    /// </summary>
    public class LocalFileStore : IFileStore
    {
        private readonly string _rootPath;
        private readonly string _tempChunkFolder;
        private readonly long _maxFileSizeBytes;

        public LocalFileStore(string rootPath, string tempChunkFolder, long maxFileSizeBytes)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _tempChunkFolder = tempChunkFolder ?? "_chunks";
            _maxFileSizeBytes = maxFileSizeBytes;

            // 确保存储根目录存在
            Directory.CreateDirectory(_rootPath);
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
        }

        public Task DeleteFileAsync(string relativePath)
        {
            try
            {
                var fullPath = GetSafeFullPath(relativePath);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch { /* 忽略删除失败 */ }
            return Task.CompletedTask;
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
                        if (file.FullName.Contains(Path.DirectorySeparatorChar + _tempChunkFolder + Path.DirectorySeparatorChar))
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
                            if (file.FullName.Contains(Path.DirectorySeparatorChar + _tempChunkFolder + Path.DirectorySeparatorChar))
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
                        if (file.FullName.Contains(Path.DirectorySeparatorChar + _tempChunkFolder + Path.DirectorySeparatorChar))
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