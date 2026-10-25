#pragma once

// ============================================================================
//  file_filter —— 纯判定层（不复制、不写盘）
//
//  职责：给定一个文件/目录，按 C# CopyEngine 的顺序给出决策与 skipReason。
//  严格复刻 C# 的判定顺序（错一个顺序就会产生不同的 skipReason，比对立刻失败）：
//
//    文件：系统文件名 → 标记文件 → System|Hidden 属性 → reparse point(D27)
//          → 文件名关键词 → 大小限制
//    目录：系统目录名 → System|Hidden 属性 → reparse point(D27)
//          → （关键词过滤由调用方单独调用，因为目录树与复制对它的时机不同）
//
//  通配符匹配一律走 Win32 `FindFirstFileExW`：`*.doc` 在 Windows 上会匹配 `a.docx`
//  （8.3 短名遗留行为），自己实现 glob 会与 C# 的 `Directory.EnumerateFiles` 不一致。
// ============================================================================

#include <windows.h>

#include <string>
#include <vector>

#include "copy_options.h"

namespace usbcopy::copy {

struct FileDecision {
    DecisionKind kind = DecisionKind::Copy;
    const wchar_t* reason = reason::kNone;
    bool ok() const { return kind == DecisionKind::Copy; }
};

struct FileEvaluation {
    FileDecision decision;
    // 只有「超大文件」为 true：决策是 Skip，但引擎把它计入 failure 与 total
    // （基线 §17.2 唯一例外）
    bool countedAsFailure = false;
};

// 单个文件的完整判定（含文件名关键词与大小限制）
FileEvaluation EvaluateFile(const std::wstring& fullPath, const std::wstring& fileName,
                            long long length, DWORD attributes, const CopyOptions& options,
                            bool reverseCopy);

// 只做「系统文件 / 标记 / 属性 / reparse」这一段（对应 C# ShouldSkipFile）
FileDecision EvaluateFileBasics(const std::wstring& fileName, DWORD attributes,
                                const CopyOptions& options, bool reverseCopy);

// 目录判定（不含关键词过滤）
FileDecision EvaluateDirectory(const std::wstring& dirName, DWORD attributes);

// 关键词过滤：启用但列表为空 → **false**（一个都不匹配，C# 如此）
bool PassFileNameFilter(const std::wstring& fileName, const CopyOptions& options);
bool PassFolderNameFilter(const std::wstring& folderName, const CopyOptions& options);

// 大小限制（C#：`file.Length > MaxSizeBytes`，**严格大于**）
bool ExceedsSizeLimit(long long length, const CopyOptions& options);

// 深度：C# 在**枚举子目录之前**判断 `currentDepth >= MaxDirectoryDepth`
bool DepthExceeded(int currentDepth, const CopyOptions& options);

// 系统文件/目录名（C# FileCategories.SystemFiles / SystemDirectories，忽略大小写）
bool IsSystemFile(const std::wstring& fileName);
bool IsSystemDirectory(const std::wstring& dirName);

// ---- 通配符 ----

// 单个 pattern 下该目录的匹配文件（顺序为 FindFirstFile 返回顺序，仅文件不含目录）
std::vector<std::wstring> CollectMatchesForPattern(const std::wstring& directory,
                                                   const std::wstring& pattern);

// 所有 pattern 的并集（去重、大小写不敏感），用于「源树里哪些文件被模式命中」
std::vector<std::wstring> CollectMatchedNames(const std::wstring& directory,
                                              const std::vector<std::wstring>& patterns);

// 单个文件名是否被任一 pattern 命中（测试与单文件场景用）
bool MatchesAnyPattern(const std::wstring& directory, const std::wstring& fileName,
                       const std::vector<std::wstring>& patterns);

} // namespace usbcopy::copy
