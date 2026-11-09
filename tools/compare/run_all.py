#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""M2 端到端跑批入口（纯标准库）——M2 的回归门禁。

它做四件事（**不**复刻 compare.py 的比对规则）：
  1. 用 fixtures.py 建夹具（源树 + options JSON）；
  2. 清空两侧目标目录：C++ 没有 --clean-target，harness 的也不用，
     统一由 fixtures.reset_dir（内部 shutil.rmtree）清干净，两侧起点完全一致；
  3. 跑 C# harness 与 C++ headless 各一次，产出两个 JSONL；
  4. 调 tools\\compare\\compare.py 做逐字段比对，再对**关键数值**做少量硬断言
     （进程退出码 / JSONL 形态 / 目标目录清单 / C++ summary / D8 时间戳 / 三个文件的决策）。

为什么要有第 4 步的硬断言：compare.py 比的是「两侧是否一致」，
本脚本还要钉住「**与夹具设计的期望值**是否一致」。
两侧同时错（例如都把 size-limit 写成 name-filter）在 compare.py 里是全绿的，
只有这里会红。

用法：
    python tools\\compare\\run_all.py
    python tools\\compare\\run_all.py --only m2a_basic --keep
    python tools\\compare\\run_all.py --cpp out\\bin\\release\\UsbCopy.exe --work "%TEMP%\\m2a_try"
退出码：0 全通过；1 有断言失败（含 compare.py 非 0）；2 环境缺失 / 参数错误。

扩展方式（M2-B/C）：
    1. fixtures.py 里加 ``build_xxx`` 并登记到 ``BUILDERS``；
    2. 本文件里写 ``scenario_xxx(ctx) -> list[str]``（返回失败列表，空 = 通过），
       场景工作子目录**必须**是 ``<work>/<场景名>``（main 的清理依赖这条约定）；
    3. 登记进 ``SCENARIOS``。
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))

# 允许 `python tools\compare\run_all.py`（脚本目录自动进 sys.path）
# 以及被其它脚本 import 时都能找到 fixtures.py
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import fixtures  # noqa: E402  （必须在 sys.path 调整之后）

DEFAULT_HARNESS = os.path.join(REPO_ROOT, "tools", "CopyHarness", "bin", "Release", "CopyHarness.exe")
DEFAULT_CPP = os.path.join(REPO_ROOT, "out", "bin", "release", "UsbCopy.exe")
DEFAULT_COMPARE = os.path.join(HERE, "compare.py")
DEFAULT_WORK = os.path.join(fixtures.temp_root(), "usbcopy_m2a")

# 单个子进程的超时（秒）。夹具很小，正常几秒内结束；超时说明进程卡住了
# （例如 --cpp 指到了只会弹窗的旧 GUI 版），宁可失败也不要永久挂住。
DEFAULT_TIMEOUT = 120

# 用 exe 里有没有 "--headless" 字样（UTF-16LE）粗判它是不是支持 headless 的新构建。
# 只用于打印提醒，**不作为环境错误**（读不到文件时一律认为"支持"，交给实际运行结果说话）。
HEADLESS_MARKER = "--headless".encode("utf-16-le")


# ---------------------------------------------------------------------------
# 断言收集：-smoke.py 风格（check 收集失败，末尾 PASS/FAIL），但按场景分桶
# ---------------------------------------------------------------------------

def _fmt(value, limit=240):
    """把期望/实际值转成一行可读文本（太长截断，避免刷屏）。"""
    text = repr(value)
    return text if len(text) <= limit else text[:limit] + "..."


class Checks:
    """一个场景的断言收集器；failures 非空即该场景失败。"""

    def __init__(self, scenario):
        self.scenario = scenario
        self.failures = []

    def check(self, condition, label, detail=""):
        if condition:
            print("PASS " + label)
            return True
        message = label + ("（%s）" % detail if detail else "")
        print("FAIL " + label + ("（%s）" % detail if detail else ""))
        self.failures.append("[%s] %s" % (self.scenario, message))
        return False

    def eq(self, actual, expected, label):
        return self.check(actual == expected, label,
                          "期望 %s，实际 %s" % (_fmt(expected), _fmt(actual)))


def display_path(path):
    """仓库内的路径显示成相对路径（报错信息更短），仓库外原样显示。"""
    absolute = os.path.abspath(path)
    try:
        relative = os.path.relpath(absolute, REPO_ROOT)
    except ValueError:  # 不同盘符
        return absolute
    if relative.startswith(".."):
        return absolute
    return relative.replace(os.sep, "\\")


def supports_headless(exe_path):
    """粗略判断 exe 里是否有 --headless 参数（UTF-16LE 字面量）。

    读不到文件时返回 True（不拦路，让真实运行结果说话）。
    这个检查只用来提前提醒"可能是旧构建、会弹窗卡住"，避免对着 120s 超时猜原因。
    """
    try:
        with open(exe_path, "rb") as handle:
            data = handle.read()
    except OSError:
        return True
    return HEADLESS_MARKER in data


# ---------------------------------------------------------------------------
# 子进程 / 文件读取小工具
# ---------------------------------------------------------------------------

def run_process(args, timeout, env=None):
    """跑一个子进程并捕获输出。

    返回 (proc, elapsed, error)：
        proc    —— subprocess.CompletedProcess（stdout/stderr 已按 UTF-8 解码）
        elapsed —— 耗时秒数
        error   —— None 表示进程真的跑完（不管退出码）；否则是"没跑成"的原因
    """
    started = time.time()
    try:
        proc = subprocess.run(
            args,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
            env=env,
        )
    except subprocess.TimeoutExpired:
        return None, time.time() - started, "进程超时（>%ss）被终止：%s" % (timeout, args[0])
    except OSError as exc:
        return None, time.time() - started, "无法启动进程：%s（%s）" % (args[0], exc)
    return proc, time.time() - started, None


def print_process_output(label, proc):
    """失败诊断用：把子进程 stdout/stderr 原样打出来（空的不打）。"""
    stdout = (proc.stdout or "").strip()
    stderr = (proc.stderr or "").strip()
    if stdout:
        print("[%s stdout]" % label)
        print(stdout)
    if stderr:
        print("[%s stderr]" % label)
        print(stderr)


def check_jsonl(checks, label, path):
    """检查一份 JSONL 的形态（UTF-8 无 BOM / 首行 batch / 末行 summary / 逐行合法 JSON）。

    返回 dict（含 rows），文件缺失或解析失败时 rows 可能不完整，调用方据此跳过依赖断言。
    """
    if not os.path.isfile(path):
        checks.check(False, "%s JSONL 文件存在" % label, "缺少 %s" % path)
        return None

    with open(path, "rb") as handle:
        raw = handle.read()

    info = {
        "path": path,
        "size": len(raw),
        "has_bom": raw.startswith(b"\xef\xbb\xbf"),
        "lines": [],
        "rows": [],
        "parse_error": None,
    }

    checks.check(not info["has_bom"], "%s JSONL 是 UTF-8 无 BOM" % label,
                 "文件以 BOM 开头：%s" % path)

    text = raw.decode("utf-8", errors="replace")
    info["lines"] = [line for line in text.splitlines() if line.strip()]
    if not info["lines"]:
        checks.check(False, "%s JSONL 非空" % label, "文件是空的：%s" % path)
        return info

    checks.check(info["lines"][0].startswith('{"kind":"batch"'),
                 '%s 首行以 {"kind":"batch" 开头' % label,
                 "实际首行：%s" % _fmt(info["lines"][0]))
    checks.check(info["lines"][-1].startswith('{"kind":"summary"'),
                 '%s 末行以 {"kind":"summary" 开头' % label,
                 "实际末行：%s" % _fmt(info["lines"][-1]))

    for number, line in enumerate(info["lines"], 1):
        try:
            info["rows"].append(json.loads(line))
        except ValueError as exc:
            info["parse_error"] = "第 %d 行不是合法 JSON：%s" % (number, exc)
            break
    checks.check(info["parse_error"] is None, "%s 每行都是合法 JSON" % label,
                 info["parse_error"] or "")
    return info


def files_of(info):
    """JSONL 里的 file 行 -> {path: row}（缺失/解析失败时返回空 dict）。"""
    if not info:
        return {}
    result = {}
    for row in info["rows"]:
        if row.get("kind") == "file" and isinstance(row.get("path"), str):
            result[row["path"]] = row
    return result


def summary_of(info):
    """JSONL 里的 summary 行（没有则返回空 dict）。"""
    if not info:
        return {}
    for row in info["rows"]:
        if row.get("kind") == "summary":
            return row
    return {}


def target_listing(root):
    """递归枚举目标目录里的文件：{相对路径(/分隔): 字节数}。

    这是 §17.47 / §6.5 要求的「决策流一致 ≠ 磁盘状态一致」断言的数据来源。
    """
    listing = {}
    if not os.path.isdir(root):
        return listing
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            relative = os.path.relpath(full, root).replace(os.sep, "/")
            listing[relative] = os.path.getsize(full)
    return listing


# ---------------------------------------------------------------------------
# 三个外部程序：参数固定，便于 M2-B/C 复用；也便于测试时替换成假实现
# ---------------------------------------------------------------------------

def run_harness(harness_exe, info, out_path, timeout):
    """跑 C# harness。**不传** --clean-target：目标目录由 run_all 自己清（见模块 docstring）。"""
    args = [
        harness_exe,
        "--source", info["src"],
        "--target", info["dst_cs"],
        "--options", info["options"],
        "--out", out_path,
    ]
    return run_process(args, timeout)


def run_cpp(cpp_exe, info, out_path, timeout):
    """跑 C++ headless（JSONL 由它自己写 --out；stdout 一般没有内容）。"""
    args = [
        cpp_exe,
        "--headless",
        "--source", info["src"],
        "--target", info["dst_cpp"],
        "--options", info["options"],
        "--out", out_path,
    ]
    return run_process(args, timeout)


def run_compare(compare_py, info, timeout):
    """调 compare.py（接口固定：两个 JSONL + --src-root/--dst-cs/--dst-cpp）。

    用当前解释器执行，避免 PATH 里的 python 版本/编码与调用方不一致；
    PYTHONIOENCODING=utf-8 保证捕获到的中文输出能按 UTF-8 解码。
    """
    args = [
        sys.executable, compare_py,
        info["cs_jsonl"], info["cpp_jsonl"],
        "--src-root", info["src"],
        "--dst-cs", info["dst_cs"],
        "--dst-cpp", info["dst_cpp"],
    ]
    env = os.environ.copy()
    env.setdefault("PYTHONIOENCODING", "utf-8")
    return run_process(args, timeout, env=env)


# ---------------------------------------------------------------------------
# 场景 1：m2a_basic（§17.72 最小夹具）
# ---------------------------------------------------------------------------

def _scenario_work(ctx, name):
    """场景工作目录约定：<work>/<场景名>。main 的清理也按这条约定找目录。"""
    return os.path.join(ctx["work"], name)


def scenario_m2a_basic(ctx):
    """§17.72：3 个文件（copy / name-filter / size-limit）的完整端到端跑批。

    返回失败列表（空 = 通过）。
    """
    name = "m2a_basic"
    checks = Checks(name)
    print("\n--- 场景 %s（§17.72 最小夹具）---" % name)

    # 1) 建夹具（每次重建：先 rmtree 旧的夹具目录）
    info = fixtures.build_m2a(_scenario_work(ctx, name))
    expected = info["expected"]
    print("夹具：src=%s" % info["src"])
    print("      options=%s" % info["options"])
    print("      a.txt 3B / b.log 4B / big.bin 5B；maxSizeBytes=%d，fileNameKeywords=%r"
          % (info["options_data"]["maxSizeBytes"], info["options_data"]["fileNameKeywords"]))

    # 2) 清空两个目标目录（不依赖 harness 的 --clean-target；C++ 本来就没有）
    fixtures.reset_dir(info["dst_cs"])
    fixtures.reset_dir(info["dst_cpp"])

    # 3) C# harness
    cs_proc, cs_elapsed, cs_error = run_harness(
        ctx["harness"], info, info["cs_jsonl"], ctx["timeout"])
    print("C# harness：%.2fs，退出码 %s"
          % (cs_elapsed, "N/A" if cs_proc is None else cs_proc.returncode))
    if cs_error or cs_proc is None:
        checks.check(False, "C# harness 运行完成", cs_error or "进程没有启动")
        return checks.failures
    print_process_output("C# harness", cs_proc)
    checks.eq(cs_proc.returncode, expected["harnessExitCode"], "C# harness 退出码 == 0")

    # 4) C++ headless
    cpp_proc, cpp_elapsed, cpp_error = run_cpp(
        ctx["cpp"], info, info["cpp_jsonl"], ctx["timeout"])
    print("C++ headless：%.2fs，退出码 %s"
          % (cpp_elapsed, "N/A" if cpp_proc is None else cpp_proc.returncode))
    if cpp_error or cpp_proc is None:
        checks.check(False, "C++ headless 运行完成", cpp_error or "进程没有启动")
        return checks.failures
    print_process_output("C++ headless", cpp_proc)
    checks.eq(cpp_proc.returncode, expected["cppExitCode"],
              "C++ 退出码 == 1（size-limit 计 failure，§17.33）")

    # 5) compare.py（逐字段比对由它负责；这里只要求它退出码为 0，并把输出原样打出来）
    if not (os.path.isfile(info["cs_jsonl"]) and os.path.isfile(info["cpp_jsonl"])):
        checks.check(False, "两侧 JSONL 都已生成，可以调用 compare.py", "缺少输入文件")
    else:
        compare_proc, compare_elapsed, compare_error = run_compare(
            ctx["compare"], info, ctx["timeout"])
        print("compare.py：%.2fs，退出码 %s"
              % (compare_elapsed, "N/A" if compare_proc is None else compare_proc.returncode))
        if compare_error or compare_proc is None:
            checks.check(False, "compare.py 运行完成", compare_error or "进程没有启动")
        else:
            print("[compare.py 输出（原样）]")
            print_process_output("compare.py", compare_proc)
            checks.eq(compare_proc.returncode, 0, "compare.py 退出码 == 0")

    # 6) 额外硬断言 ---------------------------------------------------------
    cs_info = check_jsonl(checks, "C#", info["cs_jsonl"])
    cpp_info = check_jsonl(checks, "C++", info["cpp_jsonl"])

    # 6a) 目标目录清单：两侧都只有 a.txt（3 字节）——决策流一致 ≠ 磁盘结果一致
    checks.eq(target_listing(info["dst_cs"]), expected["dstListing"],
              "C# 目标目录只有 a.txt（3 字节）")
    checks.eq(target_listing(info["dst_cpp"]), expected["dstListing"],
              "C++ 目标目录只有 a.txt（3 字节）")

    # 6b) C++ summary：countsDerived 七项完全一致 + sourceFiles/exitCode/cancelled
    cpp_summary = summary_of(cpp_info)
    checks.eq(cpp_summary.get("countsDerived"), expected["cppCountsDerived"],
              "C++ summary.countsDerived 与期望完全一致")
    checks.eq(cpp_summary.get("sourceFiles"), expected["cppSummary"]["sourceFiles"],
              "C++ summary.sourceFiles == 3")
    checks.eq(cpp_summary.get("exitCode"), expected["cppSummary"]["exitCode"],
              "C++ summary.exitCode == 1")
    checks.check(cpp_summary.get("cancelled") is False, "C++ summary.cancelled == false",
                 "实际 %s" % _fmt(cpp_summary.get("cancelled")))

    # 6c) 三个文件的决策（两侧都断言：C# 是基准，C++ 是被测）
    files_cs = files_of(cs_info)
    files_cpp = files_of(cpp_info)
    for side, files in (("C#", files_cs), ("C++", files_cpp)):
        for file_name, want in expected["files"].items():
            row = files.get(file_name)
            if row is None:
                checks.check(False, "%s 输出包含 %s 行" % (side, file_name), "缺失")
                continue
            checks.eq(row.get("action"), want["action"],
                      "%s %s action == %s" % (side, file_name, want["action"]))
            checks.eq(row.get("skipReason"), want["skipReason"],
                      "%s %s skipReason == %r" % (side, file_name, want["skipReason"]))

    # 6d) D8 裁决：C# 不保留 mtime（dstFt != srcFt），C++ 写回源 mtime（dstFt == srcFt）
    cs_a = files_cs.get("a.txt")
    cpp_a = files_cpp.get("a.txt")
    if cs_a is not None:
        checks.check(cs_a.get("dstFt") != cs_a.get("srcFt"),
                     "C# a.txt dstFt != srcFt（D8：C# 不保留 mtime）",
                     "srcFt=%s dstFt=%s" % (_fmt(cs_a.get("srcFt")), _fmt(cs_a.get("dstFt"))))
    if cpp_a is not None:
        checks.check(cpp_a.get("dstFt") == cpp_a.get("srcFt"),
                     "C++ a.txt dstFt == srcFt（D8：C++ 写回源 mtime）",
                     "srcFt=%s dstFt=%s" % (_fmt(cpp_a.get("srcFt")), _fmt(cpp_a.get("dstFt"))))

    return checks.failures


# 场景注册表：M2-B/C 追加场景函数后登记到这里（键 == fixtures.BUILDERS 的键）。
SCENARIOS = {
    "m2a_basic": scenario_m2a_basic,
}


def run_scenario(name, ctx):
    """跑一个场景，返回失败列表（空列表 = 通过）。"""
    return SCENARIOS[name](ctx)


# ---------------------------------------------------------------------------
# CLI / 环境校验 / 汇总
# ---------------------------------------------------------------------------

def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        description="M2 端到端跑批：C# harness + C++ headless + compare.py + 关键断言")
    parser.add_argument("--keep", action="store_true", help="保留工作目录（默认成功时删除）")
    parser.add_argument(
        "--only", action="append", metavar="NAME",
        help="只跑指定场景（可重复；默认跑全部）。可用：%s" % ", ".join(sorted(SCENARIOS)))
    parser.add_argument("--cpp", default=DEFAULT_CPP,
                        help="C++ headless 可执行文件（默认 out\\bin\\release\\UsbCopy.exe）")
    parser.add_argument("--harness", default=DEFAULT_HARNESS,
                        help="C# harness 可执行文件（默认 tools\\CopyHarness\\bin\\Release\\CopyHarness.exe）")
    parser.add_argument("--compare", default=DEFAULT_COMPARE,
                        help="比对脚本（默认 tools\\compare\\compare.py；调试可指向别处）")
    parser.add_argument("--work", default=DEFAULT_WORK,
                        help="工作目录（默认 %%TEMP%%\\usbcopy_m2a）")
    parser.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT,
                        help="单个子进程超时秒数（默认 %d）" % DEFAULT_TIMEOUT)
    return parser.parse_args(argv)


def check_environment(args):
    """返回环境缺失清单；空列表表示可以开跑。"""
    problems = []
    required = (
        ("C# harness", args.harness, "先运行 dotnet build tools\\CopyHarness\\CopyHarness.csproj"),
        ("C++ headless", args.cpp, "先运行 build.ps1"),
        ("compare.py", args.compare, "比对工具还没落地"),
    )
    for label, path, hint in required:
        if not os.path.isfile(path):
            problems.append("缺少 %s（%s）" % (display_path(path), hint))
    if not sys.executable or not os.path.isfile(sys.executable):
        problems.append("找不到可用的 Python 解释器（sys.executable 为空），无法调用 compare.py")
    return problems


def _force_utf8_when_captured():
    """被工具/CI 捕获（非 tty）时把 stdout/stderr 切成 UTF-8。

    Windows 中文环境的 Python 默认用 cp936 写管道，而调用方（CI、DSH 等）通常按
    UTF-8 解码，PASS/FAIL 里的中文会变成乱码；交互式控制台（tty）保持原编码不动。
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            if (stream is not None and not stream.isatty()
                    and (stream.encoding or "").lower() not in ("utf-8", "utf8")):
                stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass  # 老版本或已被替换的流：忽略，不影响功能


def main(argv=None):
    _force_utf8_when_captured()
    args = parse_args(argv)

    names = args.only or list(SCENARIOS)
    unknown = [name for name in names if name not in SCENARIOS]
    if unknown:
        print("ERROR 未知场景：%s" % ", ".join(unknown))
        print("可用场景：%s" % ", ".join(sorted(SCENARIOS)))
        return 2

    # 环境缺失 = exit 2（不是断言失败）
    problems = check_environment(args)
    if problems:
        for problem in problems:
            print("ERROR " + problem)
        print("（环境不完整，未开始跑批）")
        return 2

    work_root = os.path.abspath(args.work)
    try:
        os.makedirs(work_root, exist_ok=True)
    except OSError as exc:
        print("ERROR 无法创建工作目录 %s：%s" % (work_root, exc))
        return 2

    ctx = {
        "work": work_root,
        "harness": os.path.abspath(args.harness),
        "cpp": os.path.abspath(args.cpp),
        "compare": os.path.abspath(args.compare),
        "timeout": args.timeout,
        "keep": args.keep,
    }

    print("M2 端到端跑批：%s" % ", ".join(names))
    print("harness = %s" % display_path(ctx["harness"]))
    print("cpp     = %s" % display_path(ctx["cpp"]))
    print("compare = %s" % display_path(ctx["compare"]))
    print("work    = %s" % work_root)

    if not supports_headless(ctx["cpp"]):
        print("WARNING %s 里找不到 --headless 字样，可能是还没重新构建的旧版本；"
              "若跑批卡住，请先运行 build.ps1（或用 --timeout 缩短等待）"
              % display_path(ctx["cpp"]))

    all_failures = []
    scenario_failures = {}
    for name in names:
        try:
            failures = run_scenario(name, ctx)
        except fixtures.FixtureError as exc:
            print("ERROR 夹具生成失败：" + str(exc))
            return 2
        except OSError as exc:
            print("ERROR 场景 %s 发生 IO 错误：%s" % (name, exc))
            return 2
        scenario_failures[name] = failures
        all_failures.extend(failures)

    # 清理 / 保留：--keep 或该场景失败时保留现场（失败现场最有价值）
    kept = []
    for name in names:
        work_dir = os.path.join(work_root, name)
        if args.keep or scenario_failures.get(name):
            if os.path.isdir(work_dir):
                kept.append(work_dir)
        else:
            fixtures.remove_fixture_dir(work_dir)
    try:
        os.rmdir(work_root)  # 空目录才删得掉；--keep/失败时这里会失败，忽略
    except OSError:
        pass

    print("\n" + "=" * 64)
    if kept:
        print("已保留工作目录（--keep 或失败现场）：")
        for path in kept:
            print("  " + path)
    if all_failures:
        print("失败断言（%d 项）：" % len(all_failures))
        for item in all_failures:
            print("  - " + item)
        print("RESULT: FAIL")
        return 1
    print("RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
