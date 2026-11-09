#pragma once

// ============================================================================
//  volume —— 卷/驱动器信息与文件元数据（M2-A 新增）
//
//  为什么单独成文件：基线 §17.74 说 `GetDriveId` 是**一个函数**、三处调用
//  （目录命名 / 限速窗口 key / 白名单），任何一处格式不同都会让反向复制找不到目录。
//  所以格式化成 `%08X` 这件事必须只有一处实现。
//
//  Win32 调用集中在这里，业务代码（headless / copy_engine）不直接碰
//  GetVolumeInformationW / GetFileAttributesExW（硬约束 4）。
//
//  A1 待真机验证：C# 用 WMI `Win32_LogicalDisk.VolumeSerialNumber`（不做格式化），
//  C++ 用 `GetVolumeInformationW` 的 dwVolumeSerialNumber 按 `%08X`（大写补零）。
//  若真机不等，**只改 FormatSerial 一处**（基线 §5）。
// ============================================================================

#include <windows.h>

#include <string>

namespace usbcopy::volume {

// 卷序列号格式化：大写十六进制、补零到 8 位（基线 §5 / §17.74）
//   0x0000ABCD → L"0000ABCD"
std::wstring FormatSerial(DWORD serial);

// 盘根（`E:\` / `E:` / `\\server\share`）→ 卷序列号；失败返回空串。
// 失败语义对调用方很重要：`--drive` 的「就绪」判据就是它返回非空（§17.58）。
std::wstring GetDriveId(const std::wstring& root);

// 盘根 → 卷标；失败（含未就绪）返回空串
std::wstring GetVolumeLabel(const std::wstring& root);

// 盘根 → 文件系统名（NTFS/FAT32/exFAT…）；失败返回空串
std::wstring GetFileSystemName(const std::wstring& root);

// `E:\` / `E:` / `E:\sub` → `E`（大写、不带冒号）；
// 非驱动器路径（UNC / 相对路径）返回空串
std::wstring DriveLetterOf(const std::wstring& path);

// 路径的盘根：`C:\a\b` → `C:\`；无盘符（相对路径/UNC）返回空串
// 注意：结果**带**尾部分隔符，可直接喂给 GetDriveId
std::wstring VolumeRootOf(const std::wstring& path);

// GetDriveTypeW == DRIVE_REMOVABLE
bool IsRemovableDrive(const std::wstring& root);

// 「就绪」判据（§17.58）：可移动盘 **且** GetVolumeInformationW 成功
bool IsDriveReady(const std::wstring& root);

// 文件/目录元数据（GetFileAttributesExW 的薄封装）
struct FileStat {
    bool exists = false;
    DWORD attributes = INVALID_FILE_ATTRIBUTES;
    long long size = 0;       // 字节数；目录恒为 0
    long long lastWriteFt = 0; // FILETIME（UTC，int64，100ns）
};

// 取元数据；文件不存在时 exists=false（其余字段保持 0/INVALID）
bool StatFile(const std::wstring& fullPath, FileStat& out);

// 读盘剩余空间（字节）；失败返回 -1。`checkFreeSpace` 用（§17.2 的本地目标分支）
long long AvailableDiskBytes(const std::wstring& directory);

} // namespace usbcopy::volume
