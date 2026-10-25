#include "log.h"

#include <cstdio>
#include <utility>

#include "path_util.h"
#include "win_util.h"

namespace usbcopy::log {

namespace {

std::wstring TimestampText(const SYSTEMTIME& time) {
    wchar_t buffer[24]{};
    swprintf_s(buffer, L"[%02u:%02u:%02u] ", static_cast<unsigned>(time.wHour),
               static_cast<unsigned>(time.wMinute), static_cast<unsigned>(time.wSecond));
    return buffer;
}

unsigned long long FileSizeOf(const std::wstring& filePath) {
    WIN32_FILE_ATTRIBUTE_DATA info{};
    if (!GetFileAttributesExW(path::ToExtended(filePath).c_str(), GetFileExInfoStandard, &info)) {
        return 0;
    }
    return (static_cast<unsigned long long>(info.nFileSizeHigh) << 32) | info.nFileSizeLow;
}

} // namespace

Engine& Engine::Instance() {
    static Engine instance;
    return instance;
}

bool Engine::ShouldLog(const Options& options, const std::wstring& message, bool isError) {
    // 与 C# LogEngine.ShouldLog 逐行等价（基线 §1.4(e)）
    if (isError && !options.logErrors) {
        return false;
    }
    if (!isError) {
        const bool success = message.find(L"成功") != std::wstring::npos;
        if (success && !options.logSuccess) {
            return false;
        }
        if (!success && !options.logNeutral) {
            return false;
        }
    }
    return true;
}

void Engine::Configure(const Options& options) {
    std::lock_guard<std::mutex> lock(mutex_);
    options_ = options;
}

void Engine::SetSwitches(bool logSuccess, bool logErrors, bool logNeutral, bool saveToFile,
                         bool showInWindow) {
    std::lock_guard<std::mutex> lock(mutex_);
    options_.logSuccess = logSuccess;
    options_.logErrors = logErrors;
    options_.logNeutral = logNeutral;
    options_.saveToFile = saveToFile;
    options_.showInWindow = showInWindow;
}

void Engine::SetFilePath(const std::wstring& filePath) {
    std::lock_guard<std::mutex> lock(mutex_);
    options_.filePath = filePath;
}

void Engine::SetSink(std::function<void(const std::wstring&)> sink) {
    std::lock_guard<std::mutex> lock(mutex_);
    sink_ = std::move(sink);
}

Options Engine::Current() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return options_;
}

void Engine::Write(const std::wstring& message, bool isError) {
    std::function<void(const std::wstring&)> sink;
    std::wstring line;

    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!ShouldLog(options_, message, isError)) {
            return;
        }

        SYSTEMTIME time{};
        GetLocalTime(&time);
        line = TimestampText(time) + message;

        if (options_.showInWindow) {
            lines_.push_back(line);
            const size_t limit = options_.maxWindowLines == 0 ? static_cast<size_t>(900)
                                                             : options_.maxWindowLines;
            while (lines_.size() > limit) {
                lines_.erase(lines_.begin()); // 逐行丢弃最早的一条
            }
        }

        if (options_.saveToFile) {
            WriteToFileLocked(line);
        }

        if (options_.showInWindow) {
            sink = sink_; // 在锁外回调，避免 sink 里再写日志造成死锁
        }
    }

    if (sink) {
        sink(line);
    }
}

void Engine::WriteToFileLocked(const std::wstring& line) {
    if (options_.filePath.empty()) {
        return;
    }

    RotateIfNeededLocked();

    const std::string bytes = util::ToUtf8(line + L"\r\n");
    HANDLE handle = CreateFileW(path::ToExtended(options_.filePath).c_str(), FILE_APPEND_DATA,
                                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                                OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return; // 写文件失败不影响主流程（C# 同样是 try/catch 吞掉）
    }
    DWORD written = 0;
    WriteFile(handle, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
    CloseHandle(handle);
}

void Engine::RotateIfNeededLocked() {
    if (options_.filePath.empty()) {
        return;
    }
    if (FileSizeOf(options_.filePath) <= options_.maxFileSizeBytes) {
        return;
    }

    SYSTEMTIME time{};
    GetLocalTime(&time);
    wchar_t suffix[40]{};
    swprintf_s(suffix, L"_%04u%02u%02u_%02u%02u%02u", static_cast<unsigned>(time.wYear),
               static_cast<unsigned>(time.wMonth), static_cast<unsigned>(time.wDay),
               static_cast<unsigned>(time.wHour), static_cast<unsigned>(time.wMinute),
               static_cast<unsigned>(time.wSecond));

    const std::wstring directory = path::DirectoryName(options_.filePath);
    const std::wstring stem = path::FileNameWithoutExtension(options_.filePath) + suffix +
                              path::Extension(options_.filePath);
    std::wstring rotated = path::Combine(directory, stem);

    const std::wstring source = path::ToExtended(options_.filePath);
    if (MoveFileExW(source.c_str(), path::ToExtended(rotated).c_str(), MOVEFILE_WRITE_THROUGH)) {
        return;
    }

    // 同一秒内重复轮转：加序号（C# 会直接失败并放弃轮转，这里做得更好一点）
    for (int index = 1; index <= 99; ++index) {
        wchar_t numbered[16]{};
        swprintf_s(numbered, L"_%d", index);
        const std::wstring candidate = path::Combine(
            directory, path::FileNameWithoutExtension(options_.filePath) + suffix +
                           path::Extension(options_.filePath) + numbered);
        if (MoveFileExW(source.c_str(), path::ToExtended(candidate).c_str(),
                        MOVEFILE_WRITE_THROUGH)) {
            return;
        }
    }
}

std::vector<std::wstring> Engine::WindowLines() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return lines_;
}

size_t Engine::WindowLineCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return lines_.size();
}

void Engine::ClearWindow() {
    std::lock_guard<std::mutex> lock(mutex_);
    lines_.clear();
}

} // namespace usbcopy::log
