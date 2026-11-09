#include "copy_engine.h"

// ============================================================================
//  copy_engine 实现（M2-A：遍历 + CopyFileExW + SetFileTime + 计数 + FileReport）
//
//  行为契约逐条对应 兼容性基线.md：
//    · 先本目录文件、再子目录递归（C# CopyDirectoryAsync:320-338）
//    · 同一目录内模式集取并集去重（D37 / §17.28）
//    · 深度 guard 在「要不要递归」处（§17.24）；根目录 depth=0
//    · 取消：cancelAfterMs 截止时间由 CopyProgressRoutine / 循环检查点打断（§17.4/D38）
//    · 复制：CopyFileExW + SetFileTime 写回源 mtime（D8）；失败重试 1s/2s/3s（§17.6）
//    · 计数：Counters，total == success + failure（§17.31）
//    · FileReport 的 dst* 一律是「处理完那一刻」目标盘真实值（§17.48）
//    · dryRun：不建目录 / 不写文件 / 不设时间，dst* 保持运行前状态（§17.41）
//
//  不抛异常、不继承；所有路径都用扩展长路径（path::ToExtended）交给 Win32。
// ============================================================================

#include <windows.h>

#include <map>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

#include "file_filter.h"
#include "path_util.h"
#include "settings.h"
#include "volume.h"
#include "win_util.h"

namespace usbcopy::copy {

namespace {

// ---------------------------------------------------------------------------
//  小工具
// ---------------------------------------------------------------------------

std::wstring ToForwardSlashes(const std::wstring& text) {
    std::wstring result = text;
    for (wchar_t& c : result) {
        if (c == L'\\') {
            c = L'/';
        }
    }
    return result;
}

std::wstring ToBackslashes(const std::wstring& text) {
    std::wstring result = text;
    for (wchar_t& c : result) {
        if (c == L'/') {
            c = L'\\';
        }
    }
    return result;
}

// 相对路径拼接：输出一律 '/' 分隔（§13 通则 2）；空父目录时不留前导 '/'
std::wstring JoinRelative(const std::wstring& relativeDir, const std::wstring& name) {
    if (relativeDir.empty()) {
        return name;
    }
    return relativeDir + L"/" + name;
}

long long FileTimeValue(const FILETIME& time) {
    return static_cast<long long>((static_cast<unsigned long long>(time.dwHighDateTime) << 32) |
                                  static_cast<unsigned long long>(time.dwLowDateTime));
}

FILETIME MakeFileTime(long long value) {
    FILETIME time{};
    const unsigned long long raw = static_cast<unsigned long long>(value);
    time.dwLowDateTime = static_cast<DWORD>(raw & 0xFFFFFFFFULL);
    time.dwHighDateTime = static_cast<DWORD>(raw >> 32);
    return time;
}

// §17.6 的简化：C# 只对 IOException / UnauthorizedAccessException 重试，
// 这里映射成「共享冲突 / 锁冲突 / 拒绝访问」三类 Win32 错误。
bool IsTransientError(DWORD error) {
    return error == ERROR_SHARING_VIOLATION || error == ERROR_LOCK_VIOLATION ||
           error == ERROR_ACCESS_DENIED;
}

// ---------------------------------------------------------------------------
//  限速窗口（§17.49 / D36）：进程级表，供 headless/GUI 与 M2-C 共用
// ---------------------------------------------------------------------------

std::mutex& DriveInsertionMutex() {
    static std::mutex mutex;
    return mutex;
}

std::map<std::wstring, unsigned long long>& DriveInsertionTable() {
    static std::map<std::wstring, unsigned long long> table;
    return table;
}

// ---------------------------------------------------------------------------
//  Engine：一次 RunCopyEngine 的全部状态（同步、单线程）
// ---------------------------------------------------------------------------

struct Engine {
    Engine(const EngineParams& parameters, const CopyOptions& options,
           const CopyCallbacks& callbacks)
        : params_(parameters),
          options_(options),
          callbacks_(callbacks),
          counters_(&callbacks),
          stopEvent_(CreateEventW(nullptr, TRUE, FALSE, nullptr)) {}

    EngineStats Run();

    // ---- 复制进度回调（取消检查点之一：文件写盘途中）----
    static DWORD CALLBACK ProgressThunk(LARGE_INTEGER totalFileSize,
                                        LARGE_INTEGER totalBytesTransferred,
                                        LARGE_INTEGER /*streamSize*/,
                                        LARGE_INTEGER /*streamBytesTransferred*/,
                                        DWORD /*streamNumber*/, DWORD /*callbackReason*/,
                                        HANDLE /*sourceFile*/, HANDLE /*destinationFile*/,
                                        LPVOID data) {
        Engine* engine = static_cast<Engine*>(data);
        if (engine == nullptr) {
            return PROGRESS_CONTINUE;
        }
        return engine->OnCopyProgress(totalBytesTransferred.QuadPart, totalFileSize.QuadPart);
    }

    DWORD OnCopyProgress(long long transferred, long long total) {
        if (CancelDue()) {
            cancelPending_ = true;
            return PROGRESS_CANCEL;
        }
        if (callbacks_.onProgress) {
            callbacks_.onProgress(currentRelativePath_, transferred, total);
        }
        return PROGRESS_CONTINUE;
    }

    // ---- 日志（同时累计 stats.logLines）----
    void Log(const std::wstring& message, bool isError) {
        ++stats_.logLines;
        if (callbacks_.onLog) {
            callbacks_.onLog(message, isError);
        }
    }

    bool CancelDue() const {
        return deadlineTick_ != 0 && GetTickCount64() >= deadlineTick_;
    }

    // 取消检查点：已到截止时间就置 cancelled 并返回 true（调用方停止后续处理）
    bool CheckCancelled() {
        if (!cancelled_ && CancelDue()) {
            cancelled_ = true;
        }
        return cancelled_;
    }

    // 可被取消打断的等待（§17.6：不用 Sleep；stopEvent 由截止时间模拟）
    bool WaitInterruptible(unsigned long long totalMs) {
        const unsigned long long start = GetTickCount64();
        for (;;) {
            if (CancelDue()) {
                return false;
            }
            const unsigned long long elapsed = GetTickCount64() - start;
            if (elapsed >= totalMs) {
                return true;
            }
            const unsigned long long remain = totalMs - elapsed;
            const DWORD slice = static_cast<DWORD>(remain > 100ULL ? 100ULL : remain);
            if (stopEvent_.get() == nullptr) {
                WaitForSingleObject(GetCurrentProcess(), slice);
            } else if (WaitForSingleObject(stopEvent_.get(), slice) == WAIT_OBJECT_0) {
                return false;
            }
        }
    }

    // ---- 目标路径 ----
    std::wstring BuildTargetDir(const std::wstring& relativeDir) const {
        if (relativeDir.empty()) {
            return targetBase_;
        }
        return path::Combine(targetBase_, ToBackslashes(relativeDir));
    }

    // ---- 目标真实状态（§17.48）----
    void ReadTargetState(const std::wstring& targetPath, bool& exists, long long& bytes,
                         long long& ft) const {
        exists = false;
        bytes = 0;
        ft = 0;
        volume::FileStat target{};
        if (!volume::StatFile(targetPath, target) || !target.exists) {
            return;
        }
        // 同名目录不算「目标文件存在」（C# 用 File.Exists，目录返回 false）
        if ((target.attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
            return;
        }
        exists = true;
        bytes = target.size;
        ft = target.lastWriteFt;
    }

    void EmitFileReport(const std::wstring& relativePath, DecisionKind kind, const wchar_t* reason,
                        const volume::FileStat& sourceStat, const std::wstring& targetPath,
                        const std::wstring& error) {
        FileReport report;
        report.path = relativePath;
        report.kind = kind;
        report.reason = reason == nullptr ? reason::kNone : reason;
        report.bytes = sourceStat.size;
        report.srcFt = sourceStat.lastWriteFt;
        ReadTargetState(targetPath, report.dstExists, report.dstBytes, report.dstFt);
        report.attempts = 1; // §17.61：C++ 恒为 1
        report.error = error;
        if (callbacks_.onFileHandled) {
            callbacks_.onFileHandled(report);
        }
        ++stats_.filesDecided;
    }

    void EmitDirectorySkipped(const std::wstring& relativePath, const wchar_t* reason) {
        if (callbacks_.onDirectorySkipped) {
            callbacks_.onDirectorySkipped(relativePath, reason == nullptr ? reason::kNone : reason);
        }
    }

    // ---- 日志文案（§17.11 映射表，便于与 C# harness 对读）----
    void LogSkipReason(const wchar_t* reason, const std::wstring& fullPath) {
        const std::wstring text = reason == nullptr ? std::wstring() : std::wstring(reason);
        if (text == reason::kSizeLimit) {
            Log(L"文件过大已跳过：" + fullPath, true); // C# 走 isError
        } else if (text == reason::kNameFilter) {
            Log(L"文件名不包含关键词：" + fullPath, false);
        } else if (text == reason::kDuplicateSkip) {
            Log(L"跳过已存在文件：" + fullPath, false);
        } else {
            // system-file / marker-file / system-attr / hidden-attr / reparse-point
            Log(L"跳过文件：" + fullPath, false);
        }
    }

    // 写回源 mtime（D8）。失败只记日志、不推翻已经成功的字节复制。
    void SetSourceLastWrite(const std::wstring& targetPath, long long sourceFt) {
        util::HandleGuard handle(CreateFileW(path::ToExtended(targetPath).c_str(), GENERIC_WRITE,
                                            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                                            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
        if (!handle) {
            Log(L"设置文件时间失败：" + targetPath + L" | 错误：" + util::Win32Message(GetLastError()),
                true);
            return;
        }
        FILETIME fileTime = MakeFileTime(sourceFt);
        if (!SetFileTime(handle.get(), nullptr, nullptr, &fileTime)) {
            Log(L"设置文件时间失败：" + targetPath + L" | 错误：" + util::Win32Message(GetLastError()),
                true);
        }
    }

    void ReportCancelled(const std::wstring& fullSource, const std::wstring& relativePath,
                         const volume::FileStat& sourceStat, const std::wstring& targetPath) {
        cancelled_ = true;
        Log(L"复制已取消：" + fullSource, false);
        counters_.Cancel(relativePath);
        EmitFileReport(relativePath, DecisionKind::Cancelled, reason::kCancelled, sourceStat,
                       targetPath, L"已取消");
    }

    // ---- 单个文件（C# CopySingleFileAsync 的等价物）----
    void ProcessFile(const std::wstring& fullSource, const std::wstring& fileName,
                     const std::wstring& relativeDir, const volume::FileStat& sourceStat) {
        const std::wstring relativePath = JoinRelative(relativeDir, fileName);
        const std::wstring targetDir = BuildTargetDir(relativeDir);
        const std::wstring targetPath = path::Combine(targetDir, fileName);

        // ① 判定（系统文件 / 标记 / 属性 / reparse / 关键词 / 大小）
        const FileEvaluation evaluation =
            EvaluateFile(fullSource, fileName, sourceStat.size, sourceStat.attributes, options_,
                         params_.reverseCopy);
        if (evaluation.decision.kind == DecisionKind::Skip) {
            LogSkipReason(evaluation.decision.reason, fullSource);
            counters_.Skip(relativePath, evaluation.decision.reason, evaluation.countedAsFailure);
            EmitFileReport(relativePath, DecisionKind::Skip, evaluation.decision.reason, sourceStat,
                           targetPath, std::wstring());
            return;
        }

        // ② 冲突策略：M2-A 只支持 Skip；目标已存在即 duplicate-skip（不计数，§17.46）
        bool targetExists = false;
        long long targetBytes = 0;
        long long targetFt = 0;
        ReadTargetState(targetPath, targetExists, targetBytes, targetFt);
        (void)targetBytes;
        (void)targetFt;
        if (targetExists) {
            LogSkipReason(reason::kDuplicateSkip, fullSource);
            counters_.Skip(relativePath, reason::kDuplicateSkip);
            EmitFileReport(relativePath, DecisionKind::Skip, reason::kDuplicateSkip, sourceStat,
                           targetPath, std::wstring());
            return;
        }

        // ③ dry-run：只判定不落盘，dst* 保持运行前状态（§17.41）
        if (params_.dryRun) {
            Log(L"成功复制：" + fullSource + L" -> " + relativePath, false);
            counters_.Copy(relativePath);
            EmitFileReport(relativePath, DecisionKind::Copy, reason::kNone, sourceStat, targetPath,
                           std::wstring());
            return;
        }

        // ④ 只有要写文件时才建父目录（D7：空目录不建）
        if (!settings::EnsureDirectory(targetDir)) {
            const std::wstring message = util::Win32Message(GetLastError());
            Log(L"复制失败：" + fullSource + L" | 错误：" + message, true);
            counters_.Fail(relativePath, reason::kCopyError);
            EmitFileReport(relativePath, DecisionKind::Fail, reason::kCopyError, sourceStat,
                           targetPath, message);
            return;
        }

        const std::wstring longSource = path::ToExtended(fullSource);
        const std::wstring longTarget = path::ToExtended(targetPath);
        currentRelativePath_ = relativePath; // 进度回调用

        const int maxRetries = 3;
        DWORD lastError = ERROR_SUCCESS;
        for (int attempt = 0; attempt <= maxRetries; ++attempt) {
            cancelPending_ = false;
            const BOOL copied =
                CopyFileExW(longSource.c_str(), longTarget.c_str(), &Engine::ProgressThunk, this,
                            nullptr, 0);
            if (copied != FALSE) {
                SetSourceLastWrite(targetPath, sourceStat.lastWriteFt);
                Log(L"成功复制：" + fullSource + L" -> " + relativePath, false);
                counters_.Copy(relativePath);
                EmitFileReport(relativePath, DecisionKind::Copy, reason::kNone, sourceStat,
                               targetPath, std::wstring());
                return;
            }

            lastError = GetLastError();
            if (cancelPending_ || lastError == ERROR_REQUEST_ABORTED) {
                ReportCancelled(fullSource, relativePath, sourceStat, targetPath);
                return;
            }
            if (attempt >= maxRetries || !IsTransientError(lastError)) {
                break;
            }

            // C# 的退避是 1s×attempt（1s、2s、3s），等待必须可被取消
            const int retry = attempt + 1;
            Log(L"复制失败（将重试 " + std::to_wstring(retry) + L"/" +
                    std::to_wstring(maxRetries) + L"）：" + fullSource + L" | 错误：" +
                    util::Win32Message(lastError),
                true);
            if (!WaitInterruptible(static_cast<unsigned long long>(retry) * 1000ULL)) {
                ReportCancelled(fullSource, relativePath, sourceStat, targetPath);
                return;
            }
        }

        // ⑤ 重试耗尽 / 不可重试：残留文件保留不删（§17.4）
        const std::wstring message = util::Win32Message(lastError);
        Log(L"复制失败：" + fullSource + L" | 错误：" + message, true);
        counters_.Fail(relativePath, reason::kCopyError);
        EmitFileReport(relativePath, DecisionKind::Fail, reason::kCopyError, sourceStat, targetPath,
                       message);
    }

    // ---- 目录遍历（先文件、再子目录；返回 false 表示已被取消）----
    bool WalkDirectory(const std::wstring& dirFull, const std::wstring& relativeDir, int depth) {
        if (CheckCancelled()) {
            return false;
        }

        // a. 本目录文件：模式集并集去重后只处理一次（D37）
        const std::vector<std::wstring> fileNames =
            CollectMatchedNames(dirFull, options_.searchPatterns);
        for (const std::wstring& name : fileNames) {
            if (CheckCancelled()) {
                return false; // 取消后剩余文件不产生任何决策（不是 not-visited）
            }
            const std::wstring fullPath = path::Combine(dirFull, name);
            volume::FileStat sourceStat{};
            if (!volume::StatFile(fullPath, sourceStat) || !sourceStat.exists) {
                Log(L"文件处理失败: " + name + L" | 错误：" + util::Win32Message(GetLastError()),
                    true);
                continue;
            }
            ProcessFile(fullPath, name, relativeDir, sourceStat);
            if (cancelled_) {
                return false;
            }
        }
        if (CheckCancelled()) {
            return false;
        }

        // b. 深度 guard：这里管的是「要不要继续递归」，本层文件已处理（§17.24）
        if (DepthExceeded(depth, options_)) {
            Log(L"达到目录深度限制(" + std::to_wstring(options_.maxDirectoryDepth) +
                    L")，停止遍历: " + dirFull,
                false);
            return true;
        }

        // c. 子目录：属性 → 目录判定 → 文件夹关键词；剪枝的目录才产生 dir 事件（§17.67）
        const std::vector<std::wstring> subdirectories = CollectSubdirectories(dirFull);
        for (const std::wstring& name : subdirectories) {
            if (CheckCancelled()) {
                return false;
            }
            const std::wstring childFull = path::Combine(dirFull, name);
            const DWORD attributes = path::Attributes(childFull);
            if (attributes == INVALID_FILE_ATTRIBUTES) {
                Log(L"目录访问被拒绝：" + childFull, true);
                continue;
            }
            const FileDecision decision = EvaluateDirectory(name, attributes);
            if (!decision.ok()) {
                Log(L"跳过系统目录：" + childFull, false);
                EmitDirectorySkipped(JoinRelative(relativeDir, name), decision.reason);
                continue;
            }
            if (!PassFolderNameFilter(name, options_)) {
                Log(L"跳过不包含关键词的文件夹：" + childFull, false);
                EmitDirectorySkipped(JoinRelative(relativeDir, name), reason::kFolderFilter);
                continue;
            }
            if (!WalkDirectory(childFull, JoinRelative(relativeDir, name), depth + 1)) {
                return false;
            }
        }
        return true;
    }

    // ---- sourceFiles：独立扫描，只跳过 reparse 目录，不做任何过滤/深度限制 ----
    void ScanSourceFiles(const std::wstring& dirFull, int& count) const {
        count += static_cast<int>(CollectMatchesForPattern(dirFull, L"*.*").size());
        for (const std::wstring& name : CollectSubdirectories(dirFull)) {
            const std::wstring childFull = path::Combine(dirFull, name);
            const DWORD attributes = path::Attributes(childFull);
            if (attributes == INVALID_FILE_ATTRIBUTES) {
                continue;
            }
            if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
                continue; // 源树扫描同样不跟随 junction（比对规则第 9 条）
            }
            ScanSourceFiles(childFull, count);
        }
    }

    // ---- 成员 ----
    const EngineParams& params_;
    const CopyOptions& options_;
    const CopyCallbacks& callbacks_;
    Counters counters_;

    std::wstring sourceRoot_;
    std::wstring targetBase_; // targetRoot[\relativeTargetDir]
    std::wstring currentRelativePath_;

    unsigned long long deadlineTick_ = 0;
    bool cancelled_ = false;
    bool cancelPending_ = false;

    util::HandleGuard stopEvent_;
    EngineStats stats_;
};

EngineStats Engine::Run() {
    // ① 归一化源根 / 目标根（§13 通则 2 的前提；失败只记日志，不抛异常）
    std::wstring normalizedSource;
    if (path::Normalize(params_.sourceRoot, normalizedSource) != path::Status::Ok) {
        Log(L"源目录无效：" + params_.sourceRoot, true);
        return stats_;
    }
    sourceRoot_ = normalizedSource;

    std::wstring normalizedTarget;
    if (path::Normalize(params_.targetRoot, normalizedTarget) != path::Status::Ok) {
        Log(L"目标目录无效：" + params_.targetRoot, true);
        return stats_;
    }
    targetBase_ = normalizedTarget;

    // relativeTargetDir 的分隔符统一成 '/'（对外路径口径），内部拼盘路径时再转反斜杠
    std::wstring relativeTarget = ToForwardSlashes(params_.relativeTargetDir);
    size_t begin = 0;
    while (begin < relativeTarget.size() && relativeTarget[begin] == L'/') {
        ++begin;
    }
    while (!relativeTarget.empty() && relativeTarget.back() == L'/') {
        relativeTarget.pop_back();
    }
    if (begin != 0) {
        relativeTarget = relativeTarget.substr(begin);
    }
    if (!relativeTarget.empty()) {
        targetBase_ = path::Combine(targetBase_, ToBackslashes(relativeTarget));
    }

    // ② 取消截止时间从进入 RunCopyEngine 开始计时（§17.33）
    if (params_.cancelAfterMs > 0) {
        deadlineTick_ =
            GetTickCount64() + static_cast<unsigned long long>(params_.cancelAfterMs);
    }

    // ③ 遍历（先文件、再子目录）
    WalkDirectory(sourceRoot_, std::wstring(), 0);

    // ④ 最后做一次独立扫描统计源树文件总数（summary.sourceFiles）
    int sourceFiles = 0;
    ScanSourceFiles(sourceRoot_, sourceFiles);
    stats_.sourceFiles = sourceFiles;

    stats_.total = counters_.total();
    stats_.success = counters_.success();
    stats_.failure = counters_.failure();
    stats_.cancelled = cancelled_;
    return stats_;
}

} // namespace

// ---------------------------------------------------------------------------
//  对外入口
// ---------------------------------------------------------------------------

EngineStats RunCopyEngine(const EngineParams& params, const CopyOptions& options,
                          const CopyCallbacks& callbacks) {
    Engine engine(params, options, callbacks);
    return engine.Run();
}

// ---------------------------------------------------------------------------
//  限速窗口（进程级；M2-C 才真正使用）
// ---------------------------------------------------------------------------

void RecordDriveInsertion(const std::wstring& driveId) {
    if (driveId.empty()) {
        return;
    }
    const std::lock_guard<std::mutex> guard(DriveInsertionMutex());
    auto& table = DriveInsertionTable();
    // D36：同一盘只记第一次（重复插入/补扫不重置窗口）
    if (!table.contains(driveId)) {
        table.emplace(driveId, GetTickCount64());
    }
}

void ForgetDriveInsertion(const std::wstring& driveId) {
    const std::lock_guard<std::mutex> guard(DriveInsertionMutex());
    DriveInsertionTable().erase(driveId);
}

bool HasDriveInsertion(const std::wstring& driveId) {
    const std::lock_guard<std::mutex> guard(DriveInsertionMutex());
    return DriveInsertionTable().contains(driveId);
}

void ResetDriveInsertions() {
    const std::lock_guard<std::mutex> guard(DriveInsertionMutex());
    DriveInsertionTable().clear();
}

} // namespace usbcopy::copy
