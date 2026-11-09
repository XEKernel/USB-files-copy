#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""M2 比对夹具生成器（纯标准库；只造数据，不跑任何进程）。

用法：
    python tools\\compare\\fixtures.py --work "%TEMP%\\m2a_fixture_check"
    python tools\\compare\\fixtures.py --list
    python tools\\compare\\fixtures.py --work <dir> --scenario m2a_basic

输出：生成结果的 JSON（src / options / dst_cs / dst_cpp / 期望值），便于人工检查。
退出码：0 成功；2 参数错误或目录安全校验失败（拒绝删除不是夹具的目录）。

================================================================================
§17.72 的夹具与两处数值矛盾（**先读懂再动手**）
================================================================================

基线 §17.72 / 交接文档 §6.5 第 3 步写的是：

    a.txt   3 字节   普通文本               → action=copy
    b.log   4 字节   fileNameKeywords=["a"] → skip/name-filter
    big.bin 5 字节   maxSizeBytes=1         → skip/size-limit（且 countedAsFailure=1）

矛盾 1 —— maxSizeBytes=1 与「a.txt → copy」互斥：
    a.txt 有 3 字节，3 > 1，按大小限制必然被判 size-limit，不可能 copy。
    裁决（Lead 已定）：夹具改用 maxSizeBytes=4，其余照基线。
        a.txt   3 <= 4 → copy
        big.bin 5 >  4 → size-limit
    b.log 是 4 字节，本来就不超 1 也不超 4，结论不受这个数字影响。

矛盾 2 —— 名字过滤在大小限制**之前**，所以原样用关键词 ["a"] 时 big.bin 到不了 size-limit：
    C# 引擎的判定顺序（Client/Core/CopyEngine.cs）：
        :428 ShouldSkipFile  →  :434 PassFileNameFilter  →  :440 大小限制
    "big.bin" 不含字符 "a"，会在 :434 就变成 skip/name-filter。
    实测（用真 harness 跑过）：countsDerived.sizeLimit=0、countedAsFailure=0、退出码 0，
    与 §17.72 要求的「size-limit + countedAsFailure=1 + 退出码 1」全部不符。
    裁决（本夹具）：fileNameKeywords 用 ["a", "bin"]。
        b.log   不含 a / bin         → skip/name-filter（仍然是"名字过滤"的样本）
        big.bin 含 "bin" 通过名字过滤 → skip/size-limit（5 > 4）
    a.txt 含 "a" 照旧通过；三个文件名、内容、字节数全部照基线。
    只多了 "bin" 一个关键词，是让 §17.72 的三种决策与计数期望同时成立的最小改动。

结论：本模块生成的夹具是**确定性的**（名字/内容/大小全部写死），不依赖时序、不依赖 IO
错误；所有期望值集中在 M2A_EXPECTED，run_all.py 直接复用，避免两处各写一份期望。

--------------------------------------------------------------------------------
扩展方式（M2-B/C）：
    1. 写一个 ``build_xxx(work_dir) -> dict``，形状与 ``build_m2a`` 相同
       （src / options / dst_cs / dst_cpp / cs_jsonl / cpp_jsonl / expected / ...）；
       子目录/多文件场景可以复用 ``write_file``，options 用 ``dump_options``；
    2. 把它登记进 ``BUILDERS``；
    3. 在 run_all.py 里加同名场景函数并登记进 ``SCENARIOS``（场景工作子目录名 == 场景名）。
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import shutil
import sys

# ---------------------------------------------------------------------------
# §17.72 最小夹具：3 个文件，覆盖 copy / name-filter / size-limit 三种决策
# ---------------------------------------------------------------------------

# 文件名 -> 字节内容。内容写死，保证每次生成的 bytes / srcFt 之外的一切都一致。
M2A_FILES = {
    "a.txt": b"abc",       # 3 字节，含 "a"，普通文本 → copy
    "b.log": b"log!",      # 4 字节，不含 "a"/"bin" → name-filter（注意不能含 "a"）
    "big.bin": b"12345",   # 5 字节，含 "bin" → 先过名字过滤，再被 4 字节上限判 size-limit
}

# 矛盾 1 的裁决：4 而不是基线写的 1（见模块 docstring）。
M2A_MAX_SIZE_BYTES = 4

# 矛盾 2 的裁决：加 "bin"（见模块 docstring）。b.log 不含 a/bin，仍被名字过滤挡下。
M2A_FILE_NAME_KEYWORDS = ["a", "bin"]

# options JSON（camelCase；25 个字段里只写需要的，其余保持引擎默认值）。
M2A_OPTIONS = {
    "targetDirectory": "",
    "searchPatterns": ["*.*"],
    "filterByFileName": True,
    "fileNameKeywords": M2A_FILE_NAME_KEYWORDS,
    "enableSizeLimit": True,
    "maxSizeBytes": M2A_MAX_SIZE_BYTES,
    "duplicateAction": 0,
    "compareContent": True,
}

# 期望值（run_all.py 的硬断言直接引用这里，不要在 run_all 里再抄一份）。
M2A_EXPECTED = {
    # 进程退出码（§17.33）：harness 恒 0；C++ 有 failure 项（size-limit 计 failure）→ 1
    "harnessExitCode": 0,
    "cppExitCode": 1,
    # §17.47/§6.5：目标目录清单（相对路径 -> 字节数），两侧都只该有 a.txt
    "dstListing": {"a.txt": 3},
    # 三个文件的决策（两侧都应一致）
    "files": {
        "a.txt": {"action": "copy", "skipReason": ""},
        "b.log": {"action": "skip", "skipReason": "name-filter"},
        "big.bin": {"action": "skip", "skipReason": "size-limit"},
    },
    # C++ summary.countsDerived 的**七项**（§13 规则 15 / §17.69）
    "cppCountsDerived": {
        "copy": 1,
        "skip": 2,
        "fail": 0,
        "sizeLimit": 1,
        "countedAsFailure": 1,
        "totalCounted": 2,
        "notVisited": 0,
    },
    "cppSummary": {
        "sourceFiles": 3,
        "exitCode": 1,
        "cancelled": False,
    },
}

# 夹具目录标记：重建/删除前用它确认「这是我们自己造的目录」，防止误删用户目录。
MARKER = ".usbcopy_fixture"


class FixtureError(RuntimeError):
    """夹具参数/目录错误（--work 指到了不安全目录等）；CLI 以 exit 2 退出。"""


# ---------------------------------------------------------------------------
# 通用小工具（M2-B/C 的场景可以直接复用）
# ---------------------------------------------------------------------------

def temp_root():
    """返回系统临时目录（TEMP/TMP 都不可用时退回当前目录）。"""
    return os.environ.get("TEMP") or os.environ.get("TMP") or "."


def write_file(root, relative, payload):
    """在 root 下按相对路径写一个文件（自动建父目录）。返回绝对路径。"""
    full = os.path.join(root, *relative.replace("\\", "/").split("/"))
    parent = os.path.dirname(full)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(full, "wb") as handle:
        handle.write(payload)
    return full


def dump_options(path, options):
    """写 options JSON（UTF-8 无 BOM、固定键序 = dict 顺序、结尾换行）。"""
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(options, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    return path


def reset_dir(path):
    """清空并重建一个目录（目标目录两侧都由此清理，不依赖 harness 的 --clean-target）。"""
    if os.path.isdir(path):
        shutil.rmtree(path)
    elif os.path.exists(path):
        os.remove(path)
    os.makedirs(path)
    return path


def _prepare_work_dir(work_dir):
    """安全地重建夹具工作目录。

    只允许删除：不存在的目录、空目录、带 MARKER 标记的目录（上次夹具生成的）。
    其它情况直接报错退出，避免 --work 指错地方时把用户目录删掉。
    """
    work = os.path.abspath(work_dir)
    if os.path.exists(work):
        if not os.path.isdir(work):
            raise FixtureError("拒绝重建：%s 已存在且不是目录" % work)
        entries = os.listdir(work)
        if MARKER in entries:
            shutil.rmtree(work)
        elif not entries:
            os.rmdir(work)
        else:
            raise FixtureError(
                "拒绝重建：%s 不是夹具目录（缺少 %s 标记），请换一个空的 --work" % (work, MARKER))
    os.makedirs(work)
    with open(os.path.join(work, MARKER), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("usbcopy M2 compare fixture\n")
    return work


def remove_fixture_dir(work_dir):
    """删除夹具目录（只在带 MARKER 时删）；返回是否真的删了。"""
    work = os.path.abspath(work_dir)
    if not os.path.isdir(work):
        return False
    if MARKER not in os.listdir(work):
        return False
    shutil.rmtree(work, ignore_errors=True)
    return True


# ---------------------------------------------------------------------------
# 场景：m2a_basic —— §17.72 的最小 3 文件夹具
# ---------------------------------------------------------------------------

def build_m2a(work_dir):
    """生成 §17.72 最小夹具，返回路径与期望值（**不跑任何进程**）。

    返回 dict 的键：
        name / spec / work / src / options
        dst_cs / dst_cpp          —— 两侧目标目录（本函数只给路径，不创建）
        cs_jsonl / cpp_jsonl      —— 两侧 JSONL 输出路径（run_all 用）
        source_files              —— 源树文件数（== 3）
        fixture_files             —— {文件名: 字节数}
        options_data / expected   —— options 与期望值的深拷贝
    """
    work = _prepare_work_dir(work_dir)
    src = os.path.join(work, "src")
    os.makedirs(src)
    for name, payload in M2A_FILES.items():
        write_file(src, name, payload)

    options_path = dump_options(os.path.join(work, "options.json"), M2A_OPTIONS)

    return {
        "name": "m2a_basic",
        "spec": "§17.72 最小夹具：a.txt copy / b.log name-filter / big.bin size-limit",
        "work": work,
        "src": src,
        "options": options_path,
        "dst_cs": os.path.join(work, "dst_cs"),
        "dst_cpp": os.path.join(work, "dst_cpp"),
        "cs_jsonl": os.path.join(work, "cs.jsonl"),
        "cpp_jsonl": os.path.join(work, "cpp.jsonl"),
        "source_files": len(M2A_FILES),
        "fixture_files": {name: len(payload) for name, payload in M2A_FILES.items()},
        # 深拷贝：调用方改了返回的 dict 也不会污染模块级常量
        "options_data": copy.deepcopy(M2A_OPTIONS),
        "expected": copy.deepcopy(M2A_EXPECTED),
    }


# 场景注册表：M2-B/C 新增夹具时在这里登记（名字 == run_all.py 里的场景名）。
BUILDERS = {
    "m2a_basic": build_m2a,
}

# 别名：任务书里把这张表称作 SCENARIOS，留个别名便于按文档检索。
SCENARIOS = BUILDERS


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _force_utf8_when_captured():
    """被工具/CI 捕获（非 tty）时把 stdout/stderr 切成 UTF-8。

    Windows 中文环境的 Python 默认用 cp936 写管道，而调用方（CI、DSH 等）通常按
    UTF-8 解码，中文提示会变成乱码；交互式控制台（tty）保持原编码不动。
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
    parser = argparse.ArgumentParser(
        description="生成 M2 比对夹具（纯标准库；只造数据，不跑进程）")
    parser.add_argument(
        "--work", default=os.path.join(temp_root(), "usbcopy_m2a", "fixtures"),
        help="夹具工作目录（会被重建；默认 %%TEMP%%\\usbcopy_m2a\\fixtures）")
    parser.add_argument(
        "--scenario", default="m2a_basic", choices=sorted(BUILDERS),
        help="夹具场景（默认 m2a_basic）")
    parser.add_argument(
        "--list", action="store_true", help="列出已登记的夹具场景后退出")
    args = parser.parse_args(argv)

    if args.list:
        for name in sorted(BUILDERS):
            print(name)
        return 0

    try:
        info = BUILDERS[args.scenario](args.work)
    except FixtureError as exc:
        print("ERROR " + str(exc))
        return 2
    print(json.dumps(info, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
