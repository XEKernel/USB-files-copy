#pragma once

#include <windows.h>
#include <shellapi.h>   // NOTIFYICONDATAW

#include <string>
#include <vector>

#include "ui_settings.h"

// 主窗口（纯 Win32，无框架）。
// 本阶段只实现界面：控件创建、页签切换、DPI 适配、日志视图、托盘、单实例唤醒。
// 复制/监听/上传等后端逻辑留空（见 OnCommand 中的 TODO）。
class MainWindow {
public:
    static constexpr const wchar_t* kClassName = L"UsbCopyMainWindow";
    static constexpr UINT kMsgShowExisting = WM_APP + 1;   // 第二个实例请求显示窗口
    static constexpr UINT kMsgTray = WM_APP + 2;           // 托盘图标回调

    static bool Register(HINSTANCE instance);
    static MainWindow* Create(HINSTANCE instance, int showCommand, int initialTab = 0);

    HWND handle() const { return hwnd_; }
    void ShowFromSecondInstance();

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK PageHostProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
    LRESULT Handle(UINT msg, WPARAM wParam, LPARAM lParam);

    // ---- 构建 ----
    void CreateControls();
    void CreateGeneralPage();
    void CreateAdvancedPage();
#if !defined(LOCAL_ONLY)
    void CreateServerPage();
#endif
    void CreateAboutPage();
    void RecreateFonts();
    void Layout();

    // ---- 行为 ----
    void ApplyPageVisibility(int index);
    void OnCommand(int id, int code);
    void AppendLog(const std::wstring& text, bool isError = false);
    void ClearLog();
    void BrowseTargetFolder();
    void SetCounters(int total, int success, int failed);
    void AddTrayIcon();
    void RemoveTrayIcon();
    void ShowTrayMenu();
    void ShowAboutLinks(int id);

    // ---- 控件工厂（自动登记到当前页 + 布局表）----
    HWND Control(int id) const;   // 按 ID 取控件（优先页容器）
    HWND NewControl(const wchar_t* className, const wchar_t* text, DWORD style, int id,
                    int x, int y, int w, int h, DWORD exStyle = 0);
    HWND AddGroup(const wchar_t* title, int x, int y, int w, int h);
    HWND AddLabel(const wchar_t* text, int x, int y, int w, int h, DWORD style = SS_LEFT | SS_NOPREFIX);
    HWND AddCheck(const wchar_t* text, int id, bool checked, int x, int y, int w, int h);
    HWND AddRadio(const wchar_t* text, int id, bool checked, bool groupStart,
                  int x, int y, int w, int h);
    HWND AddEdit(int id, const wchar_t* text, int x, int y, int w, int h, DWORD extraStyle = 0);
    HWND AddButton(const wchar_t* text, int id, int x, int y, int w, int h);
    HWND AddCombo(int id, const wchar_t* const* items, int count, int selected,
                  int x, int y, int w, int h);

    struct LayoutItem {
        HWND hwnd = nullptr;
        RECT design{};   // 96dpi 设计坐标（left/top/right/bottom）
    };

    HINSTANCE instance_ = nullptr;
    HWND hwnd_ = nullptr;
    HWND tab_ = nullptr;
    HWND pageHost_ = nullptr;   // 页内容容器（覆盖页签客户区，统一背景色）
    HWND logEdit_ = nullptr;
    HWND counterLabel_ = nullptr;
    HWND serverStatus_ = nullptr;

    HFONT font_ = nullptr;
    HFONT fontBold_ = nullptr;
    HICON iconLarge_ = nullptr;
    HICON iconSmall_ = nullptr;

    UINT dpi_ = 96;
    int initialTab_ = 0;   // 启动时选中哪个页签（--tab=N，便于截图/调试）
    NOTIFYICONDATAW tray_{};
    bool trayAdded_ = false;

    UiSettings settings_{};

    std::vector<std::vector<HWND>> pages_;
    std::vector<HWND>* currentPage_ = nullptr;
    std::vector<LayoutItem> layout_;
    std::vector<HWND> serverConfigCtrls_;   // 服务器参数控件（切到本地模式时禁用）
    int logLines_ = 0;
};
