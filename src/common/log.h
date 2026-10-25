#pragma once

// ============================================================================
//  log —— 日志引擎（纯后端）
//
//  行格式：[HH:mm:ss] {message}     ← 本地时间，24 小时制，无级别前缀
//  过滤：isError → LogErrors；非错误且含「成功」→ LogSuccess；其余 → LogNeutral
//        （匹配 message 原文，不含时间戳前缀；是 Contains 不是 StartsWith）
//  窗口：最多 900 行，超出逐行丢弃最早的
//  文件：CopyLog.txt（UTF-8，CRLF），超过 5MB 轮转为 CopyLog_yyyyMMdd_HHmmss.txt
//  线程安全：所有出口共用一把 std::mutex
//
//  行为对齐 兼容性基线.md §1.4(e) 与 Client/Core/LogEngine.cs
// ============================================================================

#include <windows.h>

#include <functional>
#include <mutex>
#include <string>
#include <vector>

namespace usbcopy::log {

struct Options {
    bool logSuccess = true;
    bool logErrors = true;
    bool logNeutral = true;
    bool saveToFile = true;
    bool showInWindow = true;
    std::wstring filePath;              // 空 = 不写文件
    size_t maxWindowLines = 900;        // 与 C# LogEngine.MaxLogLines 一致
    unsigned long long maxFileSizeBytes = 5ull * 1024 * 1024; // C# 默认 5MB
};

class Engine {
public:
    static Engine& Instance();

    // 整体替换配置（M3 会在设置变化时调用）
    void Configure(const Options& options);
    // 只更新五个开关（对应 43 字段里的 LogSuccess/LogErrors/LogNeutral/SaveLogToFile/ShowLogInWindow）
    void SetSwitches(bool logSuccess, bool logErrors, bool logNeutral, bool saveToFile,
                     bool showInWindow);
    // 日志文件路径（写目标目录 / 程序目录由调用方决定，见基线 §16）
    void SetFilePath(const std::wstring& filePath);

    void Write(const std::wstring& message, bool isError = false);

    // 窗口文本（M3 取来填 RichEdit；测试也用它断言）
    std::vector<std::wstring> WindowLines() const;
    size_t WindowLineCount() const;
    void ClearWindow();

    // UI 追加钩子（在锁外回调，避免回调里再写日志造成死锁）
    void SetSink(std::function<void(const std::wstring&)> sink);

    Options Current() const;

    // 过滤判定（纯函数，便于逐条验证与 C# 的等价性）
    static bool ShouldLog(const Options& options, const std::wstring& message, bool isError);

private:
    Engine() = default;

    void WriteToFileLocked(const std::wstring& line);
    void RotateIfNeededLocked();

    mutable std::mutex mutex_;
    Options options_;
    std::vector<std::wstring> lines_;
    std::function<void(const std::wstring&)> sink_;
};

} // namespace usbcopy::log
