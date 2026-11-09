#include "headless.h"

#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cwchar>
#include <cwctype>
#include <initializer_list>
#include <limits>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "copy_engine.h"
#include "copy_options.h"
#include "json.h"
#include "jsonl_writer.h"
#include "path_util.h"
#include "settings.h"
#include "volume.h"
#include "win_util.h"

// ============================================================================
//  headless.cpp —— `--headless` 控制台入口（M2-A）
//
//  规格来源：
//    · 交接文档 §6.1（命令行）/ §6.2（输出）/ §6.5（验收）
//    · 兼容性基线.md §13（JSONL 唯一权威）、§17.12 / §17.19 / §17.33 /
//      §17.37 / §17.41 / §17.49–17.50 / §17.58–17.59 / §17.64 / §17.69–17.71
//
//  开跑时序（**不许调换**，基线 §16 第 24 条 / §17.33）：
//      解析参数 → 校验盘/目录（失败 exit 2）→ GetDriveId → RecordDriveInsertion
//      → 遍历 + CopyFileExW 真复制 → 流式输出 JSONL
//
//  硬约束：不抛异常、不用继承、不 new/delete 裸指针；stdout 只允许出现合法
//  JSONL，所有诊断走 stderr；Win32 调用的主体集中在 src/common/（本文件只保留
//  时序与计时这类没有通用封装可用的调用）。
// ============================================================================

namespace usbcopy::app {

namespace {

// ---- 退出码（基线 §17.33）----
constexpr int kExitSuccess = 0;     // 全部成功
constexpr int kExitFilesFailed = 1; // 有失败项
constexpr int kExitUsage = 2;       // 参数/环境错误
constexpr int kExitCancelled = 3;   // 被取消（优先于 1）
constexpr int kExitInternal = 4;    // Writer 写入失败 / 未处理错误

// ---- `--drive` 重试（基线 §17.58）：2 秒 × 4 轮 ----
constexpr int kDriveRetryCount = 4;
constexpr int kDriveRetryDelayMs = 2000;

// ---- 命令行结果 ----
struct ParsedArgs {
    std::wstring source;
    std::wstring target;
    std::wstring drive;       // 原样保存，校验阶段归一化成 `E:`
    std::wstring optionsFile; // --options
    std::wstring outFile;     // --out
    std::wstring folderName;  // --folder-name（覆盖 relativeTargetDir，缺省空串）
    long long cancelAfterMs = 0;
    bool cancelAfterSpecified = false;
    bool targetSpecified = false;
    bool driveSpecified = false;
    bool optionsSpecified = false;
    bool emitLog = false;
    bool dryRun = false;
    bool ignoreWhitelist = false;
    bool noRetry = false;
    bool reverse = false; // M2-E，本版本诚实报错
    bool watch = false;   // M2-D，本版本诚实报错
    bool text = false;    // 本版本只支持 JSONL，诚实报错
    bool help = false;
};

// ---- 预运行日志（重试等）：batch 之后、引擎之前按序输出 ----
struct LogEntry {
    std::wstring message;
    bool isError = false;
};

// ---------------------------------------------------------------------------
//  基础小工具
// ---------------------------------------------------------------------------

wchar_t ToLowerAsciiChar(wchar_t ch) {
    if (ch >= L'A' && ch <= L'Z') {
        return static_cast<wchar_t>(ch - L'A' + L'a');
    }
    return ch;
}

std::wstring ToLowerAscii(const std::wstring& text) {
    std::wstring result = text;
    for (wchar_t& ch : result) {
        ch = ToLowerAsciiChar(ch);
    }
    return result;
}

bool IsWhiteSpaceChar(wchar_t ch) {
    return std::iswspace(static_cast<wint_t>(ch)) != 0;
}

// C# string.Trim()：去首尾 Unicode 空白（这里够用：配置里只可能是普通空白）
std::wstring TrimWhitespace(const std::wstring& text) {
    size_t begin = 0;
    size_t end = text.size();
    while (begin < end && IsWhiteSpaceChar(text[begin])) {
        ++begin;
    }
    while (end > begin && IsWhiteSpaceChar(text[end - 1])) {
        --end;
    }
    return text.substr(begin, end - begin);
}

// C# string.IsNullOrWhiteSpace
bool IsBlank(const std::wstring& text) {
    for (wchar_t ch : text) {
        if (!IsWhiteSpaceChar(ch)) {
            return false;
        }
    }
    return true;
}

std::wstring IntToWString(long long value) {
    return std::to_wstring(value);
}

// 非负十进制整数（只接受 0-9，拒绝符号/空白/溢出）；返回值 false 表示非法
bool ParseNonNegativeInt64(const std::wstring& text, long long& out) {
    if (text.empty()) {
        return false;
    }
    const long long maxValue = (std::numeric_limits<long long>::max)();
    long long value = 0;
    for (wchar_t ch : text) {
        if (ch < L'0' || ch > L'9') {
            return false;
        }
        const long long digit = static_cast<long long>(ch - L'0');
        if (value > (maxValue - digit) / 10) {
            return false;
        }
        value = value * 10 + digit;
    }
    out = value;
    return true;
}

long long FileTimeToInt64(const FILETIME& fileTime) {
    ULARGE_INTEGER value{};
    value.LowPart = fileTime.dwLowDateTime;
    value.HighPart = fileTime.dwHighDateTime;
    return static_cast<long long>(value.QuadPart);
}

// 诊断一律走 stderr（stdout 只能出现 JSONL）
void WriteStderrLine(const std::wstring& message) {
    const std::string utf8 = util::ToUtf8(message);
    if (!utf8.empty()) {
        (void)std::fwrite(utf8.data(), 1, utf8.size(), stderr);
    }
    (void)std::fwrite("\n", 1, 1, stderr);
    (void)std::fflush(stderr);
}

void PrintUsage() {
    WriteStderrLine(
        L"UsbCopy --headless 用法（M2-A）：\n"
        L"  --headless               进入无 UI 模式（--dry-run 隐含 headless）\n"
        L"  --source=<dir>           源根目录（正向=U 盘根或任意目录）\n"
        L"  --target=<dir>           目标根目录；缺省用配置 TargetDirectory（都空 → exit 2）\n"
        L"  --drive=E:               源盘；会做就绪重试（2s×4）与白名单校验\n"
        L"  --once                   默认行为（显式写上是好习惯）\n"
        L"  --json                   默认输出 JSONL（无需传；--text 未实现）\n"
        L"  --emit-log               额外输出 kind=log 行\n"
        L"  --dry-run                只判定不落盘（与 --cancel-after 互斥）\n"
        L"  --cancel-after=<ms>      引擎开始复制后多少毫秒取消\n"
        L"  --ignore-whitelist       显式跳过白名单校验（自动化用）\n"
        L"  --no-retry               盘未就绪时只查一次，不等待（CI 用）\n"
        L"  --options=<file.json>    用 CopyOptions JSON 整体替代配置（25 个 camelCase 字段）\n"
        L"  --out=<file>             写入文件（UTF-8 无 BOM）；缺省 stdout\n"
        L"  --folder-name=<name>     目标子目录名（relativeTargetDir，缺省空串）\n"
        L"  --help                   显示本说明\n"
        L"未实现（本版本明确报错，不静默忽略）：--reverse(M2-E) / --watch(M2-D) / --text\n"
        L"退出码：0 成功；1 有失败；2 参数/环境错误；3 被取消；4 输出写入失败/未处理错误");
}

// `--key=value` 与 `--key value` 两种形态；返回 false 表示这不是一个参数（未知）
bool SplitArgument(const std::wstring& argument, std::wstring& key, std::wstring& value,
                   bool& hasValue) {
    if (argument == L"-h") {
        key = argument;
        value.clear();
        hasValue = false;
        return true;
    }
    if (argument.size() < 3 || argument[0] != L'-' || argument[1] != L'-') {
        return false;
    }
    const size_t equals = argument.find(L'=');
    if (equals == std::wstring::npos) {
        key = argument;
        value.clear();
        hasValue = false;
        return true;
    }
    key = argument.substr(0, equals);
    value = argument.substr(equals + 1);
    hasValue = true;
    return true;
}

bool TakeArgumentValue(const std::vector<std::wstring>& args, size_t& index, bool hasInlineValue,
                       const std::wstring& inlineValue, const std::wstring& key,
                       std::wstring& out, std::wstring& error) {
    if (hasInlineValue) {
        out = inlineValue;
        return true;
    }
    if (index + 1 >= args.size()) {
        error = L"ERROR 参数缺少值：";
        error += key;
        return false;
    }
    ++index;
    out = args[index];
    return true;
}

bool RequireNoArgumentValue(bool hasInlineValue, const std::wstring& argument,
                            std::wstring& error) {
    if (hasInlineValue) {
        error = L"ERROR 开关参数不接受值：";
        error += argument;
        return false;
    }
    return true;
}

// 参数解析（键名大小写不敏感；`--key value` 的下一个参数无条件作为值）
bool ParseArguments(const std::vector<std::wstring>& args, ParsedArgs& parsed,
                    std::wstring& error) {
    size_t index = 0;
    while (index < args.size()) {
        std::wstring key;
        std::wstring inlineValue;
        bool hasInlineValue = false;
        if (!SplitArgument(args[index], key, inlineValue, hasInlineValue)) {
            error = L"ERROR 未知参数：";
            error += args[index];
            return false;
        }
        const std::wstring lowerKey = ToLowerAscii(key);
        const std::wstring argument = args[index];

        if (lowerKey == L"--help" || lowerKey == L"-h") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.help = true;
        } else if (lowerKey == L"--headless") {
            // main.cpp 已用它分流；这里接受是为了直接调用 RunHeadless 的测试/工具
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
        } else if (lowerKey == L"--once") {
            // 默认行为就是跑一次；接受此开关只为显式表达（§17.37）
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
        } else if (lowerKey == L"--json") {
            // JSONL 是默认输出；接受此开关只为显式表达
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
        } else if (lowerKey == L"--emit-log") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.emitLog = true;
        } else if (lowerKey == L"--dry-run") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.dryRun = true;
        } else if (lowerKey == L"--reverse") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.reverse = true;
        } else if (lowerKey == L"--watch") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.watch = true;
        } else if (lowerKey == L"--text") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.text = true;
        } else if (lowerKey == L"--ignore-whitelist") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.ignoreWhitelist = true;
        } else if (lowerKey == L"--no-retry") {
            if (!RequireNoArgumentValue(hasInlineValue, argument, error)) {
                return false;
            }
            parsed.noRetry = true;
        } else if (lowerKey == L"--source") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, parsed.source,
                                   error)) {
                return false;
            }
        } else if (lowerKey == L"--target") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, parsed.target,
                                   error)) {
                return false;
            }
            parsed.targetSpecified = true;
        } else if (lowerKey == L"--drive") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, parsed.drive,
                                   error)) {
                return false;
            }
            parsed.driveSpecified = true;
        } else if (lowerKey == L"--options") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key,
                                   parsed.optionsFile, error)) {
                return false;
            }
            parsed.optionsSpecified = true;
        } else if (lowerKey == L"--out") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, parsed.outFile,
                                   error)) {
                return false;
            }
        } else if (lowerKey == L"--folder-name") {
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key,
                                   parsed.folderName, error)) {
                return false;
            }
        } else if (lowerKey == L"--cancel-after") {
            std::wstring text;
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, text, error)) {
                return false;
            }
            long long milliseconds = 0;
            if (!ParseNonNegativeInt64(text, milliseconds)) {
                error = L"ERROR --cancel-after 需要非负整数毫秒值：";
                error += text;
                return false;
            }
            parsed.cancelAfterMs = milliseconds;
            parsed.cancelAfterSpecified = true;
        } else if (lowerKey == L"--tab") {
            // GUI 专用参数（main.cpp 会解析）；headless 下接受并忽略其值，避免组合调用失败
            std::wstring ignored;
            if (!TakeArgumentValue(args, index, hasInlineValue, inlineValue, key, ignored, error)) {
                return false;
            }
        } else {
            error = L"ERROR 未知参数：";
            error += argument;
            return false;
        }
        ++index;
    }
    return true;
}

// `E:` / `E` / `E:\` → display=`E:`、root=`E:\`；无法识别返回 false
bool NormalizeDriveArgument(const std::wstring& input, std::wstring& display,
                            std::wstring& root) {
    const std::wstring trimmed = TrimWhitespace(input);
    if (trimmed.empty()) {
        return false;
    }
    std::wstring letter = volume::DriveLetterOf(trimmed);
    if (letter.empty() && trimmed.size() == 1) {
        const wchar_t ch = trimmed[0];
        if ((ch >= L'A' && ch <= L'Z') || (ch >= L'a' && ch <= L'z')) {
            letter.assign(1, static_cast<wchar_t>(std::towupper(static_cast<wint_t>(ch))));
        }
    }
    if (letter.empty()) {
        return false;
    }
    display = letter;
    display += L":";
    root = display;
    root += L"\\";
    return true;
}

// ---------------------------------------------------------------------------
//  `--options=<file.json>` → CopyOptions（25 个 camelCase 字段）
// ---------------------------------------------------------------------------

// 严格类型检查：字段存在但类型不符 → exit 2（缺字段保持默认）
class OptionsReader {
public:
    OptionsReader(const json::Value& root, std::wstring& error)
        : root_(root), error_(error) {}

    bool ReadString(const wchar_t* key, std::wstring& out) {
        if (!ok_) {
            return false;
        }
        const json::Value* value = root_.Find(key);
        if (value == nullptr) {
            return true;
        }
        if (!value->IsString()) {
            return Fail(key, L"string");
        }
        out = value->AsString();
        return true;
    }

    bool ReadBool(const wchar_t* key, bool& out) {
        if (!ok_) {
            return false;
        }
        const json::Value* value = root_.Find(key);
        if (value == nullptr) {
            return true;
        }
        if (!value->IsBool()) {
            return Fail(key, L"bool");
        }
        out = value->AsBool();
        return true;
    }

    bool ReadInt(const wchar_t* key, int& out) {
        if (!ok_) {
            return false;
        }
        const json::Value* value = root_.Find(key);
        if (value == nullptr) {
            return true;
        }
        if (!value->IsNumber() || !value->IsIntLike()) {
            return Fail(key, L"整数");
        }
        const long long raw = value->Int64Raw();
        if (raw < static_cast<long long>((std::numeric_limits<int>::min)()) ||
            raw > static_cast<long long>((std::numeric_limits<int>::max)())) {
            return Fail(key, L"32 位整数");
        }
        out = static_cast<int>(raw);
        return true;
    }

    bool ReadInt64(const wchar_t* key, long long& out) {
        if (!ok_) {
            return false;
        }
        const json::Value* value = root_.Find(key);
        if (value == nullptr) {
            return true;
        }
        if (!value->IsNumber() || !value->IsIntLike()) {
            return Fail(key, L"整数");
        }
        out = value->Int64Raw();
        return true;
    }

    bool ReadStringArray(const wchar_t* key, std::vector<std::wstring>& out) {
        if (!ok_) {
            return false;
        }
        const json::Value* value = root_.Find(key);
        if (value == nullptr) {
            return true;
        }
        if (!value->IsArray()) {
            return Fail(key, L"字符串数组");
        }
        const size_t count = value->Size();
        std::vector<std::wstring> items;
        items.reserve(count);
        for (size_t index = 0; index < count; ++index) {
            const json::Value& item = value->At(index);
            if (!item.IsString()) {
                return Fail(key, L"字符串数组");
            }
            items.push_back(item.AsString());
        }
        out = std::move(items);
        return true;
    }

private:
    bool Fail(const wchar_t* key, const wchar_t* expected) {
        ok_ = false;
        error_ = L"ERROR options 字段类型不符：";
        error_ += key;
        error_ += L"（期望 ";
        error_ += expected;
        error_ += L"）";
        return false;
    }

    const json::Value& root_;
    std::wstring& error_;
    bool ok_ = true;
};

bool ApplyOptionsJson(const json::Value& root, copy::CopyOptions& options,
                      std::wstring& error) {
    if (!root.IsObject()) {
        error = L"ERROR options 根节点不是 JSON 对象";
        return false;
    }
    OptionsReader reader(root, error);
    int duplicateActionRaw = static_cast<int>(options.duplicateAction);
    // 字段顺序与 CopyOptions 一致（§2 的 25 个字段）；缺字段保持默认
    const bool ok =
        reader.ReadString(L"targetDirectory", options.targetDirectory) &&
        reader.ReadStringArray(L"searchPatterns", options.searchPatterns) &&
        reader.ReadBool(L"createDirectoryTree", options.createDirectoryTree) &&
        reader.ReadBool(L"limitDirectoryDepth", options.limitDirectoryDepth) &&
        reader.ReadInt(L"maxDirectoryDepth", options.maxDirectoryDepth) &&
        reader.ReadBool(L"filterByFolderName", options.filterByFolderName) &&
        reader.ReadStringArray(L"folderNameKeywords", options.folderNameKeywords) &&
        reader.ReadBool(L"filterByFileName", options.filterByFileName) &&
        reader.ReadStringArray(L"fileNameKeywords", options.fileNameKeywords) &&
        reader.ReadBool(L"enableSizeLimit", options.enableSizeLimit) &&
        reader.ReadInt64(L"maxSizeBytes", options.maxSizeBytes) &&
        reader.ReadBool(L"enableSpeedLimit", options.enableSpeedLimit) &&
        reader.ReadInt(L"speedLimitMinutes", options.speedLimitMinutes) &&
        reader.ReadInt(L"speedLimitBytesPerSecond", options.speedLimitBytesPerSecond) &&
        reader.ReadBool(L"checkFreeSpace", options.checkFreeSpace) &&
        reader.ReadBool(L"stopCopyWhenFileExists", options.stopCopyWhenFileExists) &&
        reader.ReadString(L"stopCopyFileName", options.stopCopyFileName) &&
        reader.ReadBool(L"reverseCopyWhenFileExists", options.reverseCopyWhenFileExists) &&
        reader.ReadString(L"reverseCopyFileName", options.reverseCopyFileName) &&
        reader.ReadString(L"reverseCopyMarker", options.reverseCopyMarker) &&
        reader.ReadString(L"reverseCopyIndicator", options.reverseCopyIndicator) &&
        reader.ReadInt(L"duplicateAction", duplicateActionRaw) &&
        reader.ReadBool(L"compareContent", options.compareContent) &&
        reader.ReadBool(L"enableWhitelist", options.enableWhitelist) &&
        reader.ReadStringArray(L"whitelistedDriveIds", options.whitelistedDriveIds);
    if (!ok) {
        return false;
    }
    options.duplicateAction = static_cast<copy::DuplicateAction>(duplicateActionRaw);
    return true;
}

// ---------------------------------------------------------------------------
//  配置（AppSettings）→ CopyOptions（1:1 复刻 Form1.Settings.cs BuildCopyOptions）
// ---------------------------------------------------------------------------

// C# AddCustomExtensions：按英文逗号切分 → Trim → TrimStart('.') → 拼 `*.ext`
void AppendCustomPatterns(const std::wstring& raw, std::vector<std::wstring>& patterns) {
    size_t start = 0;
    while (start <= raw.size()) {
        const size_t comma = raw.find(L',', start);
        const std::wstring piece =
            (comma == std::wstring::npos) ? raw.substr(start) : raw.substr(start, comma - start);
        std::wstring extension = TrimWhitespace(piece);
        size_t firstNonDot = 0;
        while (firstNonDot < extension.size() && extension[firstNonDot] == L'.') {
            ++firstNonDot;
        }
        extension.erase(0, firstNonDot);
        if (!extension.empty()) {
            patterns.push_back(L"*." + extension);
        }
        if (comma == std::wstring::npos) {
            break;
        }
        start = comma + 1;
    }
}

// C# `.Distinct()` 默认是区分大小写的字符串比较（基线 §2.3），保序去重
std::vector<std::wstring> DeduplicateCaseSensitive(const std::vector<std::wstring>& values) {
    std::vector<std::wstring> result;
    result.reserve(values.size());
    for (const std::wstring& value : values) {
        if (std::find(result.begin(), result.end(), value) == result.end()) {
            result.push_back(value);
        }
    }
    return result;
}

// 扩展名分组（§2.3 / FileCategories.cs）：
// PPT → Word → Excel → PDF → 图片 → 视频 → 压缩包 → 自定义 → 音频，保序去重
std::vector<std::wstring> BuildSearchPatterns(const settings::AppSettings& appSettings) {
    if (appSettings.copyAllFiles) {
        return {L"*.*"};
    }
    std::vector<std::wstring> patterns;
    const auto append = [&patterns](std::initializer_list<const wchar_t*> group) {
        patterns.reserve(patterns.size() + group.size());
        for (const wchar_t* pattern : group) {
            patterns.emplace_back(pattern);
        }
    };
    if (appSettings.copyPpt) {
        append({L"*.ppt", L"*.pptx"});
    }
    if (appSettings.copyWord) {
        append({L"*.doc", L"*.docx", L"*.txt"});
    }
    if (appSettings.copyExcel) {
        append({L"*.xlsx", L"*.xls"});
    }
    if (appSettings.copyPdf) {
        append({L"*.pdf"});
    }
    if (appSettings.copyImage) {
        append({L"*.jpg", L"*.jpeg", L"*.png", L"*.gif", L"*.bmp", L"*.webp"});
    }
    if (appSettings.copyVideo) {
        append({L"*.mp4", L"*.avi", L"*.mov", L"*.mkv", L"*.wmv", L"*.flv"});
    }
    if (appSettings.copyCompressed) {
        append({L"*.zip", L"*.rar", L"*.7z",  L"*.tar", L"*.gz", L"*.bz2",
                L"*.xz",  L"*.zst", L"*.001", L"*.iso", L"*.wim", L"*.cab"});
    }
    if (appSettings.useCustomExtensions && !IsBlank(appSettings.customExtensions)) {
        AppendCustomPatterns(appSettings.customExtensions, patterns);
    }
    if (appSettings.copyAudio) {
        append({L"*.mp3", L"*.wma", L"*.wav", L"*.ape", L"*.ogg", L"*.flac", L"*.aac"});
    }
    return DeduplicateCaseSensitive(patterns);
}

// speedLimitIndex 0..3 → 1/2/5/10 MB/s；越界按 2 MB（Form1.UIControls.cs 的映射）
int SpeedLimitBytesPerSecond(int speedLimitIndex) {
    switch (speedLimitIndex) {
        case 0:
            return 1 * 1024 * 1024;
        case 1:
            return 2 * 1024 * 1024;
        case 2:
            return 5 * 1024 * 1024;
        case 3:
            return 10 * 1024 * 1024;
        default:
            return 2 * 1024 * 1024;
    }
}

// maxSizeBytes = maxFileSizeMb * 1024 * 1024（防越界溢出，语义仍是 1:1 乘法）
long long MegabytesToBytes(long long megabytes) {
    constexpr long long kBytesPerMegabyte = 1024LL * 1024LL;
    const long long maxValue = (std::numeric_limits<long long>::max)();
    const long long minValue = (std::numeric_limits<long long>::min)();
    if (megabytes > maxValue / kBytesPerMegabyte) {
        return maxValue;
    }
    if (megabytes < minValue / kBytesPerMegabyte) {
        return minValue;
    }
    return megabytes * kBytesPerMegabyte;
}

void ApplyAppSettings(const settings::AppSettings& appSettings, copy::CopyOptions& options) {
    options.targetDirectory = appSettings.targetDirectory;
    options.searchPatterns = BuildSearchPatterns(appSettings);
    options.createDirectoryTree = appSettings.createDirectoryTree;
    options.limitDirectoryDepth = appSettings.limitDirectoryDepth;
    options.maxDirectoryDepth = appSettings.limitDirectoryDepth
                                    ? appSettings.maxDirectoryDepth
                                    : (std::numeric_limits<int>::max)();
    options.filterByFolderName = appSettings.enableFolderNameFilter;
    options.folderNameKeywords = copy::ParseKeywords(appSettings.folderNameKeywords);
    options.filterByFileName = appSettings.enableFileNameFilter;
    options.fileNameKeywords = copy::ParseKeywords(appSettings.fileNameKeywords);
    options.enableSizeLimit = appSettings.enableFileSizeLimit;
    options.maxSizeBytes = MegabytesToBytes(appSettings.maxFileSizeMb);
    options.enableSpeedLimit = appSettings.enableSpeedLimit;
    options.speedLimitMinutes = appSettings.speedLimitMinutes;
    options.speedLimitBytesPerSecond = SpeedLimitBytesPerSecond(appSettings.speedLimitIndex);
    // Form1.BuildCopyOptions 不设置 CheckFreeSpace / CompareContent / ReverseCopyMarker /
    // ReverseCopyIndicator → 全部保持 CopyOptions 默认值（与 C# 默认构造一致）
    options.checkFreeSpace = true;
    options.stopCopyWhenFileExists = appSettings.enableStopCopyFile;
    options.stopCopyFileName = TrimWhitespace(appSettings.stopCopyFileName);
    options.reverseCopyWhenFileExists = appSettings.enableReverseCopyFile;
    options.reverseCopyFileName = TrimWhitespace(appSettings.reverseCopyFileName);
    options.duplicateAction = static_cast<copy::DuplicateAction>(appSettings.duplicateFileAction);
    options.compareContent = true;
    options.enableWhitelist = appSettings.enableWhitelist;
    options.whitelistedDriveIds = copy::ParseKeywords(appSettings.whitelistDriveIds);
}

// ---------------------------------------------------------------------------
//  主流程
// ---------------------------------------------------------------------------

int RunHeadlessImpl(const std::vector<std::wstring>& args) {
    std::wstring error;

    // ---- 1. 解析参数 ----
    ParsedArgs parsed;
    if (!ParseArguments(args, parsed, error)) {
        WriteStderrLine(error);
        return kExitUsage;
    }
    if (parsed.help) {
        PrintUsage();
        return kExitSuccess;
    }

    // ---- 2. M2-A 明确未实现的开关：诚实报错，不静默忽略 ----
    if (parsed.reverse) {
        WriteStderrLine(L"ERROR --reverse 尚未实现（M2-E 反向复制），本版本只支持正向复制");
        return kExitUsage;
    }
    if (parsed.watch) {
        WriteStderrLine(L"ERROR --watch 尚未实现（M2-D 设备监听），本版本只支持 --once");
        return kExitUsage;
    }
    if (parsed.text) {
        WriteStderrLine(L"ERROR --text 尚未实现，本版本只支持 --json（JSONL 默认输出）");
        return kExitUsage;
    }

    // ---- 3. 配置来源：--options 整体替代配置，否则读 settings ----
    copy::CopyOptions options;
    if (parsed.optionsSpecified) {
        json::Value root;
        std::wstring loadError;
        if (!json::LoadFile(parsed.optionsFile, root, &loadError)) {
            std::wstring message = L"ERROR 读取 options 失败：";
            message += parsed.optionsFile;
            if (!loadError.empty()) {
                message += L"（";
                message += loadError;
                message += L"）";
            }
            WriteStderrLine(message);
            return kExitUsage;
        }
        if (!ApplyOptionsJson(root, options, error)) {
            WriteStderrLine(error);
            return kExitUsage;
        }
    } else {
        settings::AppSettings appSettings;
        std::wstring loadError;
        if (!settings::Store::Instance().Load(appSettings, &loadError)) {
            std::wstring message = L"ERROR 读取配置失败";
            if (!loadError.empty()) {
                message += L"：";
                message += loadError;
            }
            WriteStderrLine(message);
            return kExitUsage;
        }
        ApplyAppSettings(appSettings, options);
    }

    // ---- 4. --target 内存覆盖 options.targetDirectory（不回写配置）----
    if (parsed.targetSpecified) {
        options.targetDirectory = parsed.target;
    }

    // ---- 5. 参数组合校验 ----
    if (options.duplicateAction != copy::DuplicateAction::Skip) {
        // D1/D37：M2-A 的引擎只实现 Skip 冲突策略（交接文档 §6.4）
        std::wstring message = L"ERROR M2-A 只支持 duplicateAction=0（Skip），当前值为 ";
        message += IntToWString(static_cast<long long>(options.duplicateAction));
        message += L"（Overwrite/KeepBoth/ReplaceWithNewer 尚未实现）";
        WriteStderrLine(message);
        return kExitUsage;
    }
    if (parsed.dryRun && parsed.cancelAfterSpecified) {
        // 基线 §17.64：非法组合，静默忽略会让用户以为"取消生效了"
        WriteStderrLine(L"ERROR --dry-run 与 --cancel-after 不能同时使用（dry-run 不落盘，取消无意义）");
        return kExitUsage;
    }
    if (options.enableSpeedLimit) {
        // 真实配置可能带限速：只警告、继续跑，不能 exit 2（任务 A 节）
        WriteStderrLine(L"WARN 限速尚未实现（M2-C），本次全速复制");
    }

    // ---- 6. 源盘归一化 ----
    std::wstring driveDisplay;
    std::wstring driveRoot;
    if (parsed.driveSpecified) {
        if (!NormalizeDriveArgument(parsed.drive, driveDisplay, driveRoot)) {
            std::wstring message = L"ERROR --drive 值无效：";
            message += parsed.drive;
            message += L"（期望形如 E:）";
            WriteStderrLine(message);
            return kExitUsage;
        }
    }

    // ---- 7. 源/目标输入校验（参数级，快速失败）----
    std::wstring sourceInput = parsed.source;
    if (IsBlank(sourceInput)) {
        if (driveRoot.empty()) {
            WriteStderrLine(L"ERROR 缺少源目录：请给 --source 或 --drive");
            return kExitUsage;
        }
        sourceInput = driveRoot; // forward + --drive：源根 = 盘根（§17.59）
    }
    std::wstring targetInput = options.targetDirectory;
    if (IsBlank(targetInput)) {
        WriteStderrLine(L"ERROR 缺少目标目录：请给 --target 或在设置里配置");
        return kExitUsage;
    }

    // ---- 8. 路径归一化（相对路径按 CWD；源目录必须在后面校验存在性）----
    std::wstring sourceRoot;
    const path::Status sourceStatus = path::Normalize(sourceInput, sourceRoot);
    if (sourceStatus != path::Status::Ok) {
        std::wstring message = L"ERROR 源目录无效：";
        message += sourceInput;
        message += L"（";
        message += path::StatusText(sourceStatus);
        message += L"）";
        WriteStderrLine(message);
        return kExitUsage;
    }
    std::wstring targetRoot;
    const path::Status targetStatus = path::Normalize(targetInput, targetRoot);
    if (targetStatus != path::Status::Ok) {
        std::wstring message = L"ERROR 目标目录无效：";
        message += targetInput;
        message += L"（";
        message += path::StatusText(targetStatus);
        message += L"）";
        WriteStderrLine(message);
        return kExitUsage;
    }

    // ---- 9. 盘就绪校验（§17.58：每轮 2s、最多 4 轮；--no-retry 只查一次）----
    std::vector<LogEntry> preLogs;
    if (!driveRoot.empty()) {
        bool ready = volume::IsDriveReady(driveRoot);
        if (!ready && !parsed.noRetry) {
            for (int attempt = 1; attempt <= kDriveRetryCount && !ready; ++attempt) {
                LogEntry entry;
                entry.message = L"驱动器尚未就绪，重试 ";
                entry.message += IntToWString(static_cast<long long>(attempt));
                entry.message += L"/";
                entry.message += IntToWString(static_cast<long long>(kDriveRetryCount));
                entry.message += L"：";
                entry.message += driveDisplay;
                entry.isError = false; // §17.58：重试是 LogNeutral
                preLogs.push_back(entry);
                std::this_thread::sleep_for(std::chrono::milliseconds(kDriveRetryDelayMs));
                ready = volume::IsDriveReady(driveRoot);
            }
        }
        if (!ready) {
            std::wstring message = L"ERROR 驱动器不可用：";
            message += driveDisplay;
            WriteStderrLine(message);
            return kExitUsage;
        }
    }

    // ---- 10. 源目录必须已存在（--source 任意目录；--drive 时为盘根）----
    if (!path::DirectoryExists(sourceRoot)) {
        std::wstring message = L"ERROR 源目录不存在：";
        message += sourceRoot;
        WriteStderrLine(message);
        return kExitUsage;
    }

    // ---- 11. GetDriveId → 白名单校验（§17.40）→ RecordDriveInsertion（§17.49）----
    std::wstring driveId;
    const std::wstring sourceVolumeRoot = volume::VolumeRootOf(sourceRoot);
    if (!sourceVolumeRoot.empty()) {
        driveId = volume::GetDriveId(sourceVolumeRoot);
    }
    if (options.enableWhitelist && !options.whitelistedDriveIds.empty() &&
        !parsed.ignoreWhitelist) {
        bool matched = false;
        if (!driveId.empty()) {
            for (const std::wstring& allowed : options.whitelistedDriveIds) {
                // §17.40 / §17.54：C# List<string>.Contains 是 Ordinal（区分大小写），
                // M2 照抄 1:1；大小写归一化留到 M3 在 UI 上做
                if (allowed == driveId) {
                    matched = true;
                    break;
                }
            }
        }
        if (!matched) {
            // 没有盘符可算（UNC / 取不到序列号）时 driveId 为空 → 视为不匹配
            std::wstring message = L"ERROR 驱动器不在白名单中：";
            message += driveDisplay.empty() ? sourceRoot : driveDisplay;
            message += L"（卷序列号：";
            message += driveId.empty() ? L"UNKNOWN" : driveId;
            message += L"）";
            WriteStderrLine(message);
            return kExitUsage;
        }
    }
    if (!driveId.empty()) {
        // --dry-run 也要调（§17.49）：保持"真实复制时会怎么走"的前置状态一致
        copy::RecordDriveInsertion(driveId);
    }

    // ---- 12. 打开输出（--out 文件或 stdout）：失败 exit 2 ----
    FILETIME startedFileTime{};
    GetSystemTimePreciseAsFileTime(&startedFileTime);
    const long long startedFt = FileTimeToInt64(startedFileTime);
    const ULONGLONG tickStart = GetTickCount64();

    jsonl::Writer writer;
    const bool opened =
        parsed.outFile.empty() ? writer.OpenStdout() : writer.OpenFile(parsed.outFile);
    if (!opened) {
        if (parsed.outFile.empty()) {
            WriteStderrLine(L"ERROR 无法打开标准输出");
        } else {
            std::wstring message = L"ERROR 无法创建输出文件：";
            message += parsed.outFile;
            WriteStderrLine(message);
        }
        return kExitUsage;
    }

    // ---- 13. 首行 batch（§13 A 表）----
    jsonl::BatchLine batch;
    batch.index = 1; // --once 恒为 1（§17.64）
    batch.direction = L"forward";
    batch.drive = driveDisplay; // 没有 --drive 时为空串
    batch.startedFt = startedFt;
    if (!writer.WriteLine(jsonl::FormatBatch(batch))) {
        WriteStderrLine(L"ERROR 写入输出失败：batch");
        return kExitInternal;
    }

    // ---- 14. --emit-log：batch 之后输出预运行日志（重试等）----
    if (parsed.emitLog) {
        for (const LogEntry& entry : preLogs) {
            const wchar_t* level = entry.isError ? L"error" : L"info";
            if (!writer.WriteLine(jsonl::FormatLog(level, entry.message))) {
                WriteStderrLine(L"ERROR 写入输出失败：log");
                return kExitInternal;
            }
        }
    }

    // ---- 15. 回调 → 流式 JSONL + derived 计数 ----
    int filesDecided = 0;
    int logLineCount = 0;
    int derivedCopy = 0;
    int derivedSkip = 0;
    int derivedFail = 0;
    int derivedSizeLimit = 0;
    int derivedCancel = 0; // D38：取消也计入 failure，但 schema 里没有独立字段
    int lastTotal = 0;
    int lastSuccess = 0;
    int lastFailure = 0;
    bool hasCounts = false;
    bool writeFailed = false;

    copy::CopyCallbacks callbacks;
    callbacks.onLog = [&](const std::wstring& message, bool isError) {
        ++logLineCount;
        if (!parsed.emitLog) {
            return;
        }
        const wchar_t* level = isError ? L"error" : L"info";
        if (!writer.WriteLine(jsonl::FormatLog(level, message))) {
            writeFailed = true;
        }
    };
    callbacks.onFileHandled = [&](const copy::FileReport& report) {
        ++filesDecided;
        switch (report.kind) {
            case copy::DecisionKind::Copy:
                ++derivedCopy;
                break;
            case copy::DecisionKind::Skip:
                ++derivedSkip;
                if (report.reason != nullptr &&
                    std::wcscmp(report.reason, copy::reason::kSizeLimit) == 0) {
                    // 超大文件：决策是 skip，但引擎计入 failure（§17.2 唯一例外）
                    ++derivedSizeLimit;
                }
                break;
            case copy::DecisionKind::Fail:
                ++derivedFail;
                break;
            case copy::DecisionKind::Cancelled:
                ++derivedCancel;
                break;
            case copy::DecisionKind::NotVisited:
                break; // C++ 引擎不产出 not-visited（由比对脚本重算，§17.38）
        }
        if (!writer.WriteLine(jsonl::FormatFileFromReport(report))) {
            writeFailed = true;
        }
    };
    callbacks.onCounts = [&](int total, int success, int failure) {
        lastTotal = total;
        lastSuccess = success;
        lastFailure = failure;
        hasCounts = true;
    };
    callbacks.onDirectorySkipped = [&](const std::wstring& relativePath,
                                       const wchar_t* skipReason) {
        jsonl::DirLine dir;
        dir.path = relativePath;
        dir.skipReason = skipReason != nullptr ? skipReason : L"";
        if (!writer.WriteLine(jsonl::FormatDir(dir))) {
            writeFailed = true;
        }
    };

    // ---- 16. 调引擎（时序：RecordDriveInsertion 已在上方完成）----
    copy::EngineParams engineParams;
    engineParams.sourceRoot = sourceRoot;
    engineParams.targetRoot = targetRoot;
    engineParams.relativeTargetDir = parsed.folderName;
    engineParams.reverseCopy = false;
    engineParams.dryRun = parsed.dryRun;
    engineParams.cancelAfterMs = parsed.cancelAfterMs;
    const copy::EngineStats stats = copy::RunCopyEngine(engineParams, options, callbacks);

    // ---- 17. 末行 summary（§13 D 表 + §17.69：没有独立 counts 字段）----
    const long long durationMs = static_cast<long long>(GetTickCount64() - tickStart);
    const int total = hasCounts ? lastTotal : stats.total;
    const int success = hasCounts ? lastSuccess : stats.success;
    const int failure = hasCounts ? lastFailure : stats.failure;
    int notVisited = stats.sourceFiles - filesDecided;
    if (notVisited < 0) {
        notVisited = 0;
    }
    // D38：countedAsFailure = fail + sizeLimit + cancel（取消也计失败）
    const int countedAsFailure = derivedFail + derivedSizeLimit + derivedCancel;
    const int totalCounted = derivedCopy + countedAsFailure;

    if (!writer.ok()) {
        writeFailed = true;
    }
    int exitCode = kExitSuccess;
    if (stats.cancelled) {
        exitCode = kExitCancelled; // 取消优先于"有失败"（§17.33）
    } else if (failure > 0) {
        exitCode = kExitFilesFailed;
    }
    if (writeFailed) {
        exitCode = kExitInternal;
    }

    jsonl::SummaryLine summary;
    summary.total = total;
    summary.success = success;
    summary.failure = failure;
    summary.cancelled = stats.cancelled;
    summary.countsLastTotal = total;
    summary.countsLastSuccess = success;
    summary.countsLastFailure = failure;
    summary.derived.copy = derivedCopy;
    summary.derived.skip = derivedSkip;
    summary.derived.fail = derivedFail;
    summary.derived.sizeLimit = derivedSizeLimit;
    summary.derived.countedAsFailure = countedAsFailure;
    summary.derived.totalCounted = totalCounted;
    summary.derived.notVisited = notVisited;
    summary.sourceFiles = stats.sourceFiles;
    summary.filesDecided = filesDecided;
    summary.durationMs = durationMs;
    summary.logLines = logLineCount;
    summary.target = targetRoot; // 解析后的绝对路径（§17.70）
    summary.exitCode = exitCode;  // 必须与函数实际返回值一致

    if (!writer.WriteLine(jsonl::FormatSummary(summary))) {
        WriteStderrLine(L"ERROR 写入输出失败：summary");
        return kExitInternal;
    }
    if (writeFailed) {
        WriteStderrLine(L"ERROR 写入输出失败：中间行（summary 已写出 exitCode=4）");
        return kExitInternal;
    }
    return exitCode;
}

} // namespace

bool WantsHeadless(const std::vector<std::wstring>& args) {
    // --headless 或 --dry-run（后者隐含 headless，§17.19）；键名大小写不敏感
    for (const std::wstring& argument : args) {
        std::wstring key = argument;
        const size_t equals = key.find(L'=');
        if (equals != std::wstring::npos) {
            key = key.substr(0, equals);
        }
        const std::wstring lower = ToLowerAscii(key);
        if (lower == L"--headless" || lower == L"--dry-run") {
            return true;
        }
    }
    return false;
}

int RunHeadless(const std::vector<std::wstring>& args) {
    try {
        return RunHeadlessImpl(args);
    } catch (...) {
        // 兜底：理论上本文件不抛异常；内存耗尽等意外统一记 exit 4（§17.33）
        WriteStderrLine(L"ERROR 未处理错误（exit 4）");
        return kExitInternal;
    }
}

} // namespace usbcopy::app
