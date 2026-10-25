#pragma once

// ============================================================================
//  copy_options —— 复制任务的配置与决策枚举（对应 C# Client/Core/CopyOptions.cs）
//
//  字段名、默认值逐一取自 C#（见 兼容性基线.md §2 与 §17）。
//  这里只放**纯数据**，不放引擎；`--headless` 与 GUI 共用同一份结构。
// ============================================================================

#include <limits>
#include <string>
#include <vector>

namespace usbcopy::copy {

// 重复文件处理（数值与 C# 一致，可直接写进 JSON）
enum class DuplicateAction {
    Skip = 0,
    Overwrite = 1,
    KeepBoth = 2,
    ReplaceWithNewer = 3,
};

// 每个文件的最终决策（headless 输出的 action 字段）
enum class DecisionKind {
    Copy,
    Skip,
    Fail,
    NotVisited, // 源树里有、引擎没处理（深度截断/系统目录/取消）
};

const wchar_t* DecisionKindText(DecisionKind kind);

// skipReason 规范枚举（基线 §13）：全部是字面量指针，便于直接输出、比较与测试
namespace reason {
inline constexpr const wchar_t* kNone = L"";
inline constexpr const wchar_t* kSystemFile = L"system-file";
inline constexpr const wchar_t* kSystemAttr = L"system-attr";
inline constexpr const wchar_t* kHiddenAttr = L"hidden-attr";
inline constexpr const wchar_t* kMarkerFile = L"marker-file";
inline constexpr const wchar_t* kReparsePoint = L"reparse-point";
inline constexpr const wchar_t* kNameFilter = L"name-filter";
inline constexpr const wchar_t* kSizeLimit = L"size-limit";
inline constexpr const wchar_t* kDuplicateSkip = L"duplicate-skip";
inline constexpr const wchar_t* kContentIdentical = L"content-identical";
inline constexpr const wchar_t* kDestNewer = L"dest-newer";
inline constexpr const wchar_t* kCopyError = L"copy-error";
inline constexpr const wchar_t* kSystemDir = L"system-dir";
inline constexpr const wchar_t* kAttrDir = L"attr-dir";
inline constexpr const wchar_t* kFolderFilter = L"folder-filter";
inline constexpr const wchar_t* kDepthLimit = L"depth-limit";
inline constexpr const wchar_t* kNotVisited = L"not-visited";
inline constexpr const wchar_t* kNotWhitelisted = L"not-whitelisted";
inline constexpr const wchar_t* kStopFile = L"stop-file";
inline constexpr const wchar_t* kReverseMarker = L"reverse-marker";
} // namespace reason

// 25 个字段，顺序与 C# CopyOptions 一致（便于逐项对读）
struct CopyOptions {
    std::wstring targetDirectory;                                          // 反向时=本地备份根
    std::vector<std::wstring> searchPatterns{L"*.*"};                      // 通配符（OS 语义）
    bool createDirectoryTree = false;
    bool limitDirectoryDepth = false;
    int maxDirectoryDepth = (std::numeric_limits<int>::max)();             // C# 默认 int.MaxValue
    bool filterByFolderName = false;
    std::vector<std::wstring> folderNameKeywords;
    bool filterByFileName = false;
    std::vector<std::wstring> fileNameKeywords;
    bool enableSizeLimit = false;
    long long maxSizeBytes = 0;
    bool enableSpeedLimit = false;
    int speedLimitMinutes = 5;
    int speedLimitBytesPerSecond = 2 * 1024 * 1024;
    bool checkFreeSpace = true;                                            // 本地目标才检查
    bool stopCopyWhenFileExists = false;
    std::wstring stopCopyFileName = L"stop.copy";
    bool reverseCopyWhenFileExists = false;
    std::wstring reverseCopyFileName = L"reverse.copy";
    std::wstring reverseCopyMarker = L".reverse_copied";                   // 反向完成后写盘
    std::wstring reverseCopyIndicator = L"copy.stop";                      // 旧版兼容标记
    DuplicateAction duplicateAction = DuplicateAction::Skip;
    bool compareContent = true;                                            // 仅本地目标
    bool enableWhitelist = false;
    std::vector<std::wstring> whitelistedDriveIds;
};

// 关键词解析：按英文逗号切分 → Trim → 去空 → **忽略大小写去重**（C# ParseKeywords）
// 注意：扩展名模式是"区分大小写去重"，两者别混（基线 §2.3）
std::vector<std::wstring> ParseKeywords(const std::wstring& raw);

} // namespace usbcopy::copy
