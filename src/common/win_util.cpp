#include "win_util.h"

#include <vector>

namespace util {

int Scale(int value, UINT dpi) {
    if (dpi == 0) {
        dpi = 96;
    }
    return MulDiv(value, static_cast<int>(dpi), 96);
}

UINT DpiOf(HWND hwnd) {
    // GetDpiForWindow 需要 Win10 1607+，这里动态取，便于在更早系统上退化为系统 DPI
    using GetDpiForWindowFn = UINT(WINAPI*)(HWND);
    static GetDpiForWindowFn getDpiForWindow = reinterpret_cast<GetDpiForWindowFn>(
        GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetDpiForWindow"));

    if (getDpiForWindow != nullptr && hwnd != nullptr) {
        UINT dpi = getDpiForWindow(hwnd);
        if (dpi != 0) {
            return dpi;
        }
    }

    HDC screen = GetDC(nullptr);
    UINT fallback = 96;
    if (screen != nullptr) {
        int dpi = GetDeviceCaps(screen, LOGPIXELSX);
        if (dpi > 0) {
            fallback = static_cast<UINT>(dpi);
        }
        ReleaseDC(nullptr, screen);
    }
    return fallback;
}

HFONT CreateUiFont(UINT dpi, int pointSize, bool bold) {
    LOGFONTW lf{};
    lf.lfHeight = -MulDiv(pointSize, static_cast<int>(dpi), 72);
    lf.lfWeight = bold ? FW_SEMIBOLD : FW_NORMAL;
    lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfQuality = CLEARTYPE_QUALITY;
    lf.lfPitchAndFamily = DEFAULT_PITCH | FF_DONTCARE;
    wcscpy_s(lf.lfFaceName, L"Microsoft YaHei UI");
    return CreateFontIndirectW(&lf);
}

namespace {

BOOL CALLBACK ApplyFontProc(HWND child, LPARAM font) {
    SendMessageW(child, WM_SETFONT, static_cast<WPARAM>(font), TRUE);
    return TRUE;
}

} // namespace

void SetFontForChildren(HWND parent, HFONT font) {
    if (parent != nullptr && font != nullptr) {
        EnumChildWindows(parent, ApplyFontProc, reinterpret_cast<LPARAM>(font));
    }
}

std::wstring Win32Message(DWORD code) {
    if (code == 0) {
        return L"(无错误)";
    }

    LPWSTR buffer = nullptr;
    DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
                                      FORMAT_MESSAGE_IGNORE_INSERTS,
                                  nullptr, code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
                                  reinterpret_cast<LPWSTR>(&buffer), 0, nullptr);
    std::wstring text;
    if (length != 0 && buffer != nullptr) {
        text.assign(buffer, length);
        // 去掉结尾换行
        while (!text.empty() && (text.back() == L'\r' || text.back() == L'\n' || text.back() == L' ')) {
            text.pop_back();
        }
    } else {
        text = L"未知错误 " + std::to_wstring(code);
    }
    if (buffer != nullptr) {
        LocalFree(buffer);
    }
    return text;
}

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) {
        return {};
    }
    int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()),
                                   nullptr, 0, nullptr, nullptr);
    if (size <= 0) {
        return {};
    }
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()),
                        result.data(), size, nullptr, nullptr);
    return result;
}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) {
        return {};
    }
    int size = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), nullptr, 0);
    if (size <= 0) {
        return {};
    }
    std::wstring result(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), result.data(), size);
    return result;
}

} // namespace util
