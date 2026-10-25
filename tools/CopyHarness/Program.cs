// ============================================================================
//  CopyHarness —— C# 侧验收 harness（M2 对照基准）
//
//  背景：C# 版没有 headless 入口，CopyEngine 只在 UI 流程里被调用，因此
//        "同一测试树，C# 与 C++ 各跑一次逐文件比对" 无法直接实现。
//        本程序用反射调用 CopyEngine 的目录复制内核（不改产品代码），
//        把每个文件的决策输出为 JSON Lines，与 C++ --headless 同格式。
//
//  输出约定（每行一个 JSON 对象，紧凑、键序固定）：
//    {"kind":"file","path":"a/b.txt","action":"copy|skip|fail|not-visited",
//     "skipReason":"","bytes":123,"srcFt":133...,"dstExists":true,
//     "dstBytes":123,"dstFt":133...,"attempts":1}
//    {"kind":"dir","path":"a/b","skipReason":"system-dir|attr-dir|folder-filter"}
//    {"kind":"log","message":"..."}                      // 仅 --emit-log
//    {"kind":"summary","total":n,"success":n,"failure":n,
//     "cancelled":false,"countsLast":[t,s,f],"durationMs":123}
//
//  字段说明
//    srcFt / dstFt = FILETIME（100ns 精度，long）。
//      ★ 刻意不用 "yyyy-MM-ddTHH:mm:ssZ"：JSON 时间串丢了亚秒，比对必然误判不等。
//    action = not-visited 表示"源树里有这个文件，但引擎没做出任何决策"
//      （被深度限制截断的目录、系统目录等），是最容易漏掉的一类差异。
//    dstExists=true 但 action=not-visited 通常意味着取消后留下的半成品文件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using U盘文件复制;
using U盘文件复制.Core;

namespace UsbCopy.Harness
{
    internal sealed class Options
    {
        public string Source;
        public string Target;
        public string OptionsFile;
        public bool Reverse;
        public string FolderName = "";
        public int CancelAfterMs;
        public bool CleanTarget;
        public bool EmitLog;
        public bool RecordInsertion = true;
        public string OutFile;
        public bool Help;
    }

    internal sealed class Decision
    {
        public string Action;      // copy / skip / fail
        public string SkipReason;
        public string RelativePath;
        public int Attempts;
        public string Error;
    }

    internal static class Program
    {
        private static readonly object Sync = new object();
        private static readonly List<string> RawLog = new List<string>();

        private static int Main(string[] args)
        {
            Options options;
            try
            {
                options = ParseArgs(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("参数错误：" + ex.Message);
                PrintUsage();
                return 2;
            }

            if (options.Help)
            {
                PrintUsage();
                return 0;
            }
            if (string.IsNullOrEmpty(options.Source) || string.IsNullOrEmpty(options.Target))
            {
                Console.Error.WriteLine("缺少 --source 或 --target");
                PrintUsage();
                return 2;
            }
            if (!Directory.Exists(options.Source))
            {
                Console.Error.WriteLine("源目录不存在：" + options.Source);
                return 2;
            }

            var copyOptions = LoadOptions(options.OptionsFile);
            if (copyOptions == null)
            {
                return 2;
            }

            if (options.CleanTarget && Directory.Exists(options.Target))
            {
                Directory.Delete(options.Target, true);
            }

            var destination = new LocalFileDestination(options.Target);
            var engine = new CopyEngine();
            engine.Log += (message, isError) =>
            {
                lock (Sync)
                {
                    RawLog.Add(message);
                }
            };

            int lastTotal = 0, lastSuccess = 0, lastFailure = 0;
            engine.CountsChanged += (total, success, failure) =>
            {
                lastTotal = total;
                lastSuccess = success;
                lastFailure = failure;
            };

            var stopwatch = Stopwatch.StartNew();
            bool cancelled = false;

            // 复刻产品的调用顺序：HandleUsbInsertedAsync 在复制前对 U 盘调
            // RecordDriveInsertion(drive)。没有这一步，_driveInsertionTimes 为空，
            // ShouldLimitSpeed 恒为 false —— 限速会静默失效（基线 §17.1 条件 2）。
            //   forward：被复制的文件在源盘上 → 记录源盘根
            //   reverse：被复制的文件在本地备份目录、目标是 U 盘 → 记录目标盘根
            //            （产品里记录的就是 U 盘；本地盘不在表里，所以反向永不限速）
            if (options.RecordInsertion)
            {
                try
                {
                    string driveRoot = options.Reverse
                        ? Path.GetPathRoot(Path.GetFullPath(options.Target))
                        : Path.GetPathRoot(Path.GetFullPath(options.Source));
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        engine.RecordDriveInsertion(new DriveInfo(driveRoot));
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("RecordDriveInsertion 失败（限速将不生效）：" + ex.Message);
                }
            }

            using (var cts = new CancellationTokenSource())
            {
                if (options.CancelAfterMs > 0)
                {
                    cts.CancelAfter(options.CancelAfterMs);
                }
                try
                {
                    RunDirectoryCopy(engine, options, copyOptions, destination, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("复制过程抛出未处理异常：" + ex);
                    return 3;
                }
            }
            stopwatch.Stop();

            List<string> log;
            lock (Sync)
            {
                log = new List<string>(RawLog);
            }

            var decisions = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
            var dirEvents = new List<JObject>();
            ParseLog(log, copyOptions, decisions, dirEvents);

            var lines = new List<string>();
            int sourceFileCount = AppendFileLines(options, decisions, lines);
            foreach (var dirEvent in dirEvents)
            {
                lines.Add(dirEvent.ToString(Formatting.None));
            }
            if (options.EmitLog)
            {
                foreach (var message in log)
                {
                    lines.Add(new JObject
                    {
                        ["kind"] = "log",
                        ["message"] = message
                    }.ToString(Formatting.None));
                }
            }

            // 从"每文件决策"派生计数。
            // 为什么不用引擎的 CountsChanged：C# 的 finally 只在
            // (attempt >= maxRetries || success) 时才 RaiseCountsChanged，
            // 于是「硬错误第一次就 break」和「取消导致的失败」会 failure+1 但从不通知，
            // total 也不加 —— 计数与真实结果不一致（基线 D30）。
            // 比对时只用派生计数；countsLast 仅作参考输出。
            int decisionCopy = 0, decisionSkip = 0, decisionFail = 0, decisionSizeLimit = 0;
            foreach (var decision in decisions.Values)
            {
                if (decision.Action == "copy")
                {
                    decisionCopy++;
                }
                else if (decision.Action == "skip")
                {
                    decisionSkip++;
                    if (decision.SkipReason == "size-limit")
                    {
                        // 超大文件：决策上属"跳过"，但引擎把它计入 failure（基线 §17.2 唯一例外）
                        decisionSizeLimit++;
                    }
                }
                else if (decision.Action == "fail")
                {
                    decisionFail++;
                }
            }
            int countedAsFailure = decisionFail + decisionSizeLimit;
            int totalCounted = decisionCopy + countedAsFailure;
            int notVisited = sourceFileCount - decisions.Count;
            if (notVisited < 0)
            {
                notVisited = 0;
            }

            var summary = new JObject
            {
                ["kind"] = "summary",
                ["total"] = lastTotal,
                ["success"] = lastSuccess,
                ["failure"] = lastFailure,
                ["cancelled"] = cancelled,
                ["countsLast"] = new JArray(lastTotal, lastSuccess, lastFailure),
                ["countsDerived"] = new JObject
                {
                    ["copy"] = decisionCopy,
                    ["skip"] = decisionSkip,
                    ["fail"] = decisionFail,
                    ["sizeLimit"] = decisionSizeLimit,
                    // 对应引擎的 failure/total：超大文件算失败（§17.2 唯一例外）
                    ["countedAsFailure"] = countedAsFailure,
                    ["totalCounted"] = totalCounted,
                    ["notVisited"] = notVisited
                },
                ["sourceFiles"] = sourceFileCount,
                ["durationMs"] = stopwatch.ElapsedMilliseconds,
                ["filesDecided"] = decisions.Count,
                ["logLines"] = log.Count
            };
            lines.Add(summary.ToString(Formatting.None));

            if (!string.IsNullOrEmpty(options.OutFile))
            {
                File.WriteAllLines(options.OutFile, lines, new UTF8Encoding(false));
                Console.WriteLine("已写入 " + options.OutFile + "（" + lines.Count + " 行）");
            }
            else
            {
                // 直接写标准输出流的 UTF-8 视图：Console.Out 会按控制台代码页（本机 GBK）
                // 编码，路径里的中文会被写坏，比对工具读到的就不是 UTF-8 了。
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                stdout.AutoFlush = true;
                foreach (var line in lines)
                {
                    stdout.WriteLine(line);
                }
                stdout.Flush();
            }
            return 0;
        }

        /// <summary>
        /// 反射调用私有内核 CopyDirectoryAsync —— 产品代码零改动。
        /// 与产品调用点一致：正向 relativeTargetDir = 卷标_序列号（由 --folder-name 传），
        /// 反向固定为 ""（见 CopyEngine.ReverseCopyAsync）。
        /// </summary>
        private static void RunDirectoryCopy(CopyEngine engine, Options options, CopyOptions copyOptions,
                                             IFileDestination destination, CancellationToken ct)
        {
            MethodInfo method = typeof(CopyEngine).GetMethod(
                "CopyDirectoryAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(
                    "CopyEngine.CopyDirectoryAsync 不存在 —— 产品改过方法签名？请同步更新 harness");
            }

            string relativeTargetDir = options.Reverse ? "" : options.FolderName;
            object[] arguments =
            {
                new DirectoryInfo(options.Source), relativeTargetDir, copyOptions, destination,
                options.Reverse, 0, ct
            };

            var task = (Task)method.Invoke(engine, arguments);
            task.GetAwaiter().GetResult();
        }

        private static CopyOptions LoadOptions(string file)
        {
            var options = new CopyOptions();
            if (string.IsNullOrEmpty(file))
            {
                return options;
            }
            try
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                var loaded = JsonConvert.DeserializeObject<CopyOptions>(json);
                return loaded ?? options;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("读取 options 失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 把日志翻译成结构化决策。
        /// 消息文案来自 CopyEngine.cs（详见 兼容性基线.md §2.15 日志→决策映射表）；
        /// 文案一旦改动，这里的映射必须同步 —— 这是 harness 唯一脆弱点。
        /// </summary>
        private static void ParseLog(List<string> log, CopyOptions options,
                                     Dictionary<string, Decision> decisions, List<JObject> dirEvents)
        {
            foreach (string message in log)
            {
                try
                {
                    if (message.StartsWith("成功复制：", StringComparison.Ordinal))
                    {
                        int arrow = message.IndexOf(" -> ", StringComparison.Ordinal);
                        if (arrow < 0)
                        {
                            continue;
                        }
                        string source = message.Substring(5, arrow - 5);
                        string relative = message.Substring(arrow + 4);
                        Record(decisions, source, d =>
                        {
                            d.Action = "copy";
                            d.RelativePath = relative;
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("跳过文件：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(5);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = ClassifySkip(source, options);
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("文件名不包含关键词：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(10);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = "name-filter";
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("文件过大已跳过：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(8);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = "size-limit";
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("跳过已存在文件：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(8);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = "duplicate-skip";
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("目标文件内容相同，跳过：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(12);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = "content-identical";
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("远程文件版本较新或相同，跳过：", StringComparison.Ordinal))
                    {
                        string source = message.Substring(15);
                        Record(decisions, source, d =>
                        {
                            d.Action = "skip";
                            d.SkipReason = "dest-newer";
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("复制失败：", StringComparison.Ordinal))
                    {
                        string body = message.Substring(5);
                        int bar = body.IndexOf(" | 错误：", StringComparison.Ordinal);
                        string source = bar >= 0 ? body.Substring(0, bar) : body;
                        string error = bar >= 0 ? body.Substring(bar + 6) : "";
                        Record(decisions, source, d =>
                        {
                            d.Action = "fail";
                            d.SkipReason = "copy-error";
                            d.Error = error;
                            d.Attempts++;
                        });
                    }
                    else if (message.StartsWith("跳过系统目录：", StringComparison.Ordinal))
                    {
                        dirEvents.Add(DirEvent(message.Substring(7), ClassifyDirSkip(message.Substring(7))));
                    }
                    else if (message.StartsWith("跳过不包含关键词的文件夹：", StringComparison.Ordinal))
                    {
                        dirEvents.Add(DirEvent(message.Substring(13), "folder-filter"));
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 文案与预期不符时不要中断整个 harness
                }
            }
        }

        private static void Record(Dictionary<string, Decision> decisions, string source,
                                   Action<Decision> apply)
        {
            Decision decision;
            if (!decisions.TryGetValue(source, out decision))
            {
                decision = new Decision();
                decisions[source] = decision;
            }
            apply(decision);
        }

        /// <summary>
        /// "跳过文件：" 这条日志把「系统文件 / 隐藏 / 标记文件」合并成一句，
        /// 无法区分。harness 拿到路径后自己看属性，还原成细粒度原因，
        /// 这样 C++ 侧可以输出更细的原因而不丢信息。
        /// </summary>
        private static string ClassifySkip(string fullPath, CopyOptions options)
        {
            string name = Path.GetFileName(fullPath);
            if (FileCategories.SystemFiles.Contains(name))
            {
                return "system-file";
            }
            if (string.Equals(name, options.ReverseCopyMarker, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, options.ReverseCopyIndicator, StringComparison.OrdinalIgnoreCase))
            {
                return "marker-file";
            }
            try
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.System) != 0)
                {
                    return "system-attr";
                }
                if ((attributes & FileAttributes.Hidden) != 0)
                {
                    return "hidden-attr";
                }
            }
            catch (Exception)
            {
                return "stat-failed";
            }
            return "unknown-skip";
        }

        private static string ClassifyDirSkip(string fullPath)
        {
            string name = Path.GetFileName(fullPath);
            if (FileCategories.SystemDirectories.Contains(name))
            {
                return "system-dir";
            }
            try
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & (FileAttributes.System | FileAttributes.Hidden)) != 0)
                {
                    return "attr-dir";
                }
            }
            catch (Exception)
            {
                return "stat-failed";
            }
            return "unknown-skip";
        }

        private static JObject DirEvent(string path, string reason)
        {
            return new JObject
            {
                ["kind"] = "dir",
                ["path"] = path,
                ["skipReason"] = reason
            };
        }

        /// <summary>
        /// 源树里的每个文件都输出一行（含"引擎根本没处理"的）。
        /// 枚举跳过 reparse point：C# 产品的 EnumerateFiles 不跳，会跟进 junction
        /// —— 这是 C++ 有意修正的差异（基线 §15 A2），比对时按规则忽略 junction 子树。
        /// </summary>
        private static int AppendFileLines(Options options, Dictionary<string, Decision> decisions,
                                           List<string> lines)
        {
            int count = 0;
            foreach (string file in EnumerateFilesSkippingReparse(options.Source))
            {
                Decision decision;
                decisions.TryGetValue(file, out decision);

                long bytes = 0, srcFt = 0;
                try
                {
                    var info = new FileInfo(file);
                    bytes = info.Length;
                    srcFt = info.LastWriteTimeUtc.ToFileTimeUtc();
                }
                catch (Exception)
                {
                    // 源文件在复制过程中被删除/占用：保留 0，比对时靠 action 判定
                }

                string destinationPath = BuildDestinationPath(options, decision, file);
                bool dstExists = false;
                long dstBytes = 0, dstFt = 0;
                if (destinationPath != null && File.Exists(destinationPath))
                {
                    var dstInfo = new FileInfo(destinationPath);
                    dstExists = true;
                    dstBytes = dstInfo.Length;
                    dstFt = dstInfo.LastWriteTimeUtc.ToFileTimeUtc();
                }

                var row = new JObject
                {
                    ["kind"] = "file",
                    ["path"] = RelativePath(options.Source, file),
                    ["action"] = decision != null ? decision.Action : "not-visited",
                    ["skipReason"] = decision != null ? (decision.SkipReason ?? "") : "",
                    ["bytes"] = bytes,
                    ["srcFt"] = srcFt,
                    ["dstExists"] = dstExists,
                    ["dstBytes"] = dstBytes,
                    ["dstFt"] = dstFt,
                    ["attempts"] = decision != null ? decision.Attempts : 0
                };
                lines.Add(row.ToString(Formatting.None));
                count++;
            }
            return count;
        }

        private static string BuildDestinationPath(Options options, Decision decision, string sourceFile)
        {
            try
            {
                string root = Path.GetFullPath(options.Target);
                string relative;
                if (decision != null && !string.IsNullOrEmpty(decision.RelativePath))
                {
                    relative = decision.RelativePath;
                }
                else
                {
                    relative = RelativePath(options.Source, sourceFile);
                }
                relative = relative.Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(root, relative);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string RelativePath(string root, string fullPath)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string normalizedFull = Path.GetFullPath(fullPath);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            string relative = normalizedFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalizedFull.Substring(prefix.Length)
                : Path.GetFileName(normalizedFull);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static IEnumerable<string> EnumerateFilesSkippingReparse(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            var results = new List<string>();

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception)
                {
                    continue;
                }

                results.AddRange(files);
                foreach (string child in directories)
                {
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue; // 不跟进符号链接/junction（避免环、避免越出源树）
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    pending.Push(child);
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        private static Options ParseArgs(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--source":
                        options.Source = Next(args, ref i);
                        break;
                    case "--target":
                        options.Target = Next(args, ref i);
                        break;
                    case "--options":
                        options.OptionsFile = Next(args, ref i);
                        break;
                    case "--mode":
                        options.Reverse = string.Equals(Next(args, ref i), "reverse",
                                                        StringComparison.OrdinalIgnoreCase);
                        break;
                    case "--folder-name":
                        options.FolderName = Next(args, ref i);
                        break;
                    case "--cancel-after":
                        options.CancelAfterMs = int.Parse(Next(args, ref i));
                        break;
                    case "--clean-target":
                        options.CleanTarget = true;
                        break;
                    case "--no-record-insertion":
                        options.RecordInsertion = false;
                        break;
                    case "--emit-log":
                        options.EmitLog = true;
                        break;
                    case "--out":
                        options.OutFile = Next(args, ref i);
                        break;
                    case "--help":
                    case "-h":
                        options.Help = true;
                        break;
                    default:
                        throw new ArgumentException("未知参数：" + args[i]);
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("参数 " + args[index] + " 缺少值");
            }
            return args[++index];
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
@"CopyHarness —— C# CopyEngine 的 headless 运行器（输出 JSON Lines）

  --source <dir>          源目录（只读）
  --target <dir>          目标目录（会被写入；配合 --clean-target）
  --options <file.json>   CopyOptions 的 JSON（camelCase；缺省 = 引擎默认值 *.*）
  --mode forward|reverse  默认 forward；reverse 复刻 ReverseCopyAsync 的目录内核
  --folder-name <name>    正向时目标子目录名（产品传 卷标_卷序列号；测试可留空）
  --cancel-after <ms>     指定毫秒后取消（验证取消后的半成品文件行为）
  --clean-target          运行前删除目标目录
  --no-record-insertion   不调用 RecordDriveInsertion（限速将不生效；用于对照）
  --emit-log              额外输出原始日志行（kind=log）
  --out <file>            写入文件而不是 stdout
  --help                  显示本说明

退出码：0 正常；2 参数/环境错误；3 复制过程未处理异常
");
        }
    }
}
