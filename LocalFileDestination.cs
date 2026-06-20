// LocalFileDestination.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制
{
    /// <summary>
    /// 本地文件系统实现
    /// </summary>
    public class LocalFileDestination : IFileDestination
    {
        private readonly string _rootDirectory;

        /// <summary>
        /// 初始化本地目标
        /// </summary>
        public LocalFileDestination(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("根目录不能为空", nameof(rootDirectory));

            _rootDirectory = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(_rootDirectory);
        }

        public string DestinationType => "本地存储";

        public IProgress<(string filePath, long bytesTransferred, long totalBytes)> Progress { get; set; }

        private string GetFullPath(string relativePath)
        {
            relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                       .Replace('/', Path.DirectorySeparatorChar)
                                       .TrimStart(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));

            if (!fullPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"路径遍历攻击: {relativePath}");

            return fullPath;
        }

        public async Task WriteFileAsync(string relativePath, Stream fileStream, CancellationToken cancellationToken)
        {
            if (fileStream == null)
                throw new ArgumentNullException(nameof(fileStream));

            var fullPath = GetFullPath(relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await fileStream.CopyToAsync(file, 81920, cancellationToken);
            }
        }

        public Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            var fullPath = GetFullPath(relativePath);
            return Task.FromResult(File.Exists(fullPath));
        }

        public Task<DateTime> GetFileLastWriteTimeUtcAsync(string relativePath, CancellationToken cancellationToken)
        {
            var fullPath = GetFullPath(relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"文件不存在: {relativePath}");

            var lastWrite = File.GetLastWriteTimeUtc(fullPath);
            return Task.FromResult(lastWrite);
        }

        public Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
        {
            var fullPath = GetFullPath(relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            return Task.CompletedTask;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            // 本地存储始终可用
            return Task.FromResult(Directory.Exists(_rootDirectory));
        }

        public Task<List<FileMetadataInfo>> ListFilesAsync(string relativePath, bool recursive, CancellationToken cancellationToken)
        {
            var results = new List<FileMetadataInfo>();
            try
            {
                var searchPath = string.IsNullOrWhiteSpace(relativePath)
                    ? _rootDirectory
                    : GetFullPath(relativePath);
                var dir = new DirectoryInfo(searchPath);

                if (!dir.Exists)
                    return Task.FromResult(results);

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (var file in dir.EnumerateFiles("*", searchOption))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relPath = file.FullName
                        .Substring(_rootDirectory.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace('\\', '/');

                    results.Add(new FileMetadataInfo
                    {
                        Path = relPath,
                        Name = file.Name,
                        SizeBytes = file.Length,
                        LastWriteTimeUtc = file.LastWriteTimeUtc,
                        IsDirectory = false
                    });
                }

                if (!recursive)
                {
                    foreach (var subDir in dir.EnumerateDirectories())
                    {
                        var relPath = subDir.FullName
                            .Substring(_rootDirectory.Length)
                            .TrimStart(Path.DirectorySeparatorChar)
                            .Replace('\\', '/');

                        results.Add(new FileMetadataInfo
                        {
                            Path = relPath + "/",
                            Name = subDir.Name,
                            SizeBytes = 0,
                            LastWriteTimeUtc = subDir.LastWriteTimeUtc,
                            IsDirectory = true
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException) { }

            return Task.FromResult(results);
        }

        public Task<StorageStatsInfo> GetStatsAsync(CancellationToken cancellationToken)
        {
            var stats = new StorageStatsInfo();
            try
            {
                var rootDir = new DirectoryInfo(_rootDirectory);
                if (rootDir.Exists)
                {
                    foreach (var file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        stats.TotalFiles++;
                        stats.TotalSizeBytes += file.Length;
                    }
                }

                var rootPath = Path.GetPathRoot(_rootDirectory);
                if (!string.IsNullOrEmpty(rootPath))
                {
                    var drive = new DriveInfo(rootPath);
                    stats.AvailableDiskBytes = drive.AvailableFreeSpace;
                    stats.TotalDiskBytes = drive.TotalSize;
                }
            }
            catch { }

            return Task.FromResult(stats);
        }

        public Task<SearchResultInfo> SearchFilesAsync(
            string keyword, string extension,
            DateTime? startDate, DateTime? endDate,
            bool recursive, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            var allResults = new List<FileMetadataInfo>();
            try
            {
                var rootDir = new DirectoryInfo(_rootDirectory);
                if (!rootDir.Exists)
                    return Task.FromResult(new SearchResultInfo());

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (var file in rootDir.EnumerateFiles("*", searchOption))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.IsNullOrWhiteSpace(keyword) &&
                        file.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        var ext = extension.StartsWith(".") ? extension : "." + extension;
                        if (!file.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (startDate.HasValue && file.LastWriteTimeUtc < startDate.Value)
                        continue;
                    if (endDate.HasValue && file.LastWriteTimeUtc > endDate.Value)
                        continue;

                    var relPath = file.FullName
                        .Substring(_rootDirectory.Length)
                        .TrimStart(Path.DirectorySeparatorChar)
                        .Replace('\\', '/');

                    allResults.Add(new FileMetadataInfo
                    {
                        Path = relPath,
                        Name = file.Name,
                        SizeBytes = file.Length,
                        LastWriteTimeUtc = file.LastWriteTimeUtc,
                        IsDirectory = false
                    });
                }

                allResults = allResults.OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                var total = allResults.Count;
                var paged = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                return Task.FromResult(new SearchResultInfo
                {
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                    Items = paged
                });
            }
            catch (UnauthorizedAccessException) { }

            return Task.FromResult(new SearchResultInfo());
        }
    }
}