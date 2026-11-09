#pragma once

// ============================================================================
//  jsonl_writer —— JSON Lines 输出（`--headless` / harness 共用格式）
//
//  唯一权威：兼容性基线.md §13 的五张表（A batch / B file / C dir / D summary /
//  E log）。四条通则必须遵守：
//    1. 字段总是存在，不用 null、不省略（缺值 ""/0/false）
//    2. path 一律相对源根，'/' 分隔
//    3. *Ft 一律 int64 FILETIME（100ns），不是 ISO 串
//    4. 每行键序固定（与 §13 表格同序）
//
//  实现要点：
//    · 复用 `usbcopy::json` 的 Value/SerializeUtf8：键保序、转义正确、UTF-8 无 BOM
//    · 一行一个对象 + '\n'，**流式**写出（内存恒定，§17.67）
//    · `Writer::OpenStdout` 负责 `/SUBSYSTEM:WINDOWS` 下的 AttachConsole
//      （GUI 子系统进程默认没有标准输出；先看继承来的句柄，再挂父控制台）
// ============================================================================

#include <windows.h>

#include <string>

#include "copy_callbacks.h"

namespace usbcopy::jsonl {

// A. kind=batch（每批第一行；--once 恒 index=1）
struct BatchLine {
    int index = 1;
    const wchar_t* direction = L"forward"; // forward / reverse
    std::wstring drive;                    // 盘符（E:）；--source 直接指目录时为空串
    long long startedFt = 0;               // 批开始时刻
};

// B. kind=file（每个源文件一行；C++ 不输出 not-visited）
struct FileLine {
    std::wstring path;
    const wchar_t* action = L"copy";
    const wchar_t* skipReason = L"";
    const wchar_t* finalAction = nullptr;     // nullptr → 与 action 相同
    const wchar_t* finalSkipReason = nullptr; // nullptr → 与 skipReason 相同
    long long bytes = 0;
    long long srcFt = 0;
    bool dstExists = false;
    long long dstBytes = 0;
    long long dstFt = 0;
    int attempts = 1;
    std::wstring error;
};

// C. kind=dir（引擎看到但没进去的目录）
struct DirLine {
    std::wstring path;
    const wchar_t* skipReason = L"system-dir";
};

// D. kind=summary 的 countsDerived（比对只用这一组，§13 规则 15）
struct CountsDerived {
    int copy = 0;
    int skip = 0;               // 含 sizeLimit
    int fail = 0;
    int sizeLimit = 0;
    int countedAsFailure = 0;   // fail + sizeLimit
    int totalCounted = 0;       // copy + countedAsFailure
    int notVisited = 0;
};

// D. kind=summary（每批最后一行）
struct SummaryLine {
    int total = 0;
    int success = 0;
    int failure = 0;
    bool cancelled = false;
    int countsLastTotal = 0;
    int countsLastSuccess = 0;
    int countsLastFailure = 0;
    CountsDerived derived;
    int sourceFiles = 0;
    int filesDecided = 0;
    long long durationMs = 0;
    int logLines = 0;
    std::wstring target;
    int exitCode = 0;
};

// ---- 格式化（返回不带换行的 UTF-8 单行 JSON；便于单测逐字节断言）----
std::string FormatBatch(const BatchLine& line);
std::string FormatFile(const FileLine& line);
std::string FormatDir(const DirLine& line);
std::string FormatSummary(const SummaryLine& line);
std::string FormatLog(const wchar_t* level, const std::wstring& message);

// 从引擎的完整文件事件直接生成 B 表一行（finalAction/finalSkipReason 与首次一致）
std::string FormatFileFromReport(const copy::FileReport& report);

// ---- 流式写出 ----
class Writer {
public:
    ~Writer() { Close(); }
    Writer() = default;
    Writer(const Writer&) = delete;
    Writer& operator=(const Writer&) = delete;

    // 取继承来的 stdout；无效时 AttachConsole(ATTACH_PARENT_PROCESS) 后用 CONOUT$
    bool OpenStdout();

    // 创建/覆盖文件（UTF-8 无 BOM；失败返回 false）
    bool OpenFile(const std::wstring& path);

    // 写一行（自动补 '\n'；line 必须是 UTF-8）
    bool WriteLine(const std::string& line);

    void Close();

    bool ok() const { return handle_ != INVALID_HANDLE_VALUE && !failed_; }
    bool isFile() const { return owned_; }
    int lineCount() const { return lines_; }

private:
    bool WriteAll(const char* data, size_t size);

    HANDLE handle_ = INVALID_HANDLE_VALUE;
    bool owned_ = false; // true=我们自己 CreateFile 的，需要 CloseHandle
    bool failed_ = false;
    int lines_ = 0;
};

} // namespace usbcopy::jsonl
