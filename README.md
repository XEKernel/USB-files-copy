# U盘文件复制器

![.NET Framework 4.7.2](https://img.shields.io/badge/Client-.NET%20Framework%204.7.2-blue)
![.NET 8](https://img.shields.io/badge/Server-.NET%208-purple)
![License](https://img.shields.io/badge/License-MIT-green)

U盘文件自动备份工具，支持本地存储和远程服务器双模式，配备 Web 管理面板和远程目录浏览功能。

## 项目结构

```
├── Client/                         # 客户端 (Windows Forms)
│   ├── Forms/                      # 界面层：窗体、控件读写、事件绑定
│   │   ├── Form1.cs                # 主窗体（事件编排，调用后端类）
│   │   ├── Form1.Designer.cs       # 控件声明与布局（设计器文件）
│   │   ├── Form1.*.cs              # 界面部分类（设置绑定/扩展名选择/控件事件）
│   │   ├── RemoteBrowserForm.cs    # 远程目录浏览窗口
│   │   └── Program.cs              # 入口
│   ├── Core/                       # 后端层：纯功能代码（零 UI 依赖）
│   │   ├── CopyEngine.cs           # 复制引擎（递归复制/限速/目录树/过滤）
│   │   ├── CopyOptions.cs          # 复制任务配置模型
│   │   ├── UsbMonitor.cs           # USB 插入监听（WMI）
│   │   ├── LogEngine.cs            # 日志引擎（过滤/写文件，事件通知界面）
│   │   ├── SettingsStore.cs        # 设置持久化（XML + DPAPI 加密）
│   │   ├── FileCategories.cs       # 扩展名分类/系统目录/枚举
│   │   ├── KeyboardHook.cs         # 全局键盘钩子（U+S+B / ESC×5）
│   │   ├── AutoStartManager.cs     # 开机自启动（注册表）
│   ├── Storage/                    # 存储目标层
│   │   ├── IFileDestination.cs     # 存储目标接口
│   │   ├── LocalFileDestination.cs # 本地文件系统实现
│   │   ├── HttpFileDestination.cs  # HTTP 服务器上传实现
│   │   ├── NetworkHelper.cs        # HTTP 请求辅助类
│   │   └── ServerConfig.cs         # 服务器配置
│   └── U盘文件复制.csproj          # 客户端项目文件
│
├── Server/                         # 服务端 (ASP.NET Core Web API)
│   ├── Program.cs                  # 入口 + 中间件管道
│   ├── Controllers/                # API 控制器
│   │   ├── FileController.cs       # 文件操作 API
│   │   └── HealthController.cs     # 健康检查
│   ├── Middleware/                  # 中间件
│   │   ├── ApiKeyAuthMiddleware.cs # Bearer Token / Basic 认证
│   │   └── RequestLoggingMiddleware.cs
│   ├── Services/                    # 服务层
│   │   ├── IFileStore.cs           # 文件存储接口
│   │   └── LocalFileStore.cs       # 本地文件系统实现
│   ├── wwwroot/                    # Web 管理面板
│   │   ├── index.html
│   │   ├── css/style.css
│   │   └── js/app.js
│   └── appsettings.json            # 配置文件（令牌、存储路径）
│
├── U盘文件复制.sln                 # 解决方案文件
└── README.md
```

## 核心功能

### 客户端

| 功能 | 说明 |
|------|------|
| USB 自动检测 | WMI 实时监控 U 盘插入，自动触发复制 |
| 文件类型过滤 | 支持 PPT/文档/表格/PDF/图片/视频/音频/压缩包/自定义扩展名 |
| 重复文件处理 | 跳过 / 覆盖 / 都保留 / 以新换旧 四种策略 |
| 文件大小限制 | 按 MB 限制单个文件大小 |
| 关键词过滤 | 文件名包含指定关键词才复制 |
| 速度控制 | 前 N 分钟限制复制速度（1-10 MB/s） |
| 反向复制 | U 盘根目录放置标记文件（默认 `reverse.copy`，兼容旧版 `copy.stop`）触发反向恢复 |
| 本地/远程双模式 | 支持保存到本地目录或上传到远程服务器 |
| 远程目录浏览 | 查看/搜索/下载/删除服务器上的文件 |
| 键盘快捷键 | `U→S→B` 显示窗口，`ESC×5` 快速退出 |
| 开机自启动 | 支持 Windows 启动时自动运行 |

### 服务端 API

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/api/health` | 健康检查（免认证） |
| PUT | `/api/file/file?path=` | 上传文件 |
| HEAD | `/api/file/file?path=` | 检查文件是否存在 |
| GET | `/api/file/list?path=&recursive=&page=&pageSize=` | 文件列表（分页） |
| GET | `/api/file/download?path=` | 下载文件（支持 Range） |
| DELETE | `/api/file/file?path=` | 删除文件 |
| GET | `/api/file/search?keyword=&extension=&startDate=&endDate=` | 文件搜索 |
| GET | `/api/file/stats` | 存储统计 |
| PUT | `/api/file/chunk?path=&index=&total=` | 上传分块 |
| GET | `/api/file/chunk-status?path=` | 分块状态查询 |
| POST | `/api/file/merge?path=&total=` | 合并分块 |
| POST | `/api/file/cleanup?hoursOld=` | 清理过期分块 |
| POST | `/api/file/download-zip` | 批量打包下载（body: `{"paths":[...]}`） |
| GET | `/api/file/trash` | 列出回收站（软删除） |
| POST | `/api/file/restore?path=` | 从回收站恢复文件 |
| POST | `/api/file/trash-clear?hoursOld=` | 清空回收站（可选保留时长） |

> **注意**：`DELETE /api/file/file` 为软删除（文件移入 `.trash/` 回收站，可恢复）；
> 回收站内文件会在 30 天后被后台服务自动清除，也可通过"清空回收站"手动清理。

### Web 管理面板

直接访问 `http://localhost:5000` 即可使用：
- **文件浏览**：目录导航、递归模式、分页浏览、多选打包下载
- **文件上传**：拖拽上传、分块上传（断点续传）
- **文件搜索**：关键词、扩展名、日期范围过滤
- **回收站**：软删除文件恢复、彻底删除、清空回收站
- **存储统计**：文件数、空间使用率、未完成分块
- **文件预览**：文本/图片在线预览，其他类型下载
- **维护工具**：清理过期分块
- **令牌管理**：localStorage 持久化

## 快速开始

### 服务端

```bash
cd Server
dotnet run
# 服务启动在 http://localhost:5000
```

配置 `appsettings.json`（**该文件已被 `.gitignore` 排除，不随仓库分发，请参照 `appsettings.Example.json` 自行创建**）：

```json
{
  "Cors": {
    "AllowedOrigins": []
  },
  "FileStorage": {
    "RootPath": "Storage",
    "TempChunkFolder": "_chunks",
    "MaxFileSizeBytes": 1073741824,
    "MaxChunkSizeBytes": 16777216,
    "MaxChunkCount": 10000,
    "MaxZipEntries": 200,
    "MaxZipSizeBytes": 1073741824,
    "ReservedNames": [ "fileindex.db", "audit.log" ],
    "AllowedTokens": ["<用 openssl rand -hex 24 生成的随机值>"],
    "BasicPasswords": ["<可选，随机值>"]
  }
}
```

> **安全须知（重要）**
> 1. `AllowedTokens` 是客户端与 Web 面板的唯一凭证，**必须使用随机值**（`openssl rand -hex 24`），
>    切勿沿用示例值或历史提交中出现过的值。仓库历史中曾提交过真实令牌，若你使用过 v1.5.0 及更早版本，
>    请视为该令牌已泄露并立即更换。
> 2. 未配置任何令牌时，服务端会**拒绝启动**并打印提示（避免"能启动但全部请求 401"的困惑）。
> 3. 推荐用环境变量注入，避免令牌落盘：`FileStorage__AllowedTokens__0=<你的令牌>`。
> 4. `Cors:AllowedOrigins` 默认为空数组（不允许任何跨域来源）。Web 面板与客户端都不需要 CORS，
>    填 `"*"` 会让**任意网站**都能调用本服务接口。
> 5. 令牌只能通过 HTTPS 传输，请务必在前置反向代理上启用 TLS，不要直接暴露 HTTP 端口。

### 客户端

1. 用 Visual Studio 打开 `U盘文件复制.sln`
2. 生成 `Client/U盘文件复制.csproj`（.NET Framework 4.7.2）
3. 运行 `Client/bin/Debug/U盘文件复制器.exe`

#### 连接到远程服务器

在界面右侧切换到「服务器」模式，填写：
- 服务器地址：`localhost`（本机）或服务器 IP
- 端口：`5000`
- API 令牌：与服务器 `AllowedTokens` 一致
- 取消勾选 HTTPS（服务端默认仅监听 HTTP）

点击「测试连接」验证，通过后即可使用。

#### 配置文件位置（设置无法保存时请看这里）

客户端设置保存在 `settings.xml`，路径按以下顺序决定：

1. 若程序目录下已存在 `settings.xml` → 继续使用（便携/绿色部署，兼容旧版本）
2. 否则若 `%APPDATA%\U盘文件复制器\settings.xml` 已存在 → 使用它
3. 都没有时：**程序目录可写就用程序目录**，不可写则自动改用 `%APPDATA%\U盘文件复制器\`

程序每次启动会把当前配置位置写入日志，格式为 `配置文件位置：<完整路径>`。

**遇到"关掉软件再打开就恢复默认设置"**，请按以下顺序排查：

- 查看程序目录或 `%APPDATA%\U盘文件复制器\` 下是否存在 `settings.xml` 及其修改时间；
- 若程序放在 `C:\Program Files`、`C:\Program Files (x86)` 等受保护目录，普通权限无法写入配置，
  **请把整个程序文件夹移动到有写权限的位置**（如 `D:\U盘文件复制器`、桌面），或以管理员身份运行；
  保存失败时程序会弹出明确提示（含配置文件路径与失败原因），不再静默失败；
- 教学机/机房电脑若启用了还原卡、冰点还原、UWF 写保护等，关机后会丢弃所有写入，
  这种情况请把程序放在**未受保护的分区**（如 D 盘数据盘）；
- 从压缩包内直接运行 exe（Windows 会解压到临时目录）也会导致设置丢失，请先**完整解压**再运行。

> v1.6.0 起，保存失败会弹出对话框提示，不再只是写日志。

## 技术栈

| 层级 | 技术 |
|------|------|
| 客户端 | .NET Framework 4.7.2 / Windows Forms / Newtonsoft.Json |
| 服务端 | .NET 8 / ASP.NET Core / Swagger / Bearer Token + Basic 双认证 |
| Web 前端 | 原生 HTML/CSS/JS（无框架依赖） |
| 存储 | 本地文件系统（客户端 + 服务端） |

## 更新历史

### v1.6.0（2026-09-13）
**安全修复（服务端）**
- **修复路径穿越漏洞**：存储根校验由字符串前缀比较改为目录边界比较，并显式拒绝 `..`；
  此前 `Storage_backup` 等同前缀同级目录内的文件可被读取与删除
- **修复分块上传任意写文件漏洞**：`chunk` 接口的目录部分此前完全未校验，
  可向存储根之外的任意可写目录（含 `C:\Windows\Temp`）写入文件，现统一走存储根边界校验
- **凭证移出仓库**：`appsettings.json` 不再纳入版本控制（改用 `appsettings.Example.json` 占位），
  未配置令牌时服务端拒绝启动；**如果你用过 v1.5.0 及更早版本，请立即更换令牌**
- **CORS 默认关闭跨域**（原为 `*`，任何网站均可调用）
- **不再回显内部异常信息**（原会泄露服务端绝对路径、账户名）
- **隔离系统文件**：`fileindex.db`、`audit.log` 不再出现在列表/搜索/统计中，且禁止下载与删除
- 分块上传增加单块大小（默认 16MB）与总分片数上限；打包下载改为流式产出并限制条目数与总大小
- `page`/`pageSize` 参数钳制；健康检查端点改为精确匹配；ZIP 条目名过滤 `..`（防解压目录穿越）
- 时间戳统一为 ISO 8601 UTC（`2026-09-13T05:27:34Z`），修复客户端时间比较的时区偏差

**Bug 修复（客户端）**
- **修复「设置无法保存/重启后恢复默认」**：启动时 `SetDefaultValues()` 触发的控件事件会在
  `LoadSettings()` 之前把默认值写回 `settings.xml`，导致用户配置每次启动都被覆盖
- 配置文件支持可写目录回退（程序目录不可写时自动改用 `%APPDATA%`），保存失败会弹窗提示
- 设置写入改为原子替换（先写临时文件再替换），避免中断导致配置损坏；配置变更防抖，不再逐字符写盘
- **修复「限速 + 分块上传」组合下所有文件上传失败**（限速包装流不支持 `Length`/`Seek`，现自动回退整文件上传）
- 修复反向复制查找的备份目录名与正向复制不一致，导致反向复制始终提示"找不到备份目录"
- 修复服务器配置变更时 HttpClient 连接池泄漏
- 修复退出时资源清理（USB 监听/键盘钩子/托盘图标）因异步 fire-and-forget 而未真正执行
- 修复托盘通知重复订阅：点击"复制完成"气泡不再误打开下载页
- 「测试连接」改为校验受保护接口，令牌错误不再显示"连接成功"
- 分块读取循环读满，避免网络抖动时上传残缺分块

### v1.5.0（2026-08-02）
- **SQLite 文件索引**：搜索从全盘扫描升级为索引查询，大量文件下性能显著提升
- **回收站**：删除改为软删除（移入 .trash 可恢复），新增回收站列表/恢复/清空 API 与 Web 页面
- **批量打包下载**：Web 面板多选文件打包为 ZIP 下载
- **访问审计日志**：记录每次 API 访问（认证方式/令牌脱敏/IP/请求），默认 Storage/audit.log
- **自动更新检查**：客户端启动时检查 GitHub Releases，有新版托盘提示（可开关）
- **修复严重安全漏洞**：API 认证中间件路径判断失效导致请求可绕过认证，现已修复并验证
- **修复**：存储统计误将索引数据库计入文件数

### v1.4.1（2026-08-02）
- 修复：服务器配置页"测试连接"按钮与"浏览远程"/连接状态标签重叠
- 架构：界面层（Forms/）与后端层（Core/）彻底分离，业务代码零 UI 依赖
- 安全：移除默认弱令牌，前端强制手动输入令牌
- 修复：本地模式远程浏览下载路径错误、Basic 认证不可用、分块开关保存失效等 6 个问题

### v1.4.0（2026-08-02）
- 控件语义化重命名（textBox1~27 → txt/chk/rdo/btn 可读命名）
- 客户端按 Forms/ Storage/ 分层重组
- 清理死代码、AI 占位注释、误标 auto-generated 等屎山

## 许可证

MIT License

---

*最后更新：2026年8月*
*版本：V1.5.0*
