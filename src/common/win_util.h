#pragma once

#include <windows.h>
#include <string>

namespace util {

// 把 96dpi 设计值按当前 DPI 缩放
int Scale(int value, UINT dpi);

// 取窗口当前 DPI（Win10 1607+ 用 GetDpiForWindow，更早系统退回系统 DPI）
UINT DpiOf(HWND hwnd);

// 统一界面字体：Microsoft YaHei UI
HFONT CreateUiFont(UINT dpi, int pointSize, bool bold);

// 递归给所有子控件设置字体（DPI 变化后调用）
void SetFontForChildren(HWND parent, HFONT font);

// Win32 错误码 → 可读文本
std::wstring Win32Message(DWORD code);

std::string ToUtf8(const std::wstring& text);
std::wstring FromUtf8(const std::string& text);

// 轻量 RAII（仅 HANDLE，避免手写 CloseHandle）
struct HandleGuard {
    HANDLE h = nullptr;
    HandleGuard() = default;
    explicit HandleGuard(HANDLE value) : h(value) {}
    ~HandleGuard() { reset(); }
    HandleGuard(const HandleGuard&) = delete;
    HandleGuard& operator=(const HandleGuard&) = delete;

    void reset(HANDLE value = nullptr) {
        if (h && h != INVALID_HANDLE_VALUE) {
            CloseHandle(h);
        }
        h = value;
    }
    HANDLE get() const { return h; }
    explicit operator bool() const { return h != nullptr && h != INVALID_HANDLE_VALUE; }
};

} // namespace util
