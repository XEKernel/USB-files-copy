#include "path_util.h"

#include <algorithm>
#include <cctype>
#include <cwctype>
#include <vector>

namespace usbcopy::path {

namespace {

bool IsSeparator(wchar_t c) { return c == L'\\' || c == L'/'; }

std::wstring Trim(const std::wstring& text) {
    size_t begin = 0;
    size_t end = text.size();
    while (begin < end && (text[begin] == L' ' || text[begin] == L'\t' || text[begin] == L'\r' ||
                           text[begin] == L'\n')) {
        ++begin;
    }
    while (end > begin && (text[end - 1] == L' ' || text[end - 1] == L'\t' ||
                           text[end - 1] == L'\r' || text[end - 1] == L'\n')) {
        --end;
    }
    return text.substr(begin, end - begin);
}

std::wstring ToBackslashes(const std::wstring& text) {
    std::wstring result = text;
    std::replace(result.begin(), result.end(), L'/', L'\\');
    return result;
}

// 去掉尾部分隔符，但保留根形态："C:\" / "\\?\C:\" / "\\\\"
std::wstring TrimTrailingSeparators(const std::wstring& text) {
    size_t size = text.size();
    while (size > 0 && IsSeparator(text[size - 1])) {
        if (size == 3 && text[1] == L':') {
            break; // 根形态 "C:\"
        }
        if (size == 7 && text.compare(0, 4, L"\\\\?\\") == 0 && text[5] == L':') {
            break; // 根形态 "\\?\C:\"
        }
        if (size <= 2) {
            break; // \\ 或 /
        }
        --size;
    }
    return text.substr(0, size);
}

bool CallGetFullPathName(const std::wstring& input, std::wstring& output) {
    DWORD needed = GetFullPathNameW(input.c_str(), 0, nullptr, nullptr);
    if (needed == 0) {
        return false;
    }
    std::wstring buffer(static_cast<size_t>(needed) + 1, L'\0');
    DWORD written = GetFullPathNameW(input.c_str(), static_cast<DWORD>(buffer.size()),
                                     buffer.data(), nullptr);
    if (written == 0 || written >= buffer.size()) {
        return false;
    }
    buffer.resize(static_cast<size_t>(written));
    output = std::move(buffer);
    return true;
}

bool StartsWithNoCase(const std::wstring& text, size_t offset, const wchar_t* prefix,
                      size_t length) {
    if (offset + length > text.size()) {
        return false;
    }
    return CompareStringOrdinal(text.c_str() + offset, static_cast<int>(length), prefix,
                                static_cast<int>(length), TRUE) == CSTR_EQUAL;
}

} // namespace

const wchar_t* StatusText(Status status) {
    switch (status) {
    case Status::Ok: return L"成功";
    case Status::Empty: return L"路径为空";
    case Status::Rooted: return L"不允许绝对路径";
    case Status::Traversal: return L"路径含 .. 段（疑似目录穿越）";
    case Status::Invalid: return L"路径含非法字符";
    case Status::TooLong: return L"路径过长";
    case Status::NotUnderRoot: return L"路径越出允许的根目录";
    case Status::Win32Error: return L"路径解析失败";
    }
    return L"未知错误";
}

bool HasParentSegment(const std::wstring& path) {
    size_t index = 0;
    const size_t size = path.size();
    while (index <= size) {
        size_t end = index;
        while (end < size && !IsSeparator(path[end])) {
            ++end;
        }
        if (end - index == 2 && path[index] == L'.' && path[index + 1] == L'.') {
            return true;
        }
        if (end >= size) {
            break;
        }
        index = end + 1;
    }
    return false;
}

bool HasIllegalChars(const std::wstring& relativePath) {
    for (wchar_t c : relativePath) {
        if (c < 0x20 || c == 0x7F) {
            return true;
        }
        switch (c) {
        case L'<':
        case L'>':
        case L'"':
        case L'|':
        case L'?':
        case L'*':
        case L':': // 同时阻断 NTFS 备用数据流（file.aspx:evil）
            return true;
        default:
            break;
        }
    }
    return false;
}

bool IsExtended(const std::wstring& path) {
    if (path.size() < 4) {
        return false;
    }
    return StartsWithNoCase(path, 0, L"\\\\?\\", 4);
}

Status Normalize(const std::wstring& input, std::wstring& fullPath) {
    std::wstring text = Trim(input);
    if (text.empty()) {
        return Status::Empty;
    }

    const bool extended = IsExtended(text);
    if (extended) {
        text = text.substr(4); // 去掉 \\?\ 再归一化（扩展前缀路径不会被规范化 ..）
        if (text.empty()) {
            return Status::Empty;
        }
    }

    std::wstring normalized;
    if (!CallGetFullPathName(text, normalized)) {
        return Status::Win32Error;
    }

    normalized = TrimTrailingSeparators(normalized);
    if (normalized.size() > 32767) {
        return Status::TooLong;
    }
    if (extended) {
        normalized = L"\\\\?\\" + normalized;
    }
    fullPath = std::move(normalized);
    return Status::Ok;
}

bool IsUnder(const std::wstring& base, const std::wstring& candidate) {
    std::wstring root = TrimTrailingSeparators(ToBackslashes(base));
    std::wstring child = TrimTrailingSeparators(ToBackslashes(candidate));
    if (root.empty() || child.empty()) {
        return false;
    }
    if (CompareStringOrdinal(root.c_str(), static_cast<int>(root.size()), child.c_str(),
                             static_cast<int>(child.size()), TRUE) == CSTR_EQUAL) {
        return true;
    }
    if (!root.empty() && root.back() != L'\\') {
        root.push_back(L'\\'); // 目录边界：避免 C:\Storage 命中 C:\Storage_backup
    }
    if (child.size() <= root.size()) {
        return false;
    }
    return StartsWithNoCase(child, 0, root.c_str(), root.size());
}

Status ResolveUnder(const std::wstring& base, const std::wstring& relative,
                    std::wstring& fullPath) {
    if (Trim(relative).empty()) {
        return Status::Empty;
    }

    std::wstring normalizedBase;
    Status status = Normalize(base, normalizedBase);
    if (status != Status::Ok) {
        return status;
    }

    std::wstring rel = ToBackslashes(relative);

    // UNC 前缀（\\server\share）与驱动器绝对路径（C:\x）/驱动器相对路径（C:x）都拒绝
    if (rel.size() >= 2 && rel[0] == L'\\' && rel[1] == L'\\') {
        return Status::Rooted;
    }
    if (rel.size() >= 2 && std::iswalpha(rel[0]) != 0 && rel[1] == L':') {
        return Status::Rooted;
    }

    // C# 的 TrimStart(DirectorySeparatorChar)
    size_t start = 0;
    while (start < rel.size() && IsSeparator(rel[start])) {
        ++start;
    }
    rel = rel.substr(start);
    if (Trim(rel).empty()) {
        return Status::Empty;
    }

    if (HasParentSegment(rel)) {
        return Status::Traversal;
    }
    if (HasIllegalChars(rel)) {
        return Status::Invalid;
    }

    std::wstring combined;
    if (!CallGetFullPathName(Combine(normalizedBase, rel), combined)) {
        return Status::Win32Error;
    }
    combined = TrimTrailingSeparators(combined);

    if (!IsUnder(normalizedBase, combined)) {
        return Status::NotUnderRoot;
    }

    fullPath = std::move(combined);
    return Status::Ok;
}

std::wstring Combine(const std::wstring& left, const std::wstring& right) {
    if (left.empty()) {
        return right;
    }
    if (right.empty()) {
        return left;
    }
    std::wstring result = left;
    if (!IsSeparator(result.back())) {
        result.push_back(L'\\');
    }
    size_t start = 0;
    while (start < right.size() && IsSeparator(right[start])) {
        ++start;
    }
    result.append(right, start, std::wstring::npos);
    return result;
}

std::wstring DirectoryName(const std::wstring& fullPath) {
    if (fullPath.empty()) {
        return {};
    }
    size_t end = fullPath.size();
    while (end > 0 && IsSeparator(fullPath[end - 1])) {
        if (end == 3 && fullPath[1] == L':') {
            break; // 根形态 "C:\" 的目录名仍是自身
        }
        --end;
    }
    size_t last = end;
    while (last > 0 && !IsSeparator(fullPath[last - 1])) {
        --last;
    }
    if (last == 0) {
        return {};
    }
    // 去掉分隔符但保留根（"C:\a" → "C:\"，不是 "C:"）
    size_t cut = last;
    while (cut > 1 && IsSeparator(fullPath[cut - 1])) {
        if (cut == 3 && fullPath[1] == L':') {
            break;
        }
        --cut;
    }
    return fullPath.substr(0, cut);
}

std::wstring FileName(const std::wstring& fullPath) {
    size_t end = fullPath.size();
    while (end > 0 && IsSeparator(fullPath[end - 1])) {
        --end;
    }
    size_t last = end;
    while (last > 0 && !IsSeparator(fullPath[last - 1])) {
        --last;
    }
    return fullPath.substr(last, end - last);
}

std::wstring Extension(const std::wstring& fullPath) {
    const std::wstring name = FileName(fullPath);
    const size_t dot = name.rfind(L'.');
    if (dot == std::wstring::npos) {
        return {};
    }
    // 与 .NET Path.GetExtension 一致：".gitignore" 视为扩展名 ".gitignore"
    std::wstring ext = name.substr(dot);
    std::transform(ext.begin(), ext.end(), ext.begin(),
                   [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return ext;
}

std::wstring FileNameWithoutExtension(const std::wstring& fullPath) {
    const std::wstring name = FileName(fullPath);
    const size_t dot = name.rfind(L'.');
    if (dot == std::wstring::npos) {
        return name;
    }
    return name.substr(0, dot);
}

std::wstring ToExtended(const std::wstring& fullPath) {
    if (fullPath.empty() || IsExtended(fullPath)) {
        return fullPath;
    }
    const std::wstring text = ToBackslashes(fullPath);
    if (text.size() >= 2 && text[0] == L'\\' && text[1] == L'\\') {
        return L"\\\\?\\UNC\\" + text.substr(2); // \\server\share → \\?\UNC\server\share
    }
    return L"\\\\?\\" + text;
}

std::wstring FromExtended(const std::wstring& path) {
    if (!IsExtended(path)) {
        return path;
    }
    if (path.size() >= 8 && StartsWithNoCase(path, 4, L"UNC\\", 4)) {
        return L"\\\\" + path.substr(8);
    }
    return path.substr(4);
}

DWORD Attributes(const std::wstring& fullPath) {
    if (fullPath.empty()) {
        return INVALID_FILE_ATTRIBUTES;
    }
    return GetFileAttributesW(ToExtended(fullPath).c_str());
}

bool HasAttribute(const std::wstring& fullPath, DWORD attribute) {
    const DWORD value = Attributes(fullPath);
    if (value == INVALID_FILE_ATTRIBUTES) {
        return false;
    }
    return (value & attribute) != 0;
}

bool IsReparsePoint(const std::wstring& fullPath) {
    return HasAttribute(fullPath, FILE_ATTRIBUTE_REPARSE_POINT);
}

bool IsDirectory(const std::wstring& fullPath) {
    return HasAttribute(fullPath, FILE_ATTRIBUTE_DIRECTORY);
}

bool FileExists(const std::wstring& fullPath) {
    const DWORD value = Attributes(fullPath);
    return value != INVALID_FILE_ATTRIBUTES && (value & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool DirectoryExists(const std::wstring& fullPath) {
    const DWORD value = Attributes(fullPath);
    return value != INVALID_FILE_ATTRIBUTES && (value & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool IsDirectoryWritable(const std::wstring& directory) {
    if (directory.empty() || !DirectoryExists(directory)) {
        return false;
    }

    static LONG counter = 0;
    const LONG sequence = InterlockedIncrement(&counter);
    wchar_t name[96]{};
    swprintf_s(name, L".write_probe_%lu_%ld.tmp", GetCurrentProcessId(), sequence);

    const std::wstring probe = Combine(directory, name);
    // FILE_FLAG_DELETE_ON_CLOSE：句柄一关就删除，不留残留（与 C# 的 DeleteOnClose 一致）
    HANDLE handle = CreateFileW(ToExtended(probe).c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                                FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return false;
    }
    const char payload = 0;
    DWORD written = 0;
    const BOOL ok = WriteFile(handle, &payload, 1, &written, nullptr);
    CloseHandle(handle);
    return ok != FALSE && written == 1;
}

std::wstring ExeDirectory() {
    std::wstring buffer(MAX_PATH, L'\0');
    for (;;) {
        const DWORD length =
            GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0) {
            return {};
        }
        if (length < buffer.size()) {
            buffer.resize(static_cast<size_t>(length));
            return DirectoryName(buffer);
        }
        buffer.resize(buffer.size() * 2); // 被截断，加倍重试
    }
}

} // namespace usbcopy::path
