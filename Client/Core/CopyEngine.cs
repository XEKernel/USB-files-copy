using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 文件复制引擎（纯后端，零 UI 依赖）。
    /// 通过 Log / CountsChanged 事件向界面层报告进度。
    /// </summary>
    public class CopyEngine
    {
        /// <summary>日志事件：(message, isError)</summary>
        public event Action<string, bool> Log;

        /// <summary>计数变化事件：(totalFiles, successCount, failureCount)</summary>
        public event Action<int, int, int> CountsChanged;

        private const int NormalBufferSize = 81920; // 80KB
        private const int LimitedBufferSize = 4096; // 4KB

        private int _totalFiles;
        private int _successCount;
        private int _failureCount;

        private readonly Dictionary<string, DateTime> _driveInsertionTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, string> _driveIdCache = new Dictionary<string, string>();

        /// <summary>
        /// 记录驱动器插入时间（用于限速窗口判断）
        /// </summary>
        public void RecordDriveInsertion(DriveInfo drive)
        {
            string driveId = GetDriveId(drive);
            if (!_driveInsertionTimes.ContainsKey(driveId))
                _driveInsertionTimes[driveId] = DateTime.Now;
        }

        /// <summary>
        /// 重置统计计数（每次新复制任务开始时调用）
        /// </summary>
        public void ResetCounters()
        {
            _totalFiles = 0;
            _successCount = 0;
            _failureCount = 0;
            RaiseCountsChanged();
        }

        /// <summary>
        /// 获取可移动驱动器列表
        /// </summary>
        public IEnumerable<DriveInfo> GetRemovableDrives()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                bool isValidDrive = false;
                try
                {
                    isValidDrive = drive.DriveType == DriveType.Removable && drive.IsReady;
                }
                catch (IOException ex)
                {
                    RaiseLog($"驱动器访问失败：{drive.Name} - {ex.Message}", true);
                }

                if (isValidDrive)
                {
                    yield return drive;
                }
            }
        }

        /// <summary>
        /// 获取驱动器卷序列号（WMI，带缓存）
        /// </summary>
        public string GetDriveId(DriveInfo drive)
        {
            string key = drive.Name.TrimEnd('\\');
            if (_driveIdCache.TryGetValue(key, out var cached))
                return cached;

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = '{key}'"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        var id = disk["VolumeSerialNumber"]?.ToString() ?? "UNKNOWN";
                        _driveIdCache[key] = id;
                        return id;
                    }
                }
            }
            catch { }
            return "UNKNOWN";
        }

        /// <summary>
        /// 检查 U 盘是否包含特殊文件（停止复制 / 反向复制标记）
        /// </summary>
        public bool ContainsSpecialFile(DriveInfo drive, CopyOptions options, out SpecialFileAction action)
        {
            action = SpecialFileAction.None;

            try
            {
                if (options.StopCopyWhenFileExists)
                {
                    string stopFilePath = Path.Combine(drive.RootDirectory.FullName, options.StopCopyFileName);
                    if (File.Exists(stopFilePath))
                    {
                        action = SpecialFileAction.StopCopy;
                        return true;
                    }
                }

                if (options.ReverseCopyWhenFileExists)
                {
                    string reverseFilePath = Path.Combine(drive.RootDirectory.FullName, options.ReverseCopyFileName);
                    if (File.Exists(reverseFilePath))
                    {
                        action = SpecialFileAction.ReverseCopy;
                        return true;
                    }
                }

                // 历史反向复制标记（兼容旧版）
                string legacyPath = Path.Combine(drive.RootDirectory.FullName, options.ReverseCopyIndicator);
                if (File.Exists(legacyPath))
                {
                    action = SpecialFileAction.ReverseCopy;
                    return true;
                }
            }
            catch (Exception ex)
            {
                RaiseLog($"检查特殊文件时出错：{ex.Message}", true);
            }

            return false;
        }

        /// <summary>
        /// 复制单个可移动驱动器到指定存储目标
        /// </summary>
        public async Task CopyDriveAsync(DriveInfo drive, IFileDestination destination, CopyOptions options, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RaiseLog($"发现U盘：{drive.Name}", true);

            // 记录目录树（仅本地存储支持）
            if (destination is LocalFileDestination && options.CreateDirectoryTree)
                await RecordDirectoryTree(drive, options);

            // 特殊文件检查：停止复制则跳过（反向复制由调用方处理）
            if (ContainsSpecialFile(drive, options, out var actionType))
            {
                if (actionType == SpecialFileAction.StopCopy)
                {
                    RaiseLog($"检测到阻止复制文件，跳过该U盘：{drive.Name}", true);
                    return;
                }
            }

            // 相对根路径：统一使用文件夹名（本地模式 root 为目标目录，远程模式 root 为服务器存储根）
            string folderName = CreateDriveFolderName(drive, options);
            await CopyDirectoryAsync(drive.RootDirectory, folderName, options, destination, false, 0, ct);
        }

        /// <summary>
        /// 反向复制（从本地备份到 U 盘）
        /// </summary>
        public async Task ReverseCopyAsync(DriveInfo usbDrive, CopyOptions options, CancellationToken ct)
        {
            try
            {
                RaiseLog($"检测到反向复制标记，开始反向复制...", true);

                string usbRoot = usbDrive.RootDirectory.FullName;
                string localRoot = GetLocalBackupPath(usbDrive, options);

                if (!Directory.Exists(localRoot))
                {
                    RaiseLog($"找不到对应的本地备份目录：{localRoot}", true);
                    return;
                }

                string markerPath = Path.Combine(usbRoot, options.ReverseCopyMarker);
                if (File.Exists(markerPath))
                {
                    RaiseLog($"该U盘已完成反向复制（检测到标记文件 {options.ReverseCopyMarker}）", true);
                    return;
                }

                try
                {
                    File.WriteAllText(markerPath, $"反向复制完成于: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    RaiseLog($"创建标记文件失败: {ex.Message}", true);
                }

                // 反向复制时，目标为本地U盘，使用 LocalFileDestination
                var localDestination = new LocalFileDestination(usbRoot);
                await CopyDirectoryAsync(new DirectoryInfo(localRoot), "", options, localDestination, true, 0, ct);

                RaiseLog($"反向复制完成：{localRoot} -> {usbRoot}", true);
            }
            catch (Exception ex)
            {
                RaiseLog($"反向复制失败：{ex.Message}", true);
            }
        }

        /// <summary>
        /// 异步复制目录（递归）
        /// </summary>
        private async Task CopyDirectoryAsync(
            DirectoryInfo source,
            string relativeTargetDir,
            CopyOptions options,
            IFileDestination destination,
            bool isReverseCopy,
            int currentDepth,
            CancellationToken ct)
        {
            try
            {
                await CopyFilesWithPatternsAsync(source, relativeTargetDir, options, destination, isReverseCopy, ct);
                await ProcessSubdirectoriesAsync(source, relativeTargetDir, options, destination, isReverseCopy, currentDepth, ct);
            }
            catch (Exception ex)
            {
                RaiseLog($"目录处理失败: {source.FullName} | 错误：{ex.Message}", true);
            }
        }

        private async Task CopyFilesWithPatternsAsync(
            DirectoryInfo source,
            string relativeDirPath,
            CopyOptions options,
            IFileDestination destination,
            bool isReverseCopy,
            CancellationToken ct)
        {
            foreach (var pattern in options.SearchPatterns)
            {
                try
                {
                    var files = source.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        await CopySingleFileAsync(file, relativeDirPath, options, destination, isReverseCopy, ct);
                    }
                }
                catch (Exception ex)
                {
                    RaiseLog($"文件处理失败: {pattern} | 错误：{ex.Message}", true);
                }
            }
        }

        private async Task ProcessSubdirectoriesAsync(
            DirectoryInfo source,
            string relativeParentPath,
            CopyOptions options,
            IFileDestination destination,
            bool isReverseCopy,
            int currentDepth,
            CancellationToken ct)
        {
            if (options.LimitDirectoryDepth && currentDepth >= options.MaxDirectoryDepth)
            {
                RaiseLog($"达到目录深度限制({options.MaxDirectoryDepth})，停止遍历: {source.FullName}", false);
                return;
            }

            foreach (var dir in source.EnumerateDirectories())
            {
                try
                {
                    if (FileCategories.SystemDirectories.Contains(dir.Name) ||
                        (dir.Attributes & (FileAttributes.System | FileAttributes.Hidden)) != 0)
                    {
                        RaiseLog($"跳过系统目录：{dir.FullName}", false);
                        continue;
                    }

                    if (!PassFolderNameFilter(dir.Name, options))
                    {
                        RaiseLog($"跳过不包含关键词的文件夹：{dir.FullName}", false);
                        continue;
                    }

                    ct.ThrowIfCancellationRequested();
                    string childRelative = string.IsNullOrEmpty(relativeParentPath)
                        ? dir.Name
                        : Path.Combine(relativeParentPath, dir.Name).Replace('\\', '/');
                    await CopyDirectoryAsync(dir, childRelative, options, destination, isReverseCopy, currentDepth + 1, ct);
                }
                catch (UnauthorizedAccessException)
                {
                    RaiseLog($"目录访问被拒绝：{dir.FullName}", true);
                }
            }
        }

        private async Task CopySingleFileAsync(
            FileInfo file,
            string relativeDirPath,
            CopyOptions options,
            IFileDestination destination,
            bool isReverseCopy,
            CancellationToken ct)
        {
            const int maxRetries = 3;
            int attempt = 0;
            TimeSpan delay = TimeSpan.FromSeconds(1);
            bool success = false;

            // 构建相对路径
            string relativeFilePath = string.IsNullOrEmpty(relativeDirPath)
                ? file.Name
                : Path.Combine(relativeDirPath, file.Name).Replace('\\', '/');

            if (ShouldSkipFile(file, options, isReverseCopy))
            {
                RaiseLog($"跳过文件：{file.FullName}", false);
                return;
            }

            if (!PassFileNameFilter(file.Name, options))
            {
                RaiseLog($"文件名不包含关键词：{file.FullName}", false);
                return;
            }

            if (options.EnableSizeLimit && file.Length > options.MaxSizeBytes)
            {
                Interlocked.Increment(ref _totalFiles);
                Interlocked.Increment(ref _failureCount);
                RaiseLog($"文件过大已跳过：{file.FullName}", true);
                RaiseCountsChanged();
                return;
            }

            string driveId = GetDriveId(new DriveInfo(Path.GetPathRoot(file.FullName)));
            bool limitSpeed = ShouldLimitSpeed(driveId, options);
            int bufferSize = limitSpeed ? LimitedBufferSize : NormalBufferSize;
            int speedLimit = limitSpeed ? options.SpeedLimitBytesPerSecond : int.MaxValue;

            if (limitSpeed && attempt == 0)
            {
                RaiseLog($"限速模式复制：{file.FullName} (速度限制: {speedLimit / 1024 / 1024} MB/秒, 前{options.SpeedLimitMinutes}分钟)", false);
            }

            while (attempt <= maxRetries && !success)
            {
                try
                {
                    bool remoteExists = await destination.FileExistsAsync(relativeFilePath, ct);
                    DateTime? remoteLastWrite = null;
                    if (remoteExists)
                    {
                        try
                        {
                            remoteLastWrite = await destination.GetFileLastWriteTimeUtcAsync(relativeFilePath, ct);
                        }
                        catch (NotSupportedException) { /* 服务器不支持 Last-Modified，忽略 */ }
                        catch (FileNotFoundException) { remoteExists = false; }
                    }

                    var action = options.DuplicateAction;

                    if (remoteExists)
                    {
                        switch (action)
                        {
                            case DuplicateFileAction.Skip:
                                RaiseLog($"跳过已存在文件：{file.FullName}", false);
                                return;
                            case DuplicateFileAction.Overwrite:
                                await destination.DeleteFileAsync(relativeFilePath, ct);
                                remoteExists = false;
                                break;
                            case DuplicateFileAction.KeepBoth:
                                string ext = Path.GetExtension(relativeFilePath);
                                string nameWithoutExt = relativeFilePath.Substring(0, relativeFilePath.Length - ext.Length);
                                int counter = 1;
                                string newPath;
                                do
                                {
                                    newPath = $"{nameWithoutExt} ({counter++}){ext}";
                                } while (await destination.FileExistsAsync(newPath, ct));
                                relativeFilePath = newPath;
                                break;
                            case DuplicateFileAction.ReplaceWithNewer:
                                if (remoteLastWrite.HasValue && file.LastWriteTimeUtc <= remoteLastWrite.Value)
                                {
                                    RaiseLog($"远程文件版本较新或相同，跳过：{file.FullName}", false);
                                    return;
                                }
                                await destination.DeleteFileAsync(relativeFilePath, ct);
                                remoteExists = false;
                                break;
                        }
                    }

                    // 复制文件内容
                    using (var sourceStream = new FileStream(
                        file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous))
                    {
                        if (limitSpeed)
                        {
                            using (var throttled = new ThrottledStream(sourceStream, speedLimit))
                            {
                                await destination.WriteFileAsync(relativeFilePath, throttled, ct);
                            }
                        }
                        else
                        {
                            await destination.WriteFileAsync(relativeFilePath, sourceStream, ct);
                        }
                    }

                    RaiseLog($"成功复制：{file.FullName} -> {relativeFilePath}", false);
                    Interlocked.Increment(ref _successCount);
                    success = true;
                }
                catch (Exception ex) when (IsTransientError(ex, ct) && attempt < maxRetries)
                {
                    attempt++;
                    RaiseLog($"复制失败（将重试 {attempt}/{maxRetries}）：{file.FullName} | 错误：{ex.Message}", true);
                    await Task.Delay(TimeSpan.FromSeconds(delay.TotalSeconds * attempt), ct);
                }
                catch (Exception ex)
                {
                    RaiseLog($"复制失败：{file.FullName} | 错误：{ex.Message}", true);
                    Interlocked.Increment(ref _failureCount);
                    break;
                }
                finally
                {
                    if (attempt >= maxRetries || success)
                    {
                        Interlocked.Increment(ref _totalFiles);
                        RaiseCountsChanged();
                    }
                }
            }
        }

        private bool ShouldSkipFile(FileInfo file, CopyOptions options, bool isReverseCopy)
        {
            if (FileCategories.SystemFiles.Contains(file.Name))
                return true;

            if (isReverseCopy && file.Name == options.ReverseCopyMarker)
                return true;

            if (!isReverseCopy && file.Name == options.ReverseCopyIndicator)
                return true;

            return (file.Attributes & (FileAttributes.System | FileAttributes.Hidden)) != 0;
        }

        private bool ShouldLimitSpeed(string driveId, CopyOptions options)
        {
            if (!options.EnableSpeedLimit) return false;

            if (_driveInsertionTimes.TryGetValue(driveId, out DateTime insertionTime))
            {
                return (DateTime.Now - insertionTime).TotalMinutes < options.SpeedLimitMinutes;
            }

            return false;
        }

        private bool IsTransientError(Exception ex, CancellationToken ct)
        {
            return ex is IOException ||
                   ex is UnauthorizedAccessException ||
                   (ex is OperationCanceledException && !ct.IsCancellationRequested);
        }

        private bool PassFileNameFilter(string fileName, CopyOptions options)
        {
            if (!options.FilterByFileName)
                return true;

            if (options.FileNameKeywords.Count == 0)
                return false;

            return options.FileNameKeywords.Any(kw =>
                fileName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool PassFolderNameFilter(string folderName, CopyOptions options)
        {
            if (!options.FilterByFolderName)
                return true;

            if (options.FolderNameKeywords.Count == 0)
                return false;

            return options.FolderNameKeywords.Any(kw =>
                folderName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private async Task RecordDirectoryTree(DriveInfo drive, CopyOptions options)
        {
            try
            {
                string driveId = GetDriveId(drive);
                string treeFilePath = Path.Combine(options.TargetDirectory,
                    $"{SanitizeFolderName(drive.VolumeLabel ?? drive.Name.Replace(":\\", ""))}_目录树_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                await Task.Run(() =>
                {
                    using (var writer = new StreamWriter(treeFilePath, false, Encoding.UTF8))
                    {
                        writer.WriteLine($"U盘目录树 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"驱动器: {drive.Name}");
                        writer.WriteLine($"卷标: {drive.VolumeLabel ?? "无"}");
                        writer.WriteLine($"总空间: {drive.TotalSize / (1024 * 1024 * 1024.0):F2} GB");
                        writer.WriteLine($"可用空间: {drive.AvailableFreeSpace / (1024 * 1024 * 1024.0):F2} GB");
                        writer.WriteLine($"目录深度限制: {(options.LimitDirectoryDepth ? options.MaxDirectoryDepth.ToString() : "无限制")}");
                        writer.WriteLine();
                        writer.WriteLine("目录结构:");
                        writer.WriteLine();

                        WriteDirectoryTree(writer, drive.RootDirectory.FullName, 0, options);
                    }
                });

                RaiseLog($"已生成目录树文件: {treeFilePath}", true);
            }
            catch (Exception ex)
            {
                RaiseLog($"生成目录树失败: {ex.Message}", true);
            }
        }

        private void WriteDirectoryTree(StreamWriter writer, string path, int level, CopyOptions options)
        {
            if (level > options.MaxDirectoryDepth) return;

            try
            {
                string indent = new string(' ', level * 2);
                string dirName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(dirName)) dirName = path;
                writer.WriteLine($"{indent}├── {dirName}/");

                if (options.FilterByFolderName && options.FolderNameKeywords.Count > 0)
                {
                    if (!options.FolderNameKeywords.Any(kw =>
                        dirName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return;
                    }
                }

                var directories = Directory.GetDirectories(path);
                foreach (var dir in directories)
                {
                    if (FileCategories.SystemDirectories.Contains(Path.GetFileName(dir)))
                        continue;
                    WriteDirectoryTree(writer, dir, level + 1, options);
                }

                var files = Directory.GetFiles(path);
                foreach (var file in files)
                {
                    if (FileCategories.SystemFiles.Contains(Path.GetFileName(file)))
                        continue;
                    var fileInfo = new FileInfo(file);
                    string fileName = Path.GetFileName(file);
                    writer.WriteLine($"{indent}│   ├── {fileName} ({fileInfo.Length / 1024.0:F1} KB)");
                }
            }
            catch (UnauthorizedAccessException)
            {
                writer.WriteLine($"{new string(' ', level * 2)}│   └── [访问被拒绝]");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"{new string(' ', level * 2)}│   └── [错误: {ex.Message}]");
            }
        }

        private string CreateDriveFolderName(DriveInfo drive, CopyOptions options)
        {
            string volumeSerial = GetDriveId(drive);
            string folderName = SanitizeFolderName(
                !string.IsNullOrWhiteSpace(drive.VolumeLabel) ?
                $"{drive.VolumeLabel}_{volumeSerial}" :
                $"{drive.Name.Replace(":\\", "")}_{volumeSerial}");

            return folderName;
        }

        private string GetLocalBackupPath(DriveInfo drive, CopyOptions options)
        {
            string folderName = SanitizeFolderName(!string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.VolumeLabel
                : $"{drive.Name.Replace(":\\", "")}_DRIVE");

            return Path.Combine(options.TargetDirectory, folderName);
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "UNKNOWN_DRIVE";

            var invalidChars = Path.GetInvalidFileNameChars();
            var cleanName = new string(name
                .Where(c => !invalidChars.Contains(c) || c == '_' || c == '-')
                .ToArray())
                .Trim();

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

            if (reservedNames.Contains(cleanName))
            {
                cleanName += "_DATA";
            }

            return cleanName.Length > 50
                ? $"{cleanName.Substring(0, 45)}~"
                : cleanName;
        }

        private void RaiseLog(string message, bool isError)
        {
            Log?.Invoke(message, isError);
        }

        private void RaiseCountsChanged()
        {
            int total = Interlocked.CompareExchange(ref _totalFiles, 0, 0);
            int success = Interlocked.CompareExchange(ref _successCount, 0, 0);
            int failure = Interlocked.CompareExchange(ref _failureCount, 0, 0);
            CountsChanged?.Invoke(total, success, failure);
        }

        /// <summary>
        /// 限速流包装器：限制底层流的读取速率
        /// </summary>
        private sealed class ThrottledStream : Stream
        {
            private readonly Stream _inner;
            private readonly int _bytesPerSecond;
            private readonly Stopwatch _timer = new Stopwatch();
            private long _bytesThisSecond;

            public ThrottledStream(Stream inner, int bytesPerSecond)
            {
                _inner = inner;
                _bytesPerSecond = bytesPerSecond;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                int maxRead = Math.Min(count, _bytesPerSecond);
                if (!_timer.IsRunning) _timer.Restart();

                if (_bytesThisSecond >= _bytesPerSecond && _timer.ElapsedMilliseconds < 1000)
                {
                    int delay = 1000 - (int)_timer.ElapsedMilliseconds;
                    if (delay > 0) await Task.Delay(delay, ct);
                    _timer.Restart();
                    _bytesThisSecond = 0;
                }
                else if (_timer.ElapsedMilliseconds >= 1000)
                {
                    _timer.Restart();
                    _bytesThisSecond = 0;
                }

                int actualRead = await _inner.ReadAsync(buffer, offset, Math.Min(maxRead, count), ct);
                _bytesThisSecond += actualRead;
                return actualRead;
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
