#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""C# harness 与 C++ headless 的 JSONL 双向比对器（M2-A 验收脚本）。

唯一权威规格：`兼容性基线.md` §13（五张表 A/B/C/D/E + 四条通则 + 比对规则
4–18）以及 §17.45 / §17.47 / §17.48 / §17.56 / §17.57 / §17.61 / §17.63 /
§17.67 / §17.69 / §17.73。本文件不发明任何新格式。

命令行（交给 `tools/compare/run_all.py` 调用，必须稳定）：

    python tools\\compare\\compare.py <csharp.jsonl> <cpp.jsonl> \\
        --src-root <SRC> --dst-cs <DST1> --dst-cpp <DST2> \\
        [--emit-detail] [--expect-cancel] [--allow-extra-cs-files] [--no-dst-listing]

退出码：0 全部通过；1 有差异；2 用法/文件错误。
stdout 是中文人读报告，最后一行是 `RESULT: PASS` / `RESULT: FAIL (n 项)`。

实现只依赖 Python 标准库，Python 3.8+ / Windows。
"""

import argparse
import json
import os
import stat as stat_module
import sys

# ---------------------------------------------------------------------------
# 常量
# ---------------------------------------------------------------------------

EXIT_PASS = 0
EXIT_DIFF = 1
EXIT_USAGE = 2

#: §13 规则 9：枚举源树时只跳过 reparse 目录；Windows 上 reparse point 的属性位。
FILE_ATTRIBUTE_REPARSE_POINT = 0x400

KINDS = ("batch", "file", "dir", "log", "summary")

#: §13 A–E 五张表。字段顺序 = 键序硬约束（§13 四条通则第 4 条 / §17.67）。
SCHEMA = {
    "batch": [
        ("kind", "str"),
        ("index", "int"),
        ("direction", "str"),
        ("drive", "str"),
        ("startedFt", "int"),
    ],
    "file": [
        ("kind", "str"),
        ("path", "str"),
        ("action", "str"),
        ("skipReason", "str"),
        ("finalAction", "str"),
        ("finalSkipReason", "str"),
        ("bytes", "int"),
        ("srcFt", "int"),
        ("dstExists", "bool"),
        ("dstBytes", "int"),
        ("dstFt", "int"),
        ("attempts", "int"),
        ("error", "str"),
    ],
    "dir": [
        ("kind", "str"),
        ("path", "str"),
        ("skipReason", "str"),
    ],
    "summary": [
        ("kind", "str"),
        ("total", "int"),
        ("success", "int"),
        ("failure", "int"),
        ("cancelled", "bool"),
        ("countsLast", "array3"),
        ("countsDerived", "object"),
        ("sourceFiles", "int"),
        ("filesDecided", "int"),
        ("durationMs", "int"),
        ("logLines", "int"),
        ("target", "str"),
        ("exitCode", "int"),
    ],
    "log": [
        ("kind", "str"),
        ("level", "str"),
        ("message", "str"),
    ],
}

#: §13 D 表 / §17.69 示例的 summary 键序（权威）。
SUMMARY_FIELDS = [name for name, _ in SCHEMA["summary"]]

#: 已知生产端偏差：harness 的 Program.cs 把 durationMs 写在 filesDecided 之前，
#: 与 D 表/示例相反。C++ 的 jsonl_writer 按表输出。严格键序会永远判 C# 失败，
#: 因此**只对 C# 侧**接受这一种历史键序并打印 [WARN]；其余任何键序差异都是 error。
HARNESS_SUMMARY_FIELDS = [
    "kind",
    "total",
    "success",
    "failure",
    "cancelled",
    "countsLast",
    "countsDerived",
    "sourceFiles",
    "durationMs",
    "filesDecided",
    "logLines",
    "target",
    "exitCode",
]

#: §13 规则 15 / §17.69：countsDerived 的 7 个字段（顺序也是 schema 的一部分）。
COUNTS_DERIVED_FIELDS = [
    "copy",
    "skip",
    "fail",
    "sizeLimit",
    "countedAsFailure",
    "totalCounted",
    "notVisited",
]

#: §13 B 表 / skipReason 规范枚举。
ACTIONS = ("copy", "skip", "fail", "cancel", "not-visited")

FILE_SKIP_REASONS = (
    "system-file",
    "system-attr",
    "hidden-attr",
    "marker-file",
    "reparse-point",
    "name-filter",
    "size-limit",
    "duplicate-skip",
    "content-identical",
    "dest-newer",
    "copy-error",
    "cancelled",
    "not-visited",
)

DIR_SKIP_REASONS = ("system-dir", "attr-dir", "folder-filter", "depth-limit")

LOG_LEVELS = ("info", "error")
DIRECTIONS = ("forward", "reverse")

MAX_DETAILS = 40
MAX_DETAIL_PAIRS = 50


# ---------------------------------------------------------------------------
# 异常 / 行对象
# ---------------------------------------------------------------------------


class JsonlError(Exception):
    """JSONL 文件级错误（读不了 / 不是合法 JSON / 路径不对）→ 退出码 2。"""


class Row(dict):
    """一行 JSON object，额外携带原始行号（仍然是一个 dict，便于测试与比对）。"""

    __slots__ = ("line",)

    def __init__(self, mapping, line):
        super(Row, self).__init__(mapping)
        self.line = line


class NonObjectRow(dict):
    """合法 JSON 但不是 object 的行（违反 §13 通则 1）。记为 schema error，不是文件错误。"""

    __slots__ = ("line", "raw")

    def __init__(self, raw, line):
        super(NonObjectRow, self).__init__()
        self.raw = raw
        self.line = line


class SideData(object):
    """一侧 JSONL 归一化之后的视图（§13 第 0 步 / §17.73）。"""

    def __init__(self):
        self.batch_rows = []
        self.file_rows = []          # 全部 file 行（含 not-visited，仅用于统计）
        self.not_visited_rows = []   # 已剔除，不参与逐行比对
        self.visited_files = {}      # 归一化 path -> file 行（有决策的）
        self.dir_rows = []
        self.log_rows = []
        self.summary_rows = []
        self.duplicate_paths = []    # [(path, 第一行, 重复行)]


class CompareOptions(object):
    """compare_jsonl() 的输入参数（由命令行 / run_all.py 构造）。"""

    def __init__(self, src_root, dst_cs, dst_cpp, expect_cancel=False,
                 allow_extra_cs_files=False, no_dst_listing=False, emit_detail=False):
        self.src_root = src_root
        self.dst_cs = dst_cs
        self.dst_cpp = dst_cpp
        self.expect_cancel = expect_cancel
        self.allow_extra_cs_files = allow_extra_cs_files
        self.no_dst_listing = no_dst_listing
        self.emit_detail = emit_detail


class RuleResult(object):
    """一条规则的比对结果，对应报告里的一行 [PASS]/[FAIL]/[SKIP]。"""

    def __init__(self, rule, title, ok, skipped=False, note="", details=None,
                 warnings=None, checks=0):
        self.rule = rule
        self.title = title
        self.ok = bool(ok)
        self.skipped = bool(skipped)
        self.note = note
        self.details = details or []
        self.warnings = warnings or []
        self.checks = checks


class Report(object):
    def __init__(self):
        self.results = []
        self.detail_pairs = []  # [(path, cs_row, cpp_row)]

    def add(self, rule, title, ok, **kwargs):
        result = RuleResult(rule, title, ok, **kwargs)
        self.results.append(result)
        return result

    @property
    def ok(self):
        return all(item.ok for item in self.results)

    @property
    def fail_count(self):
        return sum(1 for item in self.results if not item.ok)


class Stats(object):
    def __init__(self):
        self.cs_rows = 0
        self.cpp_rows = 0
        self.cs_kinds = {}
        self.cpp_kinds = {}
        self.cs_not_visited = 0
        self.cpp_not_visited = 0
        self.pairs = 0
        self.source_files = 0
        self.reparse_dirs = 0
        self.both_unvisited = 0
        self.source_enumerated = False


# ---------------------------------------------------------------------------
# 基础工具
# ---------------------------------------------------------------------------


def _path_sort_key(path):
    """§17.73：忽略大小写的序号比较。

    说明：C# `CompareStringOrdinal(..., TRUE)` 与 Python `str.lower()` 在极少数
    Unicode 字符（如某些土耳其语/组合字符）上存在边缘差异；文件名实践里可接受，
    任务书明确允许。排序只影响报告顺序与配对遍历顺序，不影响集合语义。
    """
    return (path.lower(), path)


def normalize_path(path):
    """§17.73 归一化第 1 步：分隔符统一为 `/`，折叠连续分隔符；大小写/空白不动。"""
    if not isinstance(path, str):
        return path
    normalized = path.replace("\\", "/")
    while "//" in normalized:
        normalized = normalized.replace("//", "/")
    return normalized


def _row_line(row):
    return getattr(row, "line", "?")


def _is_int(value):
    """bool 是 int 的子类（isinstance(True, int) 为 True），必须显式排除。"""
    return isinstance(value, int) and not isinstance(value, bool)


def _type_name(value):
    return type(value).__name__


def _short(value, limit=60):
    text = repr(value)
    if len(text) > limit:
        return text[: limit - 3] + "..."
    return text


def _check_type(value, expected):
    if expected == "str":
        return isinstance(value, str)
    if expected == "int":
        return _is_int(value)
    if expected == "bool":
        return isinstance(value, bool)
    if expected == "array3":
        return (isinstance(value, list) and len(value) == 3
                and all(_is_int(item) for item in value))
    if expected == "object":
        return isinstance(value, dict) and not isinstance(value, NonObjectRow)
    return False


def _type_expectation(expected):
    if expected == "array3":
        return "int[3]"
    if expected == "object":
        return "object"
    return expected


def _cap_details(details, limit=MAX_DETAILS):
    if len(details) <= limit:
        return details
    return details[:limit] + ["...（其余 %d 项省略）" % (len(details) - limit)]


def _key_mismatch_text(expected, actual):
    missing = [key for key in expected if key not in actual]
    extra = [key for key in actual if key not in expected]
    parts = []
    if missing:
        parts.append("缺少：" + ",".join(missing))
    if extra:
        parts.append("多余：" + ",".join(extra))
    if not missing and not extra:
        parts.append("键序不符")
    parts.append("期望 [" + ",".join(expected) + "]")
    parts.append("实际 [" + ",".join(actual) + "]")
    return "；".join(parts)


# ---------------------------------------------------------------------------
# 读取 / 行级校验（§13 四条通则）
# ---------------------------------------------------------------------------


def load_jsonl(path):
    """读 JSONL，返回 list[dict]（每行带原始行号 `row.line`）。

    空行忽略；非法 JSON / 读不了文件 → JsonlError（main 转成退出码 2）。
    合法 JSON 但不是 object 的行用 NonObjectRow 承载，交给行级校验记为 error。
    """
    rows = []
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            for lineno, raw in enumerate(handle, 1):
                text = raw.strip()
                if not text:
                    continue
                try:
                    value = json.loads(text)
                except ValueError as exc:
                    raise JsonlError("%s 第 %d 行不是合法 JSON：%s" % (path, lineno, exc))
                if isinstance(value, dict):
                    rows.append(Row(value, lineno))
                else:
                    rows.append(NonObjectRow(value, lineno))
    except OSError as exc:
        raise JsonlError("无法读取 %s：%s" % (path, exc))
    return rows


def _validate_counts_derived(value, side, line):
    errors = []
    keys = list(value.keys())
    if keys != COUNTS_DERIVED_FIELDS:
        errors.append("%s 第 %s 行 countsDerived：键序/字段不符（%s）"
                      % (side, line, _key_mismatch_text(COUNTS_DERIVED_FIELDS, keys)))
    for field in COUNTS_DERIVED_FIELDS:
        if field in value and not _is_int(value[field]):
            errors.append("%s 第 %s 行 countsDerived.%s：类型应为 int，实际 %s"
                          % (side, line, field, _type_name(value[field])))
    return errors


def validate_records(rows, side, allow_harness_summary_order=False):
    """§13 行级校验：对象 / kind / 键序与齐全 / 类型 / 规范枚举。

    返回 (errors, warnings)。任何 error 都代表「schema 不许破」，会终止后续比对。
    `allow_harness_summary_order` 只给 C# 侧用（见 HARNESS_SUMMARY_FIELDS 注释）。
    """
    errors = []
    warnings = []
    if not rows:
        return ["%s：文件为空（至少需要 batch + summary 两行）" % side], warnings

    for row in rows:
        line = _row_line(row)
        if isinstance(row, NonObjectRow):
            errors.append("%s 第 %s 行：不是 JSON object（实际 %s），违反 §13 通则 1"
                          % (side, line, _type_name(row.raw)))
            continue

        kind = row.get("kind")
        if not isinstance(kind, str):
            errors.append("%s 第 %s 行：kind 必须是 string，实际 %s"
                          % (side, line, _short(kind)))
            continue
        if kind not in SCHEMA:
            errors.append("%s 第 %s 行：kind=%r 不在 {batch,file,dir,log,summary} 内"
                          % (side, line, kind))
            continue

        expected_fields = [name for name, _ in SCHEMA[kind]]
        actual_fields = list(row.keys())
        order_ok = actual_fields == expected_fields
        if (not order_ok and kind == "summary" and allow_harness_summary_order
                and actual_fields == HARNESS_SUMMARY_FIELDS):
            order_ok = True
            warnings.append(
                "%s 第 %s 行 summary：使用 harness 历史键序（durationMs 在 filesDecided 之前）；"
                "§13 D 表/示例为 filesDecided 在前，按已知生产端偏差接受" % (side, line))
        if not order_ok:
            errors.append("%s 第 %s 行 kind=%s：键序/字段不符（%s）"
                          % (side, line, kind,
                             _key_mismatch_text(expected_fields, actual_fields)))

        for field, field_type in SCHEMA[kind]:
            if field not in row:
                continue
            value = row[field]
            if not _check_type(value, field_type):
                errors.append("%s 第 %s 行 kind=%s 字段 %s：类型应为 %s，实际 %s %s"
                              % (side, line, kind, field, _type_expectation(field_type),
                                 _type_name(value), _short(value)))
                continue
            if kind == "summary" and field == "countsDerived":
                errors.extend(_validate_counts_derived(value, side, line))

        # ---- 规范枚举（§13 skipReason 表 / §17.68）----
        if kind == "batch":
            direction = row.get("direction")
            if isinstance(direction, str) and direction not in DIRECTIONS:
                errors.append("%s 第 %s 行 batch.direction=%r 不在 {forward,reverse} 内"
                              % (side, line, direction))
        elif kind == "file":
            action = row.get("action")
            reason = row.get("skipReason")
            if isinstance(action, str):
                if action not in ACTIONS:
                    errors.append("%s 第 %s 行 file.action=%r 不在 {copy,skip,fail,cancel,not-visited} 内"
                                  % (side, line, action))
                elif isinstance(reason, str):
                    if action == "copy" and reason != "":
                        errors.append("%s 第 %s 行 file：action=copy 时 skipReason 必须为空串，实际 %r"
                                      % (side, line, reason))
                    elif action == "not-visited" and reason != "not-visited":
                        errors.append("%s 第 %s 行 file：action=not-visited 时 skipReason 必须是 "
                                      "%r（§17.68），实际 %r" % (side, line, "not-visited", reason))
                    elif action in ("skip", "fail", "cancel"):
                        if reason not in FILE_SKIP_REASONS or reason == "not-visited":
                            errors.append("%s 第 %s 行 file.action=%s：skipReason=%r 不在规范枚举内"
                                          % (side, line, action, reason))
        elif kind == "dir":
            reason = row.get("skipReason")
            if isinstance(reason, str) and reason not in DIR_SKIP_REASONS:
                errors.append("%s 第 %s 行 dir.skipReason=%r 不在规范枚举内"
                              % (side, line, reason))
        elif kind == "log":
            level = row.get("level")
            if isinstance(level, str) and level not in LOG_LEVELS:
                errors.append("%s 第 %s 行 log.level=%r 不在 {info,error} 内"
                              % (side, line, level))
    return errors, warnings


def validate_structure(rows, side):
    """§17.67：首行必须是 batch，末行必须是 summary（硬约束）。"""
    errors = []
    if not rows:
        return ["%s：文件为空" % side]
    first = rows[0]
    first_kind = None if isinstance(first, NonObjectRow) else first.get("kind")
    if first_kind != "batch":
        errors.append("%s：首行必须是 kind=batch（实际 %r，第 %s 行）"
                      % (side, first_kind, _row_line(first)))
    last = rows[-1]
    last_kind = None if isinstance(last, NonObjectRow) else last.get("kind")
    if last_kind != "summary":
        errors.append("%s：末行必须是 kind=summary（实际 %r，第 %s 行）"
                      % (side, last_kind, _row_line(last)))
    return errors


# ---------------------------------------------------------------------------
# 归一化（§13 第 0 步 / §17.73）
# ---------------------------------------------------------------------------


def normalize_rows(rows):
    """丢掉 log（单独保存）、剔除 not-visited、按归一化 path 建索引。

    同一 path 出现多行 → 记录到 duplicate_paths（规则 3 判 error；正常不会出现）。
    """
    data = SideData()
    seen = {}
    for row in rows:
        if isinstance(row, NonObjectRow):
            continue
        kind = row.get("kind")
        if kind == "batch":
            data.batch_rows.append(row)
        elif kind == "file":
            data.file_rows.append(row)
            path = normalize_path(row.get("path", ""))
            if path in seen:
                data.duplicate_paths.append((path, seen[path], row))
                continue
            seen[path] = row
            if row.get("action") == "not-visited":
                data.not_visited_rows.append(row)
            else:
                data.visited_files[path] = row
        elif kind == "dir":
            data.dir_rows.append(row)
        elif kind == "log":
            data.log_rows.append(row)
        elif kind == "summary":
            data.summary_rows.append(row)
    return data


def pair_files(cs_data, cpp_data):
    """按归一化 path 配对（忽略大小写序号序）。返回 [(path, cs_row|None, cpp_row|None)]。"""
    pairs = []
    all_paths = set(cs_data.visited_files) | set(cpp_data.visited_files)
    for path in sorted(all_paths, key=_path_sort_key):
        pairs.append((path, cs_data.visited_files.get(path),
                      cpp_data.visited_files.get(path)))
    return pairs


# ---------------------------------------------------------------------------
# 磁盘枚举（规则 9 / 规则 10 / 规则 14）
# ---------------------------------------------------------------------------


def enumerate_source_files(src_root):
    """枚举源树里的所有文件（相对路径，`/` 分隔）。

    规则 9：**只跳过 reparse 目录**，不做任何过滤（不按扩展名/属性/深度剪枝）。
    返回 (files:set, reparse_dirs:list, errors:list)。
    """
    files = set()
    reparse_dirs = []
    errors = []
    if not os.path.isdir(src_root):
        return files, reparse_dirs, ["源根不存在或不是目录：%s" % src_root]

    stack = [("", src_root)]
    while stack:
        rel_dir, abs_dir = stack.pop()
        try:
            with os.scandir(abs_dir) as iterator:
                entries = list(iterator)
        except OSError as exc:
            errors.append("无法枚举目录 %s：%s" % (abs_dir, exc))
            continue
        for entry in entries:
            relative = entry.name if not rel_dir else rel_dir + "/" + entry.name
            try:
                attributes = getattr(entry.stat(follow_symlinks=False),
                                     "st_file_attributes", 0)
            except OSError:
                attributes = 0
            is_reparse = bool(attributes & FILE_ATTRIBUTE_REPARSE_POINT)
            try:
                if entry.is_symlink():
                    is_reparse = True
            except OSError:
                pass
            try:
                is_dir = entry.is_dir(follow_symlinks=True)
            except OSError:
                is_dir = False
            if is_reparse and is_dir:
                # §13 规则 10：跳过整个 reparse 子树（两侧都不产出、也不进 not-visited）
                reparse_dirs.append(normalize_path(relative))
                continue
            if is_dir:
                stack.append((relative, entry.path))
            else:
                # reparse **文件**也算源树文件（规则 9 只跳过 reparse 目录）
                files.add(normalize_path(relative))
    return files, reparse_dirs, errors


def list_target_files(root):
    """规则 14：递归枚举目标目录 → {相对路径: 字节数}。

    目标目录不存在时返回空映射 + 一条 error 文本（由调用方决定如何断言）。
    """
    listing = {}
    errors = []
    if not os.path.isdir(root):
        return listing, ["目标目录不存在：%s" % root]
    stack = [("", root)]
    while stack:
        rel_dir, abs_dir = stack.pop()
        try:
            with os.scandir(abs_dir) as iterator:
                entries = list(iterator)
        except OSError as exc:
            errors.append("无法枚举目标目录 %s：%s" % (abs_dir, exc))
            continue
        for entry in entries:
            relative = entry.name if not rel_dir else rel_dir + "/" + entry.name
            try:
                if entry.is_dir(follow_symlinks=False):
                    stack.append((relative, entry.path))
                elif entry.is_file(follow_symlinks=False) or entry.is_symlink():
                    size = entry.stat(follow_symlinks=False).st_size
                    listing[normalize_path(relative)] = size
            except OSError as exc:
                errors.append("无法读取目标项 %s：%s" % (entry.path, exc))
    return listing, errors


# ---------------------------------------------------------------------------
# 汇总派生
# ---------------------------------------------------------------------------


def _expected_exit_code(summary):
    """§13 规则 17 的裁决：C++ 的 exitCode 由它自己的 summary 推导。

    cancelled → 3（取消优先于 1）；countedAsFailure > 0 → 1；否则 0。
    （C# harness 恒填 0，§13 D 表与规则 17 原文冲突，按任务裁决改为每侧自洽断言。）
    """
    if summary.get("cancelled") is True:
        return 3
    counts = summary.get("countsDerived")
    if isinstance(counts, dict):
        counted = counts.get("countedAsFailure")
        if _is_int(counted) and counted > 0:
            return 1
    return 0


def _get_summary(side_data):
    return side_data.summary_rows[-1] if side_data.summary_rows else None


def _is_cancel_pair(cs_row, cpp_row):
    return (cs_row is not None and cs_row.get("action") == "cancel") or \
           (cpp_row is not None and cpp_row.get("action") == "cancel")


def _dst_fields_to_compare(action):
    """§13 规则 7 + 规则 8 的合并解释。

    规则 7 原文说 copy/skip「三项全比」，但规则 8 明确「D8 只断言两侧各自的
    srcFt/dstFt 关系、不比两侧数值」。对 copy 行而言，C# dstFt != srcFt 而
    C++ dstFt == srcFt 是**有意偏离**，跨侧比较 dstFt 必然假失败；所以 copy 行
    的 dstFt 交给规则 8，这里只比 dstExists/dstBytes。skip 行与 D8 无关，三项全比。
    """
    if action == "fail":
        return ("dstExists",)
    if action == "copy":
        return ("dstExists", "dstBytes")
    if action == "skip":
        return ("dstExists", "dstBytes", "dstFt")
    return ()


# ---------------------------------------------------------------------------
# 主比对
# ---------------------------------------------------------------------------


def _raw_kind_counts(rows):
    counts = dict((kind, 0) for kind in KINDS)
    for row in rows:
        if isinstance(row, NonObjectRow):
            continue
        kind = row.get("kind")
        if kind in counts:
            counts[kind] += 1
    return counts


def compare_jsonl(cs_rows, cpp_rows, options):
    """核心：输入两侧已解析的行，返回 (Report, Stats)。不打印。"""
    report = Report()
    stats = Stats()
    stats.cs_rows = len(cs_rows)
    stats.cpp_rows = len(cpp_rows)
    stats.cs_kinds = _raw_kind_counts(cs_rows)
    stats.cpp_kinds = _raw_kind_counts(cpp_rows)

    # ------------------------------------------------------------------
    # 规则 0：行级 schema + 首尾顺序
    # ------------------------------------------------------------------
    cs_errors, cs_warnings = validate_records(
        cs_rows, "C#", allow_harness_summary_order=True)
    cpp_errors, cpp_warnings = validate_records(
        cpp_rows, "C++", allow_harness_summary_order=False)
    schema_ok = not (cs_errors or cpp_errors)
    report.add(
        "0", "行级校验（schema，§13 五张表）", schema_ok,
        note="C# %d 行 / C++ %d 行" % (len(cs_rows), len(cpp_rows)),
        details=_cap_details(cs_errors + cpp_errors),
        warnings=_cap_details(cs_warnings + cpp_warnings),
        checks=len(cs_rows) + len(cpp_rows))

    structure_errors = validate_structure(cs_rows, "C#") + \
        validate_structure(cpp_rows, "C++")
    structure_ok = not structure_errors
    report.add(
        "0", "首尾顺序（batch 开头 / summary 结尾）", structure_ok,
        details=_cap_details(structure_errors), checks=2)

    if not (schema_ok and structure_ok):
        for rule, title in (
            ("3", "归一化与配对"),
            ("4", "逐文件 action/skipReason"),
            ("5", "attempts（逐行）"),
            ("6", "bytes"),
            ("7", "dst*（dstExists / dstBytes / dstFt）"),
            ("8", "D8 mtime 断言"),
            ("9", "not-visited 集合重算"),
            ("10", "reparse-point 子树"),
            ("11", "取消场景识别"),
            ("12", "取消场景每侧独立断言"),
            ("13", "fail ↔ cancel 映射"),
            ("14", "目标目录清单"),
            ("15", "countsDerived / sourceFiles"),
            ("16", "batch"),
            ("17", "summary"),
            ("18", "dir 集合"),
        ):
            report.add(rule, title, True, skipped=True,
                       note="规则 0 未通过，跳过")
        return report, stats

    # ------------------------------------------------------------------
    # 归一化
    # ------------------------------------------------------------------
    cs_data = normalize_rows(cs_rows)
    cpp_data = normalize_rows(cpp_rows)
    stats.cs_not_visited = len(cs_data.not_visited_rows)
    stats.cpp_not_visited = len(cpp_data.not_visited_rows)

    pairs = pair_files(cs_data, cpp_data)
    stats.pairs = len(pairs)
    report.detail_pairs = pairs

    # ---- 规则 3：归一化 / 重复 path / 配对完整性 ----
    details = []
    for path, first_row, second_row in cs_data.duplicate_paths:
        details.append("C# 重复 file path %r（第 %s 行与第 %s 行）"
                       % (path, _row_line(first_row), _row_line(second_row)))
    for path, first_row, second_row in cpp_data.duplicate_paths:
        details.append("C++ 重复 file path %r（第 %s 行与第 %s 行）"
                       % (path, _row_line(first_row), _row_line(second_row)))
    only_cs = [path for path, cs_row, cpp_row in pairs
               if cs_row is None]
    only_cpp = [path for path, cs_row, cpp_row in pairs
                if cpp_row is None]
    for path in only_cs:
        details.append("仅 C++ 有决策（C# 未输出）：%s" % path)
    for path in only_cpp:
        details.append("仅 C# 有决策（C++ 未输出）：%s" % path)
    report.add(
        "3", "归一化与配对（丢弃 log / 剔除 not-visited / 按 path 排序）",
        not details,
        note="配对 %d 行；仅单侧 %d" % (len(pairs), len(only_cs) + len(only_cpp)),
        details=_cap_details(details), checks=len(pairs))

    # 供规则 4–8 使用：两侧都存在的配对
    common_pairs = [(path, cs_row, cpp_row) for path, cs_row, cpp_row in pairs
                    if cs_row is not None and cpp_row is not None]

    # ------------------------------------------------------------------
    # 规则 4：action 与 skipReason
    # ------------------------------------------------------------------
    details = []
    cancel_mapping = []
    for path, cs_row, cpp_row in common_pairs:
        cs_action = cs_row.get("action")
        cpp_action = cpp_row.get("action")
        if cs_action != cpp_action:
            if options.expect_cancel and cs_action == "fail" and cpp_action == "cancel":
                # §13 规则 13 唯一例外：取消场景允许 C# fail ↔ C++ cancel
                cancel_mapping.append((path, cs_row, cpp_row))
                continue
            details.append("%s：action 不同（C# %s / C++ %s）"
                           % (path, cs_action, cpp_action))
            continue
        cs_reason = cs_row.get("skipReason")
        cpp_reason = cpp_row.get("skipReason")
        if cs_reason != cpp_reason:
            details.append("%s：skipReason 不同（C# %r / C++ %r）"
                           % (path, cs_reason, cpp_reason))
    report.add(
        "4", "逐文件 action / skipReason", not details,
        note="配对 %d 行" % len(common_pairs),
        details=_cap_details(details), checks=len(common_pairs))

    # ------------------------------------------------------------------
    # 规则 5：attempts 逐行判（C++ 恒 1；C# >= 1）
    # ------------------------------------------------------------------
    details = []
    for path, cs_row, cpp_row in common_pairs:
        if cpp_row.get("attempts") != 1:
            details.append("%s：C++ attempts=%r（必须恒为 1，§17.61）"
                           % (path, cpp_row.get("attempts")))
        cs_attempts = cs_row.get("attempts")
        if _is_int(cs_attempts) and cs_attempts < 1:
            details.append("%s：C# attempts=%r（必须 >= 1）" % (path, cs_attempts))
    report.add(
        "5", "attempts（C++ 恒 1 / C# >= 1，逐行）", not details,
        note="配对 %d 行 × 2 项" % len(common_pairs),
        details=_cap_details(details), checks=len(common_pairs) * 2)

    # ------------------------------------------------------------------
    # 规则 6：bytes（取消行不跨侧比）
    # ------------------------------------------------------------------
    details = []
    checks = 0
    for path, cs_row, cpp_row in common_pairs:
        if cs_row.get("action") != cpp_row.get("action"):
            continue  # 规则 4 已报；不再叠加噪音
        if _is_cancel_pair(cs_row, cpp_row):
            continue  # §13 规则 12 / §17.57：取消落点不可控
        checks += 1
        if cs_row.get("bytes") != cpp_row.get("bytes"):
            details.append("%s：bytes 不同（C# %r / C++ %r）"
                           % (path, cs_row.get("bytes"), cpp_row.get("bytes")))
    report.add(
        "6", "bytes（取消行不跨侧比）", not details,
        note="比较 %d 行" % checks, details=_cap_details(details), checks=checks)

    # ------------------------------------------------------------------
    # 规则 7：dstExists / dstBytes / dstFt
    # ------------------------------------------------------------------
    details = []
    checks = 0
    for path, cs_row, cpp_row in common_pairs:
        action = cs_row.get("action")
        if action != cpp_row.get("action"):
            continue
        if action == "cancel":
            continue  # 不跨侧比（§13 规则 7 / §17.57）
        fields = _dst_fields_to_compare(action)
        if not fields:
            continue
        checks += 1
        for field in fields:
            if cs_row.get(field) != cpp_row.get(field):
                details.append("%s（action=%s）：%s 不同（C# %r / C++ %r）"
                               % (path, action, field, cs_row.get(field),
                                  cpp_row.get(field)))
    report.add(
        "7", "dst*（copy 比 dstExists/dstBytes；skip 三项；fail 只比 dstExists）",
        not details, note="比较 %d 行" % checks,
        details=_cap_details(details), checks=checks)

    # ------------------------------------------------------------------
    # 规则 8：D8 —— copy 行 C# dstFt != srcFt、C++ dstFt == srcFt
    # ------------------------------------------------------------------
    details = []
    checks = 0
    for path, cs_row, cpp_row in common_pairs:
        if cs_row.get("action") != "copy" or cpp_row.get("action") != "copy":
            continue
        checks += 1
        if not cs_row.get("dstExists") or not cpp_row.get("dstExists"):
            details.append("%s：action=copy 但目标不存在（C# dstExists=%r / C++ dstExists=%r）"
                           % (path, cs_row.get("dstExists"), cpp_row.get("dstExists")))
            continue
        cs_src, cs_dst = cs_row.get("srcFt"), cs_row.get("dstFt")
        cpp_src, cpp_dst = cpp_row.get("srcFt"), cpp_row.get("dstFt")
        if _is_int(cs_src) and _is_int(cs_dst) and cs_dst == cs_src:
            details.append("%s：C# 未偏离 D8 —— dstFt == srcFt（%d），应不相等"
                           % (path, cs_dst))
        if _is_int(cpp_src) and _is_int(cpp_dst) and cpp_dst != cpp_src:
            details.append("%s：C++ 未写回源 mtime —— dstFt=%d != srcFt=%d（D8 要求相等）"
                           % (path, cpp_dst, cpp_src))
    report.add(
        "8", "D8 mtime（C# dstFt != srcFt；C++ dstFt == srcFt）", not details,
        note="copy %d 行" % checks, details=_cap_details(details), checks=checks)

    # ------------------------------------------------------------------
    # 规则 9 / 10：not-visited 重算、reparse
    # ------------------------------------------------------------------
    src_files, reparse_dirs, enum_errors = enumerate_source_files(options.src_root)
    stats.source_enumerated = True
    stats.source_files = len(src_files)
    stats.reparse_dirs = len(reparse_dirs)

    if enum_errors:
        report.add("9", "not-visited 集合重算", False,
                   details=_cap_details(enum_errors), checks=1)
        stats.cs_not_visited = 0
        stats.cpp_not_visited = 0
    else:
        visited_cs = set(cs_data.visited_files)
        visited_cpp = set(cpp_data.visited_files)
        not_visited_cs = src_files - visited_cs
        not_visited_cpp = src_files - visited_cpp
        both_unvisited = src_files - visited_cs - visited_cpp
        stats.cs_not_visited = len(not_visited_cs)
        stats.cpp_not_visited = len(not_visited_cpp)
        stats.both_unvisited = len(both_unvisited)
        details = []
        if not_visited_cs != not_visited_cpp:
            details.append("重算的 not-visited 集合不相等（C# %d 个 / C++ %d 个）"
                           % (len(not_visited_cs), len(not_visited_cpp)))
            for path in sorted(not_visited_cs - not_visited_cpp, key=_path_sort_key):
                details.append("C# 未访问 / C++ 已访问：%s" % path)
            for path in sorted(not_visited_cpp - not_visited_cs, key=_path_sort_key):
                details.append("C++ 未访问 / C# 已访问：%s" % path)
        report.add(
            "9", "not-visited 集合重算（源树 - 各侧已决策 path）", not details,
            note="源树 %d 个文件；C# 未访问 %d / C++ 未访问 %d；两侧都未访问 %d"
                 % (len(src_files), len(not_visited_cs), len(not_visited_cpp),
                    len(both_unvisited)),
            details=_cap_details(details), checks=len(src_files))

    if not reparse_dirs:
        report.add("10", "reparse-point 子树", True, skipped=True,
                   note="源树没有 reparse 目录", checks=1)
    else:
        details = []
        # 规则 10 只要求「跳过且不递归」；不要求两侧逐行比对子树。
        for path in sorted(reparse_dirs, key=_path_sort_key):
            details.append("已忽略 reparse 子树：%s" % path)
        report.add(
            "10", "reparse-point 子树（跳过且不递归）", True,
            note="源树有 %d 个 reparse 目录，子树两侧都不产出" % len(reparse_dirs),
            details=_cap_details(details), checks=len(reparse_dirs))

    # ------------------------------------------------------------------
    # 规则 11 / 12 / 13：取消场景
    # ------------------------------------------------------------------
    cs_cancel_rows = [row for row in cs_data.visited_files.values()
                      if row.get("action") == "cancel"]
    cpp_cancel_rows = [row for row in cpp_data.visited_files.values()
                       if row.get("action") == "cancel"]

    details = []
    if options.expect_cancel:
        ok = bool(cpp_cancel_rows)
        if not ok:
            details.append("传了 --expect-cancel（意图信号），但 C++ 侧没有任何 "
                           "action=cancel 行（结果信号缺失，§17.63）")
        note = "取消场景：C++ cancel 行 %d 条" % len(cpp_cancel_rows)
    else:
        ok = not cpp_cancel_rows and not cs_cancel_rows
        if cpp_cancel_rows:
            details.append("未传 --expect-cancel，但 C++ 侧出现 %d 条 action=cancel"
                           % len(cpp_cancel_rows))
        if cs_cancel_rows:
            details.append("未传 --expect-cancel，但 C# 侧出现 %d 条 action=cancel"
                           % len(cs_cancel_rows))
        note = "非取消场景"
    report.add("11", "取消场景识别（意图 + 结果两个信号）", ok,
               note=note, details=_cap_details(details), checks=1)

    if not options.expect_cancel:
        report.add("12", "取消场景每侧独立断言", True, skipped=True,
                   note="非取消场景", checks=0)
    else:
        details = []
        cpp_summary = _get_summary(cpp_data)
        checks = 0
        if cpp_summary is None:
            details.append("C++ 缺 summary 行，无法断言取消状态")
        else:
            checks += 2
            if cpp_summary.get("cancelled") is not True:
                details.append("C++ summary.cancelled 应为 true，实际 %r"
                               % (cpp_summary.get("cancelled"),))
            if cpp_summary.get("exitCode") != 3:
                details.append("C++ summary.exitCode 应为 3（被取消），实际 %r"
                               % (cpp_summary.get("exitCode"),))
        if not cpp_cancel_rows:
            details.append("C++ 侧没有任何 action=cancel 行")
        for row in sorted(cpp_cancel_rows, key=lambda item: _path_sort_key(
                normalize_path(item.get("path", "")))):
            path = normalize_path(row.get("path", ""))
            checks += 3
            if row.get("dstExists") is not True:
                details.append("%s：action=cancel 时 dstExists 应为 true，实际 %r"
                               % (path, row.get("dstExists")))
            dst_bytes = row.get("dstBytes")
            src_bytes = row.get("bytes")
            if not (_is_int(dst_bytes) and _is_int(src_bytes)
                    and 0 < dst_bytes < src_bytes):
                details.append("%s：action=cancel 时应留下半成品（0 < dstBytes < bytes），"
                               "实际 dstBytes=%r / bytes=%r"
                               % (path, dst_bytes, src_bytes))
        note = "C++ cancel 行 %d 条（C# harness 无 cancel，按 §17.57 不做取消断言）" \
               % len(cpp_cancel_rows)
        report.add("12", "取消场景每侧独立断言（C++ 侧）", not details,
                   note=note, details=_cap_details(details), checks=checks)

    details = []
    if options.expect_cancel:
        bad = []
        for path, cs_row, cpp_row in common_pairs:
            if cpp_row.get("action") != "cancel":
                continue
            if cs_row.get("action") not in ("fail", "cancel"):
                bad.append("%s：C++ action=cancel 但 C# action=%s（只允许 fail ↔ cancel）"
                           % (path, cs_row.get("action")))
        details = bad
        note = "取消场景允许的 C# fail ↔ C++ cancel 映射：%d 处" % len(cancel_mapping)
    else:
        note = "非取消场景：fail 必须对 fail，无任何例外"
        for path, cs_row, cpp_row in common_pairs:
            if {cs_row.get("action"), cpp_row.get("action")} == {"fail", "cancel"}:
                details.append("%s：未传 --expect-cancel 却出现 fail ↔ cancel 映射" % path)
    report.add("13", "fail ↔ cancel 映射（仅取消场景允许）", not details,
               note=note, details=_cap_details(details), checks=len(common_pairs))

    # ------------------------------------------------------------------
    # 规则 14：目标目录清单
    # ------------------------------------------------------------------
    if options.expect_cancel:
        report.add("14", "目标目录清单", True, skipped=True,
                   note="取消场景落点不可控（§17.57），自动跳过；确需断言请单独设计夹具",
                   checks=0)
    elif options.no_dst_listing:
        report.add("14", "目标目录清单", True, skipped=True,
                   note="命令行指定 --no-dst-listing", checks=0)
    else:
        details = []
        failed = []
        warnings = []
        checks = 0
        cs_listing, cs_errors = list_target_files(options.dst_cs)
        cpp_listing, cpp_errors = list_target_files(options.dst_cpp)
        for message in cs_errors + cpp_errors:
            warnings.append(message)
        cs_only = sorted(set(cs_listing) - set(cpp_listing), key=_path_sort_key)
        cpp_only = sorted(set(cpp_listing) - set(cs_listing), key=_path_sort_key)
        common = sorted(set(cs_listing) & set(cpp_listing), key=_path_sort_key)
        checks += len(cs_only) + len(cpp_only) + len(common)
        for path in cpp_only:
            message = "C++ 目标多出文件（不允许）：%s（%d B）" % (path, cpp_listing[path])
            details.append(message)
            failed.append(message)
        for path in cs_only:
            if options.allow_extra_cs_files:
                # D37 场景：C# 会多出 `a (1).docx`；必须逐条打印，不许静默通过。
                details.append("C# 目标多出文件（--allow-extra-cs-files 允许）：%s（%d B）"
                               % (path, cs_listing[path]))
            else:
                message = ("C# 目标多出文件（不允许，需 --allow-extra-cs-files）："
                           "%s（%d B）" % (path, cs_listing[path]))
                details.append(message)
                failed.append(message)
        for path in common:
            if cs_listing[path] != cpp_listing[path]:
                message = ("%s：目标文件大小不同（C# %d B / C++ %d B）"
                           % (path, cs_listing[path], cpp_listing[path]))
                details.append(message)
                failed.append(message)
        # copy 行对应的目标文件必须存在（在对应侧清单里）
        for side_name, side_data, listing in (
                ("C#", cs_data, cs_listing), ("C++", cpp_data, cpp_listing)):
            for path, row in sorted(side_data.visited_files.items(),
                                    key=lambda item: _path_sort_key(item[0])):
                if row.get("action") != "copy":
                    continue
                checks += 1
                if path not in listing:
                    message = "%s：action=copy 但目标不存在：%s" % (side_name, path)
                    details.append(message)
                    failed.append(message)
        report.add(
            "14", "目标目录清单（{相对路径: 大小}）", not failed,
            note="C# %d 个文件 / C++ %d 个文件" % (len(cs_listing), len(cpp_listing)),
            details=_cap_details(details), warnings=_cap_details(warnings),
            checks=checks)

    # ------------------------------------------------------------------
    # 规则 15：countsDerived / sourceFiles
    # ------------------------------------------------------------------
    details = []
    warnings = []
    checks = 0
    cs_summary = _get_summary(cs_data)
    cpp_summary = _get_summary(cpp_data)
    if cs_summary is None or cpp_summary is None:
        details.append("缺少 summary 行（C# %s / C++ %s）"
                       % ("有" if cs_summary else "无",
                          "有" if cpp_summary else "无"))
    else:
        cs_counts = cs_summary.get("countsDerived")
        cpp_counts = cpp_summary.get("countsDerived")
        if not isinstance(cs_counts, dict) or not isinstance(cpp_counts, dict):
            details.append("countsDerived 不是 object")
        else:
            cancel_pair_count = len(cancel_mapping) if options.expect_cancel else 0
            for field in COUNTS_DERIVED_FIELDS:
                checks += 1
                cs_value = cs_counts.get(field)
                cpp_value = cpp_counts.get(field)
                if cs_value == cpp_value:
                    continue
                # §17.32 的取消映射延伸到计数：C# 把取消记为 fail 决策，C++ 记为独立
                # cancel 决策（countsDerived 没有 cancel 字段，只并入 countedAsFailure）。
                # 取消场景允许 fail 的差额恰好等于已映射的取消对数；countedAsFailure
                # 仍要求严格相等（两边都应把取消计失败）。
                if (options.expect_cancel and field == "fail"
                        and _is_int(cs_value) and _is_int(cpp_value)
                        and cs_value == cpp_value + cancel_pair_count):
                    continue
                details.append("countsDerived.%s 不同（C# %r / C++ %r）"
                               % (field, cs_value, cpp_value))
            if not options.expect_cancel:
                for side_name, counts in (("C#", cs_counts), ("C++", cpp_counts)):
                    fail = counts.get("fail")
                    size_limit = counts.get("sizeLimit")
                    counted = counts.get("countedAsFailure")
                    if all(_is_int(item) for item in (fail, size_limit, counted)):
                        if counted != fail + size_limit:
                            warnings.append(
                                "%s countsDerived 内部不自洽：countedAsFailure=%d，"
                                "但 fail+sizeLimit=%d（§17.69）"
                                % (side_name, counted, fail + size_limit))
                    copy = counts.get("copy")
                    total = counts.get("totalCounted")
                    if all(_is_int(item) for item in (copy, counted, total)):
                        if total != copy + counted:
                            warnings.append(
                                "%s countsDerived 内部不自洽：totalCounted=%d，"
                                "但 copy+countedAsFailure=%d（§17.69）"
                                % (side_name, total, copy + counted))
            # 取消场景不检查上面的内部公式：C++ 的 countedAsFailure 含 cancel，
            # 而 countsDerived 没有 cancel 字段，公式无法在本脚本内还原。
        checks += 1
        if cs_summary.get("sourceFiles") != cpp_summary.get("sourceFiles"):
            details.append("sourceFiles 不同（C# %r / C++ %r）"
                           % (cs_summary.get("sourceFiles"),
                              cpp_summary.get("sourceFiles")))
    report.add(
        "15", "countsDerived / sourceFiles（§17.69）", not details,
        note="7 个字段 + sourceFiles" + (
            "；取消场景允许 fail 差额 == 取消映射数" if options.expect_cancel else ""),
        details=_cap_details(details),
        warnings=_cap_details(warnings), checks=checks)

    # ------------------------------------------------------------------
    # 规则 16：batch
    # ------------------------------------------------------------------
    details = []
    checks = 0
    if len(cs_data.batch_rows) != 1:
        details.append("C# batch 行数=%d（必须恰好 1）" % len(cs_data.batch_rows))
    if len(cpp_data.batch_rows) != 1:
        details.append("C++ batch 行数=%d（必须恰好 1）" % len(cpp_data.batch_rows))
    if cs_data.batch_rows and cpp_data.batch_rows:
        cs_batch = cs_data.batch_rows[0]
        cpp_batch = cpp_data.batch_rows[0]
        checks += 2
        if cs_batch.get("index") != cpp_batch.get("index"):
            details.append("batch.index 不同（C# %r / C++ %r）"
                           % (cs_batch.get("index"), cpp_batch.get("index")))
        if cs_batch.get("direction") != cpp_batch.get("direction"):
            details.append("batch.direction 不同（C# %r / C++ %r）"
                           % (cs_batch.get("direction"),
                              cpp_batch.get("direction")))
    report.add("16", "batch（index / direction；startedFt 不比）", not details,
               note="batch 行数 C# %d / C++ %d"
                    % (len(cs_data.batch_rows), len(cpp_data.batch_rows)),
               details=_cap_details(details), checks=checks)

    # ------------------------------------------------------------------
    # 规则 17：summary（cancelled / exitCode；durationMs、logLines 不比）
    # ------------------------------------------------------------------
    details = []
    checks = 0
    if len(cs_data.summary_rows) != 1:
        details.append("C# summary 行数=%d（必须恰好 1）" % len(cs_data.summary_rows))
    if len(cpp_data.summary_rows) != 1:
        details.append("C++ summary 行数=%d（必须恰好 1）" % len(cpp_data.summary_rows))
    if cs_summary is not None and cpp_summary is not None:
        if not options.expect_cancel:
            checks += 2
            if cs_summary.get("cancelled") is not False:
                details.append("非取消场景：C# summary.cancelled 必须为 false，实际 %r"
                               % (cs_summary.get("cancelled"),))
            if cpp_summary.get("cancelled") is not False:
                details.append("非取消场景：C++ summary.cancelled 必须为 false，实际 %r"
                               % (cpp_summary.get("cancelled"),))
        # 取消场景不跨侧比 cancelled（C# harness 无 cancel，§17.57 / 任务裁决）
        checks += 1
        cs_code = cs_summary.get("exitCode")
        if cs_code != 0:
            details.append("C# summary.exitCode 必须为 0（harness 总以 0 退出），实际 %r"
                           % (cs_code,))
        checks += 1
        expected_cpp_code = _expected_exit_code(cpp_summary)
        cpp_code = cpp_summary.get("exitCode")
        if cpp_code != expected_cpp_code:
            details.append(
                "C++ summary.exitCode 与自身 summary 推导值不一致（推导 %d，实际 %r）"
                "：cancelled→3；countedAsFailure>0→1；否则 0"
                % (expected_cpp_code, cpp_code))
    report.add(
        "17", "summary（cancelled / exitCode；durationMs、logLines 不比）",
        not details,
        note="取消场景不跨侧比 cancelled" if options.expect_cancel
             else "非取消场景两侧 cancelled 必须均为 false",
        details=_cap_details(details), checks=checks)

    # ------------------------------------------------------------------
    # 规则 18：dir 集合
    # ------------------------------------------------------------------
    details = []
    checks = 0
    for side_name, side_data in (("C#", cs_data), ("C++", cpp_data)):
        seen = {}
        for row in side_data.dir_rows:
            path = normalize_path(row.get("path", ""))
            if path in seen:
                details.append("%s 重复 dir path %r（第 %s 行与第 %s 行）"
                               % (side_name, path, _row_line(seen[path]),
                                  _row_line(row)))
            else:
                seen[path] = row
    cs_dir = {}
    for row in cs_data.dir_rows:
        path = normalize_path(row.get("path", ""))
        if path not in cs_dir:
            cs_dir[path] = row.get("skipReason")
    cpp_dir = {}
    for row in cpp_data.dir_rows:
        path = normalize_path(row.get("path", ""))
        if path not in cpp_dir:
            cpp_dir[path] = row.get("skipReason")
    for path in sorted(set(cs_dir) | set(cpp_dir), key=_path_sort_key):
        checks += 1
        if path not in cpp_dir:
            details.append("仅 C# 输出 dir：%s（skipReason=%r）"
                           % (path, cs_dir[path]))
        elif path not in cs_dir:
            details.append("仅 C++ 输出 dir：%s（skipReason=%r）"
                           % (path, cpp_dir[path]))
        elif cs_dir[path] != cpp_dir[path]:
            details.append("%s：dir.skipReason 不同（C# %r / C++ %r）"
                           % (path, cs_dir[path], cpp_dir[path]))
    report.add("18", "dir 集合（{path: skipReason}）", not details,
               note="dir 行数 C# %d / C++ %d"
                    % (len(cs_data.dir_rows), len(cpp_data.dir_rows)),
               details=_cap_details(details), checks=checks)

    return report, stats


# ---------------------------------------------------------------------------
# 报告渲染
# ---------------------------------------------------------------------------


def _rule_tag(result):
    if result.skipped:
        return "SKIP"
    return "PASS" if result.ok else "FAIL"


def _format_pair_row(title, row):
    if row is None:
        return "    %s: (缺失)" % title
    return ("    %s: action=%r skipReason=%r bytes=%r srcFt=%r "
            "dstExists=%r dstBytes=%r dstFt=%r attempts=%r"
            % (title, row.get("action"), row.get("skipReason"),
               row.get("bytes"), row.get("srcFt"), row.get("dstExists"),
               row.get("dstBytes"), row.get("dstFt"), row.get("attempts")))


def render_report(report, stats, options, stream, sources=None):
    """把 Report / Stats 打印成中文人读报告（最后一行 RESULT）。"""
    write = stream.write
    write("===== JSONL 双向比对（C# harness ↔ C++ headless）=====\n")
    if sources:
        write("C# : %s\n" % sources[0])
        write("C++: %s\n" % sources[1])
    write("源根: %s\n" % options.src_root)
    modes = ["取消场景（--expect-cancel）" if options.expect_cancel else "普通场景"]
    if options.allow_extra_cs_files:
        modes.append("允许 C# 目标多出文件")
    if options.no_dst_listing:
        modes.append("目标清单关闭")
    write("模式: %s\n" % " / ".join(modes))
    write("-" * 70 + "\n")

    for result in report.results:
        head = "[%s] 规则%s %s" % (_rule_tag(result), result.rule, result.title)
        if result.checks:
            head += "（检查 %d 项）" % result.checks
        if result.note:
            head += "：" + result.note
        write(head + "\n")
        for detail in result.details:
            write("      - " + detail + "\n")
        for warning in result.warnings:
            write("      [WARN] " + warning + "\n")

    write("-" * 70 + "\n")

    if options.emit_detail:
        write("----- 配对明细（前 %d 对 file 行）-----\n" % MAX_DETAIL_PAIRS)
        shown = 0
        for path, cs_row, cpp_row in report.detail_pairs[:MAX_DETAIL_PAIRS]:
            shown += 1
            write("%d. %s\n" % (shown, path))
            write(_format_pair_row("C# ", cs_row) + "\n")
            write(_format_pair_row("C++", cpp_row) + "\n")

    write("----- 统计 -----\n")
    write("输入行数: C# %d / C++ %d\n" % (stats.cs_rows, stats.cpp_rows))
    cs_file = stats.cs_kinds.get("file", 0)
    cpp_file = stats.cpp_kinds.get("file", 0)
    write("file 行: C# %d（含 not-visited %d）/ C++ %d（含 not-visited %d）\n"
          % (cs_file, stats.cs_not_visited, cpp_file, stats.cpp_not_visited))
    write("配对 file 行数: %d\n" % stats.pairs)
    write("dir 行: C# %d / C++ %d\n"
          % (stats.cs_kinds.get("dir", 0), stats.cpp_kinds.get("dir", 0)))
    write("batch 行: C# %d / C++ %d；summary 行: C# %d / C++ %d\n"
          % (stats.cs_kinds.get("batch", 0), stats.cpp_kinds.get("batch", 0),
             stats.cs_kinds.get("summary", 0), stats.cpp_kinds.get("summary", 0)))
    if stats.source_enumerated:
        write("源树文件: %d（reparse 目录 %d；两侧都未访问 %d）\n"
              % (stats.source_files, stats.reparse_dirs, stats.both_unvisited))
    else:
        write("源树文件: 未枚举（规则 0 未通过）\n")
    pass_count = sum(1 for item in report.results
                     if item.ok and not item.skipped)
    fail_count = sum(1 for item in report.results
                     if not item.ok and not item.skipped)
    skip_count = sum(1 for item in report.results if item.skipped)
    write("各规则通过数: PASS %d / FAIL %d / SKIP %d\n"
          % (pass_count, fail_count, skip_count))
    write("各规则: " + " | ".join("规则%s %s" % (item.rule, _rule_tag(item))
                                 for item in report.results) + "\n")
    if report.ok:
        write("RESULT: PASS\n")
    else:
        write("RESULT: FAIL (%d 项)\n" % report.fail_count)


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------


def run_compare(cs_path, cpp_path, options, stream=None):
    """读两个 JSONL 并比对；返回退出码（0/1/2）。打印到 stream（默认 stdout）。"""
    stream = stream if stream is not None else sys.stdout
    try:
        cs_rows = load_jsonl(cs_path)
        cpp_rows = load_jsonl(cpp_path)
    except JsonlError as exc:
        sys.stderr.write("ERROR: %s\n" % exc)
        return EXIT_USAGE
    report, stats = compare_jsonl(cs_rows, cpp_rows, options)
    render_report(report, stats, options, stream, sources=(cs_path, cpp_path))
    return EXIT_PASS if report.ok else EXIT_DIFF


def build_parser():
    parser = argparse.ArgumentParser(
        prog="compare.py",
        description="C# harness 与 C++ headless 的 JSONL 双向比对（兼容性基线 §13）。",
        epilog="退出码：0 全部通过；1 有差异；2 用法/文件错误。")
    parser.add_argument("csharp", help="C# harness 输出的 JSONL 文件")
    parser.add_argument("cpp", help="C++ headless 输出的 JSONL 文件")
    parser.add_argument("--src-root", required=True,
                        help="源根目录（规则 9 重算 not-visited 用；只跳过 reparse 目录）")
    parser.add_argument("--dst-cs", required=True,
                        help="C# 侧目标目录（规则 14 目标清单断言）")
    parser.add_argument("--dst-cpp", required=True,
                        help="C++ 侧目标目录（规则 14 目标清单断言）")
    parser.add_argument("--emit-detail", action="store_true",
                        help="打印配对明细（前 %d 行）" % MAX_DETAIL_PAIRS)
    parser.add_argument("--expect-cancel", action="store_true",
                        help="本场景命令行用过 --cancel-after（§17.63 的意图信号；脚本看不到历史命令行）")
    parser.add_argument("--allow-extra-cs-files", action="store_true",
                        help="D37 场景：允许 C# 目标目录多出重复副本（逐条打印，不静默通过；C++ 多出仍失败）")
    parser.add_argument("--no-dst-listing", action="store_true",
                        help="关闭目标目录清单比对（默认开启）")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        sys.stdout.reconfigure(errors="replace")
    except (AttributeError, ValueError):
        pass
    if not os.path.isdir(args.src_root):
        sys.stderr.write("ERROR: 源根目录不存在：%s\n" % args.src_root)
        return EXIT_USAGE
    options = CompareOptions(
        src_root=args.src_root,
        dst_cs=args.dst_cs,
        dst_cpp=args.dst_cpp,
        expect_cancel=args.expect_cancel,
        allow_extra_cs_files=args.allow_extra_cs_files,
        no_dst_listing=args.no_dst_listing,
        emit_detail=args.emit_detail)
    return run_compare(args.csharp, args.cpp, options, stream=sys.stdout)


if __name__ == "__main__":
    sys.exit(main())
