#pragma once

// ============================================================================
//  settings —— 配置模型与持久化
//
//  字段与默认值严格对齐 兼容性基线.md §1.1（43 个顶层）/ §1.2（ServerConfig 11 个）。
//  持久化格式：settings.json（UTF-8、camelCase、原子替换）
//  敏感字段：server.password / server.apiToken → DPAPI(CurrentUser) + Base64
//  兼容导入：首次启动检测 C# 版 settings.xml（见 §1.4(d)）
// ============================================================================

#include <windows.h>

#include <atomic>
#include <mutex>
#include <string>

#include "json.h"

namespace usbcopy::settings {

// ---------------------------------------------------------------------------
//  ServerConfig（对应 C# Client/Storage/ServerConfig.cs）
// ---------------------------------------------------------------------------
struct ServerConfig {
    std::wstring serverAddress;
    int port = 443;
    bool useHttps = true;
    std::wstring password;  // 内存中为明文；落盘时 DPAPI + Base64
    std::wstring apiToken;  // 同上
    std::wstring remoteRootPath = L"/";
    std::wstring apiBasePath = L"/api/file";
    bool validateCertificate = true;
    int timeoutSeconds = 30;
    int chunkSizeBytes = 1024 * 1024;
    int maxRetries = 3;

    // 计算属性（不入库，与 C# 同名）
    std::wstring BaseUrl() const;
    std::wstring ApiBaseUrl() const;
};

// ---------------------------------------------------------------------------
//  AppSettings（43 个顶层字段，顺序与基线表一致）
// ---------------------------------------------------------------------------
struct AppSettings {
    // 1-2 目标与文件类型
    std::wstring targetDirectory; // C# 为 null，C++ 用空串表示
    bool copyPpt = true;
    bool copyWord = true;
    bool copyExcel = true;
    bool copyPdf = true;
    bool copyImage = true;
    bool copyVideo = true;
    bool copyAudio = true;
    bool copyCompressed = true;
    bool copyAllFiles = false;
    bool useCustomExtensions = false;
    std::wstring customExtensions;
    // 13 重复策略：0=跳过 1=覆盖 2=保留两者 3=保留较新
    int duplicateFileAction = 0;
    // 14-15 大小限制
    bool enableFileSizeLimit = false;
    long long maxFileSizeMb = 100;
    // 16-19 名称过滤
    bool enableFileNameFilter = false;
    std::wstring fileNameKeywords;
    bool enableFolderNameFilter = false;
    std::wstring folderNameKeywords;
    // 20-22 目录结构
    bool createDirectoryTree = true;
    bool limitDirectoryDepth = false;
    int maxDirectoryDepth = 3;
    // 23-27 日志
    bool logSuccess = true;
    bool logErrors = true;
    bool logNeutral = true;
    bool saveLogToFile = true;
    bool showLogInWindow = true;
    // 28-31 标记文件
    bool enableStopCopyFile = false;
    std::wstring stopCopyFileName = L"stop.copy";
    bool enableReverseCopyFile = false;
    std::wstring reverseCopyFileName = L"reverse.copy";
    // 32-34 限速
    bool enableSpeedLimit = false;
    int speedLimitMinutes = 5;
    int speedLimitIndex = 1;
    // 35-36 自启动
    bool autoStart = false;
    bool autoStartHidden = false;
    // 37 服务器
    ServerConfig server;
    // 38-41 保存位置与界面
    int saveLocation = 0;
    bool useChunkedUpload = true;
    bool showTrayIcon = true;
    bool showCompletionNotify = true;
    // 42-43 白名单
    bool enableWhitelist = false;
    std::wstring whitelistDriveIds;
};

// 全部字段回落默认值（等价于重新构造）
void SetDefaults(AppSettings& settings);

// 单机版强制项（§8）：server 全默认、saveLocation=0、useChunkedUpload=false
void ApplyLocalOnlyDefaults(AppSettings& settings);

// ---------------------------------------------------------------------------
//  JSON 映射（camelCase；server 为嵌套对象）
//  includeServer=false 时不写/不读 server 节点（单机版行为）
// ---------------------------------------------------------------------------
json::Value ToJson(const AppSettings& settings, bool includeServer = true);
bool FromJson(const json::Value& root, AppSettings& settings, bool includeServer = true);

// ---------------------------------------------------------------------------
//  DPAPI（entropy = UTF-8("U盘文件复制器_SALT_2024")，29 字节；输出 Base64）
// ---------------------------------------------------------------------------
std::wstring EncryptSecret(const std::wstring& plainText);
bool DecryptSecret(const std::wstring& base64Text, std::wstring& plainText);

// 供测试/校验：entropy 的原始字节（与 C# 逐字节一致）
const std::wstring& EntropyText();

// ---------------------------------------------------------------------------
//  Store：路径解析、加载门禁、读写、XML 导入
// ---------------------------------------------------------------------------
class Store {
public:
    static Store& Instance();

    // 三级路径解析（结果缓存）：exe 目录 → %APPDATA%\UsbCopy → 桌面\UsbCopy
    const std::wstring& FilePath();
    const std::wstring& Directory();

    // 测试用：强制指定配置文件路径（传空串恢复自动解析）
    void SetFilePathForTesting(const std::wstring& filePath);
    // 清掉路径缓存（M3 移动程序后可能需要重算）
    void ResetPathCache();

    // ---- 加载抑制门禁（对应 C# _settingsLoaded）----
    void BeginLoad();
    void EndLoad();
    bool IsLoading() const;

    // 加载期间静默丢弃（不排队、不延迟补发）；否则转 Save
    bool RequestSave(const AppSettings& settings, std::wstring* error);

    // ---- 读写 ----
    bool Load(AppSettings& settings, std::wstring* error);
    bool Save(const AppSettings& settings, std::wstring* error);

    // 一次性导入 C# 版 settings.xml；成功后原文件改名 .imported
    // imported 返回是否真的导入了
    bool ImportLegacyXml(AppSettings& settings, bool* imported, std::wstring* error);

    // 从指定 XML 导入（ImportLegacyXml 的实际执行体，测试也走这里）
    bool ImportLegacyXmlFrom(const std::wstring& legacyXmlPath, AppSettings& settings,
                             bool* imported, std::wstring* error);

    // 单机版构建时 server 节点不落盘
    static bool IncludeServerNode();

private:
    Store() = default;

    void EnsureResolvedLocked();
    std::wstring ResolveFilePathLocked();
    bool SaveLocked(const AppSettings& settings, std::wstring* error);

    std::mutex mutex_;
    std::atomic<bool> loading_{false};
    std::wstring filePath_;
    std::wstring directory_;
    bool resolved_ = false;
    bool forced_ = false;
};

// 目录创建（递归）；已存在返回 true
bool EnsureDirectory(const std::wstring& directory);

// 原子写：<file>.tmp → MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)
bool WriteFileAtomic(const std::wstring& filePath, const std::string& bytes, std::wstring* error);

// %APPDATA%\UsbCopy
std::wstring RoamingDirectory();
// C# 版遗留目录 %APPDATA%\U盘文件复制器（仅用于导入）
std::wstring LegacyRoamingDirectory();
// 桌面目录
std::wstring DesktopDirectory();

} // namespace usbcopy::settings
