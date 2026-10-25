#pragma once

// ============================================================================
//  path_util —— 路径归一化与安全边界校验
//
//  对齐目标（C# 侧）：
//    Server/Services/LocalFileStore.cs:67-92  GetSafeFullPath
//      · 统一分隔符 → 去掉前导分隔符 → 显式拒绝任意位置的 ".." 段
//      · Path.GetFullPath(Combine(root, rel))
//      · 目录边界比较：StartsWith(root + '\', OrdinalIgnoreCase) 或完全相等
//
//  与 C# 的差异（均为收紧，已记入 兼容性基线.md §15）：
//    · 相对路径里的 ':' 一律拒绝（阻断 NTFS 备用数据流 server.aspx:evil）
//    · 通配符与控制字符拒绝（C# 会落到后续 IO 调用才失败）
//    · 绝对路径/驱动器相对路径显式返回 Rooted，不再"去前导分隔符后硬拼"
// ============================================================================

#include <windows.h>

#include <string>

namespace usbcopy::path {

enum class Status {
    Ok = 0,
    Empty,          // 输入为空或纯空白
    Rooted,         // 相对路径输入却是绝对/驱动器相对形式
    Traversal,      // 含 ".." 段
    Invalid,        // 含非法字符（控制字符、通配符、':'）
    TooLong,        // 归一化后超过 32767（Win32 扩展路径上限）
    NotUnderRoot,   // 归一化后越出根目录
    Win32Error,     // GetFullPathNameW / GetFileAttributesW 失败
};

// 状态 → 便于日志的中文短语（不要用于界面长文案）
const wchar_t* StatusText(Status status);

// ---- 基础判断 ----

// 是否含 ".." 段（任意位置；兼容 '/' 与 '\'；".." 与 "..abc" 严格区分）
bool HasParentSegment(const std::wstring& path);

// 是否含非法字符：控制字符、< > " | ? *，以及 ':'（相对路径专用）
bool HasIllegalChars(const std::wstring& relativePath);

// 是否以 \\?\ 或 \\.\ 开头
bool IsExtended(const std::wstring& path);

// ---- 归一化 ----

// GetFullPathNameW + 去尾部分隔符（保留 "C:\" 与 "\\\\?\\C:\" 这类根形态）
// 注意：不做符号链接解析，纯文本归一化（与 Path.GetFullPath 一致）
Status Normalize(const std::wstring& input, std::wstring& fullPath);

// ---- 目录边界 ----

// child 是否等于 base 或位于 base 之下（大小写不敏感，按目录边界）
// 调用前两侧都应已 Normalize；"C:\Storage" 与 "C:\Storage_backup" 互不包含
bool IsUnder(const std::wstring& base, const std::wstring& candidate);

// base + relative → 安全全路径（服务端 GetSafeFullPath 的等价物）
Status ResolveUnder(const std::wstring& base, const std::wstring& relative,
                    std::wstring& fullPath);

// ---- 组合与拆分（不归一化，纯字符串操作）----

std::wstring Combine(const std::wstring& left, const std::wstring& right);
std::wstring DirectoryName(const std::wstring& fullPath);   // 无分隔符时返回空串
std::wstring FileName(const std::wstring& fullPath);        // 含扩展名
std::wstring Extension(const std::wstring& fullPath);       // 小写，含点；无扩展名返回空串
std::wstring FileNameWithoutExtension(const std::wstring& fullPath);

// ---- 长路径 ----

// 加 \\?\ 前缀：C:\a → \\?\C:\a；\\server\share\x → \\?\UNC\server\share\x
std::wstring ToExtended(const std::wstring& fullPath);
// 去掉前缀；不是扩展路径时原样返回
std::wstring FromExtended(const std::wstring& path);

// ---- 文件属性 ----

// 失败（文件不存在等）返回 INVALID_FILE_ATTRIBUTES
DWORD Attributes(const std::wstring& fullPath);
bool HasAttribute(const std::wstring& fullPath, DWORD attribute);
// reparse point（符号链接 / 目录联接 / 云占位）—— M2 跳过判断的基础（P1-1）
bool IsReparsePoint(const std::wstring& fullPath);
bool IsDirectory(const std::wstring& fullPath);
bool FileExists(const std::wstring& fullPath);

// ---- 目录可写探测（不留残留文件）----
bool IsDirectoryWritable(const std::wstring& directory);

// ---- 其他 ----

// 本程序所在目录（不带尾部分隔符）
std::wstring ExeDirectory();

// 目录是否存在
bool DirectoryExists(const std::wstring& fullPath);

} // namespace usbcopy::path
