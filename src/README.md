# C++ / Win32 客户端（重构中）

当前状态：**M1 公共库完成 + 界面完成**。复制引擎、U 盘监听、服务器上传均未接入，按钮只记录日志。

- 目标：C++（MSVC）+ 原生 Win32，无框架；Win10+；单文件（静态 CRT `/MT`）
- 规划：仓库上一级 `重构规划-CPP-Win32.md`
- 兼容性基线（字段/DTO/格式的唯一事实来源）：仓库上一级 `兼容性基线.md`
- 逐项对照清单：仓库上一级 `重构对照清单-CSharp-vs-CPP.md`
- 旧 C# 版本仍在 `Client/`、`Server/`，v1.7.2 为对外发布版本

## 构建

```powershell
# 首次需要工具链（本机无 Windows SDK，用 NuGet 解压包）
# 见 ..\toolchain\env.ps1 与技能 windows-msvc-toolchain-nosdk

.\build.ps1                 # 完整版    → out\bin\release\UsbCopy.exe
.\build.ps1 -Lite           # 单机版    → out\bin\release-lite\UsbCopyLite.exe
.\build.ps1 -Config debug   # 调试版（/Od /Zi + PDB）
.\build.ps1 -Tests          # M1 测试   → out\bin\release-tests\m1_tests.exe（445 项）
.\build.ps1 -Tests -Lite    # 单机版测试 → out\bin\release-tests-lite\（434 项，验 server 裁剪）
.\build.ps1 -Tests -Run     # 构建并直接跑测试
.\build.ps1 -Lite -Run      # 构建并启动
```

测试程序支持只读探针模式（跨语言比对用）：

```powershell
.\out\bin\release-tests\m1_tests.exe --import-xml <settings.xml>   # 导入并打印全部字段
```

编译选项固定为 `/std:c++20 /permissive- /W4 /WX /utf-8 /EHsc /MT /GR-`：
**`/WX` 意味着任何警告都会让构建失败**，这是硬门槛，不要为了编过去而加 `/wd`。

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
    path_util.h/.cpp     归一化、.. 拒绝、目录边界（OrdinalIgnoreCase）、\\?\ 长路径、reparse point
    json.h/.cpp          极简 JSON（int64 保真、键保序、深度 64）
    xml_lite.h/.cpp      只解析 XmlSerializer 输出子集（settings.xml 导入用）
    settings.h/.cpp      AppSettings（43+11 字段）、两级路径、原子写、DPAPI、导入、损坏备份、加载门禁
    log.h/.cpp           [HH:mm:ss] 行格式、三开关、900 行、5MB 轮转、线程安全
  client/
    app/
      main.cpp           WinMain、单实例、COM、通用控件初始化、--tab 解析
      MainWindow.h/.cpp  窗口、页签、页容器、控件工厂、布局、日志视图、托盘
      ui_settings.h      界面状态模型（M3 将替换为 settings::AppSettings）
    res/
      app.rc             图标 + 清单 + 版本信息
      app.manifest       ComCtl v6 / PerMonitorV2 / longPathAware / asInvoker
      icon.ico
  tests/
    m1_tests.cpp          M1 验收测试（控制台，退出码 0 = 全绿；`--import-xml` 为探针模式）
build.ps1                 编译 + 资源 + 链接（不依赖 CMake / nmake）
tools/
  interop/
    verify_dpapi_settings.py  跨语言验证：Python 独立 DPAPI → C# 真实加解密 → C++ 导入
    README.md                 为什么它必须入库、怎么跑、退出码含义
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
- **固定窗口尺寸**：960×590 客户区（`WM_GETMINMAXINFO` 锁定），避免控件在缩放时错位；
  日志区自带滚动条。
- **配置路径**：与 C# 一样两级 —— 已存在的 `exe\settings.json` → 已存在的
  `%APPDATA%\UsbCopy\settings.json` → exe 目录可写则用 exe 目录 → 否则用 `%APPDATA%`
  （两级都不可写时**保存会失败并报错**，不静默兜底）。
  单机版用 `settings.lite.json`，与完整版**永不共享**配置文件（避免单机版重置掉服务器配置）。
- **损坏配置**：解析失败时先把文件改名 `settings.json.corrupt`（重名加时间戳）再回退默认值，
  避免下一次保存把用户 43 个字段永久覆盖。
- **DPAPI**：`CryptProtectData(CurrentUser)` + 固定 entropy（`U盘文件复制器_SALT_2024` 的 UTF-8，
  29 字节），密文以 **Base64** 存盘，与 C# 版可互相解密。
  改这块后必须跑 `tools\interop\verify_dpapi_settings.py`（让 C# 版真实参与一次加解密）。

## 尚未接入（后续里程碑）

| 项 | 计划 |
|---|---|
| 配置 UI 接线（600ms 防抖、「保存设置」按钮、日志路径跟随目标） | M3 |
| U 盘监听（`RegisterDeviceNotification`）+ 启动补扫 | M2 |
| 复制引擎（过滤/去重/限速/目录树/反向复制） | M2 |
| 服务器上传（WinHTTP）+ 远程浏览 | M4 |
| 托盘热键（U→S→B、ESC×5）、开机自启 | M3 |
| 无 UI 的 `--headless` 模式（便于自动化验证） | M2 |
