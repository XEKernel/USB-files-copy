#pragma once

#include <string>

// 界面状态模型。
// 本阶段只做 UI：所有字段来源于控件，尚未持久化、也未接入复制引擎。
// 字段语义与 C# 版 AppSettings 一一对应，便于后续后端直接消费。
struct UiSettings {
    // ---- 保存位置 ----
    std::wstring targetDir;
    bool saveToServer = false;   // 仅完整版有效

    // ---- 复制文件类型 ----
    bool typeWord = true;
    bool typePpt = true;
    bool typeExcel = true;
    bool typePdf = true;
    bool typeImage = true;
    bool typeVideo = true;
    bool typeAudio = true;
    bool typeArchive = true;
    bool allFiles = false;
    bool useCustomExtensions = false;
    std::wstring customExtensions;

    // ---- 重复文件 ----
    enum class DuplicateAction { Skip = 0, Overwrite = 1, KeepBoth = 2, Newer = 3 };
    DuplicateAction duplicate = DuplicateAction::Skip;

    // ---- 文件大小限制 ----
    bool limitSize = false;
    int maxSizeMB = 100;

    // ---- 限速 ----
    bool limitSpeed = false;
    int speedMinutes = 5;
    int speedIndex = 1;          // 对应下拉项：1/2/5/10 MB/s

    // ---- 过滤 ----
    bool filterByName = false;
    std::wstring nameKeywords;
    bool filterByFolder = false;
    std::wstring folderKeywords;

    // ---- 目录结构 ----
    bool createDirectoryTree = true;
    bool limitDepth = false;
    int maxDepth = 3;

    // ---- 日志 ----
    bool logSuccess = true;
    bool logError = true;
    bool logNeutral = true;
    bool logToFile = true;
    bool logToWindow = true;

    // ---- U 盘特殊文件 ----
    bool stopWhenFileExists = false;
    std::wstring stopFileName = L"stop.copy";
    bool reverseWhenFileExists = false;
    std::wstring reverseFileName = L"reverse.copy";

    // ---- 自启动 / 托盘 ----
    bool autoStart = false;
    bool autoStartHidden = false;
    bool showTrayIcon = true;
    bool notifyOnComplete = true;

    // ---- 白名单 ----
    bool restrictToWhitelist = false;
    std::wstring whitelistIds;

    // ---- 服务器（仅完整版）----
    std::wstring serverAddress;
    int serverPort = 443;
    bool useHttps = true;
    bool chunkedUpload = true;
    int chunkSizeMB = 1;
    std::wstring serverToken;
    std::wstring serverPassword;
};
