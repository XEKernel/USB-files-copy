# C++ / Win32 客户端（重构中）

当前状态：**仅界面**。复制引擎、U 盘监听、服务器上传均未接入，按钮只记录日志。

- 目标：C++（MSVC）+ 原生 Win32，无框架；Win10+；单文件（静态 CRT `/MT`）
- 规划：仓库上一级 `重构规划-CPP-Win32.md`
- 旧 C# 版本仍在 `Client/`、`Server/`，v1.7.1 为对外发布版本

## 构建

```powershell
# 首次需要工具链（本机无 Windows SDK，用 NuGet 解压包）
# 见 ..\toolchain\env.ps1 与技能 windows-msvc-toolchain-nosdk

.\build.ps1                 # 完整版  → out\bin\release\UsbCopy.exe
.\build.ps1 -Lite           # 单机版  → out\bin\release-lite\UsbCopyLite.exe
.\build.ps1 -Config debug   # 调试版（/Od /Zi + PDB）
.\build.ps1 -Lite -Run      # 构建并启动
```

要求：MSVC 14.4x+、Windows SDK 10.0.26100+（NuGet 包即可）、`toolchain\env.ps1` 可定位到二者。

## 运行参数

| 参数 | 说明 |
|---|---|
| `--tab=N` | 启动时直接切到第 N 个页签（0=主界面，1=高级，2=服务器配置[仅完整版]，3=关于），便于截图与调试 |
| 无参数 | 正常启动 |

## 结构

```
src/
  common/
    win_util.h/.cpp      Scale/DpiOf/CreateUiFont/SetFontForChildren/UTF-8 转换/HandleGuard
  client/
    app/
      main.cpp           WinMain、单实例、COM、通用控件初始化、--tab 解析
      MainWindow.h/.cpp  窗口、页签、页容器、控件工厂、布局、日志视图、托盘
      ui_settings.h      界面状态模型（与 C# AppSettings 字段一一对应）
    res/
      app.rc             图标 + 清单 + 版本信息
      app.manifest       ComCtl v6 / PerMonitorV2 / longPathAware / asInvoker
      icon.ico
build.ps1                 编译 + 资源 + 链接（不依赖 CMake / nmake）
```

## 关键实现说明

- **页容器**：`UsbCopyPageHost` 子窗口覆盖页签显示区，所有页内控件都挂在它下面。
  原因：主题下 TabCtrl 页体是 `#F9F9F9`，而按钮/复选框按 `COLOR_BTNFACE (#F0F0F0)` 绘制，
  直接挂在主窗口上会出现一块块灰底；用容器统一背景色后消失，同时为后续滚动做了准备。
- **控件通知**：页容器的 `WM_COMMAND` 统一转发给主窗口处理。
- **DPI**：清单声明 PerMonitorV2；控件尺寸全部按 96dpi 设计值经 `MulDiv` 缩放；
  `WM_DPICHANGED` 里重建字体并重新布局。
- **字体**：`Microsoft YaHei UI` 9pt（标题用 semibold），ClearType。
- **单文件**：`/MT` 静态 CRT；图标/清单/版本信息编译进 exe；运行只依赖系统 DLL。
- **固定窗口尺寸**：960×656 客户区（`WM_GETMINMAXINFO` 锁定），避免控件在缩放时错位；
  日志区自带滚动条。

## 尚未接入（后续里程碑）

| 项 | 计划 |
|---|---|
| 配置持久化（JSON / 原子写入 / 可写目录回退） | M1 公共库 + M3 |
| U 盘监听（`RegisterDeviceNotification`）+ 启动补扫 | M2 |
| 复制引擎（过滤/去重/限速/目录树/反向复制） | M2 |
| 服务器上传（WinHTTP）+ 远程浏览 | M4 |
| 托盘热键（U→S→B、ESC×5）、开机自启 | M3 |
| 无 UI 的 `--headless` 模式（便于自动化验证） | M2 |
