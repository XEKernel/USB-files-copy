#include "jsonl_writer.h"

#include <algorithm>
#include <string>

#include "json.h"
#include "path_util.h"

namespace usbcopy::jsonl {

namespace {

json::Value Text(const wchar_t* value) {
    return json::Value(value != nullptr ? value : L"");
}

json::Value Text(const std::wstring& value) {
    return json::Value(value);
}

json::Value Number(long long value) {
    return json::Value(value);
}

json::Value Boolean(bool value) {
    return json::Value(value);
}

} // namespace

std::string FormatBatch(const BatchLine& line) {
    json::Value row = json::Value::MakeObject();
    // A 表键序：kind / index / direction / drive / startedFt
    row.Set(L"kind", Text(L"batch"));
    row.Set(L"index", Number(line.index));
    row.Set(L"direction", Text(line.direction));
    row.Set(L"drive", Text(line.drive));
    row.Set(L"startedFt", Number(line.startedFt));
    return json::SerializeUtf8(row);
}

std::string FormatFile(const FileLine& line) {
    const wchar_t* action = line.action != nullptr ? line.action : L"";
    const wchar_t* skipReason = line.skipReason != nullptr ? line.skipReason : L"";
    const wchar_t* finalAction = line.finalAction != nullptr ? line.finalAction : action;
    const wchar_t* finalSkipReason =
        line.finalSkipReason != nullptr ? line.finalSkipReason : skipReason;

    json::Value row = json::Value::MakeObject();
    // B 表键序（13 项，见基线 §13）
    row.Set(L"kind", Text(L"file"));
    row.Set(L"path", Text(line.path));
    row.Set(L"action", Text(action));
    row.Set(L"skipReason", Text(skipReason));
    row.Set(L"finalAction", Text(finalAction));
    row.Set(L"finalSkipReason", Text(finalSkipReason));
    row.Set(L"bytes", Number(line.bytes));
    row.Set(L"srcFt", Number(line.srcFt));
    row.Set(L"dstExists", Boolean(line.dstExists));
    row.Set(L"dstBytes", Number(line.dstBytes));
    row.Set(L"dstFt", Number(line.dstFt));
    row.Set(L"attempts", Number(line.attempts));
    row.Set(L"error", Text(line.error));
    return json::SerializeUtf8(row);
}

std::string FormatDir(const DirLine& line) {
    json::Value row = json::Value::MakeObject();
    // C 表键序：kind / path / skipReason
    row.Set(L"kind", Text(L"dir"));
    row.Set(L"path", Text(line.path));
    row.Set(L"skipReason", Text(line.skipReason));
    return json::SerializeUtf8(row);
}

std::string FormatSummary(const SummaryLine& line) {
    json::Value countsLast = json::Value::MakeArray();
    countsLast.Push(Number(line.countsLastTotal));
    countsLast.Push(Number(line.countsLastSuccess));
    countsLast.Push(Number(line.countsLastFailure));

    json::Value derived = json::Value::MakeObject();
    // countsDerived 键序：copy / skip / fail / sizeLimit / countedAsFailure /
    //                     totalCounted / notVisited
    derived.Set(L"copy", Number(line.derived.copy));
    derived.Set(L"skip", Number(line.derived.skip));
    derived.Set(L"fail", Number(line.derived.fail));
    derived.Set(L"sizeLimit", Number(line.derived.sizeLimit));
    derived.Set(L"countedAsFailure", Number(line.derived.countedAsFailure));
    derived.Set(L"totalCounted", Number(line.derived.totalCounted));
    derived.Set(L"notVisited", Number(line.derived.notVisited));

    json::Value row = json::Value::MakeObject();
    // D 表键序（11 项；§17.69：没有独立的 counts 字段）
    row.Set(L"kind", Text(L"summary"));
    row.Set(L"total", Number(line.total));
    row.Set(L"success", Number(line.success));
    row.Set(L"failure", Number(line.failure));
    row.Set(L"cancelled", Boolean(line.cancelled));
    row.Set(L"countsLast", countsLast);
    row.Set(L"countsDerived", derived);
    row.Set(L"sourceFiles", Number(line.sourceFiles));
    row.Set(L"filesDecided", Number(line.filesDecided));
    row.Set(L"durationMs", Number(line.durationMs));
    row.Set(L"logLines", Number(line.logLines));
    row.Set(L"target", Text(line.target));
    row.Set(L"exitCode", Number(line.exitCode));
    return json::SerializeUtf8(row);
}

std::string FormatLog(const wchar_t* level, const std::wstring& message) {
    json::Value row = json::Value::MakeObject();
    // E 表键序：kind / level / message
    row.Set(L"kind", Text(L"log"));
    row.Set(L"level", Text(level));
    row.Set(L"message", Text(message));
    return json::SerializeUtf8(row);
}

std::string FormatFileFromReport(const copy::FileReport& report) {
    FileLine line;
    line.path = report.path;
    line.action = copy::DecisionKindText(report.kind);
    line.skipReason = report.reason != nullptr ? report.reason : L"";
    // C++ 每个文件只有一次终态决策（§17.61 attempts 恒 1），final* 与首次一致
    line.finalAction = line.action;
    line.finalSkipReason = line.skipReason;
    line.bytes = report.bytes;
    line.srcFt = report.srcFt;
    line.dstExists = report.dstExists;
    line.dstBytes = report.dstBytes;
    line.dstFt = report.dstFt;
    line.attempts = report.attempts;
    line.error = report.error;
    return FormatFile(line);
}

bool Writer::OpenStdout() {
    Close();

    HANDLE handle = GetStdHandle(STD_OUTPUT_HANDLE);
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) {
        // `/SUBSYSTEM:WINDOWS` 的进程从控制台启动时继承不到标准句柄：
        // 挂到父进程的控制台，再自己打开 CONOUT$（交接文档 §6.3 的方案）。
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) {
            return false;
        }
        handle = CreateFileW(L"CONOUT$", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                             OPEN_EXISTING, 0, nullptr);
        if (handle == INVALID_HANDLE_VALUE) {
            handle_ = INVALID_HANDLE_VALUE;
            return false;
        }
        owned_ = true;
    }
    handle_ = handle;
    failed_ = false;
    lines_ = 0;
    return true;
}

bool Writer::OpenFile(const std::wstring& path) {
    Close();
    if (path.empty()) {
        return false;
    }
    const HANDLE handle =
        CreateFileW(path::ToExtended(path).c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                    CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return false;
    }
    handle_ = handle;
    owned_ = true;
    failed_ = false;
    lines_ = 0;
    return true;
}

bool Writer::WriteAll(const char* data, size_t size) {
    size_t written = 0;
    while (written < size) {
        const size_t remaining = size - written;
        const DWORD chunk = static_cast<DWORD>(std::min<size_t>(remaining, 1u << 20));
        DWORD wrote = 0;
        if (!WriteFile(handle_, data + written, chunk, &wrote, nullptr)) {
            failed_ = true;
            return false;
        }
        if (wrote == 0) {
            failed_ = true;
            return false;
        }
        written += wrote;
    }
    return true;
}

bool Writer::WriteLine(const std::string& line) {
    if (handle_ == INVALID_HANDLE_VALUE || handle_ == nullptr) {
        return false;
    }
    if (!WriteAll(line.data(), line.size())) {
        return false;
    }
    const char newline = '\n';
    if (!WriteAll(&newline, 1)) {
        return false;
    }
    ++lines_;
    return true;
}

void Writer::Close() {
    if (owned_ && handle_ != INVALID_HANDLE_VALUE && handle_ != nullptr) {
        CloseHandle(handle_);
    }
    handle_ = INVALID_HANDLE_VALUE;
    owned_ = false;
}

} // namespace usbcopy::jsonl
