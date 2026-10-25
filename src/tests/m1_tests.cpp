// ============================================================================
//  m1_tests —— M1 公共库验收测试（控制台程序，全绿才算通过）
//
//  覆盖：path_util（6 类）、json（往返/保序/深嵌套/非法输入/整数保真）、
//        settings（43+11 字段往返、DPAPI、XML 导入、损坏回退、加载门禁、单机版）、
//        log（行格式、900 行截断、5MB 轮转、三开关、多线程）
// ============================================================================

#include <windows.h>

#include <cstdio>
#include <string>
#include <thread>
#include <vector>

#include "json.h"
#include "log.h"
#include "path_util.h"
#include "settings.h"
#include "win_util.h"
#include "xml_lite.h"

using namespace usbcopy; // 测试里大量使用 path:: / json:: / settings:: / log:: / xml::

namespace {

int g_checks = 0;
int g_failures = 0;
int g_skips = 0;
const char* g_section = "";

void Section(const char* name) {
    g_section = name;
    std::printf("\n=== %s ===\n", name);
}

void Report(const char* status, const char* file, int line, const std::string& detail) {
    ++g_checks;
    if (detail.empty()) {
        std::printf("[%s] %s:%d\n", status, file, line);
    } else {
        std::printf("[%s] %s:%d  %s\n", status, file, line, detail.c_str());
    }
}

void Check(bool ok, const char* expr, const char* file, int line) {
    if (ok) {
        Report("PASS", file, line, std::string(" ") + expr);
    } else {
        ++g_failures;
        Report("FAIL", file, line, std::string(" ") + expr);
    }
}

void CheckText(const std::wstring& actual, const std::wstring& expected, const char* expr,
               const char* file, int line) {
    if (actual == expected) {
        Report("PASS", file, line, std::string(" ") + expr);
        return;
    }
    ++g_failures;
    Report("FAIL", file, line,
           std::string(" ") + expr + "  实际=[" + util::ToUtf8(actual) + "] 期望=[" +
               util::ToUtf8(expected) + "]");
}

void CheckInt(long long actual, long long expected, const char* expr, const char* file, int line) {
    if (actual == expected) {
        Report("PASS", file, line, std::string(" ") + expr);
        return;
    }
    ++g_failures;
    Report("FAIL", file, line,
           std::string(" ") + expr + "  实际=" + std::to_string(actual) + " 期望=" +
               std::to_string(expected));
}

void Skip(const char* reason, const char* file, int line) {
    ++g_skips;
    std::printf("[SKIP] %s:%d  %s\n", file, line, reason);
}

#define CHECK(expr) Check((expr), #expr, __FILE__, __LINE__)
#define CHECK_TEXT(actual, expected) \
    CheckText(std::wstring(actual), std::wstring(expected), #actual, __FILE__, __LINE__)
#define CHECK_INT(actual, expected) \
    CheckInt(static_cast<long long>(actual), static_cast<long long>(expected), #actual, __FILE__, \
             __LINE__)
#define SKIP(reason) Skip((reason), __FILE__, __LINE__)

// ---------------------------------------------------------------------------
//  文件工具
// ---------------------------------------------------------------------------

std::wstring TempRoot() {
    wchar_t buffer[MAX_PATH]{};
    GetTempPathW(MAX_PATH, buffer);
    return path::Combine(path::FromExtended(buffer),
                         L"usbcopy_m1_tests_" + std::to_wstring(GetCurrentProcessId()));
}

bool WriteBytes(const std::wstring& filePath, const std::string& bytes) {
    HANDLE handle = CreateFileW(path::ToExtended(filePath).c_str(), GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return false;
    }
    DWORD written = 0;
    const BOOL ok = WriteFile(handle, bytes.data(), static_cast<DWORD>(bytes.size()), &written,
                              nullptr);
    CloseHandle(handle);
    return ok != FALSE && written == bytes.size();
}

bool WriteTextUtf8(const std::wstring& filePath, const std::wstring& text) {
    std::string bytes = util::ToUtf8(text);
    // XmlSerializer 输出带 BOM，这里一并模拟
    bytes.insert(0, "\xEF\xBB\xBF");
    return WriteBytes(filePath, bytes);
}

std::string ReadAllBytes(const std::wstring& filePath) {
    HANDLE handle = CreateFileW(path::ToExtended(filePath).c_str(), GENERIC_READ,
                                FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
                                FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return {};
    }
    std::string result;
    char buffer[8192];
    DWORD read = 0;
    while (ReadFile(handle, buffer, sizeof(buffer), &read, nullptr) && read > 0) {
        result.append(buffer, read);
    }
    CloseHandle(handle);
    return result;
}

unsigned long long FileSize(const std::wstring& filePath) {
    WIN32_FILE_ATTRIBUTE_DATA info{};
    if (!GetFileAttributesExW(path::ToExtended(filePath).c_str(), GetFileExInfoStandard, &info)) {
        return 0;
    }
    return (static_cast<unsigned long long>(info.nFileSizeHigh) << 32) | info.nFileSizeLow;
}

size_t CountLines(const std::wstring& filePath) {
    const std::string bytes = ReadAllBytes(filePath);
    size_t count = 0;
    for (char c : bytes) {
        if (c == '\n') {
            ++count;
        }
    }
    return count;
}

size_t CountMatching(const std::wstring& directory, const wchar_t* pattern) {
    WIN32_FIND_DATAW data{};
    HANDLE handle = FindFirstFileW(path::ToExtended(path::Combine(directory, pattern)).c_str(),
                                   &data);
    if (handle == INVALID_HANDLE_VALUE) {
        return 0;
    }
    size_t count = 0;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
            ++count;
        }
    } while (FindNextFileW(handle, &data));
    FindClose(handle);
    return count;
}

void RemoveTree(const std::wstring& directory) {
    WIN32_FIND_DATAW data{};
    HANDLE handle =
        FindFirstFileW(path::ToExtended(path::Combine(directory, L"*")).c_str(), &data);
    if (handle != INVALID_HANDLE_VALUE) {
        do {
            const std::wstring name = data.cFileName;
            if (name == L"." || name == L"..") {
                continue;
            }
            const std::wstring child = path::Combine(directory, name);
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
                RemoveTree(child);
            } else {
                SetFileAttributesW(path::ToExtended(child).c_str(), FILE_ATTRIBUTE_NORMAL);
                DeleteFileW(path::ToExtended(child).c_str());
            }
        } while (FindNextFileW(handle, &data));
        FindClose(handle);
    }
    RemoveDirectoryW(path::ToExtended(directory).c_str());
}

// ---------------------------------------------------------------------------
//  1. path_util
// ---------------------------------------------------------------------------

void TestPathUtil(const std::wstring& root) {
    Section("path_util");

    // --- 1) ".." 段拒绝（任意位置）---
    CHECK(path::HasParentSegment(L".."));
    CHECK(path::HasParentSegment(L"..\\a"));
    CHECK(path::HasParentSegment(L"a\\..\\b"));
    CHECK(path::HasParentSegment(L"a/../b"));
    CHECK(path::HasParentSegment(L"C:\\a\\..\\b"));
    CHECK(path::HasParentSegment(L"a\\..\\..\\b"));
    CHECK(path::HasParentSegment(L"..\\"));
    CHECK(!path::HasParentSegment(L"a\\..b"));
    CHECK(!path::HasParentSegment(L"a\\b..c"));
    CHECK(!path::HasParentSegment(L"..."));
    CHECK(!path::HasParentSegment(L"a\\b"));

    // --- 2) 同前缀兄弟目录边界 ---
    CHECK(!path::IsUnder(L"C:\\Storage", L"C:\\Storage_backup"));
    CHECK(!path::IsUnder(L"C:\\Storage", L"C:\\StorageX"));
    CHECK(!path::IsUnder(L"C:\\Storage\\a", L"C:\\Storage\\ab.txt"));
    CHECK(path::IsUnder(L"C:\\Storage", L"C:\\Storage\\a.txt"));
    CHECK(path::IsUnder(L"C:\\Storage", L"C:\\Storage"));
    CHECK(path::IsUnder(L"C:\\Storage", L"C:\\storage\\A.TXT")); // 大小写不敏感
    CHECK(path::IsUnder(L"C:\\Storage\\", L"C:\\Storage\\a\\b.txt"));
    CHECK(!path::IsUnder(L"C:\\Storage\\a", L"C:\\Storage")); // 父目录不在子目录之下

    // --- 3) ResolveUnder 的安全边界 ---
    std::wstring resolved;
    CHECK(path::ResolveUnder(root, L"sub\\file.txt", resolved) == path::Status::Ok);
    CHECK(path::IsUnder(root, resolved));
    CHECK(path::ResolveUnder(root, L"..\\evil.txt", resolved) == path::Status::Traversal);
    CHECK(path::ResolveUnder(root, L"a\\..\\..\\evil.txt", resolved) == path::Status::Traversal);
    CHECK(path::ResolveUnder(root, L"", resolved) == path::Status::Empty);
    CHECK(path::ResolveUnder(root, L"C:\\Windows\\evil.txt", resolved) == path::Status::Rooted);
    CHECK(path::ResolveUnder(root, L"\\\\server\\share\\x", resolved) == path::Status::Rooted);
    CHECK(path::ResolveUnder(root, L"file.txt:hidden", resolved) == path::Status::Invalid);
    CHECK(path::ResolveUnder(root, L"a*b.txt", resolved) == path::Status::Invalid);
    CHECK(path::ResolveUnder(root, L"/sub/x.txt", resolved) == path::Status::Ok);
    CHECK(path::IsUnder(root, resolved));

    // --- 4) \\?\ 长路径前缀 ---
    CHECK_TEXT(path::ToExtended(L"C:\\a\\b"), L"\\\\?\\C:\\a\\b");
    CHECK_TEXT(path::ToExtended(L"\\\\server\\share\\x"), L"\\\\?\\UNC\\server\\share\\x");
    CHECK_TEXT(path::ToExtended(L"\\\\?\\C:\\a"), L"\\\\?\\C:\\a");
    CHECK_TEXT(path::FromExtended(L"\\\\?\\C:\\a"), L"C:\\a");
    CHECK_TEXT(path::FromExtended(L"\\\\?\\UNC\\server\\share\\x"), L"\\\\server\\share\\x");
    CHECK(path::IsExtended(L"\\\\?\\C:\\a"));
    CHECK(!path::IsExtended(L"C:\\a"));

    // 超长路径（> MAX_PATH）用扩展前缀读写仍然可行
    std::wstring deep = root;
    for (int i = 0; i < 12; ++i) {
        deep = path::Combine(deep, L"0123456789012345678901234567890"); // 31 字符 × 12
    }
    CHECK(deep.size() > MAX_PATH);
    bool created = true;
    std::wstring cursor = root;
    for (int i = 0; i < 12; ++i) {
        cursor = path::Combine(cursor, L"0123456789012345678901234567890");
        if (!CreateDirectoryW(path::ToExtended(cursor).c_str(), nullptr) &&
            GetLastError() != ERROR_ALREADY_EXISTS) {
            created = false;
            break;
        }
    }
    CHECK(created);
    if (created) {
        const std::wstring longFile = path::Combine(deep, L"long.txt");
        CHECK(WriteBytes(longFile, "x"));
        CHECK(path::FileExists(longFile));
    }

    // --- 5) 归一化 ---
    std::wstring full;
    CHECK(path::Normalize(L"C:\\a\\b\\..\\c", full) == path::Status::Ok);
    CHECK_TEXT(full, L"C:\\a\\c");
    CHECK(path::Normalize(L"C:\\a\\b\\", full) == path::Status::Ok);
    CHECK_TEXT(full, L"C:\\a\\b");
    CHECK(path::Normalize(L"C:\\", full) == path::Status::Ok);
    CHECK_TEXT(full, L"C:\\");
    CHECK(path::Normalize(L"\\\\?\\C:\\a\\..\\b", full) == path::Status::Ok);
    CHECK_TEXT(full, L"\\\\?\\C:\\b");
    CHECK(path::Normalize(L"   ", full) == path::Status::Empty);

    // --- 6) reparse point / 属性 ---
    CHECK(path::IsDirectory(root));
    CHECK(path::IsDirectoryWritable(root));

    const std::wstring linkTarget = path::Combine(root, L"reparse_target");
    const std::wstring linkPath = path::Combine(root, L"reparse_link");
    CreateDirectoryW(path::ToExtended(linkTarget).c_str(), nullptr);
    constexpr DWORD kAllowUnprivileged = 0x2; // SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE
    if (CreateSymbolicLinkW(path::ToExtended(linkPath).c_str(), linkTarget.c_str(),
                            SYMBOLIC_LINK_FLAG_DIRECTORY | kAllowUnprivileged)) {
        CHECK(path::IsReparsePoint(linkPath));
        CHECK(path::IsDirectory(linkPath)); // 属性上仍是目录
        CHECK(!path::IsReparsePoint(linkTarget));
    } else {
        SKIP("本机不允许创建符号链接，改用系统自带 junction 验证 reparse point");
        const wchar_t* junctions[] = {L"C:\\Users\\All Users", L"C:\\Documents and Settings",
                                      L"C:\\ProgramData\\Application Data"};
        bool verified = false;
        for (const wchar_t* candidate : junctions) {
            if (path::DirectoryExists(candidate)) {
                CHECK(path::IsReparsePoint(candidate));
                verified = true;
                break;
            }
        }
        if (!verified) {
            SKIP("未找到可用的 junction，IsReparsePoint 正值未验证");
        }
    }

    // 组合与拆分
    CHECK_TEXT(path::Combine(L"C:\\a", L"b\\c"), L"C:\\a\\b\\c");
    CHECK_TEXT(path::DirectoryName(L"C:\\a\\b.txt"), L"C:\\a");
    CHECK_TEXT(path::FileName(L"C:\\a\\b.txt"), L"b.txt");
    CHECK_TEXT(path::Extension(L"C:\\a\\B.TXT"), L".txt");
    CHECK_TEXT(path::FileNameWithoutExtension(L"C:\\a\\b.tar.gz"), L"b.tar");
}

// ---------------------------------------------------------------------------
//  2. json
// ---------------------------------------------------------------------------

void TestJson() {
    Section("json");

    std::wstring error;

    json::Value root = json::Value::MakeObject();
    root.Set(L"targetDirectory", json::Value(L"C:\\Users\\徐\\桌面"));
    root.Set(L"count", json::Value(42));
    root.Set(L"big", json::Value(9007199254740993ll)); // 2^53+1，double 会丢精度
    root.Set(L"ratio", json::Value(2.5));
    root.Set(L"enabled", json::Value(true));
    root.Set(L"nothing", json::Value());
    root.Set(L"escaped", json::Value(L"引号\" 反斜杠\\ 换行\n制表\t"));

    json::Value items = json::Value::MakeArray();
    items.Push(json::Value(1));
    items.Push(json::Value(L"二"));
    json::Value nested = json::Value::MakeObject();
    nested.Set(L"inner", json::Value(false));
    items.Push(std::move(nested));
    root.Set(L"items", std::move(items));

    const std::wstring text = json::SerializeText(root);
    json::Value parsed;
    CHECK(json::Parse(text, parsed, &error));
    CHECK_TEXT(parsed.GetString(L"targetDirectory"), L"C:\\Users\\徐\\桌面");
    CHECK_INT(parsed.GetInt64(L"big"), 9007199254740993ll);
    CHECK(parsed.GetDouble(L"ratio") == 2.5);
    CHECK(parsed.GetBool(L"enabled", false));
    CHECK_TEXT(parsed.GetString(L"escaped"), L"引号\" 反斜杠\\ 换行\n制表\t");
    const json::Value* nothing = parsed.Find(L"nothing");
    CHECK(nothing != nullptr && nothing->IsNull());
    const json::Value* parsedItems = parsed.Find(L"items");
    CHECK(parsedItems != nullptr && parsedItems->Size() == 3);
    CHECK_INT(parsedItems->At(0).AsInt64(), 1);
    CHECK_TEXT(parsedItems->At(1).AsString(), L"二");
    CHECK(!parsedItems->At(2).GetBool(L"inner", true));

    // 键序保持插入顺序（不排序）
    const size_t posTarget = text.find(L"\"targetDirectory\"");
    const size_t posCount = text.find(L"\"count\"");
    const size_t posItems = text.find(L"\"items\"");
    CHECK(posTarget != std::wstring::npos && posCount != std::wstring::npos &&
          posItems != std::wstring::npos);
    CHECK(posTarget < posCount && posCount < posItems);

    // \u 转义（含代理对）
    json::Value escapeValue;
    CHECK(json::Parse(L"\"\\u4e2d\\u6587\"", escapeValue, &error));
    CHECK_TEXT(escapeValue.AsString(), L"中文");
    CHECK(json::Parse(L"\"\\ud83d\\ude00\"", escapeValue, &error));
    CHECK_INT(escapeValue.AsString().size(), 2); // 代理对 → 两个 wchar_t

    // 数字形态
    json::Value number;
    CHECK(json::Parse(L"-1.5e2", number, &error));
    CHECK(number.AsDouble() == -150.0);
    CHECK(json::Parse(L"0", number, &error));
    CHECK(number.IsIntLike());
    CHECK(json::Parse(L"12345678901234", number, &error));
    CHECK_INT(number.AsInt64(), 12345678901234ll);

    // 深嵌套：拒绝而不是栈溢出
    std::wstring deep;
    for (int i = 0; i < 200; ++i) {
        deep.push_back(L'[');
    }
    for (int i = 0; i < 200; ++i) {
        deep.push_back(L']');
    }
    json::Value deepValue;
    CHECK(!json::Parse(deep, deepValue, &error));
    CHECK(!error.empty());

    // 非法输入
    const wchar_t* invalidInputs[] = {L"",
                                      L"{",
                                      L"{\"a\":}",
                                      L"[1,2",
                                      L"{\"a\" 1}",
                                      L"{}x",
                                      L"nul",
                                      L"01",
                                      L"'a'",
                                      L"{\"a\":1,}",
                                      L"[1,]",
                                      L"{a:1}",
                                      L"\"未闭合",
                                      L"tru"};
    for (const wchar_t* invalid : invalidInputs) {
        json::Value value;
        const bool ok = json::Parse(invalid, value, &error);
        if (ok) {
            std::printf("    意外接受非法输入：%s\n", util::ToUtf8(invalid).c_str());
        }
        CHECK(!ok);
    }

    // 文件往返（UTF-8）
    const std::wstring jsonPath = path::Combine(TempRoot(), L"json_roundtrip.json");
    std::wstring saveError;
    CHECK(json::SaveFile(jsonPath, root, &saveError));
    json::Value fromFile;
    CHECK(json::LoadFile(jsonPath, fromFile, &error));
    CHECK_TEXT(fromFile.GetString(L"targetDirectory"), L"C:\\Users\\徐\\桌面");
    CHECK_INT(fromFile.GetInt64(L"big"), 9007199254740993ll);
}

// ---------------------------------------------------------------------------
//  3. settings
// ---------------------------------------------------------------------------

settings::AppSettings MakeFeatureSettings() {
    settings::AppSettings s;
    settings::SetDefaults(s);

    s.targetDirectory = L"D:\\特征\\目标";
    s.copyPpt = false;
    s.copyWord = false;
    s.copyExcel = false;
    s.copyPdf = false;
    s.copyImage = false;
    s.copyVideo = false;
    s.copyAudio = false;
    s.copyCompressed = false;
    s.copyAllFiles = true;
    s.useCustomExtensions = true;
    s.customExtensions = L"abc,def";
    s.duplicateFileAction = 3;
    s.enableFileSizeLimit = true;
    s.maxFileSizeMb = 777;
    s.enableFileNameFilter = true;
    s.fileNameKeywords = L"kw1,kw2";
    s.enableFolderNameFilter = true;
    s.folderNameKeywords = L"fk1";
    s.createDirectoryTree = false;
    s.limitDirectoryDepth = true;
    s.maxDirectoryDepth = 9;
    s.logSuccess = false;
    s.logErrors = false;
    s.logNeutral = false;
    s.saveLogToFile = false;
    s.showLogInWindow = false;
    s.enableStopCopyFile = true;
    s.stopCopyFileName = L"halt.flag";
    s.enableReverseCopyFile = true;
    s.reverseCopyFileName = L"back.flag";
    s.enableSpeedLimit = true;
    s.speedLimitMinutes = 17;
    s.speedLimitIndex = 3;
    s.autoStart = true;
    s.autoStartHidden = true;
    s.server.serverAddress = L"192.0.2.10";
    s.server.port = 8443;
    s.server.useHttps = false;
    s.server.password = L"p@ss中文";
    s.server.apiToken = L"tok-abc-123";
    s.server.remoteRootPath = L"/root";
    s.server.apiBasePath = L"/api/x";
    s.server.validateCertificate = false;
    s.server.timeoutSeconds = 77;
    s.server.chunkSizeBytes = 65536;
    s.server.maxRetries = 7;
    s.saveLocation = 1;
    s.useChunkedUpload = false;
    s.showTrayIcon = false;
    s.showCompletionNotify = false;
    s.enableWhitelist = true;
    s.whitelistDriveIds = L"ABCD1234,EFGH5678";
    return s;
}

// 逐字段比对（43 顶层 + 11 server），任何一项不同都会打印字段名
void CompareAllFields(const settings::AppSettings& a, const settings::AppSettings& b) {
    CHECK_TEXT(a.targetDirectory, b.targetDirectory);
    CHECK_INT(a.copyPpt, b.copyPpt);
    CHECK_INT(a.copyWord, b.copyWord);
    CHECK_INT(a.copyExcel, b.copyExcel);
    CHECK_INT(a.copyPdf, b.copyPdf);
    CHECK_INT(a.copyImage, b.copyImage);
    CHECK_INT(a.copyVideo, b.copyVideo);
    CHECK_INT(a.copyAudio, b.copyAudio);
    CHECK_INT(a.copyCompressed, b.copyCompressed);
    CHECK_INT(a.copyAllFiles, b.copyAllFiles);
    CHECK_INT(a.useCustomExtensions, b.useCustomExtensions);
    CHECK_TEXT(a.customExtensions, b.customExtensions);
    CHECK_INT(a.duplicateFileAction, b.duplicateFileAction);
    CHECK_INT(a.enableFileSizeLimit, b.enableFileSizeLimit);
    CHECK_INT(a.maxFileSizeMb, b.maxFileSizeMb);
    CHECK_INT(a.enableFileNameFilter, b.enableFileNameFilter);
    CHECK_TEXT(a.fileNameKeywords, b.fileNameKeywords);
    CHECK_INT(a.enableFolderNameFilter, b.enableFolderNameFilter);
    CHECK_TEXT(a.folderNameKeywords, b.folderNameKeywords);
    CHECK_INT(a.createDirectoryTree, b.createDirectoryTree);
    CHECK_INT(a.limitDirectoryDepth, b.limitDirectoryDepth);
    CHECK_INT(a.maxDirectoryDepth, b.maxDirectoryDepth);
    CHECK_INT(a.logSuccess, b.logSuccess);
    CHECK_INT(a.logErrors, b.logErrors);
    CHECK_INT(a.logNeutral, b.logNeutral);
    CHECK_INT(a.saveLogToFile, b.saveLogToFile);
    CHECK_INT(a.showLogInWindow, b.showLogInWindow);
    CHECK_INT(a.enableStopCopyFile, b.enableStopCopyFile);
    CHECK_TEXT(a.stopCopyFileName, b.stopCopyFileName);
    CHECK_INT(a.enableReverseCopyFile, b.enableReverseCopyFile);
    CHECK_TEXT(a.reverseCopyFileName, b.reverseCopyFileName);
    CHECK_INT(a.enableSpeedLimit, b.enableSpeedLimit);
    CHECK_INT(a.speedLimitMinutes, b.speedLimitMinutes);
    CHECK_INT(a.speedLimitIndex, b.speedLimitIndex);
    CHECK_INT(a.autoStart, b.autoStart);
    CHECK_INT(a.autoStartHidden, b.autoStartHidden);
    CHECK_TEXT(a.server.serverAddress, b.server.serverAddress);
    CHECK_INT(a.server.port, b.server.port);
    CHECK_INT(a.server.useHttps, b.server.useHttps);
    CHECK_TEXT(a.server.password, b.server.password);
    CHECK_TEXT(a.server.apiToken, b.server.apiToken);
    CHECK_TEXT(a.server.remoteRootPath, b.server.remoteRootPath);
    CHECK_TEXT(a.server.apiBasePath, b.server.apiBasePath);
    CHECK_INT(a.server.validateCertificate, b.server.validateCertificate);
    CHECK_INT(a.server.timeoutSeconds, b.server.timeoutSeconds);
    CHECK_INT(a.server.chunkSizeBytes, b.server.chunkSizeBytes);
    CHECK_INT(a.server.maxRetries, b.server.maxRetries);
    CHECK_INT(a.saveLocation, b.saveLocation);
    CHECK_INT(a.useChunkedUpload, b.useChunkedUpload);
    CHECK_INT(a.showTrayIcon, b.showTrayIcon);
    CHECK_INT(a.showCompletionNotify, b.showCompletionNotify);
    CHECK_INT(a.enableWhitelist, b.enableWhitelist);
    CHECK_TEXT(a.whitelistDriveIds, b.whitelistDriveIds);
}

void TestSettingsDefaults() {
    Section("settings: 默认值（基线 §1.1 / §1.2）");

    settings::AppSettings s;
    settings::SetDefaults(s);

    CHECK(s.targetDirectory.empty());
    CHECK(s.copyPpt && s.copyWord && s.copyExcel && s.copyPdf && s.copyImage && s.copyVideo &&
          s.copyAudio && s.copyCompressed);
    CHECK(!s.copyAllFiles);
    CHECK(!s.useCustomExtensions);
    CHECK(s.customExtensions.empty());
    CHECK_INT(s.duplicateFileAction, 0);
    CHECK(!s.enableFileSizeLimit);
    CHECK_INT(s.maxFileSizeMb, 100);
    CHECK(!s.enableFileNameFilter);
    CHECK(s.fileNameKeywords.empty());
    CHECK(!s.enableFolderNameFilter);
    CHECK(s.folderNameKeywords.empty());
    CHECK(s.createDirectoryTree); // 默认为 true
    CHECK(!s.limitDirectoryDepth);
    CHECK_INT(s.maxDirectoryDepth, 3);
    CHECK(s.logSuccess && s.logErrors && s.logNeutral);
    CHECK(s.saveLogToFile && s.showLogInWindow);
    CHECK(!s.enableStopCopyFile);
    CHECK_TEXT(s.stopCopyFileName, L"stop.copy");
    CHECK(!s.enableReverseCopyFile);
    CHECK_TEXT(s.reverseCopyFileName, L"reverse.copy");
    CHECK(!s.enableSpeedLimit);
    CHECK_INT(s.speedLimitMinutes, 5);
    CHECK_INT(s.speedLimitIndex, 1);
    CHECK(!s.autoStart);
    CHECK(!s.autoStartHidden);
    CHECK_INT(s.saveLocation, 0);
    CHECK(s.useChunkedUpload);
    CHECK(s.showTrayIcon);
    CHECK(s.showCompletionNotify);
    CHECK(!s.enableWhitelist);
    CHECK(s.whitelistDriveIds.empty());

    CHECK(s.server.serverAddress.empty());
    CHECK_INT(s.server.port, 443);
    CHECK(s.server.useHttps);
    CHECK(s.server.password.empty());
    CHECK(s.server.apiToken.empty());
    CHECK_TEXT(s.server.remoteRootPath, L"/");
    CHECK_TEXT(s.server.apiBasePath, L"/api/file");
    CHECK(s.server.validateCertificate);
    CHECK_INT(s.server.timeoutSeconds, 30);
    CHECK_INT(s.server.chunkSizeBytes, 1024 * 1024);
    CHECK_INT(s.server.maxRetries, 3);

    // 计算属性与 C# 同名实现一致
    settings::AppSettings url = s;
    url.server.serverAddress = L"example.com";
    url.server.port = 8443;
    url.server.useHttps = true;
    CHECK_TEXT(url.server.BaseUrl(), L"https://example.com:8443");
    CHECK_TEXT(url.server.ApiBaseUrl(), L"https://example.com:8443/api/file");
}

void TestDpapi() {
    Section("settings: DPAPI 与 entropy");

    CHECK_TEXT(settings::EntropyText(), L"U盘文件复制器_SALT_2024");

    // 与 C# 逐字节一致：UTF-8("U盘文件复制器_SALT_2024")，共 29 字节
    const std::string utf8 = util::ToUtf8(settings::EntropyText());
    const unsigned char expected[29] = {0x55, 0xE7, 0x9B, 0x98, 0xE6, 0x96, 0x87, 0xE4, 0xBB, 0xB6,
                                        0xE5, 0xA4, 0x8D, 0xE5, 0x88, 0xB6, 0xE5, 0x99, 0xA8, 0x5F,
                                        0x53, 0x41, 0x4C, 0x54, 0x5F, 0x32, 0x30, 0x32, 0x34};
    CHECK_INT(utf8.size(), 29);
    bool entroyMatches = utf8.size() == 29;
    for (size_t i = 0; entroyMatches && i < 29; ++i) {
        entroyMatches = static_cast<unsigned char>(utf8[i]) == expected[i];
    }
    CHECK(entroyMatches);

    // 往返
    const std::wstring secret = L"tok-中文-1234567890";
    const std::wstring encrypted = settings::EncryptSecret(secret);
    CHECK(!encrypted.empty());
    CHECK(encrypted != secret);
    CHECK(encrypted.find(L'-') == std::wstring::npos); // Base64 里没有 '-'

    std::wstring decrypted;
    CHECK(settings::DecryptSecret(encrypted, decrypted));
    CHECK_TEXT(decrypted, secret);

    // 每次加密结果不同（含随机盐），但都能解回同一明文
    const std::wstring encryptedAgain = settings::EncryptSecret(secret);
    std::wstring decryptedAgain;
    CHECK(settings::DecryptSecret(encryptedAgain, decryptedAgain));
    CHECK_TEXT(decryptedAgain, secret);

    // 空值语义
    CHECK(settings::EncryptSecret(L"").empty());
    std::wstring emptyDecrypted = L"x";
    CHECK(settings::DecryptSecret(L"", emptyDecrypted));
    CHECK(emptyDecrypted.empty());

    // 非法 Base64 / 篡改密文 → 失败而不是崩溃
    std::wstring broken;
    CHECK(!settings::DecryptSecret(L"!!!not-base64!!!", broken));
    std::wstring tampered = encrypted;
    tampered[0] = tampered[0] == L'A' ? L'B' : L'A';
    CHECK(!settings::DecryptSecret(tampered, broken));
}

void TestSettingsPersistence(const std::wstring& root) {
    Section("settings: 43+11 字段往返 / 原子写 / 损坏回退 / 加载门禁");

    auto& store = settings::Store::Instance();
    const std::wstring savePath = path::Combine(root, L"persist\\settings.json");
    store.SetFilePathForTesting(savePath);

    // 往返
    const settings::AppSettings features = MakeFeatureSettings();
    std::wstring error;
    CHECK(store.Save(features, &error));
    CHECK(path::FileExists(savePath));
    CHECK(!path::FileExists(savePath + L".tmp")); // 原子写不留残余

    settings::AppSettings loaded;
    CHECK(store.Load(loaded, &error));
    CompareAllFields(features, loaded);

    // 落盘为 UTF-8 camelCase
    const std::string bytes = ReadAllBytes(savePath);
    const std::string text(bytes.begin(), bytes.end());
    CHECK(text.find("\"targetDirectory\"") != std::string::npos);
    CHECK(text.find("\"maxFileSizeMB\"") != std::string::npos);
    CHECK(text.find("\"apiToken\"") != std::string::npos);
    CHECK(text.find("\"TargetDirectory\"") == std::string::npos); // 不能是 PascalCase

    // 覆盖保存不残留旧值（写第二次，字段值全部不同）
    settings::AppSettings second;
    settings::SetDefaults(second);
    second.targetDirectory = L"E:\\第二次";
    second.maxFileSizeMb = 42;
    CHECK(store.Save(second, &error));
    settings::AppSettings reloaded;
    CHECK(store.Load(reloaded, &error));
    CHECK_TEXT(reloaded.targetDirectory, L"E:\\第二次");
    CHECK_INT(reloaded.maxFileSizeMb, 42);
    CHECK(reloaded.copyPpt); // 回落默认

    // 损坏 JSON → 回退默认值（返回 false，不崩溃）
    const std::wstring corruptPath = path::Combine(root, L"persist\\corrupt.json");
    CHECK(WriteBytes(corruptPath, "{ this is not json "));
    store.SetFilePathForTesting(corruptPath);
    settings::AppSettings corrupted = MakeFeatureSettings();
    std::wstring corruptError;
    CHECK(!store.Load(corrupted, &corruptError));
    CHECK(!corruptError.empty());
    CHECK_INT(corrupted.maxFileSizeMb, 100);
    CHECK(corrupted.copyPpt);

    // 空文件同样回退
    const std::wstring emptyPath = path::Combine(root, L"persist\\empty.json");
    CHECK(WriteBytes(emptyPath, ""));
    store.SetFilePathForTesting(emptyPath);
    settings::AppSettings emptySettings = MakeFeatureSettings();
    CHECK(!store.Load(emptySettings, &corruptError));
    CHECK_INT(emptySettings.maxFileSizeMb, 100);

    // 缺失文件 → 默认值且算成功
    store.SetFilePathForTesting(path::Combine(root, L"persist\\missing.json"));
    settings::AppSettings missing;
    CHECK(store.Load(missing, &error));
    CHECK_INT(missing.maxFileSizeMb, 100);

    // 加载门禁：加载期间所有保存请求静默丢弃
    const std::wstring gatePath = path::Combine(root, L"gate\\settings.json");
    store.SetFilePathForTesting(gatePath);
    settings::AppSettings gateSettings = MakeFeatureSettings();
    store.BeginLoad();
    CHECK(store.IsLoading());
    std::wstring gateError;
    CHECK(!store.RequestSave(gateSettings, &gateError));
    CHECK(!path::FileExists(gatePath));
    store.EndLoad();
    CHECK(!store.IsLoading());
    CHECK(store.RequestSave(gateSettings, &gateError));
    CHECK(path::FileExists(gatePath));

    // Load 内部会自行开/关门禁（对应 C# 的 finally）
    settings::AppSettings afterLoad;
    CHECK(store.Load(afterLoad, &error));
    CHECK(!store.IsLoading());
    afterLoad.maxFileSizeMb = 321;
    CHECK(store.RequestSave(afterLoad, &gateError));
    settings::AppSettings afterReload;
    CHECK(store.Load(afterReload, &error));
    CHECK_INT(afterReload.maxFileSizeMb, 321);
}

void TestSettingsXmlImport(const std::wstring& root) {
    Section("settings: settings.xml 导入");

    const std::wstring legacyDir = path::Combine(root, L"legacy");
    CHECK(settings::EnsureDirectory(legacyDir));
    const std::wstring xmlPath = path::Combine(legacyDir, L"settings.xml");
    const std::wstring jsonPath = path::Combine(legacyDir, L"settings.json");

    const std::wstring encryptedPassword = settings::EncryptSecret(L"legacy-pass");
    const std::wstring encryptedToken = settings::EncryptSecret(L"legacy-token");

    // 模拟 XmlSerializer 的真实输出：声明 + 命名空间属性 + 空元素 + 实体转义
    std::wstring xml;
    xml += L"<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
    xml += L"<AppSettings xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
           L"xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\r\n";
    xml += L"  <TargetDirectory>E:\\导入\\目标</TargetDirectory>\r\n";
    xml += L"  <CopyPpt>false</CopyPpt>\r\n";
    xml += L"  <CopyWord>true</CopyWord>\r\n";
    xml += L"  <DuplicateFileAction>2</DuplicateFileAction>\r\n";
    xml += L"  <MaxFileSizeMB>512</MaxFileSizeMB>\r\n";
    xml += L"  <FileNameKeywords>a&amp;b&lt;c</FileNameKeywords>\r\n";
    xml += L"  <CreateDirectoryTree>false</CreateDirectoryTree>\r\n";
    xml += L"  <SpeedLimitIndex>2</SpeedLimitIndex>\r\n";
    xml += L"  <AutoStartHidden>true</AutoStartHidden>\r\n";
    xml += L"  <Server>\r\n";
    xml += L"    <ServerAddress>10.0.0.5</ServerAddress>\r\n";
    xml += L"    <Port>9443</Port>\r\n";
    xml += L"    <UseHttps>false</UseHttps>\r\n";
    xml += L"    <Password>" + encryptedPassword + L"</Password>\r\n";
    xml += L"    <ApiToken>" + encryptedToken + L"</ApiToken>\r\n";
    xml += L"    <ChunkSizeBytes>1048576</ChunkSizeBytes>\r\n";
    xml += L"  </Server>\r\n";
    xml += L"  <SaveLocation>1</SaveLocation>\r\n";
    xml += L"  <WhitelistDriveIds>AAAA1111</WhitelistDriveIds>\r\n";
    xml += L"  <CustomExtensions />\r\n";
    xml += L"</AppSettings>\r\n";
    CHECK(WriteTextUtf8(xmlPath, xml));

    auto& store = settings::Store::Instance();
    store.SetFilePathForTesting(jsonPath);

    settings::AppSettings importedSettings;
    bool imported = false;
    std::wstring error;
    CHECK(store.ImportLegacyXmlFrom(xmlPath, importedSettings, &imported, &error));
    CHECK(imported);
    CHECK_TEXT(importedSettings.targetDirectory, L"E:\\导入\\目标");
    CHECK(!importedSettings.copyPpt);
    CHECK(importedSettings.copyWord);
    CHECK_INT(importedSettings.duplicateFileAction, 2);
    CHECK_INT(importedSettings.maxFileSizeMb, 512);
    CHECK_TEXT(importedSettings.fileNameKeywords, L"a&b<c"); // 实体已解码
    CHECK(!importedSettings.createDirectoryTree);
    CHECK_INT(importedSettings.speedLimitIndex, 2);
    CHECK(importedSettings.autoStartHidden);
    CHECK_TEXT(importedSettings.server.serverAddress, L"10.0.0.5");
    CHECK_INT(importedSettings.server.port, 9443);
    CHECK(!importedSettings.server.useHttps);
    CHECK_TEXT(importedSettings.server.password, L"legacy-pass"); // DPAPI 已解密
    CHECK_TEXT(importedSettings.server.apiToken, L"legacy-token");
    CHECK_INT(importedSettings.server.chunkSizeBytes, 1048576);
    CHECK_INT(importedSettings.saveLocation, 1);
    CHECK_TEXT(importedSettings.whitelistDriveIds, L"AAAA1111");
    CHECK(importedSettings.customExtensions.empty()); // <CustomExtensions />

    // 原文件已改名，JSON 已产生，且能读回同样的值
    CHECK(!path::FileExists(xmlPath));
    CHECK(path::FileExists(xmlPath + L".imported"));
    CHECK(path::FileExists(jsonPath));
    settings::AppSettings reread;
    CHECK(store.Load(reread, &error));
    CompareAllFields(importedSettings, reread);

    // 再次调用不应重复导入（JSON 已存在 → 走 ImportLegacyXml 会被跳过）
    bool secondImport = true;
    CHECK(store.ImportLegacyXml(importedSettings, &secondImport, &error));
    CHECK(!secondImport);

    // 畸形 XML：报错但不崩溃、不生成 JSON
    const std::wstring badDir = path::Combine(root, L"legacy_bad");
    CHECK(settings::EnsureDirectory(badDir));
    const std::wstring badXml = path::Combine(badDir, L"settings.xml");
    CHECK(WriteBytes(badXml, "<AppSettings><TargetDirectory>x</AppSettings>"));
    store.SetFilePathForTesting(path::Combine(badDir, L"settings.json"));
    settings::AppSettings badSettings;
    bool badImported = true;
    std::wstring badError;
    CHECK(!store.ImportLegacyXmlFrom(badXml, badSettings, &badImported, &badError));
    CHECK(!badError.empty());
    CHECK(!badImported);
    CHECK(!path::FileExists(path::Combine(badDir, L"settings.json")));
    CHECK(path::FileExists(badXml)); // 失败不删原文件

    // 非 AppSettings 根元素 → 拒绝
    const std::wstring wrongRoot = path::Combine(badDir, L"wrong.xml");
    CHECK(WriteBytes(wrongRoot, "<?xml version=\"1.0\"?><Config><A>1</A></Config>"));
    store.SetFilePathForTesting(path::Combine(badDir, L"settings2.json"));
    settings::AppSettings wrongSettings;
    bool wrongImported = true;
    CHECK(!store.ImportLegacyXmlFrom(wrongRoot, wrongSettings, &wrongImported, &badError));
    CHECK(!wrongImported);
}

void TestLocalOnly() {
    Section("settings: 单机版（LOCAL_ONLY）语义");

    settings::AppSettings s = MakeFeatureSettings();
    settings::ApplyLocalOnlyDefaults(s);
    CHECK_INT(s.saveLocation, 0);
    CHECK(!s.useChunkedUpload);
    CHECK_INT(s.server.port, 443);
    CHECK(s.server.useHttps);
    CHECK(s.server.password.empty());
    CHECK(s.server.apiToken.empty());
    CHECK(s.server.serverAddress.empty());

    // 其余字段不受影响
    CHECK_TEXT(s.targetDirectory, L"D:\\特征\\目标");
    CHECK_INT(s.maxFileSizeMb, 777);

    // 不写 server 节点，但其它字段完整
    const json::Value lite = settings::ToJson(s, false);
    CHECK(lite.Find(L"server") == nullptr);
    CHECK_TEXT(lite.GetString(L"targetDirectory"), L"D:\\特征\\目标");
    CHECK_INT(lite.Size(), 42); // 43 个顶层字段去掉 server

    // 读到带 server 的 JSON 时忽略 server
    const json::Value full = settings::ToJson(MakeFeatureSettings(), true);
    settings::AppSettings fromFull;
    CHECK(settings::FromJson(full, fromFull, false));
    CHECK(fromFull.server.serverAddress.empty());
    CHECK_INT(fromFull.server.port, 443);
    CHECK_TEXT(fromFull.targetDirectory, L"D:\\特征\\目标");
}

void TestPathResolution() {
    Section("settings: 三段路径解析");

    auto& store = settings::Store::Instance();
    store.SetFilePathForTesting(L"");
    store.ResetPathCache();

    const std::wstring resolved = store.FilePath();
    CHECK(!resolved.empty());
    CHECK_TEXT(path::FileName(resolved), L"settings.json");

    // 必须落在三级候选之一：exe 目录 / %APPDATA%\UsbCopy / 桌面\UsbCopy
    const std::wstring exeDir = path::ExeDirectory();
    const std::wstring roaming = settings::RoamingDirectory();
    const std::wstring desktop = path::Combine(settings::DesktopDirectory(), L"UsbCopy");
    const bool inExe = path::IsUnder(exeDir, resolved);
    const bool inRoaming = path::IsUnder(roaming, resolved);
    const bool inDesktop = path::IsUnder(desktop, resolved);
    CHECK(inExe || inRoaming || inDesktop);
    if (!inExe) {
        SKIP("本机 exe 目录不可写，配置已回退到 APPDATA/桌面（正是三级解析的兜底路径）");
    }

    // 目录缓存与文件一致
    CHECK_TEXT(store.Directory(), path::DirectoryName(resolved));

    // 回退目标本身可用（本机 exe 目录可写，所以解析不会走到这里；
    // 但 M3 搬到 Program Files 后靠的就是这两级，先确认它们真的能写）
    const std::wstring roamingDir = settings::RoamingDirectory();
    CHECK_TEXT(path::FileName(roamingDir), L"UsbCopy");
    const bool roamingExisted = path::IsDirectory(roamingDir);
    if (!roamingExisted) {
        CHECK(settings::EnsureDirectory(roamingDir));
    }
    CHECK(path::IsDirectoryWritable(roamingDir));
    if (!roamingExisted) {
        // 测试不留残留：只删自己刚建的空目录
        RemoveDirectoryW(path::ToExtended(roamingDir).c_str());
    }

    const std::wstring desktopDir = settings::DesktopDirectory();
    CHECK(!desktopDir.empty());
    CHECK(path::IsDirectory(desktopDir));
    CHECK(path::IsDirectoryWritable(desktopDir));

    // C# 遗留目录名是中文（用于导入，不用于存放）
    CHECK_TEXT(path::FileName(settings::LegacyRoamingDirectory()), L"U盘文件复制器");
}

void TestXmlLite() {
    Section("xml_lite");

    xml::Node root;
    std::wstring error;
    CHECK(xml::Parse(L"<a><b>1</b><c x=\"y\">2</c><d/></a>", root, &error));
    CHECK_TEXT(root.name, L"a");
    CHECK_TEXT(root.ChildText(L"b"), L"1");
    CHECK_TEXT(root.ChildText(L"c"), L"2");
    CHECK(root.Child(L"d") != nullptr);
    CHECK_TEXT(root.ChildText(L"d"), L"");

    // 注释 / CDATA / 实体 / 数字实体
    CHECK(xml::Parse(L"<a><!-- x --><b><![CDATA[<raw>]]></b><c>&#65;&#x42;</c></a>", root, &error));
    CHECK_TEXT(root.ChildText(L"b"), L"<raw>");
    CHECK_TEXT(root.ChildText(L"c"), L"AB");

    // 属性值里的 '>' 不能截断标签
    CHECK(xml::Parse(L"<a b=\"x>y\"><c>ok</c></a>", root, &error));
    CHECK_TEXT(root.ChildText(L"c"), L"ok");

    // 错误：标签不匹配 / 未闭合 / 嵌套过深
    CHECK(!xml::Parse(L"<a><b></a>", root, &error));
    CHECK(!xml::Parse(L"<a><b>1</b>", root, &error));
    CHECK(!xml::Parse(L"", root, &error));
    std::wstring deep;
    for (int i = 0; i < 100; ++i) {
        deep += L"<a>";
    }
    CHECK(!xml::Parse(deep, root, &error));
}

// ---------------------------------------------------------------------------
//  4. log
// ---------------------------------------------------------------------------

std::wstring BuildExpectedLine(const std::wstring& message) {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    wchar_t buffer[24]{};
    swprintf_s(buffer, L"[%02u:%02u:%02u] ", static_cast<unsigned>(time.wHour),
               static_cast<unsigned>(time.wMinute), static_cast<unsigned>(time.wSecond));
    return std::wstring(buffer) + message;
}

void TestLog(const std::wstring& root) {
    Section("log: 行格式 / 三开关 / 900 行 / 5MB 轮转 / 并发");

    auto& engine = log::Engine::Instance();
    const std::wstring logDir = path::Combine(root, L"logs");
    CHECK(settings::EnsureDirectory(logDir));
    const std::wstring logPath = path::Combine(logDir, L"CopyLog.txt");

    log::Options options;
    options.filePath = logPath;
    engine.Configure(options);
    engine.ClearWindow();

    // --- 行格式：[HH:mm:ss] 消息（与 C# 逐字符一致；跨秒边界则重试）---
    const std::wstring message = L"找到 U 盘：E:\\（测试）";
    bool formatOk = false;
    std::wstring actual;
    std::wstring expected;
    for (int attempt = 0; attempt < 3 && !formatOk; ++attempt) {
        engine.ClearWindow();
        engine.Write(message);
        const std::vector<std::wstring> lines = engine.WindowLines();
        actual = lines.empty() ? L"" : lines.front();
        expected = BuildExpectedLine(message);
        formatOk = (actual == expected);
    }
    CHECK(formatOk);
    if (!formatOk) {
        std::printf("    实际=[%s]\n    期望=[%s]\n", util::ToUtf8(actual).c_str(),
                    util::ToUtf8(expected).c_str());
    }

    // 文件内容与窗口一致，CRLF 结尾，UTF-8 无 BOM
    const std::string fileBytes = ReadAllBytes(logPath);
    CHECK(fileBytes.find(util::ToUtf8(expected) + "\r\n") != std::string::npos);
    CHECK(!(fileBytes.size() >= 3 && static_cast<unsigned char>(fileBytes[0]) == 0xEF));

    // --- 三开关 ---
    log::Options filter;
    filter.showInWindow = false;
    filter.saveToFile = false;

    auto shouldLog = [&filter](bool logSuccess, bool logErrors, bool logNeutral,
                               const wchar_t* text, bool isError) {
        filter.logSuccess = logSuccess;
        filter.logErrors = logErrors;
        filter.logNeutral = logNeutral;
        return log::Engine::ShouldLog(filter, text, isError);
    };

    CHECK(!shouldLog(false, true, true, L"复制成功", false));      // LogSuccess 关
    CHECK(!shouldLog(false, true, true, L"未成功完成", false));    // Contains 也算成功类
    CHECK(shouldLog(false, true, true, L"开始复制", false));       // 普通消息走 LogNeutral
    CHECK(!shouldLog(true, true, false, L"开始复制", false));      // LogNeutral 关
    CHECK(shouldLog(true, true, false, L"复制成功", false));       // 成功类不受影响
    CHECK(!shouldLog(true, false, true, L"写入失败", true));       // LogErrors 关
    CHECK(shouldLog(true, true, true, L"写入失败", true));
    // isError=true 时不做「成功」判断：LogSuccess 关了也照写
    CHECK(shouldLog(false, true, true, L"错误里提到成功", true));

    // --- 900 行上限，逐行丢弃最早的 ---
    log::Options windowOnly;
    windowOnly.showInWindow = true;
    windowOnly.saveToFile = false;
    engine.Configure(windowOnly);
    engine.ClearWindow();
    for (int i = 0; i < 950; ++i) {
        engine.Write(L"line " + std::to_wstring(i));
    }
    CHECK_INT(engine.WindowLineCount(), 900);
    const std::vector<std::wstring> lines = engine.WindowLines();
    CHECK(lines.front().find(L"line 50") != std::wstring::npos);
    CHECK(lines.back().find(L"line 949") != std::wstring::npos);

    // --- 轮转：小阈值（验证逻辑）---
    const std::wstring rotateDir = path::Combine(root, L"rotate");
    CHECK(settings::EnsureDirectory(rotateDir));
    const std::wstring rotatePath = path::Combine(rotateDir, L"CopyLog.txt");
    log::Options rotate;
    rotate.filePath = rotatePath;
    rotate.maxWindowLines = 900;
    rotate.maxFileSizeBytes = 4096;
    engine.Configure(rotate);
    const std::wstring payload(200, L'x');
    for (int i = 0; i < 80; ++i) {
        engine.Write(payload);
    }
    CHECK(CountMatching(rotateDir, L"CopyLog_*.txt") >= 1);
    CHECK(FileSize(rotatePath) <= 4096 + 512);

    // --- 轮转：默认 5MB 阈值 ---
    const std::wstring bigPath = path::Combine(rotateDir, L"Big.txt");
    log::Options big;
    big.filePath = bigPath;
    big.maxWindowLines = 900;
    engine.Configure(big);
    const std::wstring chunk(1023, L'y');
    for (int i = 0; i < 5300; ++i) {
        engine.Write(chunk);
    }
    CHECK(CountMatching(rotateDir, L"Big_*.txt") >= 1);
    CHECK(FileSize(bigPath) < 5ull * 1024 * 1024 + 4096);

    // --- 多线程并发 ---
    const std::wstring mtPath = path::Combine(rotateDir, L"MT.txt");
    log::Options multi;
    multi.filePath = mtPath;
    engine.Configure(multi);
    engine.ClearWindow();
    std::vector<std::thread> threads;
    threads.reserve(4);
    for (int t = 0; t < 4; ++t) {
        threads.emplace_back([&engine, t]() {
            for (int i = 0; i < 500; ++i) {
                engine.Write(L"t" + std::to_wstring(t) + L"-" + std::to_wstring(i));
            }
        });
    }
    for (std::thread& thread : threads) {
        thread.join();
    }
    CHECK_INT(engine.WindowLineCount(), 900);
    CHECK_INT(CountLines(mtPath), 2000); // 文件不丢行（5MB 内不轮转）

    // --- 关闭开关后不写文件/不入窗口 ---
    log::Options off;
    off.showInWindow = false;
    off.saveToFile = false;
    engine.Configure(off);
    engine.ClearWindow();
    const std::wstring quietPath = path::Combine(rotateDir, L"Quiet.txt");
    engine.Write(L"不应出现");
    CHECK_INT(engine.WindowLineCount(), 0);
    CHECK(!path::FileExists(quietPath));

    // 恢复默认配置，避免影响后续（当前进程内无其它测试）
    engine.Configure(log::Options());
}

} // namespace

// 探针模式：m1_tests.exe --import-xml <settings.xml>
// 只做「导入 + 打印」，用于把 C# 版真实生成的 settings.xml 拿来与 C++ 解析结果比对。
int RunImportProbe(const std::wstring& xmlPath) {
    // 导入会重命名源文件，先复制一份再导
    const std::wstring probeRoot = TempRoot() + L"_probe";
    settings::EnsureDirectory(probeRoot);
    const std::wstring copyPath = path::Combine(probeRoot, L"settings.xml");

    const std::string bytes = ReadAllBytes(xmlPath);
    if (bytes.empty()) {
        std::printf("ERROR 无法读取 %s\n", util::ToUtf8(xmlPath).c_str());
        return 2;
    }
    WriteBytes(copyPath, bytes);

    settings::Store::Instance().SetFilePathForTesting(path::Combine(probeRoot, L"settings.json"));

    settings::AppSettings s;
    bool imported = false;
    std::wstring error;
    const bool ok = settings::Store::Instance().ImportLegacyXmlFrom(copyPath, s, &imported, &error);

    std::printf("imported=%d ok=%d error=%s\n", imported ? 1 : 0, ok ? 1 : 0,
                util::ToUtf8(error).c_str());
    if (!ok || !imported) {
        RemoveTree(probeRoot);
        return 1;
    }

    // 以 C# 字段名逐行输出，便于与 settings.xml 对照
    std::printf("TargetDirectory=%s\n", util::ToUtf8(s.targetDirectory).c_str());
    std::printf("CopyPpt=%d\nCopyWord=%d\nCopyExcel=%d\nCopyPdf=%d\n", s.copyPpt ? 1 : 0,
                s.copyWord ? 1 : 0, s.copyExcel ? 1 : 0, s.copyPdf ? 1 : 0);
    std::printf("CopyImage=%d\nCopyVideo=%d\nCopyAudio=%d\nCopyCompressed=%d\n",
                s.copyImage ? 1 : 0, s.copyVideo ? 1 : 0, s.copyAudio ? 1 : 0,
                s.copyCompressed ? 1 : 0);
    std::printf("CopyAllFiles=%d\nUseCustomExtensions=%d\n", s.copyAllFiles ? 1 : 0,
                s.useCustomExtensions ? 1 : 0);
    std::printf("CustomExtensions=%s\n", util::ToUtf8(s.customExtensions).c_str());
    std::printf("DuplicateFileAction=%d\n", s.duplicateFileAction);
    std::printf("EnableFileSizeLimit=%d\nMaxFileSizeMB=%lld\n", s.enableFileSizeLimit ? 1 : 0,
                s.maxFileSizeMb);
    std::printf("EnableFileNameFilter=%d\nFileNameKeywords=%s\n", s.enableFileNameFilter ? 1 : 0,
                util::ToUtf8(s.fileNameKeywords).c_str());
    std::printf("EnableFolderNameFilter=%d\nFolderNameKeywords=%s\n",
                s.enableFolderNameFilter ? 1 : 0, util::ToUtf8(s.folderNameKeywords).c_str());
    std::printf("CreateDirectoryTree=%d\nLimitDirectoryDepth=%d\nMaxDirectoryDepth=%d\n",
                s.createDirectoryTree ? 1 : 0, s.limitDirectoryDepth ? 1 : 0, s.maxDirectoryDepth);
    std::printf("LogSuccess=%d\nLogErrors=%d\nLogNeutral=%d\nSaveLogToFile=%d\nShowLogInWindow=%d\n",
                s.logSuccess ? 1 : 0, s.logErrors ? 1 : 0, s.logNeutral ? 1 : 0,
                s.saveLogToFile ? 1 : 0, s.showLogInWindow ? 1 : 0);
    std::printf("EnableStopCopyFile=%d\nStopCopyFileName=%s\n", s.enableStopCopyFile ? 1 : 0,
                util::ToUtf8(s.stopCopyFileName).c_str());
    std::printf("EnableReverseCopyFile=%d\nReverseCopyFileName=%s\n",
                s.enableReverseCopyFile ? 1 : 0, util::ToUtf8(s.reverseCopyFileName).c_str());
    std::printf("EnableSpeedLimit=%d\nSpeedLimitMinutes=%d\nSpeedLimitIndex=%d\n",
                s.enableSpeedLimit ? 1 : 0, s.speedLimitMinutes, s.speedLimitIndex);
    std::printf("AutoStart=%d\nAutoStartHidden=%d\n", s.autoStart ? 1 : 0, s.autoStartHidden ? 1 : 0);
    std::printf("ServerAddress=%s\nPort=%d\nUseHttps=%d\n",
                util::ToUtf8(s.server.serverAddress).c_str(), s.server.port,
                s.server.useHttps ? 1 : 0);
    std::printf("Password=%s\nApiToken=%s\n", util::ToUtf8(s.server.password).c_str(),
                util::ToUtf8(s.server.apiToken).c_str());
    std::printf("RemoteRootPath=%s\nApiBasePath=%s\n",
                util::ToUtf8(s.server.remoteRootPath).c_str(),
                util::ToUtf8(s.server.apiBasePath).c_str());
    std::printf("ValidateCertificate=%d\nTimeoutSeconds=%d\nChunkSizeBytes=%d\nMaxRetries=%d\n",
                s.server.validateCertificate ? 1 : 0, s.server.timeoutSeconds,
                s.server.chunkSizeBytes, s.server.maxRetries);
    std::printf("SaveLocation=%d\nUseChunkedUpload=%d\nShowTrayIcon=%d\nShowCompletionNotify=%d\n",
                s.saveLocation, s.useChunkedUpload ? 1 : 0, s.showTrayIcon ? 1 : 0,
                s.showCompletionNotify ? 1 : 0);
    std::printf("EnableWhitelist=%d\nWhitelistDriveIds=%s\n", s.enableWhitelist ? 1 : 0,
                util::ToUtf8(s.whitelistDriveIds).c_str());

    RemoveTree(probeRoot);
    return 0;
}

int wmain(int argc, wchar_t** argv) {
    SetConsoleOutputCP(CP_UTF8);

    if (argc >= 3 && std::wstring(argv[1]) == L"--import-xml") {
        return RunImportProbe(argv[2]);
    }

    const std::wstring root = TempRoot();
    settings::EnsureDirectory(root);

    std::printf("M1 测试根目录：%s\n", util::ToUtf8(root).c_str());

    TestPathUtil(root);
    TestJson();
    TestSettingsDefaults();
    TestDpapi();
    TestSettingsPersistence(root);
    TestSettingsXmlImport(root);
    TestLocalOnly();
    TestPathResolution();
    TestXmlLite();
    TestLog(root);

    RemoveTree(root);

    std::printf("\n========================================\n");
    // 统计行用 ASCII，避免控制台/重定向编码差异影响日志判读
    std::printf("checks=%d failures=%d skipped=%d\n", g_checks, g_failures, g_skips);
    std::printf(g_failures == 0 ? "RESULT: PASS (M1)\n" : "RESULT: FAIL (see [FAIL] above)\n");
    return g_failures == 0 ? 0 : 1;
}
