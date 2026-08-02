using System;
using System.Collections.Generic;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 文件类型分类、系统目录/文件常量（纯后端，无 UI 依赖）
    /// </summary>
    public static class FileCategories
    {
        /// <summary>全部文件匹配模式</summary>
        public const string AllFilesPattern = "*.*";

        /// <summary>需要跳过的系统目录</summary>
        public static readonly HashSet<string> SystemDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information",
            "$RECYCLE.BIN",
            ".Trashes",
            ".Spotlight-V100",
            ".fseventsd",
            "Recovery"
        };

        /// <summary>需要跳过的系统文件</summary>
        public static readonly HashSet<string> SystemFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "desktop.ini",
            "thumbs.db",
            "autorun.inf"
        };

        public static readonly string[] PowerPointExtensions = { "*.ppt", "*.pptx" };
        public static readonly string[] WordExtensions = { "*.doc", "*.docx", "*.txt" };
        public static readonly string[] ExcelExtensions = { "*.xlsx", "*.xls" };
        public static readonly string[] PdfExtensions = { "*.pdf" };
        public static readonly string[] ImageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp" };
        public static readonly string[] VideoExtensions = { "*.mp4", "*.avi", "*.mov", "*.mkv", "*.wmv", "*.flv" };
        public static readonly string[] AudioExtensions = { "*.mp3", "*.wma", "*.wav", "*.ape", "*.ogg", "*.flac", "*.aac" };
        public static readonly string[] CompressedExtensions = { "*.zip", "*.rar", "*.7z", "*.tar", "*.gz", "*.bz2", "*.xz", "*.zst", "*.001", "*.iso", "*.wim", "*.cab" };
    }

    /// <summary>
    /// 重复文件处理方式
    /// </summary>
    public enum DuplicateFileAction
    {
        Skip,
        Overwrite,
        KeepBoth,
        ReplaceWithNewer
    }

    /// <summary>
    /// U 盘特殊文件触发的动作
    /// </summary>
    public enum SpecialFileAction
    {
        None,
        StopCopy,
        ReverseCopy
    }
}
