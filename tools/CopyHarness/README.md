# CopyHarness —— C# 版 CopyEngine 的 headless 运行器

## 为什么存在

M2 的验收要求「同一测试树，C# 与 C++ 各跑一次，逐文件比对」。但 **C# 版没有 headless 入口**，
`CopyEngine` 只在 UI 流程里被调用（唯一命令行参数 `/hidden` 只是隐藏启动）。

这个程序补上这个缺口（基线 §13 / §17.13 的**裁决 A**）：把 `CopyEngine` 的目录复制内核
在没有 UI 的环境下跑一遍，输出与 C++ `--headless` **同格式的 JSON Lines**，用于逐文件比对。

它是 M2 的验收锚点，也是 M4/M6 回归的复用件。

## 怎么做到"不改产品代码"

| 手段 | 说明 |
|---|---|
| 链接源码 | csproj 里用 `<Compile Include="..\..\Client\Core\CopyEngine.cs">` 链接**同一份**文件（不复制），编进本程序集的引擎与产品完全一致，不会漂移 |
| 反射调用 | `CopyEngine.CopyDirectoryAsync` 是 `private`；harness 用反射调用它（产品唯一可复用的目录内核） |
| 日志反推 | 引擎只通过 `Log(message, isError)` 暴露每文件结果 → harness 按固定前缀翻译成结构化决策（唯一脆弱点，见下） |

## 构建

```powershell
dotnet build tools\CopyHarness\CopyHarness.csproj
# → tools\CopyHarness\bin\Release\CopyHarness.exe
```

> 用**老式 csproj**（不是 SDK 式）：本机 NuGet 的 fallback 目录不存在，SDK 式项目会在
> `ResolvePackageAssets` 阶段 MSB4018。老式 csproj + HintPath 完全不走 NuGet。

## 运行

```powershell
CopyHarness.exe `
  --source <测试树> --target <目标目录> `
  --options tools\CopyHarness\options.example.json `
  --folder-name "" `
  --clean-target `
  --out out.jsonl
```

| 参数 | 说明 |
|---|---|
| `--source` / `--target` | 源（只读）/ 目标（写入） |
| `--options` | `CopyOptions` 的 JSON（camelCase）；缺省 = 引擎默认值（`*.*`） |
| `--mode forward\|reverse` | 默认 forward；reverse 复刻 `ReverseCopyAsync` 的目录内核（`relativeTargetDir=""`） |
| `--folder-name` | 正向时目标子目录名（产品在真机上传 `卷标_卷序列号`） |
| `--cancel-after <ms>` | 定时取消（验证半成品文件与取消语义） |
| `--clean-target` | 运行前删除目标目录 |
| `--no-record-insertion` | 不调 `RecordDriveInsertion`（**限速会静默失效**，用于对照） |
| `--emit-log` | 额外输出原始日志行（`kind=log`） |
| `--out` | 写文件（UTF-8 无 BOM）；缺省 stdout（**已强制 UTF-8**，不走控制台代码页） |

## 输出格式

见 `兼容性基线.md` §13「输出格式」——字段与 C++ `--headless` 完全一致，这里不重复定义。

要点：

- **每源文件一行**（含 `action=not-visited`），不是"只输出处理过的"
- `srcFt` / `dstFt` 是 **FILETIME（int64）**，不是 ISO 字符串（后者丢亚秒，比对必误判）
- 额外有 `kind=dir`（目录级跳过：`system-dir` / `attr-dir` / `folder-filter`）与 `kind=summary`
- summary 里有**两套计数**：
  - `countsLast` = 引擎 `CountsChanged` 的最后一次上报（**受 D30 缺陷影响会少算**，仅参考）
  - `countsDerived` = 从每文件决策派生的真实结果（**比对用这个**）：
    `{copy, skip, fail, sizeLimit, countedAsFailure, totalCounted, notVisited}`
    其中 `countedAsFailure = fail + sizeLimit`（超大文件跳过了但引擎计失败）、
    `totalCounted = copy + countedAsFailure`，这两项才对得上引擎口径

## 限速必须先记录插入时间

`ShouldLimitSpeed` 只在 `_driveInsertionTimes` 里有该盘时才生效，而这张表只由
`RecordDriveInsertion(drive)` 写入。产品在 `HandleUsbInsertedAsync` 里复制前会调一次；
harness 复刻了这一步（forward 记源盘根、reverse 记目标盘根）。

不调的话 `enableSpeedLimit=true` 也是全速：4MB 文件从 **3.1 秒** 变成 **0.05 秒**，
很容易误判成"限速没实现"。对照用 `--no-record-insertion`。

## 已验证场景（冒烟）

| 场景 | 断言 |
|---|---|
| 默认 `*.*` | 9 个源文件各一行；隐藏/系统文件 → `hidden-attr`/`system-attr`；`$RECYCLE.BIN` 子树 → `not-visited` |
| 深度限制 `maxDirectoryDepth=1` | `sub/c.txt` 复制；`sub/deep/*` 全部 `not-visited` |
| 大小限制 1000B | `big.bin` → `size-limit`，且 summary `failure=1`（跳过也计失败） |
| 文件名过滤 `["a"]` | `a.txt` 复制；其余 `name-filter` |
| 二次运行（不 clean） | `a.txt` → `content-identical`（SHA256 去重生效） |
| 限速 1MB/s + 4MB | 3.13s（全速对照 0.05s）→ 限速生效 |
| 限速 + 800ms 取消 | `huge.bin` → `fail`，`dstBytes=1 064 960 / 4 194 304`（**半成品**）；`countsLast=[1,1,0]` 少算一个失败 → **D30** |
| D8 现场 | `dstFt != srcFt` → 确认 C# 不保留 mtime（M2 有意偏离） |

## 唯一脆弱点：日志文案映射

`ParseLog` 依赖 `CopyEngine.cs` 里那些固定前缀（`成功复制：`、`跳过文件：`、`文件过大已跳过：` …）。
**产品改文案 → harness 会把决策变成 `not-visited`**（不会崩，但比对会大面积失败）。
映射表在基线 §17.11；改文案时同步改 `Program.cs` 的 `ParseLog` 与那张表。

> 这也正是 C++ 侧不走日志的原因：`CopyCallbacks::onDecision` 直接给出结构化决策
> （见 `重构规划-CPP-Win32.md` 附录 B）。
