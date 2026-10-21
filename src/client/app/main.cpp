#include <windows.h>

#include <commctrl.h>
#include <objbase.h>
#include <shellapi.h>
#include <stdlib.h>

#include <string>

#include "MainWindow.h"
#include "win_util.h"

namespace {

constexpr const wchar_t* kMutexName = L"Local\\UsbCopy.SingleInstance";

// 解析 --tab=N（启动页），返回 0 表示未指定
int ParseInitialTab() {
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    int tab = 0;
    if (argv != nullptr) {
        for (int i = 1; i < argc; ++i) {
            const wchar_t* arg = argv[i];
            if (wcsncmp(arg, L"--tab=", 6) == 0) {
                tab = _wtoi(arg + 6);
                if (tab < 0) {
                    tab = 0;
                }
            }
        }
        LocalFree(argv);
    }
    return tab;
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE /*prevInstance*/, PWSTR /*commandLine*/, int showCommand) {
    // ---- 单实例：已有窗口则唤起它 ----
    util::HandleGuard mutex(CreateMutexW(nullptr, TRUE, kMutexName));
    if (mutex.get() != nullptr && GetLastError() == ERROR_ALREADY_EXISTS) {
        HWND existing = FindWindowW(MainWindow::kClassName, nullptr);
        if (existing != nullptr) {
            PostMessageW(existing, MainWindow::kMsgShowExisting, 0, 0);
        }
        return 0;
    }

    // ---- COM（文件夹选择对话框需要）----
    HRESULT comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);

    // ---- 通用控件 v6（页签、按钮等视觉样式）----
    INITCOMMONCONTROLSEX controls{ sizeof(INITCOMMONCONTROLSEX),
                                   ICC_TAB_CLASSES | ICC_STANDARD_CLASSES | ICC_WIN95_CLASSES };
    InitCommonControlsEx(&controls);

    if (!MainWindow::Register(instance)) {
        MessageBoxW(nullptr, L"窗口类注册失败，程序无法启动。", L"启动失败", MB_ICONERROR);
        if (SUCCEEDED(comResult)) {
            CoUninitialize();
        }
        return 1;
    }

    MainWindow* window = MainWindow::Create(instance, showCommand, ParseInitialTab());
    if (window == nullptr) {
        MessageBoxW(nullptr, L"主窗口创建失败，程序无法启动。", L"启动失败", MB_ICONERROR);
        if (SUCCEEDED(comResult)) {
            CoUninitialize();
        }
        return 1;
    }

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    if (SUCCEEDED(comResult)) {
        CoUninitialize();
    }
    return static_cast<int>(message.wParam);
}
