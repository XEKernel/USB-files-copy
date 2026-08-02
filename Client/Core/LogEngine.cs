using System;
using System.IO;
using System.Text;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 日志引擎（纯后端）。过滤规则、文件写入在此处理；
    /// 窗口显示通过 LineAppended 事件交由界面层完成。
    /// </summary>
    public class LogEngine
    {
        /// <summary>窗口内保留的最大日志行数</summary>
        public const int MaxLogLines = 900;

        /// <summary>单个日志文件最大字节数，超过则自动轮转（默认 5MB）</summary>
        public long MaxLogFileSizeBytes { get; set; } = 5 * 1024 * 1024;

        private readonly StringBuilder _logBuffer = new StringBuilder();
        private int _currentLogLines;

        /// <summary>是否记录"成功"类消息</summary>
        public bool LogSuccess { get; set; } = true;

        /// <summary>是否记录错误消息</summary>
        public bool LogErrors { get; set; } = true;

        /// <summary>是否记录普通消息</summary>
        public bool LogNeutral { get; set; } = true;

        /// <summary>是否写入日志文件</summary>
        public bool SaveToFile { get; set; } = true;

        /// <summary>是否在窗口显示</summary>
        public bool ShowInWindow { get; set; } = true;

        /// <summary>日志文件路径（空则不写文件）</summary>
        public string LogFilePath { get; set; }

        /// <summary>日志行事件（供界面层显示）</summary>
        public event Action<string> LineAppended;

        /// <summary>
        /// 记录一条日志
        /// </summary>
        public void Write(string message, bool isError = false)
        {
            try
            {
                if (!ShouldLog(message, isError))
                    return;

                var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";

                if (ShowInWindow)
                {
                    UpdateLogBuffer(logEntry);
                    LineAppended?.Invoke(logEntry);
                }

                if (SaveToFile)
                {
                    WriteToLogFile(logEntry);
                }
            }
            catch (Exception ex)
            {
                // 日志本身出错时兜底：尝试写桌面备份日志
                var errorMessage = $"[{DateTime.Now:HH:mm:ss}] 日志记录失败: {ex.Message}";
                try
                {
                    string backupLog = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CopyLog.txt");
                    File.AppendAllText(backupLog, errorMessage + Environment.NewLine);
                }
                catch { }
            }
        }

        private bool ShouldLog(string message, bool isError)
        {
            if (isError && !LogErrors)
                return false;

            if (!isError)
            {
                if (message.Contains("成功") && !LogSuccess)
                    return false;

                if (!message.Contains("成功") && !LogNeutral)
                    return false;
            }

            return true;
        }

        private void UpdateLogBuffer(string entry)
        {
            lock (_logBuffer)
            {
                _logBuffer.AppendLine(entry);
                _currentLogLines++;

                if (_currentLogLines > MaxLogLines)
                {
                    // 截断最早的日志行，保留最后 MaxLogLines 行
                    var full = _logBuffer.ToString();
                    var newlineIdx = full.IndexOf('\n');
                    if (newlineIdx >= 0)
                    {
                        _logBuffer.Clear();
                        _logBuffer.Append(full.Substring(newlineIdx + 1));
                    }
                    _currentLogLines = MaxLogLines;
                }
            }
        }

        private void WriteToLogFile(string entry)
        {
            if (string.IsNullOrEmpty(LogFilePath))
                return;

            RotateIfNeeded();
            File.AppendAllText(LogFilePath, entry + Environment.NewLine);
        }

        /// <summary>
        /// 日志轮转：当前日志文件超过大小上限时，重命名为带时间戳的归档文件并新建
        /// </summary>
        private void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(LogFilePath);
                if (!fi.Exists || fi.Length <= MaxLogFileSizeBytes)
                    return;

                string dir = Path.GetDirectoryName(LogFilePath) ?? ".";
                string rotated = Path.Combine(dir,
                    $"{Path.GetFileNameWithoutExtension(LogFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(LogFilePath)}");
                File.Move(LogFilePath, rotated);
            }
            catch { /* 轮转失败不影响主流程 */ }
        }
    }
}
