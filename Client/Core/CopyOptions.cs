using System.Collections.Generic;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 一次复制任务的完整配置（由界面层从控件状态构建，复制引擎只读此对象）
    /// </summary>
    public class CopyOptions
    {
        /// <summary>本地目标根目录（本地模式）</summary>
        public string TargetDirectory { get; set; } = "";

        /// <summary>搜索模式（如 *.docx、*.*）</summary>
        public string[] SearchPatterns { get; set; } = { FileCategories.AllFilesPattern };

        /// <summary>是否生成目录树文件</summary>
        public bool CreateDirectoryTree { get; set; }

        /// <summary>是否限制目录深度</summary>
        public bool LimitDirectoryDepth { get; set; }

        /// <summary>最大目录深度</summary>
        public int MaxDirectoryDepth { get; set; } = int.MaxValue;

        /// <summary>是否按文件夹名过滤</summary>
        public bool FilterByFolderName { get; set; }

        /// <summary>文件夹名关键词（含关键词的目录才复制）</summary>
        public List<string> FolderNameKeywords { get; set; } = new List<string>();

        /// <summary>是否按文件名过滤</summary>
        public bool FilterByFileName { get; set; }

        /// <summary>文件名关键词（含关键词的文件才复制）</summary>
        public List<string> FileNameKeywords { get; set; } = new List<string>();

        /// <summary>是否启用文件大小限制</summary>
        public bool EnableSizeLimit { get; set; }

        /// <summary>单文件大小上限（字节）</summary>
        public long MaxSizeBytes { get; set; }

        /// <summary>是否启用限速（插入后前 N 分钟限速）</summary>
        public bool EnableSpeedLimit { get; set; }

        /// <summary>限速窗口（插入后多少分钟内）</summary>
        public int SpeedLimitMinutes { get; set; } = 5;

        /// <summary>限速值（字节/秒），由界面层依据用户选择传入</summary>
        public int SpeedLimitBytesPerSecond { get; set; } = 2 * 1024 * 1024;

        /// <summary>是否启用"存在 stop 文件则停止复制"</summary>
        public bool StopCopyWhenFileExists { get; set; }

        /// <summary>停止复制标记文件名</summary>
        public string StopCopyFileName { get; set; } = "stop.copy";

        /// <summary>是否启用反向复制（存在标记文件则反向复制）</summary>
        public bool ReverseCopyWhenFileExists { get; set; }

        /// <summary>反向复制标记文件名</summary>
        public string ReverseCopyFileName { get; set; } = "reverse.copy";

        /// <summary>反向复制完成标记文件（写入 U 盘）</summary>
        public string ReverseCopyMarker { get; set; } = ".reverse_copied";

        /// <summary>历史反向复制标记（兼容旧版 copy.stop）</summary>
        public string ReverseCopyIndicator { get; set; } = "copy.stop";

        /// <summary>重复文件处理方式</summary>
        public DuplicateFileAction DuplicateAction { get; set; } = DuplicateFileAction.Skip;
    }
}
