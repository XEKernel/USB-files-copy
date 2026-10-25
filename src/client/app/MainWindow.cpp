#include "MainWindow.h"

#include <commctrl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <winternl.h>   // RTL_OSVERSIONINFOW

#include <string>
#include <vector>

#include "win_util.h"

namespace {

constexpr const wchar_t* kPageHostClass = L"UsbCopyPageHost";

// ===== 96dpi 设计尺寸 =====
constexpr int kDesignW = 960;
constexpr int kDesignH = 590;

constexpr int kTabX = 12;
constexpr int kTabY = 12;
constexpr int kTabW = kDesignW - 24;
constexpr int kTabH = kDesignH - 24;

// 页内容区（Tab 客户区去掉页签头）
constexpr int kPageW = kTabW - 8;
constexpr int kPageH = kTabH - 36;

// 与 C# 版 LogEngine.MaxLogLines 保持一致
constexpr int kMaxLogLines = 900;

constexpr const wchar_t* kAppTitle = L"U盘文件复制器";
constexpr const wchar_t* kAppVersion = L"1.8.0";
constexpr const wchar_t* kGithubUrl = L"https://github.com/XEKernel/USB-files-copy";
constexpr const wchar_t* kBilibiliUrl = L"https://space.bilibili.com/3493093313707357";

enum : int {
    ID_TAB = 1000,

    // ---- 主界面 ----
    ID_TARGET_EDIT = 1101,
    ID_BROWSE_TARGET = 1102,
    ID_SAVE_LOCAL = 1103,
    ID_SAVE_SERVER = 1104,

    ID_TYPE_WORD = 1110,
    ID_TYPE_PPT = 1111,
    ID_TYPE_EXCEL = 1112,
    ID_TYPE_PDF = 1113,
    ID_TYPE_IMAGE = 1114,
    ID_TYPE_VIDEO = 1115,
    ID_TYPE_AUDIO = 1116,
    ID_TYPE_ARCHIVE = 1117,
    ID_TYPE_ALL = 1118,
    ID_TYPE_CUSTOM = 1119,
    ID_CUSTOM_EXT_EDIT = 1120,

    ID_DUP_SKIP = 1130,
    ID_DUP_OVERWRITE = 1131,
    ID_DUP_KEEP_BOTH = 1132,
    ID_DUP_NEWER = 1133,

    ID_LIMIT_SIZE = 1140,
    ID_MAX_SIZE_EDIT = 1141,

    ID_LIMIT_SPEED = 1150,
    ID_SPEED_MIN_EDIT = 1151,
    ID_SPEED_COMBO = 1152,

    ID_LOG_EDIT = 1160,
    ID_CLEAR_LOG = 1161,
    ID_RUN_BACKGROUND = 1162,

    // ---- 高级 ----
    ID_FILTER_NAME = 1200,
    ID_NAME_KEYWORDS = 1201,
    ID_FILTER_FOLDER = 1202,
    ID_FOLDER_KEYWORDS = 1203,

    ID_CREATE_TREE = 1210,
    ID_LIMIT_DEPTH = 1211,
    ID_DEPTH_EDIT = 1212,

    ID_LOG_SUCCESS = 1220,
    ID_LOG_ERROR = 1221,
    ID_LOG_NEUTRAL = 1222,
    ID_LOG_TO_FILE = 1223,
    ID_LOG_TO_WINDOW = 1224,

    ID_STOP_FILE = 1230,
    ID_STOP_FILE_EDIT = 1231,
    ID_REVERSE_FILE = 1232,
    ID_REVERSE_FILE_EDIT = 1233,

    ID_WHITELIST = 1240,
    ID_WHITELIST_EDIT = 1241,

    ID_AUTOSTART = 1250,
    ID_AUTOSTART_HIDDEN = 1251,
    ID_TRAY_ICON = 1252,
    ID_NOTIFY_COMPLETE = 1253,

    // ---- 服务器（仅完整版）----
    ID_SRV_LOCAL = 1300,
    ID_SRV_SERVER = 1301,
    ID_SRV_ADDRESS = 1302,
    ID_SRV_PORT = 1303,
    ID_SRV_TOKEN = 1304,
    ID_SRV_PASSWORD = 1305,
    ID_SRV_HTTPS = 1306,
    ID_SRV_CHUNKED = 1307,
    ID_SRV_CHUNK_SIZE = 1308,
    ID_SRV_TEST = 1309,
    ID_SRV_BROWSE = 1310,
    ID_SRV_STATUS = 1311,

    // ---- 关于 ----
    ID_LINK_GITHUB = 1400,
    ID_LINK_BILIBILI = 1401,

    // ---- 托盘菜单 ----
    ID_TRAY_SHOW = 1500,
    ID_TRAY_EXIT = 1501,
};

const wchar_t* const kSpeedOptions[] = { L"1 MB/s", L"2 MB/s", L"5 MB/s", L"10 MB/s" };

// 默认保存目录：桌面\U盘文件备份（与 C# 版行为一致）
std::wstring DefaultTargetDir() {
    PWSTR desktop = nullptr;
    std::wstring path;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Desktop, 0, nullptr, &desktop)) && desktop != nullptr) {
        path = desktop;
        path += L"\\U盘文件备份";
        CoTaskMemFree(desktop);
    }
    return path;
}

// 系统版本：用 RtlGetVersion（GetVersionExW 已被标记为废弃，且可能返回兼容值）
std::wstring OsVersionText() {
    using RtlGetVersionFn = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    static auto rtlGetVersion = reinterpret_cast<RtlGetVersionFn>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));

    RTL_OSVERSIONINFOW info{};
    info.dwOSVersionInfoSize = sizeof(info);
    if (rtlGetVersion != nullptr && rtlGetVersion(&info) == 0) {
        wchar_t buffer[64]{};
        swprintf_s(buffer, L"Windows %lu.%lu.%lu",
                   info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
        return buffer;
    }
    return L"Windows（版本未知）";
}

std::wstring CpuArchitectureText() {
    SYSTEM_INFO info{};
    GetNativeSystemInfo(&info);
    switch (info.wProcessorArchitecture) {
    case PROCESSOR_ARCHITECTURE_AMD64:
        return L"x64";
    case PROCESSOR_ARCHITECTURE_ARM64:
        return L"ARM64";
    case PROCESSOR_ARCHITECTURE_INTEL:
        return L"x86";
    default:
        return L"未知";
    }
}

std::wstring ExeDirectoryText() {
    wchar_t path[MAX_PATH * 2]{};
    if (GetModuleFileNameW(nullptr, path, static_cast<DWORD>(std::size(path))) == 0) {
        return L"（获取失败）";
    }
    std::wstring full = path;
    const size_t slash = full.find_last_of(L'\\');
    return (slash == std::wstring::npos) ? full : full.substr(0, slash);
}

} // namespace

HWND MainWindow::Control(int id) const {
    if (pageHost_ != nullptr) {
        HWND control = GetDlgItem(pageHost_, id);
        if (control != nullptr) {
            return control;
        }
    }
    return GetDlgItem(hwnd_, id);
}

// ============================================================================
// 注册与创建
// ============================================================================

bool MainWindow::Register(HINSTANCE instance) {
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &MainWindow::WndProc;
    wc.hInstance = instance;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = GetSysColorBrush(COLOR_BTNFACE);
    wc.lpszClassName = kClassName;
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1));
    wc.hIconSm = static_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(1), IMAGE_ICON,
                                              GetSystemMetrics(SM_CXSMICON),
                                              GetSystemMetrics(SM_CYSMICON), LR_DEFAULTCOLOR));
    if (RegisterClassExW(&wc) == 0) {
        return false;
    }

    // 页内容容器：独立类，背景统一为 COLOR_BTNFACE，
    // 避免 TabCtrl 页体（主题下为 #F9F9F9）与按钮/复选框中 #F0F0F0 背景产生灰块
    WNDCLASSEXW host{};
    host.cbSize = sizeof(host);
    host.lpfnWndProc = &MainWindow::PageHostProc;
    host.hInstance = instance;
    host.hbrBackground = GetSysColorBrush(COLOR_BTNFACE);
    host.lpszClassName = kPageHostClass;
    return RegisterClassExW(&host) != 0;
}

MainWindow* MainWindow::Create(HINSTANCE instance, int showCommand, int initialTab) {
    auto* window = new MainWindow();
    window->instance_ = instance;
    window->initialTab_ = initialTab;
    if (window->settings_.targetDir.empty()) {
        window->settings_.targetDir = DefaultTargetDir();
    }

    // 以 96dpi 设计尺寸建立窗口，非客户区由系统按当前 DPI 计算
    RECT rect{ 0, 0, util::Scale(kDesignW, util::DpiOf(nullptr)), util::Scale(kDesignH, util::DpiOf(nullptr)) };
    AdjustWindowRectEx(&rect, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE, 0);

    HWND hwnd = CreateWindowExW(0, kClassName, kAppTitle,
                                WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                                CW_USEDEFAULT, CW_USEDEFAULT,
                                rect.right - rect.left, rect.bottom - rect.top,
                                nullptr, nullptr, instance, window);
    if (hwnd == nullptr) {
        delete window;
        return nullptr;
    }

    ShowWindow(hwnd, showCommand);
    UpdateWindow(hwnd);
    return window;
}

LRESULT CALLBACK MainWindow::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {    MainWindow* self = nullptr;

    if (msg == WM_NCCREATE) {
        auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        self = static_cast<MainWindow*>(create->lpCreateParams);
        self->hwnd_ = hwnd;
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    } else {
        self = reinterpret_cast<MainWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    }

    if (self != nullptr) {
        return self->Handle(msg, wParam, lParam);
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

LRESULT CALLBACK MainWindow::PageHostProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_COMMAND:
        // 页内控件的通知统一转给主窗口，集中处理
        SendMessageW(GetParent(hwnd), WM_COMMAND, wParam, lParam);
        return 0;

    case WM_CTLCOLORSTATIC: {
        HDC dc = reinterpret_cast<HDC>(wParam);
        HWND control = reinterpret_cast<HWND>(lParam);
        if (GetDlgCtrlID(control) == ID_LOG_EDIT) {
            // 只读编辑框也走 STATIC 通知，日志区保持白底
            SetBkColor(dc, GetSysColor(COLOR_WINDOW));
            return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_WINDOW));
        }
        SetBkMode(dc, TRANSPARENT);
        return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_BTNFACE));
    }

    case WM_CTLCOLORBTN:
        return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_BTNFACE));

    default:
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}

void MainWindow::ShowFromSecondInstance() {
    if (IsIconic(hwnd_)) {
        ShowWindow(hwnd_, SW_RESTORE);
    } else {
        ShowWindow(hwnd_, SW_SHOW);
    }
    SetForegroundWindow(hwnd_);
}

// ============================================================================
// 消息处理
// ============================================================================

LRESULT MainWindow::Handle(UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_CREATE: {
        dpi_ = util::DpiOf(hwnd_);
        RecreateFonts();

#if defined(LOCAL_ONLY)
        SetWindowTextW(hwnd_, L"U盘文件复制器（单机版）");
#endif

        CreateControls();
        Layout();
        ApplyPageVisibility(0);

        // --tab=N：启动时直接切到指定页（截图与手工调试用）
        if (initialTab_ > 0 && static_cast<size_t>(initialTab_) < pages_.size()) {
            TabCtrl_SetCurSel(tab_, initialTab_);
            ApplyPageVisibility(initialTab_);
        }

        AddTrayIcon();

        AppendLog(std::wstring(L"界面已就绪（C++/Win32 原生实现，版本 ") + kAppVersion + L"）");
#if defined(LOCAL_ONLY)
        AppendLog(L"版本：单机版（不支持服务器连接）");
#else
        AppendLog(L"版本：完整版（支持服务器连接）");
#endif
        AppendLog(L"当前为界面预览：复制引擎 / U 盘监听 / 上传均未接入，按钮只记录日志。");
        SetCounters(0, 0, 0);
        return 0;
    }

    case WM_SIZE:
        if (wParam != SIZE_MINIMIZED) {
            Layout();
        }
        return 0;

    case WM_GETMINMAXINFO: {
        // 固定尺寸窗口：最小 = 最大 = 设计尺寸（避免控件被压坏）
        auto* info = reinterpret_cast<MINMAXINFO*>(lParam);
        UINT dpi = util::DpiOf(hwnd_);
        RECT rect{ 0, 0, util::Scale(kDesignW, dpi), util::Scale(kDesignH, dpi) };
        AdjustWindowRectEx(&rect, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE, 0);
        info->ptMinTrackSize.x = rect.right - rect.left;
        info->ptMinTrackSize.y = rect.bottom - rect.top;
        info->ptMaxTrackSize.x = rect.right - rect.left;
        info->ptMaxTrackSize.y = rect.bottom - rect.top;
        return 0;
    }

    case WM_DPICHANGED: {
        dpi_ = HIWORD(wParam);
        RECT* suggested = reinterpret_cast<RECT*>(lParam);
        SetWindowPos(hwnd_, nullptr, suggested->left, suggested->top,
                     suggested->right - suggested->left, suggested->bottom - suggested->top,
                     SWP_NOZORDER | SWP_NOACTIVATE);
        RecreateFonts();
        Layout();
        return 0;
    }

    case WM_NOTIFY: {
        auto* header = reinterpret_cast<NMHDR*>(lParam);
        if (header != nullptr && header->hwndFrom == tab_ && header->code == TCN_SELCHANGE) {
            ApplyPageVisibility(TabCtrl_GetCurSel(tab_));
        }
        return 0;
    }

    case WM_COMMAND: {
        int id = LOWORD(wParam);
        int code = HIWORD(wParam);
        if (code == BN_CLICKED || code == 0) {
            OnCommand(id, code);
        }
        return 0;
    }

    case WM_CTLCOLORSTATIC: {
        HDC dc = reinterpret_cast<HDC>(wParam);
        HWND control = reinterpret_cast<HWND>(lParam);
        if (control == logEdit_) {
            // 只读编辑框也走 STATIC 通知，日志区保持白底
            SetBkColor(dc, GetSysColor(COLOR_WINDOW));
            return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_WINDOW));
        }
        SetBkMode(dc, TRANSPARENT);
        return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_BTNFACE));
    }

    case kMsgShowExisting:
        ShowFromSecondInstance();
        return 0;

    case kMsgTray: {
        switch (LOWORD(lParam)) {
        case WM_RBUTTONUP:
        case WM_CONTEXTMENU:
            ShowTrayMenu();
            break;
        case WM_LBUTTONDBLCLK:
            ShowFromSecondInstance();
            break;
        default:
            break;
        }
        return 0;
    }

    case WM_CLOSE:
        RemoveTrayIcon();
        DestroyWindow(hwnd_);
        return 0;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;

    default:
        break;
    }

    return DefWindowProcW(hwnd_, msg, wParam, lParam);
}

// ============================================================================
// 字体
// ============================================================================

void MainWindow::RecreateFonts() {
    HFONT normal = util::CreateUiFont(dpi_, 9, false);
    HFONT bold = util::CreateUiFont(dpi_, 9, true);

    if (font_ != nullptr) {
        DeleteObject(font_);
    }
    if (fontBold_ != nullptr) {
        DeleteObject(fontBold_);
    }
    font_ = normal;
    fontBold_ = bold;

    util::SetFontForChildren(hwnd_, font_);
    if (counterLabel_ != nullptr) {
        SendMessageW(counterLabel_, WM_SETFONT, reinterpret_cast<WPARAM>(fontBold_), TRUE);
    }
    if (tab_ != nullptr) {
        SendMessageW(tab_, WM_SETFONT, reinterpret_cast<WPARAM>(font_), TRUE);
    }
}

// ============================================================================
// 控件工厂
// ============================================================================

HWND MainWindow::NewControl(const wchar_t* className, const wchar_t* text, DWORD style, int id,
                            int x, int y, int w, int h, DWORD exStyle) {
    HWND parent = (pageHost_ != nullptr) ? pageHost_ : hwnd_;
    HWND control = CreateWindowExW(exStyle, className, text, WS_CHILD | WS_VISIBLE | style,
                                   util::Scale(x, dpi_), util::Scale(y, dpi_),
                                   util::Scale(w, dpi_), util::Scale(h, dpi_),
                                   parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                                   instance_, nullptr);
    if (control == nullptr) {
        return nullptr;
    }
    SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(font_), TRUE);
    if (currentPage_ != nullptr) {
        currentPage_->push_back(control);
    }
    LayoutItem item;
    item.hwnd = control;
    item.design = RECT{ x, y, x + w, y + h };
    layout_.push_back(item);
    return control;
}

HWND MainWindow::AddGroup(const wchar_t* title, int x, int y, int w, int h) {
    return NewControl(L"BUTTON", title, BS_GROUPBOX, 0, x, y, w, h);
}

HWND MainWindow::AddLabel(const wchar_t* text, int x, int y, int w, int h, DWORD style) {
    return NewControl(L"STATIC", text, style, 0, x, y, w, h);
}

HWND MainWindow::AddCheck(const wchar_t* text, int id, bool checked, int x, int y, int w, int h) {
    HWND control = NewControl(L"BUTTON", text, BS_AUTOCHECKBOX, id, x, y, w, h);
    if (control != nullptr && checked) {
        SendMessageW(control, BM_SETCHECK, BST_CHECKED, 0);
    }
    return control;
}

HWND MainWindow::AddRadio(const wchar_t* text, int id, bool checked, bool groupStart,
                          int x, int y, int w, int h) {
    DWORD style = BS_AUTORADIOBUTTON;
    if (groupStart) {
        style |= WS_GROUP;
    }
    HWND control = NewControl(L"BUTTON", text, style, id, x, y, w, h);
    if (control != nullptr && checked) {
        SendMessageW(control, BM_SETCHECK, BST_CHECKED, 0);
    }
    return control;
}

HWND MainWindow::AddEdit(int id, const wchar_t* text, int x, int y, int w, int h, DWORD extraStyle) {
    return NewControl(L"EDIT", text,
                      WS_TABSTOP | ES_AUTOHSCROLL | extraStyle, id, x, y, w, h,
                      WS_EX_CLIENTEDGE);
}

HWND MainWindow::AddButton(const wchar_t* text, int id, int x, int y, int w, int h) {
    return NewControl(L"BUTTON", text, WS_TABSTOP | BS_PUSHBUTTON, id, x, y, w, h);
}

HWND MainWindow::AddCombo(int id, const wchar_t* const* items, int count, int selected,
                          int x, int y, int w, int h) {
    HWND control = NewControl(L"COMBOBOX", L"", WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
                              id, x, y, w, h);
    if (control != nullptr) {
        for (int i = 0; i < count; ++i) {
            SendMessageW(control, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(items[i]));
        }
        SendMessageW(control, CB_SETCURSEL, selected, 0);
    }
    return control;
}

// ============================================================================
// 主界面
// ============================================================================

void MainWindow::CreateGeneralPage() {
    // ---- 保存目录 ----
    AddLabel(L"保存目录：", 0, 2, 60, 20);
#if defined(LOCAL_ONLY)
    AddEdit(ID_TARGET_EDIT, settings_.targetDir.c_str(), 62, 0, 800, 22);
    AddButton(L"浏览...", ID_BROWSE_TARGET, 866, 0, 58, 22);
#else
    AddEdit(ID_TARGET_EDIT, settings_.targetDir.c_str(), 62, 0, 598, 22);
    AddButton(L"浏览...", ID_BROWSE_TARGET, 664, 0, 56, 22);
    AddRadio(L"本地", ID_SAVE_LOCAL, true, true, 734, 2, 54, 20);
    AddRadio(L"服务器", ID_SAVE_SERVER, false, false, 796, 2, 110, 20);
#endif

    // ---- 复制文件类型 ----
    AddGroup(L"复制文件类型", 0, 30, kPageW, 126);
    {
        const int col[4] = { 12, 232, 452, 672 };
        AddCheck(L"文本文档", ID_TYPE_WORD, true, col[0], 52, 180, 22);
        AddCheck(L"PPT 文件", ID_TYPE_PPT, true, col[1], 52, 180, 22);
        AddCheck(L"表格文件", ID_TYPE_EXCEL, true, col[2], 52, 180, 22);
        AddCheck(L"PDF 文件", ID_TYPE_PDF, true, col[3], 52, 180, 22);

        AddCheck(L"图片文件", ID_TYPE_IMAGE, true, col[0], 78, 180, 22);
        AddCheck(L"视频文件", ID_TYPE_VIDEO, true, col[1], 78, 180, 22);
        AddCheck(L"音频文件", ID_TYPE_AUDIO, true, col[2], 78, 180, 22);
        AddCheck(L"压缩包", ID_TYPE_ARCHIVE, true, col[3], 78, 180, 22);

        AddCheck(L"所有文件 (*.*)", ID_TYPE_ALL, false, col[0], 104, 200, 22);

        AddCheck(L"自定义扩展名", ID_TYPE_CUSTOM, false, col[2], 104, 120, 22);
        AddEdit(ID_CUSTOM_EXT_EDIT, L"", col[2] + 124, 102, 130, 22);
        AddLabel(L"英文逗号分隔", col[2] + 262, 106, 120, 18);
    }

    // ---- 重复文件设置 / 大小限制 ----
    AddGroup(L"重复文件设置", 0, 162, 452, 72);
    AddRadio(L"跳过", ID_DUP_SKIP, true, true, 12, 184, 84, 22);
    AddRadio(L"覆盖", ID_DUP_OVERWRITE, false, false, 106, 184, 84, 22);
    AddRadio(L"都保留", ID_DUP_KEEP_BOTH, false, false, 200, 184, 84, 22);
    AddRadio(L"以新换旧", ID_DUP_NEWER, false, false, 294, 184, 110, 22);

    AddGroup(L"文件大小限制", 460, 162, 468, 72);
    AddCheck(L"启用大小限制", ID_LIMIT_SIZE, false, 472, 182, 150, 20);
    AddEdit(ID_MAX_SIZE_EDIT, L"100", 630, 180, 64, 22, ES_NUMBER);
    AddLabel(L"MB", 700, 184, 30, 18);

    // ---- 复制速度控制 ----
    AddGroup(L"复制速度控制", 0, 240, 452, 72);
    AddCheck(L"启用前 N 分钟限速（减少卡顿）", ID_LIMIT_SPEED, false, 12, 258, 300, 20);
    AddLabel(L"N =", 12, 284, 32, 18);
    AddEdit(ID_SPEED_MIN_EDIT, L"5", 44, 282, 50, 22, ES_NUMBER);
    AddLabel(L"分钟", 98, 284, 36, 18);
    AddLabel(L"速度", 140, 284, 36, 18);
    AddCombo(ID_SPEED_COMBO, kSpeedOptions, 4, 1, 180, 278, 108, 200);

    // ---- 版本说明 ----
    AddGroup(L"当前版本说明", 460, 240, 468, 72);
    AddLabel(L"C++/Win32 原生重写的界面预览：", 472, 258, 440, 18);
    AddLabel(L"控件、页签、DPI、托盘已实现；后端尚未接入。", 472, 278, 440, 18);

    // ---- 日志 ----
    AddGroup(L"运行日志", 0, 318, kPageW, 212);
    logEdit_ = NewControl(L"EDIT", L"",
                          WS_TABSTOP | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
                          ID_LOG_EDIT, 12, 342, kPageW - 24, 144, WS_EX_CLIENTEDGE);
    counterLabel_ = AddLabel(L"本次复制：共 0 个，成功 0 个，失败 0 个", 12, 494, 420, 18);
    AddButton(L"清空日志", ID_CLEAR_LOG, 730, 490, 84, 24);
    AddButton(L"后台运行", ID_RUN_BACKGROUND, 824, 490, 92, 24);
}

// ============================================================================
// 高级
// ============================================================================

void MainWindow::CreateAdvancedPage() {
    // ---- 文件名 / 文件夹过滤 ----
    AddGroup(L"文件名过滤", 0, 0, 452, 92);
    AddCheck(L"仅复制文件名含指定文字的文件", ID_FILTER_NAME, false, 12, 22, 320, 20);
    AddLabel(L"关键词（英文逗号分隔）", 12, 46, 220, 18);
    AddEdit(ID_NAME_KEYWORDS, L"", 12, 64, 424, 22);

    AddGroup(L"文件夹过滤", 460, 0, 468, 92);
    AddCheck(L"仅复制路径含指定文字的文件夹", ID_FILTER_FOLDER, false, 472, 22, 340, 20);
    AddLabel(L"关键词（英文逗号分隔）", 472, 46, 220, 18);
    AddEdit(ID_FOLDER_KEYWORDS, L"", 472, 64, 444, 22);

    // ---- 目录结构 / 日志设置 ----
    AddGroup(L"目录结构", 0, 98, 452, 84);
    AddCheck(L"创建文件树状目录", ID_CREATE_TREE, true, 12, 120, 240, 20);
    AddCheck(L"限制复制目录深度", ID_LIMIT_DEPTH, false, 12, 148, 180, 20);
    AddEdit(ID_DEPTH_EDIT, L"3", 200, 146, 50, 22, ES_NUMBER);
    AddLabel(L"层", 256, 150, 20, 18);

    AddGroup(L"日志设置", 460, 98, 468, 84);
    AddCheck(L"成功信息", ID_LOG_SUCCESS, true, 472, 120, 110, 20);
    AddCheck(L"错误信息", ID_LOG_ERROR, true, 590, 120, 110, 20);
    AddCheck(L"中性信息", ID_LOG_NEUTRAL, true, 708, 120, 110, 20);
    AddCheck(L"窗口内显示", ID_LOG_TO_WINDOW, true, 472, 148, 110, 20);
    AddCheck(L"保存为文件", ID_LOG_TO_FILE, true, 590, 148, 110, 20);

    // ---- U 盘标记 / 白名单 ----
    AddGroup(L"U 盘特殊标记文件", 0, 188, 452, 88);
    AddCheck(L"存在该文件时停止复制", ID_STOP_FILE, false, 12, 210, 200, 20);
    AddEdit(ID_STOP_FILE_EDIT, L"stop.copy", 216, 208, 220, 22);
    AddCheck(L"存在该文件时反向复制", ID_REVERSE_FILE, false, 12, 238, 200, 20);
    AddEdit(ID_REVERSE_FILE_EDIT, L"reverse.copy", 216, 236, 220, 22);

    AddGroup(L"U 盘白名单", 460, 188, 468, 88);
    AddCheck(L"仅复制白名单中的 U 盘", ID_WHITELIST, false, 472, 210, 240, 20);
    AddLabel(L"卷序列号，多个用英文逗号分隔", 472, 232, 300, 18);
    AddEdit(ID_WHITELIST_EDIT, L"", 472, 250, 444, 22);

    // ---- 自启动 / 通知托盘 ----
    AddGroup(L"开机自启动", 0, 282, 452, 76);
    AddCheck(L"开机时自动启动本程序", ID_AUTOSTART, false, 12, 304, 260, 20);
    AddCheck(L"启动后隐藏在托盘（按 U→S→B 唤起）", ID_AUTOSTART_HIDDEN, false, 12, 328, 320, 20);

    AddGroup(L"通知与托盘", 460, 282, 468, 76);
    AddCheck(L"显示系统托盘图标", ID_TRAY_ICON, true, 472, 304, 240, 20);
    AddCheck(L"复制完成时弹出通知", ID_NOTIFY_COMPLETE, true, 472, 328, 240, 20);

    // ---- 说明（填满剩余空间，避免底部大片空白）----
    AddGroup(L"说明", 0, 364, kPageW, 166);
    AddLabel(L"• 以上选项在接入后端后生效；本版本仅验证界面布局与交互。", 12, 386, 760, 18);
    AddLabel(L"• 键盘快捷键：U → S → B 唤起窗口，连按 5 次 ESC 退出（待接入）。", 12, 406, 760, 18);
    AddLabel(L"• 配置持久化（原子写入 / 可写目录回退）将在 M1 公共库完成后接入。", 12, 426, 780, 18);
    AddLabel(L"• 日志窗口保留最近 900 行（与 C# 版一致），逐行滚动丢弃最早内容。", 12, 446, 820, 18);
    AddLabel(L"• 白名单按卷序列号匹配，可在资源管理器「属性 → 硬件」中查看。", 12, 466, 800, 18);
    AddLabel(L"• 反向复制会把目标目录的文件写回 U 盘，请确认不会覆盖重要数据。", 12, 486, 820, 18);
}

// ============================================================================
// 服务器（仅完整版）
// ============================================================================

#if !defined(LOCAL_ONLY)
void MainWindow::CreateServerPage() {
    // ---- 文件保存位置 ----
    AddGroup(L"文件保存位置", 0, 0, kPageW, 68);
    AddRadio(L"保存到本地目录", ID_SRV_LOCAL, true, true, 12, 24, 200, 20);
    AddRadio(L"上传到服务器", ID_SRV_SERVER, false, false, 220, 24, 200, 20);

    // ---- 服务器参数 ----
    AddGroup(L"服务器配置", 0, 74, kPageW, 150);
    {
        AddLabel(L"服务器地址", 12, 96, 90, 18);
        AddEdit(ID_SRV_ADDRESS, L"", 110, 94, 300, 22);
        AddLabel(L"端口", 426, 96, 44, 18);
        AddEdit(ID_SRV_PORT, L"443", 474, 94, 80, 22, ES_NUMBER);

        AddLabel(L"访问令牌", 12, 124, 90, 18);
        AddEdit(ID_SRV_TOKEN, L"", 110, 122, 444, 22, ES_PASSWORD);

        AddLabel(L"Basic 密码", 12, 152, 90, 18);
        AddEdit(ID_SRV_PASSWORD, L"", 110, 150, 300, 22, ES_PASSWORD);
        AddLabel(L"（令牌与密码二选一）", 426, 152, 200, 18);

        AddCheck(L"启用 HTTPS", ID_SRV_HTTPS, true, 12, 180, 140, 20);
        AddCheck(L"分块上传（断点续传）", ID_SRV_CHUNKED, true, 170, 180, 280, 20);
        AddLabel(L"分块大小", 460, 182, 64, 18);
        AddEdit(ID_SRV_CHUNK_SIZE, L"1", 528, 178, 50, 22, ES_NUMBER);
        AddLabel(L"MB", 584, 182, 30, 18);
    }

    // ---- 连接测试 ----
    AddGroup(L"连接", 0, 230, kPageW, 80);
    AddButton(L"测试连接", ID_SRV_TEST, 12, 252, 100, 26);
    AddButton(L"浏览远程目录", ID_SRV_BROWSE, 124, 252, 120, 26);
    serverStatus_ = AddLabel(L"状态：未测试", 260, 258, 400, 18);

    // ---- 说明 ----
    AddGroup(L"说明", 0, 316, kPageW, 214);
    AddLabel(L"• 上传功能尚未接入（WinHTTP 模块在 M4 实现）。", 12, 338, 760, 18);
    AddLabel(L"• 令牌不再以明文写入仓库：服务端 appsettings 已移出版本控制。", 12, 360, 820, 18);
    AddLabel(L"• 连接测试将访问受保护的 /api/file/stats 以验证令牌有效性。", 12, 382, 820, 18);
    AddLabel(L"• 切换「保存到本地目录」时，下方服务器参数会自动置灰。", 12, 404, 800, 18);
    AddLabel(L"• 分块上传用于大文件断点续传：分片写入临时目录，合并后原子替换。", 12, 426, 840, 18);
    AddLabel(L"• 协议与 v1.7.2 服务端保持一致，迁移期间可混用两版客户端。", 12, 448, 820, 18);
    AddLabel(L"• 服务端时间统一 ISO-8601 UTC，客户端按 UTC 比较新旧文件。", 12, 470, 820, 18);

    // 交给统一启用/禁用逻辑（默认本地模式 → 服务器参数不可编辑）
    serverConfigCtrls_ = {
        Control(ID_SRV_ADDRESS), Control(ID_SRV_PORT),
        Control(ID_SRV_TOKEN), Control(ID_SRV_PASSWORD),
        Control(ID_SRV_HTTPS), Control(ID_SRV_CHUNKED),
        Control(ID_SRV_CHUNK_SIZE), Control(ID_SRV_TEST),
        Control(ID_SRV_BROWSE),
    };
    for (HWND control : serverConfigCtrls_) {
        if (control != nullptr) {
            EnableWindow(control, FALSE);
        }
    }
}
#endif

// ============================================================================
// 关于
// ============================================================================

void MainWindow::CreateAboutPage() {
    HWND title = AddLabel(kAppTitle, 0, 4, 400, 30, SS_LEFT | SS_NOPREFIX);
    if (title != nullptr) {
        SendMessageW(title, WM_SETFONT, reinterpret_cast<WPARAM>(fontBold_), TRUE);
    }

    AddLabel((std::wstring(L"版本 ") + kAppVersion + L"（C++/Win32 原生实现 · 界面预览）").c_str(),
             0, 38, 700, 20);

#if defined(LOCAL_ONLY)
    AddLabel(L"把 U 盘里的文件自动备份到本地目录。", 0, 64, 800, 20);
#else
    AddLabel(L"把 U 盘里的文件自动备份到本地目录，或上传到自建服务器。", 0, 64, 800, 20);
#endif

    AddButton(L"访问 GitHub 项目主页", ID_LINK_GITHUB, 0, 96, 180, 28);
    AddButton(L"访问作者 B 站主页", ID_LINK_BILIBILI, 192, 96, 160, 28);

    // 左右两块填满页面，避免大块空白
    AddGroup(L"关于本版本", 0, 140, 452, 300);
    AddLabel(L"• 仅实现界面：复制/监听/上传尚未接入。", 12, 162, 420, 18);
    AddLabel(L"• 纯 Win32 控件，无 MFC/WinForms/Qt。", 12, 184, 420, 18);
    AddLabel(L"• 单文件：静态 CRT（/MT），无随附 DLL。", 12, 206, 420, 18);
    AddLabel(L"• 高 DPI：PerMonitorV2 + 按 DPI 缩放。", 12, 228, 420, 18);
    AddLabel(L"• 设备检测：WM_DEVICECHANGE（替代 WMI）。", 12, 250, 420, 18);
    AddLabel(L"• 传输：WinHTTP / HTTP.sys，无第三方库。", 12, 272, 420, 18);
    AddLabel(L"• 配置：JSON + 原子替换 + 可写目录回退。", 12, 294, 420, 18);
    AddLabel(L"• 安全：目录边界校验、拒绝 \"..\" 与越界分块。", 12, 316, 420, 18);
    AddLabel(L"• 仅供学习交流，请勿用于非法用途。", 12, 338, 420, 18);
    AddLabel(L"• 问题反馈请到 GitHub 提 issue。", 12, 360, 420, 18);

    AddGroup(L"环境信息", 460, 140, 468, 300);
    const std::wstring exeDir = ExeDirectoryText();
    AddLabel((L"系统版本：" + OsVersionText()).c_str(), 472, 162, 440, 18);
    AddLabel((L"处理器架构：" + CpuArchitectureText()).c_str(), 472, 184, 440, 18);
    AddLabel(L"界面框架：Win32 + ComCtl32 v6", 472, 206, 440, 18);
    AddLabel(L"运行时：静态 CRT（无外部依赖）", 472, 228, 440, 18);
    AddLabel(L"字符集：Unicode（UTF-16 Win32 / UTF-8 落盘）", 472, 250, 440, 18);
    AddLabel(L"配置目录：待接入（计划 %APPDATA%\\UsbCopy）", 472, 272, 440, 18);
    AddLabel(L"程序目录：", 472, 294, 440, 18);
    AddLabel(exeDir.c_str(), 472, 316, 448, 18);
    AddLabel(L"日志：待接入（计划 CopyLog.txt）", 472, 338, 440, 18);
    AddLabel(L"构建：MSVC / 静态链接 / 单文件", 472, 360, 440, 18);
}

// ============================================================================
// 构建与布局
// ============================================================================

void MainWindow::CreateControls() {
    // 页签控件
    tab_ = CreateWindowExW(0, WC_TABCONTROLW, L"",
                           WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | TCS_FOCUSNEVER,
                           util::Scale(kTabX, dpi_), util::Scale(kTabY, dpi_),
                           util::Scale(kTabW, dpi_), util::Scale(kTabH, dpi_),
                           hwnd_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(ID_TAB)),
                           instance_, nullptr);
    SendMessageW(tab_, WM_SETFONT, reinterpret_cast<WPARAM>(font_), TRUE);

    // 页容器：覆盖页签的显示区域，所有页内控件都挂在它下面
    pageHost_ = CreateWindowExW(0, kPageHostClass, L"",
                                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                                0, 0, 10, 10, hwnd_, nullptr, instance_, nullptr);

    TCITEMW item{};
    item.mask = TCIF_TEXT;

    auto addTab = [&](const wchar_t* text) {
        item.pszText = const_cast<LPWSTR>(text);
        TabCtrl_InsertItem(tab_, TabCtrl_GetItemCount(tab_), &item);
    };

    // 页内容整体平移，使控件落在页签客户区内
    pages_.reserve(4);

    // 主界面
    addTab(L"主界面");
    pages_.emplace_back();
    currentPage_ = &pages_.back();
    CreateGeneralPage();

    // 高级
    addTab(L"高级");
    pages_.emplace_back();
    currentPage_ = &pages_.back();
    CreateAdvancedPage();

#if !defined(LOCAL_ONLY)
    // 服务器
    addTab(L"服务器配置");
    pages_.emplace_back();
    currentPage_ = &pages_.back();
    CreateServerPage();
#endif

    // 关于
    addTab(L"关于");
    pages_.emplace_back();
    currentPage_ = &pages_.back();
    CreateAboutPage();

    currentPage_ = nullptr;
}

void MainWindow::Layout() {
    // 页签铺满
    SetWindowPos(tab_, nullptr, util::Scale(kTabX, dpi_), util::Scale(kTabY, dpi_),
                 util::Scale(kTabW, dpi_), util::Scale(kTabH, dpi_),
                 SWP_NOZORDER | SWP_NOACTIVATE);

    // 页容器对齐页签的显示区（页内控件坐标即为相对页容器的设计坐标）
    if (pageHost_ != nullptr) {
        RECT tabClient{};
        GetClientRect(tab_, &tabClient);
        TabCtrl_AdjustRect(tab_, FALSE, &tabClient);
        SetWindowPos(pageHost_, HWND_TOP,
                     util::Scale(kTabX, dpi_) + tabClient.left,
                     util::Scale(kTabY, dpi_) + tabClient.top,
                     tabClient.right - tabClient.left,
                     tabClient.bottom - tabClient.top,
                     SWP_NOACTIVATE);
    }

    for (const LayoutItem& item : layout_) {
        if (item.hwnd == nullptr) {
            continue;
        }
        const int x = util::Scale(static_cast<int>(item.design.left), dpi_);
        const int y = util::Scale(static_cast<int>(item.design.top), dpi_);
        const int w = util::Scale(static_cast<int>(item.design.right - item.design.left), dpi_);
        const int h = util::Scale(static_cast<int>(item.design.bottom - item.design.top), dpi_);
        SetWindowPos(item.hwnd, nullptr, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // 页签当前页控件需要重绘
    InvalidateRect(hwnd_, nullptr, TRUE);
}

void MainWindow::ApplyPageVisibility(int index) {
    if (index < 0 || static_cast<size_t>(index) >= pages_.size()) {
        return;
    }

    for (size_t page = 0; page < pages_.size(); ++page) {
        const int show = (static_cast<int>(page) == index) ? SW_SHOW : SW_HIDE;
        for (HWND control : pages_[page]) {
            if (control != nullptr) {
                ShowWindow(control, show);
            }
        }
    }

    // 日志区始终属于主界面页，切换页签时随页隐藏
    InvalidateRect(hwnd_, nullptr, TRUE);
}

// ============================================================================
// 交互
// ============================================================================

void MainWindow::OnCommand(int id, int /*code*/) {
    switch (id) {
    case ID_BROWSE_TARGET:
        BrowseTargetFolder();
        break;

    case ID_CLEAR_LOG:
        ClearLog();
        break;

    case ID_RUN_BACKGROUND:
        ShowWindow(hwnd_, SW_HIDE);
        if (trayAdded_) {
            tray_.uFlags = NIF_INFO;
            wcscpy_s(tray_.szInfo, L"程序已在后台运行，双击托盘图标重新打开。");
            wcscpy_s(tray_.szInfoTitle, kAppTitle);
            tray_.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIconW(NIM_MODIFY, &tray_);
        }
        break;

    case ID_LINK_GITHUB:
        ShellExecuteW(hwnd_, L"open", kGithubUrl, nullptr, nullptr, SW_SHOWNORMAL);
        break;

    case ID_LINK_BILIBILI:
        ShellExecuteW(hwnd_, L"open", kBilibiliUrl, nullptr, nullptr, SW_SHOWNORMAL);
        break;

#if !defined(LOCAL_ONLY)
    case ID_SRV_LOCAL:
    case ID_SRV_SERVER: {
        const BOOL enableServer = (id == ID_SRV_SERVER);
        settings_.saveToServer = enableServer != FALSE;
        for (HWND control : serverConfigCtrls_) {
            if (control != nullptr) {
                EnableWindow(control, enableServer);
            }
        }
        if (serverStatus_ != nullptr) {
            SetWindowTextW(serverStatus_, enableServer ? L"状态：未测试" : L"状态：本地模式");
        }
        break;
    }

    case ID_SRV_TEST:
        AppendLog(L"测试连接：后端未接入（WinHTTP 模块将在 M4 实现）");
        if (serverStatus_ != nullptr) {
            SetWindowTextW(serverStatus_, L"状态：后端未接入");
        }
        break;

    case ID_SRV_BROWSE:
        AppendLog(L"浏览远程目录：后端未接入");
        break;
#endif

    case ID_TRAY_SHOW:
        ShowFromSecondInstance();
        break;

    case ID_TRAY_EXIT:
        RemoveTrayIcon();
        DestroyWindow(hwnd_);
        break;

    default:
        break;
    }
}

void MainWindow::BrowseTargetFolder() {
    IFileOpenDialog* dialog = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(&dialog));
    if (FAILED(hr) || dialog == nullptr) {
        MessageBoxW(hwnd_, L"无法打开文件夹选择对话框。", L"提示", MB_ICONWARNING);
        return;
    }

    DWORD options = 0;
    if (SUCCEEDED(dialog->GetOptions(&options))) {
        dialog->SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
    }

    if (SUCCEEDED(dialog->Show(hwnd_))) {
        IShellItem* item = nullptr;
        if (SUCCEEDED(dialog->GetResult(&item)) && item != nullptr) {
            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr) {
                settings_.targetDir = path;
                if (HWND edit = Control(ID_TARGET_EDIT)) {
                    SetWindowTextW(edit, path);
                }
                AppendLog(L"已选择保存目录：" + settings_.targetDir);
                CoTaskMemFree(path);
            }
            item->Release();
        }
    }
    dialog->Release();
}

void MainWindow::AppendLog(const std::wstring& text, bool isError) {
    if (logEdit_ == nullptr) {
        return;
    }

    SYSTEMTIME now{};
    GetLocalTime(&now);

    wchar_t prefix[64]{};
    swprintf_s(prefix, L"[%02u:%02u:%02u] ", now.wHour, now.wMinute, now.wSecond);

    std::wstring line = prefix;
    if (isError) {
        line += L"[错误] ";
    }
    line += text;
    line += L"\r\n";

    int length = GetWindowTextLengthW(logEdit_);
    SendMessageW(logEdit_, EM_SETSEL, static_cast<WPARAM>(length), static_cast<LPARAM>(length));
    SendMessageW(logEdit_, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(line.c_str()));
    SendMessageW(logEdit_, EM_SCROLLCARET, 0, 0);

    ++logLines_;
    if (logLines_ > kMaxLogLines) {
        // 与 C# 版一致：只丢弃最早 1 行（逐行滚动，不会一次掉一大段）
        LRESULT cut = SendMessageW(logEdit_, EM_LINEINDEX, 1, 0);
        if (cut > 0) {
            SendMessageW(logEdit_, EM_SETSEL, 0, static_cast<LPARAM>(cut));
            SendMessageW(logEdit_, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(L""));
            --logLines_;
        }
    }
}

void MainWindow::ClearLog() {
    if (logEdit_ != nullptr) {
        SetWindowTextW(logEdit_, L"");
        logLines_ = 0;
        AppendLog(L"日志已清空");
    }
}

void MainWindow::SetCounters(int total, int success, int failed) {
    if (counterLabel_ == nullptr) {
        return;
    }
    wchar_t text[160]{};
    swprintf_s(text, L"本次复制：共 %d 个，成功 %d 个，失败 %d 个", total, success, failed);
    SetWindowTextW(counterLabel_, text);
}

// ============================================================================
// 托盘
// ============================================================================

void MainWindow::AddTrayIcon() {
    iconLarge_ = LoadIconW(instance_, MAKEINTRESOURCEW(1));
    iconSmall_ = static_cast<HICON>(LoadImageW(instance_, MAKEINTRESOURCEW(1), IMAGE_ICON,
                                              GetSystemMetrics(SM_CXSMICON),
                                              GetSystemMetrics(SM_CYSMICON), LR_DEFAULTCOLOR));

    SendMessageW(hwnd_, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(iconLarge_));
    SendMessageW(hwnd_, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(iconSmall_));

    tray_.cbSize = sizeof(tray_);
    tray_.hWnd = hwnd_;
    tray_.uID = 1;
    tray_.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    tray_.uCallbackMessage = kMsgTray;
    tray_.hIcon = iconLarge_;
    wcscpy_s(tray_.szTip, kAppTitle);
    trayAdded_ = Shell_NotifyIconW(NIM_ADD, &tray_) != FALSE;
}

void MainWindow::RemoveTrayIcon() {
    if (trayAdded_) {
        Shell_NotifyIconW(NIM_DELETE, &tray_);
        trayAdded_ = false;
    }
}

void MainWindow::ShowTrayMenu() {
    HMENU menu = CreatePopupMenu();
    if (menu == nullptr) {
        return;
    }
    AppendMenuW(menu, MF_STRING, ID_TRAY_SHOW, L"显示主窗口");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, ID_TRAY_EXIT, L"退出");

    POINT cursor{};
    GetCursorPos(&cursor);
    SetForegroundWindow(hwnd_);   // 让菜单能正常消失
    int command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                                 cursor.x, cursor.y, 0, hwnd_, nullptr);
    DestroyMenu(menu);

    if (command != 0) {
        OnCommand(command, 0);
    }
}
