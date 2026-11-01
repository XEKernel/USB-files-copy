#!/usr/bin/env python3
"""CopyHarness 冒烟：验证 harness 本身工作正常，并固化为可复现的基准。

为什么入库：harness 是 M2 验收的锚点。它自己如果坏了（日志文案改了、反射入口变了），
比对会大面积报"not-visited"，很容易被误当成 C++ 侧 bug。这个脚本用一组确定性的
测试树把 harness 的行为钉死。

覆盖：默认过滤、深度限制、大小限制、文件名过滤、内容去重、限速、取消（半成品）。
用法：python tools\\CopyHarness\\smoke.py [--keep]
退出码：0 全通过；1 有断言失败；2 环境缺失（未构建 harness）
"""

import argparse
import ctypes
import json
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
HARNESS = os.path.join(HERE, "bin", "Release", "CopyHarness.exe")
WORK = os.path.join(os.environ.get("TEMP", "."), "usbcopy_harness_smoke")
SRC = os.path.join(WORK, "src")
DST = os.path.join(WORK, "dst")

FILE_ATTRIBUTE_HIDDEN = 0x2
FILE_ATTRIBUTE_SYSTEM = 0x4
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def set_attr(path, attributes):
    if not kernel32.SetFileAttributesW(path, attributes):
        raise OSError(ctypes.get_last_error(), "SetFileAttributesW: " + path)


def build_tree():
    if os.path.exists(WORK):
        shutil.rmtree(WORK, ignore_errors=True)
    os.makedirs(SRC)
    os.makedirs(DST)

    def write(relative, content=b"0123456789"):
        full = os.path.join(SRC, relative.replace("/", os.sep))
        parent = os.path.dirname(full)
        if parent:
            os.makedirs(parent, exist_ok=True)
        with open(full, "wb") as handle:
            handle.write(content)
        return full

    write("a.txt")
    write("b.pdf")
    write("big.bin", b"x" * 5000)
    write("sub/c.txt")
    write("sub/deep/d.txt")
    write("sub/deep/deeper/e.txt")       # 深度 3
    write("$RECYCLE.BIN/inside.txt")     # 系统目录名命中
    write("empty/.keep")
    os.remove(os.path.join(SRC, "empty", ".keep"))  # 留一个空目录

    set_attr(write("hidden.txt"), FILE_ATTRIBUTE_HIDDEN)
    set_attr(write("system.txt"), FILE_ATTRIBUTE_SYSTEM)


def run(name, options, cancel_ms=0, clean=True, no_record=False, src=None, dst=None,
        emit_log=False):
    options_path = os.path.join(WORK, "options_%s.json" % name)
    with open(options_path, "w", encoding="utf-8") as handle:
        json.dump(options, handle, ensure_ascii=False)

    out_path = os.path.join(WORK, "out_%s.jsonl" % name)
    args = [HARNESS, "--source", src or SRC, "--target", dst or DST,
            "--options", options_path, "--out", out_path]
    if clean:
        args.append("--clean-target")
    if cancel_ms:
        args.extend(["--cancel-after", str(cancel_ms)])
    if no_record:
        args.append("--no-record-insertion")
    if emit_log:
        args.append("--emit-log")

    started = time.time()
    result = subprocess.run(args, capture_output=True)
    elapsed = time.time() - started
    if result.returncode != 0:
        failures.append("%s: exit=%d\n%s" % (name, result.returncode,
                                             result.stderr.decode("gbk", errors="replace")))
        return None, elapsed

    rows = []
    with open(out_path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows, elapsed


def files_of(rows):
    return {row["path"]: row for row in rows if row["kind"] == "file"}


def summary_of(rows):
    for row in rows:
        if row["kind"] == "summary":
            return row
    return {}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--keep", action="store_true", help="保留临时目录")
    args = parser.parse_args()

    if not os.path.isfile(HARNESS):
        print("缺少 harness：%s\n先运行：dotnet build tools\\CopyHarness\\CopyHarness.csproj" % HARNESS)
        return 2

    build_tree()

    # ---- 1) 默认 *.* ----
    rows, _ = run("default", {})
    if rows is None:
        return 1
    files = files_of(rows)
    check(len(files) == 9, "default: 源树应有 9 个文件，实际 %d" % len(files))
    check(files["a.txt"]["action"] == "copy", "default: a.txt 应 copy")
    check(files["hidden.txt"]["skipReason"] == "hidden-attr", "default: hidden.txt 应 hidden-attr")
    check(files["system.txt"]["skipReason"] == "system-attr", "default: system.txt 应 system-attr")
    check(files["$RECYCLE.BIN/inside.txt"]["action"] == "not-visited",
          "default: $RECYCLE.BIN 子树应 not-visited")
    check(files["a.txt"]["dstFt"] != files["a.txt"]["srcFt"],
          "default: C# 应不保留 mtime（D8）—— 若相等说明结论要复核")

    # ---- 2) 深度限制 ----
    rows, _ = run("depth1", {"limitDirectoryDepth": True, "maxDirectoryDepth": 1})
    files = files_of(rows)
    check(files["sub/c.txt"]["action"] == "copy", "depth1: sub/c.txt 应 copy")
    check(files["sub/deep/d.txt"]["action"] == "not-visited", "depth1: sub/deep/d.txt 应 not-visited")

    # ---- 3) 大小限制（跳过也计 failure）----
    rows, _ = run("sizelimit", {"enableSizeLimit": True, "maxSizeBytes": 1000})
    files = files_of(rows)
    summary = summary_of(rows)
    check(files["big.bin"]["skipReason"] == "size-limit", "sizelimit: big.bin 应 size-limit")
    check(summary["countsDerived"]["countedAsFailure"] == 1,
          "sizelimit: 超大文件应计入 failure（引擎口径：skip 但计失败）")

    # ---- 4) 文件名过滤 ----
    rows, _ = run("namefilter", {"filterByFileName": True, "fileNameKeywords": ["a"]})
    files = files_of(rows)
    check(files["a.txt"]["action"] == "copy", "namefilter: a.txt 应 copy")
    check(files["b.pdf"]["skipReason"] == "name-filter", "namefilter: b.pdf 应 name-filter")

    # ---- 5) 内容去重（不清目标）----
    rows, _ = run("second", {}, clean=False)
    files = files_of(rows)
    check(files["a.txt"]["skipReason"] == "content-identical",
          "second: a.txt 应 content-identical，实际 %s" % files["a.txt"]["skipReason"])

    # ---- 6) 限速生效（4MB @1MB/s 应显著 > 1 秒）----
    throttle_options = {"searchPatterns": ["*.*"], "enableSpeedLimit": True,
                        "speedLimitMinutes": 60, "speedLimitBytesPerSecond": 1024 * 1024}
    with open(os.path.join(SRC, "huge.bin"), "wb") as handle:
        handle.write(b"\0" * (4 * 1024 * 1024))
    rows, elapsed = run("throttle", throttle_options)
    check(elapsed > 1.5, "throttle: 4MB@1MB/s 耗时 %.2fs，明显过短（限速未生效）" % elapsed)

    # ---- 7) 限速 + 取消：留下半成品，且 countsLast 少算（D30）----
    rows, _ = run("cancel", throttle_options, cancel_ms=800)
    files = files_of(rows)
    summary = summary_of(rows)
    huge = files["huge.bin"]
    check(huge["action"] == "fail", "cancel: huge.bin 应 fail，实际 %s" % huge["action"])
    check(0 < huge["dstBytes"] < huge["bytes"],
          "cancel: 应留下半成品（dstBytes=%d / %d）" % (huge["dstBytes"], huge["bytes"]))
    check(summary["countsLast"][2] == 0 and summary["countsDerived"]["countedAsFailure"] == 1,
          "cancel: 应复现 D30（countsLast 少算失败）—— countsLast=%s derived=%s"
          % (summary["countsLast"], summary["countsDerived"]))

    # ---- schema 形状：首行 batch、dir 路径为相对路径 ----
    rows, _ = run("schema", {})
    check(rows and rows[0].get("kind") == "batch",
          "schema: 首行必须是 kind=batch（解析器靠它识别批次边界）")
    check(rows[0].get("index") == 1 and rows[0].get("direction") == "forward",
          "schema: batch 的 index 从 1 起、direction=forward")
    check("startedFt" in rows[0] and isinstance(rows[0]["startedFt"], int),
          "schema: batch.startedFt 必须是 int64 FILETIME")
    rows, _ = run("schema_log", {}, emit_log=True)
    log_rows = [r for r in rows if r.get("kind") == "log"]
    check(bool(log_rows), "schema: --emit-log 应产出 log 行")
    check(all(r.get("level") in ("info", "error") for r in log_rows),
          "schema: log.level 只能是 info/error")
    check(all("message" in r for r in log_rows), "schema: log 行必须有 message")

    dir_rows = [r for r in rows if r.get("kind") == "dir"]
    check(all(":" not in r["path"] for r in dir_rows),
          "schema: dir.path 必须是相对路径（绝对路径两侧必然不同，无法比对）")

    # ---- D37：模式重叠（`*.doc` 经 8.3 短名也匹配 `a.docx`）----
    # 这组用例的目的是把「C++ 去重、C# 不去重」这个**已知偏差显式化**，
    # 而不是让它藏在字典折叠里。判据是 attempts：C# 为 2、C++ 应为 1。
    d37_src = os.path.join(WORK, "d37src")
    d37_dst = os.path.join(WORK, "d37dst")
    os.makedirs(d37_src, exist_ok=True)
    for fname, payload in (("a.docx", b"docx"), ("b.doc", b"doc")):
        with open(os.path.join(d37_src, fname), "wb") as handle:
            handle.write(payload)
    overlap_patterns = ["*.doc", "*.docx"]

    rows, _ = run("d37_keepboth_nodedup",
                  {"searchPatterns": overlap_patterns, "duplicateAction": 2,
                   "compareContent": False},
                  src=d37_src, dst=d37_dst)
    files = files_of(rows)
    check(files["a.docx"]["attempts"] == 2,
          "d37: C# 把 a.docx 处理了两次（attempts=2）——这是 C++ 侧去重后 attempts=1 的对照基准")
    check(files["a.docx"]["action"] == "copy",
          "d37: action 取**第一次**决策（copy）；只输出最后一次会变成 skip 造成假失败")
    check(sorted(os.listdir(d37_dst)) == ["a (1).docx", "a.docx", "b.doc"],
          "d37: KeepBoth + 内容去重关 → C# 生成两份（C++ 去重后只有一份，属有意偏差 D37）")

    rows, _ = run("d37_keepboth_withdedup",
                  {"searchPatterns": overlap_patterns, "duplicateAction": 2,
                   "compareContent": True},
                  src=d37_src, dst=d37_dst)
    files = files_of(rows)
    check(sorted(os.listdir(d37_dst)) == ["a.docx", "b.doc"],
          "d37: 内容去重开着时 KeepBoth 不会产生第二份（去重检查在重复策略**之前**）")
    check(files["a.docx"]["finalAction"] == "skip",
          "d37: 第二次决策是 content-identical skip（finalAction 与 action 不同即证明重复枚举）")

    if not args.keep:
        shutil.rmtree(WORK, ignore_errors=True)

    print("\n=======================")
    if failures:
        for item in failures:
            print("FAIL " + item)
        return 1
    print("CopyHarness 冒烟全部通过（9 个场景，含 D37 模式重叠）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
