#include "name_util.h"

#include <algorithm>
#include <cwctype>
#include <vector>

namespace usbcopy::copy {
namespace {

// `Path.GetInvalidFileNameChars()` 的精确集合（Windows，共 41 个）：
//   9 个可见字符 + U+0000..U+001F
bool IsInvalidFileNameChar(wchar_t c) {
    if (c < 0x20) {
        return true; // 控制字符，含 U+0000
    }
    switch (c) {
    case L'"':
    case L'<':
    case L'>':
    case L'|':
    case L':':
    case L'*':
    case L'?':
    case L'\\':
    case L'/':
        return true;
    default:
        return false;
    }
}

bool IsBlank(const std::wstring& text) {
    for (wchar_t c : text) {
        if (!iswspace(c)) {
            return false;
        }
    }
    return true;
}

// C# `string.Trim()`：去掉首尾空白（Unicode 空白，非仅 ASCII 空格）
void TrimInPlace(std::wstring& text) {
    size_t begin = 0;
    while (begin < text.size() && iswspace(text[begin])) {
        ++begin;
    }
    size_t end = text.size();
    while (end > begin && iswspace(text[end - 1])) {
        --end;
    }
    text = text.substr(begin, end - begin);
}

bool EqualsIgnoreCase(const std::wstring& left, const wchar_t* right) {
    return CompareStringOrdinal(left.c_str(), static_cast<int>(left.size()), right, -1, TRUE) ==
           CSTR_EQUAL;
}

} // namespace

bool IsReservedDeviceName(const std::wstring& name) {
    // 注意：C# 用 OrdinalIgnoreCase 比较**整个清洗后的字符串**，
    // 不是"截掉扩展名再比"。所以 `CON.txt` 不算设备名（会被保留），
    // 而 `CON` 会被改成 `CON_DATA`。
    static const wchar_t* const kNames[] = {
        L"CON",  L"PRN",  L"AUX",  L"NUL",  L"COM1", L"COM2", L"COM3", L"COM4",
        L"COM5", L"COM6", L"COM7", L"COM8", L"COM9", L"LPT1", L"LPT2", L"LPT3",
        L"LPT4", L"LPT5", L"LPT6", L"LPT7", L"LPT8", L"LPT9",
    };
    for (const wchar_t* candidate : kNames) {
        if (EqualsIgnoreCase(name, candidate)) {
            return true;
        }
    }
    return false;
}

std::wstring SanitizeFolderName(const std::wstring& name) {
    // ① 空白 → UNKNOWN_DRIVE
    if (IsBlank(name)) {
        return L"UNKNOWN_DRIVE";
    }

    // ② 剔除非法字符（C# 的 `|| c == '_' || c == '-'` 是冗余的，行为等价）
    std::wstring clean;
    clean.reserve(name.size());
    for (wchar_t c : name) {
        if (!IsInvalidFileNameChar(c)) {
            clean.push_back(c);
        }
    }

    // ③ Trim（不去尾随点）
    TrimInPlace(clean);

    // ④ 设备名 → 追加 _DATA（后缀，不是前缀）
    if (IsReservedDeviceName(clean)) {
        clean += L"_DATA";
    }

    // ⑤ 超过 50 → 前 45 + `~`（这是 D34 修复的对象；本函数保持 1:1）
    if (clean.size() > kMaxFolderNameLength) {
        clean = clean.substr(0, kTruncatedPrefixLength) + L"~";
    }
    return clean;
}

std::wstring BuildDriveFolderName(const std::wstring& volumeLabel, const std::wstring& driveLetter,
                                  const std::wstring& serial) {
    // 卷标为空/全空白 → 用盘符（C#：`drive.Name.Replace(":\\", "")`）
    const std::wstring baseText = IsBlank(volumeLabel) ? driveLetter : volumeLabel;
    const std::wstring baseClean = SanitizeFolderName(baseText);

    // 序列号本身过一遍清洗（防御：万一 A1 验证后发现含连字符等）
    // 注意：C# 的 `GetDriveId` 读不到时返回 **"UNKNOWN"**（不是空串），
    // 所以空串属于退化输入，这里直接返回卷标，避免出现 `KINGSTON_UNKNOWN_DRIVE`
    if (IsBlank(serial)) {
        return baseClean;
    }
    const std::wstring serialClean = SanitizeFolderName(serial);

    const std::wstring combined = baseClean + L"_" + serialClean;
    if (combined.size() <= kMaxFolderNameLength) {
        // 不触发截断 → 与 C# 完全一致
        return SanitizeFolderName(combined);
    }

    // ---- D34：需要截断时保住序列号 ----
    // 预算：总长 50 = 卷标前缀 + `~`(1) + `_`(1) + 序列号
    constexpr size_t kSuffixBudget = 1 /*~*/ + 1 /*_*/;
    if (serialClean.size() + kSuffixBudget >= kMaxFolderNameLength) {
        // 序列号本身太长（理论上不会发生）：退回 C# 行为，但至少留下序列号头部
        return SanitizeFolderName(serialClean);
    }
    const size_t labelBudget = kMaxFolderNameLength - kSuffixBudget - serialClean.size();
    std::wstring labelPart = baseClean.substr(0, labelBudget);
    TrimInPlace(labelPart);
    if (labelPart.empty()) {
        return SanitizeFolderName(L"_" + serialClean);
    }
    return labelPart + L"~_" + serialClean;
}

std::wstring BuildDirectoryTreeFileName(const std::wstring& volumeLabelOrDriveLetter,
                                        const SYSTEMTIME& localTime) {
    // C#: `$"{SanitizeFolderName(label ?? 盘符)}_目录树_{DateTime.Now:yyyyMMdd_HHmmss}.txt"`
    wchar_t stamp[32]{};
    swprintf_s(stamp, L"%04u%02u%02u_%02u%02u%02u", static_cast<unsigned>(localTime.wYear),
               static_cast<unsigned>(localTime.wMonth), static_cast<unsigned>(localTime.wDay),
               static_cast<unsigned>(localTime.wHour), static_cast<unsigned>(localTime.wMinute),
               static_cast<unsigned>(localTime.wSecond));
    return SanitizeFolderName(volumeLabelOrDriveLetter) + L"_目录树_" + stamp + L".txt";
}

} // namespace usbcopy::copy
