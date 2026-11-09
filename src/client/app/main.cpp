#include <windows.h>

#include <commctrl.h>
#include <objbase.h>
#include <shellapi.h>
#include <stdlib.h>

#include <string>
#include <vector>

#include "MainWindow.h"
#include "headless.h"
#include "win_util.h"

namespace {

constexpr const wchar_t* kMutexName = L"Local\\UsbCopy.SingleInstance";

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE /*prevInstance*/, PWSTR /*commandLine*/, int showCommand) {
    // ---- 命令行（单实例互斥之前先判断 headless：无 UI 模式不参与单实例）----
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::wstring> args;
    int initialTab = 0;
    if (argv != nullptr) {
        for (int i = 1; i < argc; ++i) {
            const std::wstring arg = argv[i];
            args.push_back(arg);
            if (arg.rfind(L"--tab=", 0) == 0) {
                initialTab = _wtoi(arg.c_str() + 6);
                if (initialTab < 0) {
                    initialTab = 0;
                }
            }
        }
        LocalFree(argv);
    }

    if (usbcopy::app::WantsHeadless(args)) {
        return usbcopy::app::RunHeadless(args);
    }

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

    MainWindow* window = MainWindow::Create(instance, showCommand, initialTab);
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
