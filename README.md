# U盘文件复制器

![.NET Framework 4.7.2](https://img.shields.io/badge/Client-.NET%20Framework%204.7.2-blue)
![.NET 8](https://img.shields.io/badge/Server-.NET%208-purple)
![License](https://img.shields.io/badge/License-MIT-green)

U盘文件自动备份工具，支持本地存储和远程服务器双模式，配备 Web 管理面板和远程目录浏览功能。

## 项目结构

```
├── Client/                         # 客户端 (Windows Forms)
│   ├── Form1.cs                    # 主窗体
│   ├── Form1.*.cs                  # 部分类模块（复制/监控/设置/日志等）
│   ├── Program.cs                  # 入口
│   ├── IFileDestination.cs         # 存储目标接口
│   ├── LocalFileDestination.cs     # 本地文件系统实现
│   ├── HttpFileDestination.cs      # HTTP 服务器上传实现
│   ├── NetworkHelper.cs            # HTTP 请求辅助类
│   ├── RemoteBrowserForm.cs        # 远程目录浏览窗口
│   ├── ServerConfig.cs             # 服务器配置
│   └── U盘文件复制.csproj          # 客户端项目文件
│
├── Server/                         # 服务端 (ASP.NET Core Web API)
│   ├── Program.cs                  # 入口 + 中间件管道
│   ├── Controllers/                # API 控制器
│   │   ├── FileController.cs       # 文件操作 API
│   │   └── HealthController.cs     # 健康检查
│   ├── Middleware/                  # 中间件
│   │   ├── ApiKeyAuthMiddleware.cs # Bearer Token 认证
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
| 反向复制 | U 盘根目录创建 `copy.stop` 触发反向恢复 |
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

### Web 管理面板

直接访问 `http://localhost:5000` 即可使用：
- **文件浏览**：目录导航、递归模式、分页浏览
- **文件上传**：拖拽上传、分块上传（断点续传）
- **文件搜索**：关键词、扩展名、日期范围过滤
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

配置 `appsettings.json`：
```json
{
  "FileStorage": {
    "RootPath": "uploads",
    "AllowedTokens": ["1145141919810"],
    "MaxFileSizeMB": 2048
  },
  "Cors": {
    "AllowedOrigins": ["*"]
  }
}
```

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

## 技术栈

| 层级 | 技术 |
|------|------|
| 客户端 | .NET Framework 4.7.2 / Windows Forms / Newtonsoft.Json |
| 服务端 | .NET 8 / ASP.NET Core / Swagger / Bearer Token 认证 |
| Web 前端 | 原生 HTML/CSS/JS（无框架依赖） |
| 存储 | 本地文件系统（客户端 + 服务端） |

## 许可证

MIT License

---

*最后更新：2026年6月*
*版本：V1.2.2*
