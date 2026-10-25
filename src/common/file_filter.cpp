#include "file_filter.h"

#include <algorithm>
#include <cwctype>
#include <set>

#include "path_util.h"

namespace usbcopy::copy {

namespace {

bool EqualsNoCase(const std::wstring& left, const std::wstring& right) {
    return CompareStringOrdinal(left.c_str(), static_cast<int>(left.size()), right.c_str(),
                                static_cast<int>(right.size()), TRUE) == CSTR_EQUAL;
}

bool EqualsNoCase(const std::wstring& left, const wchar_t* right) {
    return CompareStringOrdinal(left.c_str(), static_cast<int>(left.size()), right, -1, TRUE) ==
           CSTR_EQUAL;
}

std::wstring ToLower(const std::wstring& text) {
    std::wstring result = text;
    std::transform(result.begin(), result.end(), result.begin(),
                   [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return result;
}

bool ContainsNoCase(const std::wstring& haystack, const std::wstring& needle) {
    if (needle.empty()) {
        return false;
    }
    const std::wstring lowerHaystack = ToLower(haystack);
    const std::wstring lowerNeedle = ToLower(needle);
    return lowerHaystack.find(lowerNeedle) != std::wstring::npos;
}

// C# FileCategories.SystemFiles（OrdinalIgnoreCase）
const wchar_t* const kSystemFiles[] = {L"desktop.ini", L"thumbs.db", L"autorun.inf"};

// C# FileCategories.SystemDirectories（OrdinalIgnoreCase）
const wchar_t* const kSystemDirectories[] = {
    L"System Volume Information", L"$RECYCLE.BIN", L".Trashes",
    L".Spotlight-V100",           L".fseventsd",    L"Recovery",
};

} // namespace

bool IsSystemFile(const std::wstring& fileName) {
    for (const wchar_t* candidate : kSystemFiles) {
        if (EqualsNoCase(fileName, candidate)) {
            return true;
        }
    }
    return false;
}

bool IsSystemDirectory(const std::wstring& dirName) {
    for (const wchar_t* candidate : kSystemDirectories) {
        if (EqualsNoCase(dirName, candidate)) {
            return true;
        }
    }
    return false;
}

FileDecision EvaluateFileBasics(const std::wstring& fileName, DWORD attributes,
                                const CopyOptions& options, bool reverseCopy) {
    // 顺序与 C# ShouldSkipFile 一致（先名字、后属性），D27 的 reparse 判断放在最前，
    // 因为它是"结构上不跟随"，比"隐藏"更具体，且 harness 侧根本不会列出 junction 下的文件
    if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        return {DecisionKind::Skip, reason::kReparsePoint};
    }
    if (IsSystemFile(fileName)) {
        return {DecisionKind::Skip, reason::kSystemFile};
    }
    if (reverseCopy) {
        if (EqualsNoCase(fileName, options.reverseCopyMarker)) {
            return {DecisionKind::Skip, reason::kMarkerFile};
        }
    } else {
        if (EqualsNoCase(fileName, options.reverseCopyIndicator)) {
            return {DecisionKind::Skip, reason::kMarkerFile};
        }
    }
    if ((attributes & FILE_ATTRIBUTE_SYSTEM) != 0) {
        return {DecisionKind::Skip, reason::kSystemAttr};
    }
    if ((attributes & FILE_ATTRIBUTE_HIDDEN) != 0) {
        return {DecisionKind::Skip, reason::kHiddenAttr};
    }
    return {};
}

FileDecision EvaluateDirectory(const std::wstring& dirName, DWORD attributes) {
    if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        return {DecisionKind::Skip, reason::kReparsePoint};
    }
    if (IsSystemDirectory(dirName)) {
        return {DecisionKind::Skip, reason::kSystemDir};
    }
    if ((attributes & (FILE_ATTRIBUTE_SYSTEM | FILE_ATTRIBUTE_HIDDEN)) != 0) {
        return {DecisionKind::Skip, reason::kAttrDir};
    }
    return {};
}

bool PassFileNameFilter(const std::wstring& fileName, const CopyOptions& options) {
    if (!options.filterByFileName) {
        return true;
    }
    if (options.fileNameKeywords.empty()) {
        return false; // C#：启用但没关键词 → 一个都不匹配
    }
    for (const std::wstring& keyword : options.fileNameKeywords) {
        if (ContainsNoCase(fileName, keyword)) {
            return true;
        }
    }
    return false;
}

bool PassFolderNameFilter(const std::wstring& folderName, const CopyOptions& options) {
    if (!options.filterByFolderName) {
        return true;
    }
    if (options.folderNameKeywords.empty()) {
        return false;
    }
    for (const std::wstring& keyword : options.folderNameKeywords) {
        if (ContainsNoCase(folderName, keyword)) {
            return true;
        }
    }
    return false;
}

bool ExceedsSizeLimit(long long length, const CopyOptions& options) {
    return options.enableSizeLimit && length > options.maxSizeBytes;
}

bool DepthExceeded(int currentDepth, const CopyOptions& options) {
    return options.limitDirectoryDepth && currentDepth >= options.maxDirectoryDepth;
}

FileEvaluation EvaluateFile(const std::wstring& fullPath, const std::wstring& fileName,
                            long long length, DWORD attributes, const CopyOptions& options,
                            bool reverseCopy) {
    FileEvaluation result;

    // ① ShouldSkipFile 段
    FileDecision basics = EvaluateFileBasics(fileName, attributes, options, reverseCopy);
    if (!basics.ok()) {
        result.decision = basics;
        return result;
    }

    // ② 文件名关键词
    if (!PassFileNameFilter(fileName, options)) {
        result.decision = {DecisionKind::Skip, reason::kNameFilter};
        return result;
    }

    // ③ 大小限制：跳过但计失败（唯一例外）
    if (ExceedsSizeLimit(length, options)) {
        result.decision = {DecisionKind::Skip, reason::kSizeLimit};
        result.countedAsFailure = true;
        return result;
    }

    (void)fullPath; // 目前判定不需要路径；保留参数是为了将来按完整路径判定（如去重）
    return result;
}

// ---------------------------------------------------------------------------
//  通配符：一律走 OS，保证与 C# 的 Directory.EnumerateFiles 完全同语义
// ---------------------------------------------------------------------------

std::vector<std::wstring> CollectMatchesForPattern(const std::wstring& directory,
                                                   const std::wstring& pattern) {
    std::vector<std::wstring> results;
    if (directory.empty() || pattern.empty()) {
        return results;
    }

    const std::wstring query = path::ToExtended(path::Combine(directory, pattern));
    WIN32_FIND_DATAW data{};
    HANDLE handle = FindFirstFileExW(query.c_str(), FindExInfoBasic, &data, FindExSearchNameMatch,
                                     nullptr, FIND_FIRST_EX_LARGE_FETCH);
    if (handle == INVALID_HANDLE_VALUE) {
        return results;
    }
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
            continue; // EnumerateFiles 只返回文件
        }
        results.emplace_back(data.cFileName);
    } while (FindNextFileW(handle, &data));
    FindClose(handle);
    return results;
}

std::vector<std::wstring> CollectMatchedNames(const std::wstring& directory,
                                              const std::vector<std::wstring>& patterns) {
    std::vector<std::wstring> results;
    std::set<std::wstring> seen; // 小写化后去重，保持首次出现顺序
    for (const std::wstring& pattern : patterns) {
        for (const std::wstring& name : CollectMatchesForPattern(directory, pattern)) {
            const std::wstring key = ToLower(name);
            if (seen.insert(key).second) {
                results.push_back(name);
            }
        }
    }
    return results;
}

bool MatchesAnyPattern(const std::wstring& directory, const std::wstring& fileName,
                       const std::vector<std::wstring>& patterns) {
    for (const std::wstring& pattern : patterns) {
        for (const std::wstring& name : CollectMatchesForPattern(directory, pattern)) {
            if (EqualsNoCase(name, fileName)) {
                return true;
            }
        }
    }
    return false;
}

std::vector<std::wstring> ParseKeywords(const std::wstring& raw) {
    std::vector<std::wstring> results;
    size_t start = 0;
    while (start <= raw.size()) {
        size_t comma = raw.find(L',', start);
        if (comma == std::wstring::npos) {
            comma = raw.size();
        }
        std::wstring token = raw.substr(start, comma - start);
        // Trim
        size_t begin = 0;
        size_t end = token.size();
        while (begin < end && (token[begin] == L' ' || token[begin] == L'\t')) {
            ++begin;
        }
        while (end > begin && (token[end - 1] == L' ' || token[end - 1] == L'\t')) {
            --end;
        }
        token = token.substr(begin, end - begin);

        if (!token.empty()) {
            bool duplicate = false;
            for (const std::wstring& existing : results) {
                if (EqualsNoCase(existing, token)) {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) {
                results.push_back(token);
            }
        }
        if (comma == raw.size()) {
            break;
        }
        start = comma + 1;
    }
    return results;
}

} // namespace usbcopy::copy
