#pragma once

// ============================================================================
//  name_util —— 目录/文件名清洗（对应 C# CopyEngine.SanitizeFolderName，
//               CopyEngine.cs:726-751）
//
//  为什么单独成文件：正向复制建目录、反向复制找目录、目录树文件名，三处必须用
//  **同一个函数**。C# 里 `GetLocalBackupPath` 的注释就是这个意思：
//  "必须与正向复制使用同一套目录命名（卷标_卷序列号），否则反向复制永远找不到
//   正向复制生成的备份目录。"
//
//  ---- C# 的六步（逐字对应，任何一步改了都会让反向复制找不到目录）----
//  ① `IsNullOrWhiteSpace(name)` → 返回 `"UNKNOWN_DRIVE"`
//  ② 剔除 `Path.GetInvalidFileNameChars()` 里的字符
//     （`" < > | : * ? \ /` + U+0000..U+001F 共 41 个）
//     C# 写的是 `!invalid.Contains(c) || c == '_' || c == '-'`，其中 `_` 与 `-`
//     本来就不在非法集里 → **后半段是冗余的，行为 = 仅剔除非法字符**
//  ③ `.Trim()` —— **只去首尾空白**，不去尾随点（`NAME.` 保留那个点）
//  ④ 设备名（CON/PRN/AUX/NUL/COM1-9/LPT1-9，忽略大小写）→ **追加**后缀 `_DATA`
//  ⑤ `Length > 50` → `Substring(0, 45) + "~"`
//  ⑥ 调用点：先拼 `{卷标或盘符}_{序列号}`，再**整体**过 ①②③④⑤
//
//  ---- D34：第 ⑥ 步的缺陷与修复 ----
//  第 ⑤ 步作用在拼好的整体上。卷标 50 字符 + `_` + 8 位序列号 = 59 字符 →
//  截成前 45 + `~` = 46 字符 → **序列号被截掉**。于是：
//    正向建出 `很长很长..._~`，反向按 `卷标_序列号` 去找 → 永远找不到。
//  这与 A1（序列号格式不一致）是**同一种故障模式**，所以 `BuildDriveFolderName`
//  改为：卷标部分先截断，**保证序列号完整存在**。
//  `SanitizeFolderName` 本身仍是 1:1（目录树文件名等处还需要它原样语义）。
// ============================================================================

#include <windows.h>

#include <string>

namespace usbcopy::copy {

// C# 常量（CopyEngine.cs:748-750）
inline constexpr size_t kMaxFolderNameLength = 50;
inline constexpr size_t kTruncatedPrefixLength = 45;

// 1:1 复刻 C# `SanitizeFolderName`（含"截断整体"的原语义）
std::wstring SanitizeFolderName(const std::wstring& name);

// 驱动器备份目录名：`{卷标或盘符}_{序列号}`，**序列号保证完整**（D34 修复）
//
//   volumeLabel : 卷标；空或全空白时用 driveLetter（C# 的 `drive.Name.Replace(":\\","")`）
//   driveLetter : 盘符，如 `E`（不带冒号）
//   serial      : 卷序列号字符串（A1：C++ 侧 `%08X`；实际值待真机验证）
//
//  结果长度 ≤ 50（除非序列号本身过长），且 `find(serial) != npos`。
std::wstring BuildDriveFolderName(const std::wstring& volumeLabel, const std::wstring& driveLetter,
                                  const std::wstring& serial);

// 目录树文件名：`{清洗后的卷标}_目录树_{yyyyMMdd_HHmmss}.txt`
// （C# 这里过的是**卷标本身**，不是 `卷标_序列号`，见 CopyEngine.cs:632）
std::wstring BuildDirectoryTreeFileName(const std::wstring& volumeLabelOrDriveLetter,
                                        const SYSTEMTIME& localTime);

// 是否为 Windows 设备名（不含扩展名比较，忽略大小写）
bool IsReservedDeviceName(const std::wstring& name);

} // namespace usbcopy::copy
