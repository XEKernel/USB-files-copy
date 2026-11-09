#include "volume.h"

#include <algorithm>
#include <cwctype>
#include <string>

#include "path_util.h"

namespace usbcopy::volume {

namespace {

// 把 `E:` / `E:\` / `E:\sub` 统一成 `E:\`；无法识别时返回空串。
// GetVolumeInformationW / GetDriveTypeW 都要求带尾部分隔符的根路径。
std::wstring NormalizeRoot(const std::wstring& root) {
    if (root.empty()) {
        return {};
    }
    std::wstring text = path::FromExtended(root);
    if (text.size() >= 2 && text[1] == L':' &&
        ((text[0] >= L'A' && text[0] <= L'Z') || (text[0] >= L'a' && text[0] <= L'z'))) {
        std::wstring result(1, static_cast<wchar_t>(std::towupper(text[0])));
        result.push_back(L':');
        result.push_back(L'\\');
        return result;
    }
    // UNC：\\server\share 需要在末尾补分隔符
    if (text.size() >= 2 && text[0] == L'\\' && text[1] == L'\\') {
        while (!text.empty() && (text.back() == L'\\' || text.back() == L'/')) {
            text.pop_back();
        }
        if (text.empty()) {
            return {};
        }
        return text + L"\\";
    }
    return {};
}

} // namespace

std::wstring FormatSerial(DWORD serial) {
    // 手写十六进制：避免 swprintf 的 CRT 家族告警，且格式只有这一处（§5 / §17.74）
    static const wchar_t kDigits[] = L"0123456789ABCDEF";
    wchar_t buffer[9] = {};
    for (int index = 0; index < 8; ++index) {
        const int shift = 28 - index * 4;
        buffer[index] = kDigits[(serial >> shift) & 0xF];
    }
    return std::wstring(buffer, 8);
}

std::wstring GetDriveId(const std::wstring& root) {
    const std::wstring normalized = NormalizeRoot(root);
    if (normalized.empty()) {
        return {};
    }
    DWORD serial = 0;
    if (!GetVolumeInformationW(normalized.c_str(), nullptr, 0, &serial, nullptr, nullptr, nullptr,
                               0)) {
        return {};
    }
    return FormatSerial(serial);
}

std::wstring GetVolumeLabel(const std::wstring& root) {
    const std::wstring normalized = NormalizeRoot(root);
    if (normalized.empty()) {
        return {};
    }
    wchar_t label[MAX_PATH + 1] = {};
    if (!GetVolumeInformationW(normalized.c_str(), label, MAX_PATH + 1, nullptr, nullptr, nullptr,
                               nullptr, 0)) {
        return {};
    }
    return std::wstring(label);
}

std::wstring GetFileSystemName(const std::wstring& root) {
    const std::wstring normalized = NormalizeRoot(root);
    if (normalized.empty()) {
        return {};
    }
    wchar_t name[MAX_PATH + 1] = {};
    if (!GetVolumeInformationW(normalized.c_str(), nullptr, 0, nullptr, nullptr, nullptr, name,
                               MAX_PATH + 1)) {
        return {};
    }
    return std::wstring(name);
}

std::wstring DriveLetterOf(const std::wstring& path) {
    const std::wstring text = path::FromExtended(path);
    if (text.size() >= 2 && text[1] == L':') {
        const wchar_t first = text[0];
        if ((first >= L'A' && first <= L'Z') || (first >= L'a' && first <= L'z')) {
            return std::wstring(1, static_cast<wchar_t>(std::towupper(first)));
        }
    }
    return {};
}

std::wstring VolumeRootOf(const std::wstring& path) {
    const std::wstring text = path::FromExtended(path);
    if (text.size() >= 2 && text[1] == L':') {
        const wchar_t first = text[0];
        if ((first >= L'A' && first <= L'Z') || (first >= L'a' && first <= L'z')) {
            std::wstring result(1, static_cast<wchar_t>(std::towupper(first)));
            result.push_back(L':');
            result.push_back(L'\\');
            return result;
        }
    }
    if (text.size() >= 2 && text[0] == L'\\' && text[1] == L'\\') {
        const size_t serverEnd = text.find(L'\\', 2);
        if (serverEnd == std::wstring::npos) {
            return {};
        }
        const size_t shareEnd = text.find(L'\\', serverEnd + 1);
        if (shareEnd == std::wstring::npos) {
            return text + L"\\";
        }
        return text.substr(0, shareEnd + 1);
    }
    return {};
}

bool IsRemovableDrive(const std::wstring& root) {
    const std::wstring normalized = NormalizeRoot(root);
    if (normalized.empty()) {
        return false;
    }
    return GetDriveTypeW(normalized.c_str()) == DRIVE_REMOVABLE;
}

bool IsDriveReady(const std::wstring& root) {
    // §17.58：GetDriveTypeW == DRIVE_REMOVABLE 且 GetVolumeInformationW 成功
    return IsRemovableDrive(root) && !GetDriveId(root).empty();
}

bool StatFile(const std::wstring& fullPath, FileStat& out) {
    out = FileStat{};
    if (fullPath.empty()) {
        return false;
    }
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path::ToExtended(fullPath).c_str(), GetFileExInfoStandard, &data)) {
        return false;
    }
    out.exists = true;
    out.attributes = data.dwFileAttributes;
    const ULARGE_INTEGER size{data.nFileSizeLow, data.nFileSizeHigh};
    out.size = static_cast<long long>(size.QuadPart);
    if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
        out.size = 0;
    }
    const ULARGE_INTEGER written{data.ftLastWriteTime.dwLowDateTime,
                                 data.ftLastWriteTime.dwHighDateTime};
    out.lastWriteFt = static_cast<long long>(written.QuadPart);
    return true;
}

long long AvailableDiskBytes(const std::wstring& directory) {
    if (directory.empty()) {
        return -1;
    }
    ULARGE_INTEGER available{};
    if (!GetDiskFreeSpaceExW(path::ToExtended(directory).c_str(), &available, nullptr, nullptr)) {
        return -1;
    }
    return static_cast<long long>(available.QuadPart);
}

} // namespace usbcopy::volume
