#include "settings.h"

#include <windows.h>
#include <shlobj.h>
#include <wincrypt.h>

#include <algorithm>
#include <cerrno>
#include <cstdlib>
#include <vector>

#include "path_util.h"
#include "win_util.h"
#include "xml_lite.h"

#pragma comment(lib, "crypt32.lib")

namespace usbcopy::settings {

namespace {

// DPAPI 附加熵：与 C# SettingsStore.cs:92 完全一致（UTF-8 共 29 字节）
constexpr wchar_t kEntropyText[] = L"U盘文件复制器_SALT_2024";

// 完整版与单机版文件名不同：两者可能被装到同一个可写目录（或共用 %APPDATA%），
// 若同名会互相覆盖（单机版保存时会把 server 节点、SaveLocation 重置）。
// exe 目录下两版本来就各自一个文件夹，文件名不同也不影响任何现有迁移路径。
#ifdef LOCAL_ONLY
constexpr const wchar_t* kFileName = L"settings.lite.json";
#else
constexpr const wchar_t* kFileName = L"settings.json";
#endif
constexpr const wchar_t* kLegacyFileName = L"settings.xml";

std::wstring KnownFolder(int csidl) {
    wchar_t buffer[MAX_PATH]{};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, csidl, nullptr, SHGFP_TYPE_CURRENT, buffer))) {
        return buffer;
    }
    return {};
}

std::wstring TempDirectory() {
    wchar_t buffer[MAX_PATH]{};
    const DWORD length = GetTempPathW(MAX_PATH, buffer);
    if (length == 0 || length > MAX_PATH) {
        return L"C:\\Windows\\Temp";
    }
    return path::FromExtended(std::wstring(buffer, length));
}

std::vector<BYTE> EntropyBytes() {
    const std::string utf8 = util::ToUtf8(kEntropyText);
    std::vector<BYTE> bytes(utf8.size());
    for (size_t i = 0; i < utf8.size(); ++i) {
        bytes[i] = static_cast<BYTE>(utf8[i]);
    }
    return bytes;
}

bool ParseBoolText(const std::wstring& text, bool fallback) {
    if (text == L"true" || text == L"True" || text == L"TRUE") {
        return true;
    }
    if (text == L"false" || text == L"False" || text == L"FALSE") {
        return false;
    }
    return fallback;
}

long long ParseIntText(const std::wstring& text, long long fallback) {
    if (text.empty()) {
        return fallback;
    }
    errno = 0;
    wchar_t* end = nullptr;
    const long long value = wcstoll(text.c_str(), &end, 10);
    if (end == text.c_str() || errno == ERANGE) {
        return fallback;
    }
    return value;
}

// settings.xml 的字段名是 XmlSerializer 的元素名（PascalCase）
void ApplyXmlElement(const xml::Node& root, const wchar_t* name, std::wstring& target) {
    if (const xml::Node* node = root.Child(name)) {
        target = xml::Trim(node->text);
    }
}

void ApplyXmlElement(const xml::Node& root, const wchar_t* name, bool& target) {
    if (const xml::Node* node = root.Child(name)) {
        target = ParseBoolText(xml::Trim(node->text), target);
    }
}

void ApplyXmlElement(const xml::Node& root, const wchar_t* name, int& target) {
    if (const xml::Node* node = root.Child(name)) {
        const long long value = ParseIntText(xml::Trim(node->text), target);
        if (value >= -2147483648ll && value <= 2147483647ll) {
            target = static_cast<int>(value);
        }
    }
}

void ApplyXmlElement(const xml::Node& root, const wchar_t* name, long long& target) {
    if (const xml::Node* node = root.Child(name)) {
        target = ParseIntText(xml::Trim(node->text), target);
    }
}

// 密文字段：解密失败则留空（不中断整体导入）
void ApplyXmlSecret(const xml::Node& root, const wchar_t* name, std::wstring& target) {
    if (const xml::Node* node = root.Child(name)) {
        std::wstring plainText;
        if (DecryptSecret(xml::Trim(node->text), plainText)) {
            target = plainText;
        } else {
            target.clear();
        }
    }
}

void ApplyXmlSettings(const xml::Node& root, AppSettings& settings) {
    ApplyXmlElement(root, L"TargetDirectory", settings.targetDirectory);
    ApplyXmlElement(root, L"CopyPpt", settings.copyPpt);
    ApplyXmlElement(root, L"CopyWord", settings.copyWord);
    ApplyXmlElement(root, L"CopyExcel", settings.copyExcel);
    ApplyXmlElement(root, L"CopyPdf", settings.copyPdf);
    ApplyXmlElement(root, L"CopyImage", settings.copyImage);
    ApplyXmlElement(root, L"CopyVideo", settings.copyVideo);
    ApplyXmlElement(root, L"CopyAudio", settings.copyAudio);
    ApplyXmlElement(root, L"CopyCompressed", settings.copyCompressed);
    ApplyXmlElement(root, L"CopyAllFiles", settings.copyAllFiles);
    ApplyXmlElement(root, L"UseCustomExtensions", settings.useCustomExtensions);
    ApplyXmlElement(root, L"CustomExtensions", settings.customExtensions);
    ApplyXmlElement(root, L"DuplicateFileAction", settings.duplicateFileAction);
    ApplyXmlElement(root, L"EnableFileSizeLimit", settings.enableFileSizeLimit);
    ApplyXmlElement(root, L"MaxFileSizeMB", settings.maxFileSizeMb);
    ApplyXmlElement(root, L"EnableFileNameFilter", settings.enableFileNameFilter);
    ApplyXmlElement(root, L"FileNameKeywords", settings.fileNameKeywords);
    ApplyXmlElement(root, L"EnableFolderNameFilter", settings.enableFolderNameFilter);
    ApplyXmlElement(root, L"FolderNameKeywords", settings.folderNameKeywords);
    ApplyXmlElement(root, L"CreateDirectoryTree", settings.createDirectoryTree);
    ApplyXmlElement(root, L"LimitDirectoryDepth", settings.limitDirectoryDepth);
    ApplyXmlElement(root, L"MaxDirectoryDepth", settings.maxDirectoryDepth);
    ApplyXmlElement(root, L"LogSuccess", settings.logSuccess);
    ApplyXmlElement(root, L"LogErrors", settings.logErrors);
    ApplyXmlElement(root, L"LogNeutral", settings.logNeutral);
    ApplyXmlElement(root, L"SaveLogToFile", settings.saveLogToFile);
    ApplyXmlElement(root, L"ShowLogInWindow", settings.showLogInWindow);
    ApplyXmlElement(root, L"EnableStopCopyFile", settings.enableStopCopyFile);
    ApplyXmlElement(root, L"StopCopyFileName", settings.stopCopyFileName);
    ApplyXmlElement(root, L"EnableReverseCopyFile", settings.enableReverseCopyFile);
    ApplyXmlElement(root, L"ReverseCopyFileName", settings.reverseCopyFileName);
    ApplyXmlElement(root, L"EnableSpeedLimit", settings.enableSpeedLimit);
    ApplyXmlElement(root, L"SpeedLimitMinutes", settings.speedLimitMinutes);
    ApplyXmlElement(root, L"SpeedLimitIndex", settings.speedLimitIndex);
    ApplyXmlElement(root, L"AutoStart", settings.autoStart);
    ApplyXmlElement(root, L"AutoStartHidden", settings.autoStartHidden);
    ApplyXmlElement(root, L"SaveLocation", settings.saveLocation);
    ApplyXmlElement(root, L"UseChunkedUpload", settings.useChunkedUpload);
    ApplyXmlElement(root, L"ShowTrayIcon", settings.showTrayIcon);
    ApplyXmlElement(root, L"ShowCompletionNotify", settings.showCompletionNotify);
    ApplyXmlElement(root, L"EnableWhitelist", settings.enableWhitelist);
    ApplyXmlElement(root, L"WhitelistDriveIds", settings.whitelistDriveIds);

    if (const xml::Node* server = root.Child(L"Server")) {
        ApplyXmlElement(*server, L"ServerAddress", settings.server.serverAddress);
        ApplyXmlElement(*server, L"Port", settings.server.port);
        ApplyXmlElement(*server, L"UseHttps", settings.server.useHttps);
        ApplyXmlSecret(*server, L"Password", settings.server.password);
        ApplyXmlSecret(*server, L"ApiToken", settings.server.apiToken);
        ApplyXmlElement(*server, L"RemoteRootPath", settings.server.remoteRootPath);
        ApplyXmlElement(*server, L"ApiBasePath", settings.server.apiBasePath);
        ApplyXmlElement(*server, L"ValidateCertificate", settings.server.validateCertificate);
        ApplyXmlElement(*server, L"TimeoutSeconds", settings.server.timeoutSeconds);
        ApplyXmlElement(*server, L"ChunkSizeBytes", settings.server.chunkSizeBytes);
        ApplyXmlElement(*server, L"MaxRetries", settings.server.maxRetries);
    }
}

// 时间戳后缀：_YYYYMMDD_HHMMSS
std::wstring TimestampSuffix() {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    wchar_t buffer[32]{};
    swprintf_s(buffer, L"_%04u%02u%02u_%02u%02u%02u", static_cast<unsigned>(time.wYear),
               static_cast<unsigned>(time.wMonth), static_cast<unsigned>(time.wDay),
               static_cast<unsigned>(time.wHour), static_cast<unsigned>(time.wMinute),
               static_cast<unsigned>(time.wSecond));
    return buffer;
}

// 把文件改名到同目录的 <原名><suffix>（重名时再拼时间戳）。
// 用于：导入成功后 *.imported、结构损坏的 XML → *.failed、损坏的 JSON → *.corrupt。
// 一律"改名而不是删除"，用户仍可手工找回内容。
bool RenameToSibling(const std::wstring& filePath, const wchar_t* suffix) {
    std::wstring target = filePath + suffix;
    if (path::FileExists(target)) {
        target = filePath + suffix + TimestampSuffix();
    }
    const std::wstring source = path::ToExtended(filePath);
    if (MoveFileExW(source.c_str(), path::ToExtended(target).c_str(), MOVEFILE_WRITE_THROUGH)) {
        return true;
    }
    // 被占用/跨卷时退化为复制 + 删除，内容保留优先
    if (CopyFileW(source.c_str(), path::ToExtended(target).c_str(), TRUE)) {
        DeleteFileW(source.c_str());
        return true;
    }
    return false;
}

// 导入成功后改名：settings.xml → settings.xml.imported
void RenameToImported(const std::wstring& filePath) {
    RenameToSibling(filePath, L".imported");
}

void ApplyLocalOnlyIfNeeded(AppSettings& settings) {
#ifdef LOCAL_ONLY
    ApplyLocalOnlyDefaults(settings);
#else
    (void)settings;
#endif
}

// 保证 Load 期间无论怎么返回都会解除门禁（C# 的 finally 语义）
struct LoadScope {
    Store& store;
    explicit LoadScope(Store& value) : store(value) { store.BeginLoad(); }
    ~LoadScope() { store.EndLoad(); }
    LoadScope(const LoadScope&) = delete;
    LoadScope& operator=(const LoadScope&) = delete;
};

} // namespace

// ---------------------------------------------------------------------------
//  ServerConfig
// ---------------------------------------------------------------------------

std::wstring ServerConfig::BaseUrl() const {
    std::wstring root = remoteRootPath;
    while (!root.empty() && (root.back() == L'/' || root.back() == L'\\')) {
        root.pop_back();
    }
    wchar_t buffer[32]{};
    swprintf_s(buffer, L"%d", port);
    return (useHttps ? L"https://" : L"http://") + serverAddress + L":" + buffer + root;
}

std::wstring ServerConfig::ApiBaseUrl() const {
    return BaseUrl() + apiBasePath;
}

// ---------------------------------------------------------------------------
//  默认值
// ---------------------------------------------------------------------------

void SetDefaults(AppSettings& settings) {
    settings = AppSettings(); // 全部走 NSDMI，默认值只在结构体定义处维护一份
}

void ApplyLocalOnlyDefaults(AppSettings& settings) {
    settings.server = ServerConfig();
    settings.saveLocation = 0;
    settings.useChunkedUpload = false;
}

const std::wstring& EntropyText() {
    static const std::wstring kText = kEntropyText;
    return kText;
}

// ---------------------------------------------------------------------------
//  DPAPI
// ---------------------------------------------------------------------------

std::wstring EncryptSecret(const std::wstring& plainText) {
    if (plainText.empty()) {
        return {}; // C#：IsNullOrEmpty → return ""
    }

    std::string plainBytes = util::ToUtf8(plainText);
    std::vector<BYTE> entropy = EntropyBytes();

    DATA_BLOB input{};
    input.pbData = reinterpret_cast<BYTE*>(plainBytes.data());
    input.cbData = static_cast<DWORD>(plainBytes.size());
    DATA_BLOB optional{};
    optional.pbData = entropy.data();
    optional.cbData = static_cast<DWORD>(entropy.size());
    DATA_BLOB output{};

    const BOOL ok = CryptProtectData(&input, L"UsbCopy", &optional, nullptr, nullptr,
                                     CRYPTPROTECT_UI_FORBIDDEN, &output);
    SecureZeroMemory(plainBytes.data(), plainBytes.size());
    if (!ok) {
        return {};
    }

    DWORD chars = 0;
    std::wstring base64;
    if (CryptBinaryToStringW(output.pbData, output.cbData, CRYPT_STRING_BASE64, nullptr, &chars)) {
        base64.resize(static_cast<size_t>(chars) + 1, L'\0');
        if (CryptBinaryToStringW(output.pbData, output.cbData, CRYPT_STRING_BASE64, base64.data(),
                                 &chars)) {
            base64.resize(chars);
            while (!base64.empty() && base64.back() == L'\0') {
                base64.pop_back();
            }
        } else {
            base64.clear();
        }
    }
    LocalFree(output.pbData);
    return base64;
}

bool DecryptSecret(const std::wstring& base64Text, std::wstring& plainText) {
    plainText.clear();
    if (base64Text.empty()) {
        return true; // 空串视为"没有密码"，与 C# 一致
    }

    DWORD bytes = 0;
    if (!CryptStringToBinaryW(base64Text.c_str(), static_cast<DWORD>(base64Text.size()),
                              CRYPT_STRING_BASE64, nullptr, &bytes, nullptr, nullptr) ||
        bytes == 0) {
        return false;
    }
    std::vector<BYTE> cipher(bytes);
    if (!CryptStringToBinaryW(base64Text.c_str(), static_cast<DWORD>(base64Text.size()),
                              CRYPT_STRING_BASE64, cipher.data(), &bytes, nullptr, nullptr)) {
        return false;
    }
    cipher.resize(bytes);

    std::vector<BYTE> entropy = EntropyBytes();
    DATA_BLOB input{};
    input.pbData = cipher.data();
    input.cbData = static_cast<DWORD>(cipher.size());
    DATA_BLOB optional{};
    optional.pbData = entropy.data();
    optional.cbData = static_cast<DWORD>(entropy.size());
    DATA_BLOB output{};

    if (!CryptUnprotectData(&input, nullptr, &optional, nullptr, nullptr,
                            CRYPTPROTECT_UI_FORBIDDEN, &output)) {
        return false;
    }
    std::string plainBytes(reinterpret_cast<const char*>(output.pbData), output.cbData);
    plainText = util::FromUtf8(plainBytes);
    SecureZeroMemory(plainBytes.data(), plainBytes.size());
    LocalFree(output.pbData);
    return true;
}

// ---------------------------------------------------------------------------
//  JSON 映射
// ---------------------------------------------------------------------------

json::Value ToJson(const AppSettings& settings, bool includeServer) {
    json::Value root = json::Value::MakeObject();

    root.Set(L"targetDirectory", json::Value(settings.targetDirectory));
    root.Set(L"copyPpt", json::Value(settings.copyPpt));
    root.Set(L"copyWord", json::Value(settings.copyWord));
    root.Set(L"copyExcel", json::Value(settings.copyExcel));
    root.Set(L"copyPdf", json::Value(settings.copyPdf));
    root.Set(L"copyImage", json::Value(settings.copyImage));
    root.Set(L"copyVideo", json::Value(settings.copyVideo));
    root.Set(L"copyAudio", json::Value(settings.copyAudio));
    root.Set(L"copyCompressed", json::Value(settings.copyCompressed));
    root.Set(L"copyAllFiles", json::Value(settings.copyAllFiles));
    root.Set(L"useCustomExtensions", json::Value(settings.useCustomExtensions));
    root.Set(L"customExtensions", json::Value(settings.customExtensions));
    root.Set(L"duplicateFileAction", json::Value(settings.duplicateFileAction));
    root.Set(L"enableFileSizeLimit", json::Value(settings.enableFileSizeLimit));
    root.Set(L"maxFileSizeMB", json::Value(settings.maxFileSizeMb));
    root.Set(L"enableFileNameFilter", json::Value(settings.enableFileNameFilter));
    root.Set(L"fileNameKeywords", json::Value(settings.fileNameKeywords));
    root.Set(L"enableFolderNameFilter", json::Value(settings.enableFolderNameFilter));
    root.Set(L"folderNameKeywords", json::Value(settings.folderNameKeywords));
    root.Set(L"createDirectoryTree", json::Value(settings.createDirectoryTree));
    root.Set(L"limitDirectoryDepth", json::Value(settings.limitDirectoryDepth));
    root.Set(L"maxDirectoryDepth", json::Value(settings.maxDirectoryDepth));
    root.Set(L"logSuccess", json::Value(settings.logSuccess));
    root.Set(L"logErrors", json::Value(settings.logErrors));
    root.Set(L"logNeutral", json::Value(settings.logNeutral));
    root.Set(L"saveLogToFile", json::Value(settings.saveLogToFile));
    root.Set(L"showLogInWindow", json::Value(settings.showLogInWindow));
    root.Set(L"enableStopCopyFile", json::Value(settings.enableStopCopyFile));
    root.Set(L"stopCopyFileName", json::Value(settings.stopCopyFileName));
    root.Set(L"enableReverseCopyFile", json::Value(settings.enableReverseCopyFile));
    root.Set(L"reverseCopyFileName", json::Value(settings.reverseCopyFileName));
    root.Set(L"enableSpeedLimit", json::Value(settings.enableSpeedLimit));
    root.Set(L"speedLimitMinutes", json::Value(settings.speedLimitMinutes));
    root.Set(L"speedLimitIndex", json::Value(settings.speedLimitIndex));
    root.Set(L"autoStart", json::Value(settings.autoStart));
    root.Set(L"autoStartHidden", json::Value(settings.autoStartHidden));

    if (includeServer) {
        json::Value server = json::Value::MakeObject();
        server.Set(L"serverAddress", json::Value(settings.server.serverAddress));
        server.Set(L"port", json::Value(settings.server.port));
        server.Set(L"useHttps", json::Value(settings.server.useHttps));
        server.Set(L"password", json::Value(EncryptSecret(settings.server.password)));
        server.Set(L"apiToken", json::Value(EncryptSecret(settings.server.apiToken)));
        server.Set(L"remoteRootPath", json::Value(settings.server.remoteRootPath));
        server.Set(L"apiBasePath", json::Value(settings.server.apiBasePath));
        server.Set(L"validateCertificate", json::Value(settings.server.validateCertificate));
        server.Set(L"timeoutSeconds", json::Value(settings.server.timeoutSeconds));
        server.Set(L"chunkSizeBytes", json::Value(settings.server.chunkSizeBytes));
        server.Set(L"maxRetries", json::Value(settings.server.maxRetries));
        root.Set(L"server", std::move(server));
    }

    root.Set(L"saveLocation", json::Value(settings.saveLocation));
    root.Set(L"useChunkedUpload", json::Value(settings.useChunkedUpload));
    root.Set(L"showTrayIcon", json::Value(settings.showTrayIcon));
    root.Set(L"showCompletionNotify", json::Value(settings.showCompletionNotify));
    root.Set(L"enableWhitelist", json::Value(settings.enableWhitelist));
    root.Set(L"whitelistDriveIds", json::Value(settings.whitelistDriveIds));
    return root;
}

bool FromJson(const json::Value& root, AppSettings& settings, bool includeServer) {
    if (!root.IsObject()) {
        return false;
    }

    SetDefaults(settings); // 缺键一律回落默认（与 XmlSerializer 一致）

    settings.targetDirectory = root.GetString(L"targetDirectory", settings.targetDirectory);
    settings.copyPpt = root.GetBool(L"copyPpt", settings.copyPpt);
    settings.copyWord = root.GetBool(L"copyWord", settings.copyWord);
    settings.copyExcel = root.GetBool(L"copyExcel", settings.copyExcel);
    settings.copyPdf = root.GetBool(L"copyPdf", settings.copyPdf);
    settings.copyImage = root.GetBool(L"copyImage", settings.copyImage);
    settings.copyVideo = root.GetBool(L"copyVideo", settings.copyVideo);
    settings.copyAudio = root.GetBool(L"copyAudio", settings.copyAudio);
    settings.copyCompressed = root.GetBool(L"copyCompressed", settings.copyCompressed);
    settings.copyAllFiles = root.GetBool(L"copyAllFiles", settings.copyAllFiles);
    settings.useCustomExtensions = root.GetBool(L"useCustomExtensions", settings.useCustomExtensions);
    settings.customExtensions = root.GetString(L"customExtensions", settings.customExtensions);
    settings.duplicateFileAction = root.GetInt(L"duplicateFileAction", settings.duplicateFileAction);
    settings.enableFileSizeLimit = root.GetBool(L"enableFileSizeLimit", settings.enableFileSizeLimit);
    settings.maxFileSizeMb = root.GetInt64(L"maxFileSizeMB", settings.maxFileSizeMb);
    settings.enableFileNameFilter =
        root.GetBool(L"enableFileNameFilter", settings.enableFileNameFilter);
    settings.fileNameKeywords = root.GetString(L"fileNameKeywords", settings.fileNameKeywords);
    settings.enableFolderNameFilter =
        root.GetBool(L"enableFolderNameFilter", settings.enableFolderNameFilter);
    settings.folderNameKeywords = root.GetString(L"folderNameKeywords", settings.folderNameKeywords);
    settings.createDirectoryTree = root.GetBool(L"createDirectoryTree", settings.createDirectoryTree);
    settings.limitDirectoryDepth = root.GetBool(L"limitDirectoryDepth", settings.limitDirectoryDepth);
    settings.maxDirectoryDepth = root.GetInt(L"maxDirectoryDepth", settings.maxDirectoryDepth);
    settings.logSuccess = root.GetBool(L"logSuccess", settings.logSuccess);
    settings.logErrors = root.GetBool(L"logErrors", settings.logErrors);
    settings.logNeutral = root.GetBool(L"logNeutral", settings.logNeutral);
    settings.saveLogToFile = root.GetBool(L"saveLogToFile", settings.saveLogToFile);
    settings.showLogInWindow = root.GetBool(L"showLogInWindow", settings.showLogInWindow);
    settings.enableStopCopyFile = root.GetBool(L"enableStopCopyFile", settings.enableStopCopyFile);
    settings.stopCopyFileName = root.GetString(L"stopCopyFileName", settings.stopCopyFileName);
    settings.enableReverseCopyFile =
        root.GetBool(L"enableReverseCopyFile", settings.enableReverseCopyFile);
    settings.reverseCopyFileName = root.GetString(L"reverseCopyFileName", settings.reverseCopyFileName);
    settings.enableSpeedLimit = root.GetBool(L"enableSpeedLimit", settings.enableSpeedLimit);
    settings.speedLimitMinutes = root.GetInt(L"speedLimitMinutes", settings.speedLimitMinutes);
    settings.speedLimitIndex = root.GetInt(L"speedLimitIndex", settings.speedLimitIndex);
    settings.autoStart = root.GetBool(L"autoStart", settings.autoStart);
    settings.autoStartHidden = root.GetBool(L"autoStartHidden", settings.autoStartHidden);

    if (includeServer) {
        if (const json::Value* server = root.Find(L"server")) {
            if (server->IsObject()) {
                settings.server.serverAddress =
                    server->GetString(L"serverAddress", settings.server.serverAddress);
                settings.server.port = server->GetInt(L"port", settings.server.port);
                settings.server.useHttps = server->GetBool(L"useHttps", settings.server.useHttps);

                std::wstring plainText;
                if (DecryptSecret(server->GetString(L"password"), plainText)) {
                    settings.server.password = plainText;
                }
                plainText.clear();
                if (DecryptSecret(server->GetString(L"apiToken"), plainText)) {
                    settings.server.apiToken = plainText;
                }
                SecureZeroMemory(plainText.data(), plainText.size() * sizeof(wchar_t));

                settings.server.remoteRootPath =
                    server->GetString(L"remoteRootPath", settings.server.remoteRootPath);
                settings.server.apiBasePath =
                    server->GetString(L"apiBasePath", settings.server.apiBasePath);
                settings.server.validateCertificate =
                    server->GetBool(L"validateCertificate", settings.server.validateCertificate);
                settings.server.timeoutSeconds =
                    server->GetInt(L"timeoutSeconds", settings.server.timeoutSeconds);
                settings.server.chunkSizeBytes =
                    server->GetInt(L"chunkSizeBytes", settings.server.chunkSizeBytes);
                settings.server.maxRetries =
                    server->GetInt(L"maxRetries", settings.server.maxRetries);
            }
        }
    }

    settings.saveLocation = root.GetInt(L"saveLocation", settings.saveLocation);
    settings.useChunkedUpload = root.GetBool(L"useChunkedUpload", settings.useChunkedUpload);
    settings.showTrayIcon = root.GetBool(L"showTrayIcon", settings.showTrayIcon);
    settings.showCompletionNotify =
        root.GetBool(L"showCompletionNotify", settings.showCompletionNotify);
    settings.enableWhitelist = root.GetBool(L"enableWhitelist", settings.enableWhitelist);
    settings.whitelistDriveIds = root.GetString(L"whitelistDriveIds", settings.whitelistDriveIds);
    return true;
}

// ---------------------------------------------------------------------------
//  目录与文件
// ---------------------------------------------------------------------------

bool EnsureDirectory(const std::wstring& directory) {
    if (directory.empty()) {
        return false;
    }
    if (path::DirectoryExists(directory)) {
        return true;
    }
    // 逐级创建（CreateDirectoryW 不会自动建父目录）
    const std::wstring parent = path::DirectoryName(directory);
    if (!parent.empty() && parent != directory) {
        EnsureDirectory(parent);
    }
    if (CreateDirectoryW(path::ToExtended(directory).c_str(), nullptr)) {
        return true;
    }
    return GetLastError() == ERROR_ALREADY_EXISTS;
}

bool WriteFileAtomic(const std::wstring& filePath, const std::string& bytes, std::wstring* error) {
    const std::wstring tempPath = filePath + L".tmp";

    HANDLE handle = CreateFileW(path::ToExtended(tempPath).c_str(), GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        if (error != nullptr) {
            *error = L"创建临时文件失败：" + util::Win32Message(GetLastError());
        }
        return false;
    }

    bool ok = true;
    size_t offset = 0;
    while (offset < bytes.size()) {
        DWORD written = 0;
        const DWORD want = static_cast<DWORD>(
            (std::min)(static_cast<size_t>(1u << 20), bytes.size() - offset));
        if (!WriteFile(handle, bytes.data() + offset, want, &written, nullptr) || written == 0) {
            if (error != nullptr) {
                *error = L"写入临时文件失败：" + util::Win32Message(GetLastError());
            }
            ok = false;
            break;
        }
        offset += written;
    }
    if (ok) {
        FlushFileBuffers(handle); // 先落盘，再做替换
    }
    CloseHandle(handle);

    if (!ok) {
        DeleteFileW(path::ToExtended(tempPath).c_str());
        return false;
    }

    if (MoveFileExW(path::ToExtended(tempPath).c_str(), path::ToExtended(filePath).c_str(),
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        return true;
    }

    // 极端情况（目标被占用/跨卷）：退化为覆盖写，再删临时文件（C# 同款兜底）
    if (CopyFileW(path::ToExtended(tempPath).c_str(), path::ToExtended(filePath).c_str(), FALSE)) {
        DeleteFileW(path::ToExtended(tempPath).c_str());
        return true;
    }
    if (error != nullptr) {
        *error = L"替换配置文件失败：" + util::Win32Message(GetLastError());
    }
    DeleteFileW(path::ToExtended(tempPath).c_str());
    return false;
}

std::wstring RoamingDirectory() {
    std::wstring base = KnownFolder(CSIDL_APPDATA);
    if (base.empty()) {
        base = TempDirectory();
    }
    return path::Combine(base, L"UsbCopy");
}

std::wstring LegacyRoamingDirectory() {
    std::wstring base = KnownFolder(CSIDL_APPDATA);
    if (base.empty()) {
        base = TempDirectory();
    }
    return path::Combine(base, L"U盘文件复制器");
}

std::wstring DesktopDirectory() {
    std::wstring desktop = KnownFolder(CSIDL_DESKTOPDIRECTORY);
    if (desktop.empty()) {
        desktop = TempDirectory();
    }
    return desktop;
}

// ---------------------------------------------------------------------------
//  Store
// ---------------------------------------------------------------------------

Store& Store::Instance() {
    static Store instance;
    return instance;
}

bool Store::IncludeServerNode() {
#ifdef LOCAL_ONLY
    return false;
#else
    return true;
#endif
}

void Store::EnsureResolvedLocked() {
    if (resolved_) {
        return;
    }
    if (forced_) {
        resolved_ = true;
        directory_ = path::DirectoryName(filePath_);
        return;
    }
    filePath_ = ResolveFilePathLocked();
    directory_ = path::DirectoryName(filePath_);
    resolved_ = true;
}

std::wstring Store::ResolveFilePathLocked() {
    const std::wstring exeDirectory = path::ExeDirectory();
    const std::wstring portable = path::Combine(exeDirectory, kFileName);
    const std::wstring roaming = path::Combine(RoamingDirectory(), kFileName);

    // ① 已存在的程序目录配置优先（便携部署，避免升级后"设置丢失"）
    if (path::FileExists(portable)) {
        return portable;
    }
    // ② 已存在的 %APPDATA% 配置次之
    if (path::FileExists(roaming)) {
        return roaming;
    }
    // ③ 都没有：程序目录可写则用程序目录，否则用 %APPDATA%
    if (path::IsDirectoryWritable(exeDirectory)) {
        return portable;
    }
    // ④ 与 C# 一致：两级都不可写时**仍然返回 roaming**，让 Save 的
    //    CreateDirectory/WriteFile 失败 → 调用方报错（C# 那里是 MessageBox）。
    //    不做静默兜底（曾经试过退到桌面，会让用户以为保存成功了）
    return roaming;
}

const std::wstring& Store::FilePath() {
    std::lock_guard<std::mutex> lock(mutex_);
    EnsureResolvedLocked();
    return filePath_;
}

const std::wstring& Store::Directory() {
    std::lock_guard<std::mutex> lock(mutex_);
    EnsureResolvedLocked();
    return directory_;
}

void Store::SetFilePathForTesting(const std::wstring& filePath) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (filePath.empty()) {
        forced_ = false;
        resolved_ = false;
        filePath_.clear();
        directory_.clear();
        return;
    }
    forced_ = true;
    resolved_ = true;
    filePath_ = filePath;
    directory_ = path::DirectoryName(filePath);
}

void Store::ResetPathCache() {
    std::lock_guard<std::mutex> lock(mutex_);
    resolved_ = false;
    if (!forced_) {
        filePath_.clear();
        directory_.clear();
    }
}

void Store::BeginLoad() {
    loading_.store(true);
}

void Store::EndLoad() {
    loading_.store(false);
}

bool Store::IsLoading() const {
    return loading_.load();
}

bool Store::RequestSave(const AppSettings& settings, std::wstring* error) {
    if (loading_.load()) {
        return false; // 加载期间静默丢弃（对应 C# _settingsLoaded 门禁）
    }
    return Save(settings, error);
}

bool Store::Load(AppSettings& settings, std::wstring* error) {
    LoadScope scope(*this); // 无论怎么返回都会 EndLoad()

    SetDefaults(settings);

    std::wstring filePath;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        EnsureResolvedLocked();
        filePath = filePath_;
    }

    if (!path::FileExists(filePath)) {
        ApplyLocalOnlyIfNeeded(settings); // 首次启动：默认值也算成功
        return true;
    }

    json::Value root;
    std::wstring parseError;
    if (!json::LoadFile(filePath, root, &parseError)) {
        // 损坏的配置改名为 *.corrupt 保留现场：否则下一次保存会把它彻底覆盖，
        // 用户 43 个字段就永久丢了（C# 没有这一步，见基线 §15 D24）
        const bool backedUp = RenameToSibling(filePath, L".corrupt");
        if (error != nullptr) {
            *error = parseError + (backedUp ? L"（原文件已重命名为 .corrupt）"
                                            : L"（原文件备份失败，下次保存会覆盖）");
        }
        SetDefaults(settings);
        ApplyLocalOnlyIfNeeded(settings);
        return false; // 损坏 → 回退默认值（调用方决定是否提示）
    }

    if (!FromJson(root, settings, IncludeServerNode())) {
        const bool backedUp = RenameToSibling(filePath, L".corrupt");
        if (error != nullptr) {
            *error = L"配置文件根节点不是 JSON 对象" +
                     std::wstring(backedUp ? L"（原文件已重命名为 .corrupt）" : L"");
        }
        SetDefaults(settings);
        ApplyLocalOnlyIfNeeded(settings);
        return false;
    }

    ApplyLocalOnlyIfNeeded(settings);
    return true;
}

bool Store::Save(const AppSettings& settings, std::wstring* error) {
    std::lock_guard<std::mutex> lock(mutex_);
    return SaveLocked(settings, error);
}

bool Store::SaveLocked(const AppSettings& settings, std::wstring* error) {
    EnsureResolvedLocked();

    if (!directory_.empty() && !EnsureDirectory(directory_)) {
        if (error != nullptr) {
            *error = L"创建配置目录失败：" + directory_;
        }
        return false;
    }

    const json::Value root = ToJson(settings, IncludeServerNode());
    std::string bytes = json::SerializeUtf8(root);
    bytes.push_back('\n'); // 便于人工查看/diff
    return WriteFileAtomic(filePath_, bytes, error);
}

bool Store::ImportLegacyXml(AppSettings& settings, bool* imported, std::wstring* error) {
    if (imported != nullptr) {
        *imported = false;
    }

    std::wstring target;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        EnsureResolvedLocked();
        target = filePath_;
    }

    if (path::FileExists(target)) {
        return true; // 已有 JSON，不再导入
    }

    std::vector<std::wstring> candidates;
    candidates.push_back(path::Combine(path::ExeDirectory(), kLegacyFileName));
    candidates.push_back(path::Combine(LegacyRoamingDirectory(), kLegacyFileName));

    for (const std::wstring& candidate : candidates) {
        if (!path::FileExists(candidate)) {
            continue;
        }
        bool didImport = false;
        if (!ImportLegacyXmlFrom(candidate, settings, &didImport, error)) {
            return false;
        }
        if (didImport && imported != nullptr) {
            *imported = true;
        }
        if (didImport) {
            return true;
        }
    }

    return true; // 没有可导入的文件也算正常
}

bool Store::ImportLegacyXmlFrom(const std::wstring& legacyXmlPath, AppSettings& settings,
                                bool* imported, std::wstring* error) {
    if (imported != nullptr) {
        *imported = false;
    }
    if (!path::FileExists(legacyXmlPath)) {
        return true;
    }

    xml::Node root;
    std::wstring parseError;
    if (!xml::ParseFile(legacyXmlPath, root, &parseError)) {
        // 结构性错误不会自愈：改名 *.failed 避免每次启动都失败一遍（内容保留，可改回）
        const bool renamed = RenameToSibling(legacyXmlPath, L".failed");
        if (error != nullptr) {
            *error = L"解析 " + legacyXmlPath + L" 失败：" + parseError +
                     (renamed ? L"（已改名为 .failed）" : L"");
        }
        return false;
    }
    if (root.name != L"AppSettings") {
        const bool renamed = RenameToSibling(legacyXmlPath, L".failed");
        if (error != nullptr) {
            *error = L"根元素不是 AppSettings：" + legacyXmlPath +
                     (renamed ? L"（已改名为 .failed）" : L"");
        }
        return false;
    }

    AppSettings importedSettings;
    SetDefaults(importedSettings);
    ApplyXmlSettings(root, importedSettings);
    ApplyLocalOnlyIfNeeded(importedSettings);

    std::wstring saveError;
    if (!Save(importedSettings, &saveError)) {
        if (error != nullptr) {
            *error = L"导入后写入 settings.json 失败：" + saveError;
        }
        return false;
    }

    settings = importedSettings;
    RenameToImported(legacyXmlPath);
    if (imported != nullptr) {
        *imported = true;
    }
    return true;
}

} // namespace usbcopy::settings
