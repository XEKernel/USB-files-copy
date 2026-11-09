#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""compare.py 的单元测试（纯标准库 unittest）。

覆盖 §13 的行级 schema、归一化、逐文件规则 4–8、not-visited 重算、dir 集合、
countsDerived / sourceFiles、batch / summary / exitCode、取消场景、目标目录清单、
D37 的 --allow-extra-cs-files 等。**不依赖真实 U 盘 / 网络 / harness**，全部用
tempfile.TemporaryDirectory() 构造源树、目标目录与 JSONL 样本。

用法：python tools\\compare\\test_compare.py
"""

import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import compare  # noqa: E402  （同目录模块）


# ---------------------------------------------------------------------------
# JSONL 行构造器（键序严格按 §13 五张表，便于测「键序」规则）
# ---------------------------------------------------------------------------


def batch_row(index=1, direction="forward", drive="", started_ft=1000):
    return {
        "kind": "batch",
        "index": index,
        "direction": direction,
        "drive": drive,
        "startedFt": started_ft,
    }


def file_row(path, action="copy", skip_reason=None, final_action=None,
             final_skip_reason=None, bytes_=3, src_ft=100, dst_exists=True,
             dst_bytes=3, dst_ft=200, attempts=1, error=""):
    if skip_reason is None:
        if action == "copy":
            skip_reason = ""
        elif action == "not-visited":
            skip_reason = "not-visited"
        elif action == "cancel":
            skip_reason = "cancelled"
        else:
            skip_reason = "copy-error"
    if final_action is None:
        final_action = action
    if final_skip_reason is None:
        final_skip_reason = skip_reason
    return {
        "kind": "file",
        "path": path,
        "action": action,
        "skipReason": skip_reason,
        "finalAction": final_action,
        "finalSkipReason": final_skip_reason,
        "bytes": bytes_,
        "srcFt": src_ft,
        "dstExists": dst_exists,
        "dstBytes": dst_bytes,
        "dstFt": dst_ft,
        "attempts": attempts,
        "error": error,
    }


def dir_row(path, skip_reason="folder-filter"):
    return {"kind": "dir", "path": path, "skipReason": skip_reason}


def summary_row(copy=1, skip=1, fail=0, size_limit=0, counted_as_failure=None,
                total_counted=None, not_visited=0, source_files=None, total=1,
                success=1, failure=0, cancelled=False, exit_code=0,
                target="D:\\backup", counts_last=None, duration_ms=5,
                log_lines=0, files_decided=None, harness_order=False):
    if counted_as_failure is None:
        counted_as_failure = fail + size_limit
    if total_counted is None:
        total_counted = copy + counted_as_failure
    if source_files is None:
        source_files = copy + skip + fail + not_visited
    if files_decided is None:
        files_decided = copy + skip + fail
    if counts_last is None:
        counts_last = [total, success, failure]
    counts = {
        "copy": copy,
        "skip": skip,
        "fail": fail,
        "sizeLimit": size_limit,
        "countedAsFailure": counted_as_failure,
        "totalCounted": total_counted,
        "notVisited": not_visited,
    }
    if harness_order:
        # harness Program.cs 的真实键序：durationMs 在 filesDecided 之前
        return {
            "kind": "summary",
            "total": total,
            "success": success,
            "failure": failure,
            "cancelled": cancelled,
            "countsLast": counts_last,
            "countsDerived": counts,
            "sourceFiles": source_files,
            "durationMs": duration_ms,
            "filesDecided": files_decided,
            "logLines": log_lines,
            "target": target,
            "exitCode": exit_code,
        }
    return {
        "kind": "summary",
        "total": total,
        "success": success,
        "failure": failure,
        "cancelled": cancelled,
        "countsLast": counts_last,
        "countsDerived": counts,
        "sourceFiles": source_files,
        "filesDecided": files_decided,
        "durationMs": duration_ms,
        "logLines": log_lines,
        "target": target,
        "exitCode": exit_code,
    }


def base_cs_rows():
    """./src 有 a.txt(3B)/b.log(3B)；C# 侧 copy a.txt（D8：dstFt != srcFt）。"""
    return [
        batch_row(),
        file_row("a.txt", dst_ft=200),
        file_row("b.log", action="skip", skip_reason="name-filter",
                 dst_exists=False, dst_bytes=0, dst_ft=0),
        dir_row("skipdir", "folder-filter"),
        summary_row(target="C:\\dst-cs"),
    ]


def base_cpp_rows():
    return [
        batch_row(),
        file_row("a.txt", dst_ft=100),  # C++ 写回源 mtime（D8 修复）
        file_row("b.log", action="skip", skip_reason="name-filter",
                 dst_exists=False, dst_bytes=0, dst_ft=0),
        dir_row("skipdir", "folder-filter"),
        summary_row(target="C:\\dst-cpp"),
    ]


def find_file(rows, path):
    for row in rows:
        if row.get("kind") == "file" and row.get("path") == path:
            return row
    raise AssertionError("样本里没有 file 行：%s" % path)


def find_summary(rows):
    for row in reversed(rows):
        if row.get("kind") == "summary":
            return row
    raise AssertionError("样本里没有 summary 行")


def fail_cs_rows(dst_exists=True, dst_bytes=50, dst_ft=600):
    return [
        batch_row(),
        file_row("a.txt", action="fail", dst_exists=dst_exists,
                 dst_bytes=dst_bytes, dst_ft=dst_ft, error="denied"),
        summary_row(copy=0, skip=0, fail=1, total=1, success=0, failure=1,
                    exit_code=0),
    ]


def fail_cpp_rows(dst_exists=True, dst_bytes=60, dst_ft=700):
    return [
        batch_row(),
        file_row("a.txt", action="fail", dst_exists=dst_exists,
                 dst_bytes=dst_bytes, dst_ft=dst_ft, error="denied"),
        summary_row(copy=0, skip=0, fail=1, total=1, success=0, failure=1,
                    exit_code=1),
    ]


def cancel_cs_rows(bytes_=100, dst_bytes=50, cancelled=False, exit_code=0):
    return [
        batch_row(),
        file_row("big.bin", action="fail", bytes_=bytes_, src_ft=500,
                 dst_exists=True, dst_bytes=dst_bytes, dst_ft=600,
                 error="cancel"),
        summary_row(copy=0, skip=0, fail=1, total=0, success=0, failure=1,
                    cancelled=cancelled, exit_code=exit_code),
    ]


def cancel_cpp_rows(bytes_=100, dst_bytes=40, cancelled=True, exit_code=3,
                    action="cancel"):
    skip_reason = None if action == "cancel" else "copy-error"
    # C++ 的真实语义（headless.cpp）：cancel 是独立决策，不进 countsDerived.fail，
    # 但计入 countedAsFailure（§17.32：取消也计失败）。C# 则把它记成 fail。
    return [
        batch_row(),
        file_row("big.bin", action=action, skip_reason=skip_reason,
                 bytes_=bytes_, src_ft=500, dst_exists=True,
                 dst_bytes=dst_bytes, dst_ft=0, error="cancelled"),
        summary_row(copy=0, skip=0, fail=0, size_limit=0,
                    counted_as_failure=1, total_counted=1, source_files=1,
                    total=1, success=0, failure=1,
                    cancelled=cancelled, exit_code=exit_code),
    ]


# ---------------------------------------------------------------------------
# 临时夹具
# ---------------------------------------------------------------------------


class Case(object):
    """一次测试用的临时 src / dst-cs / dst-cpp / JSONL 文件。"""

    _counter = 0

    def __init__(self):
        self._tmp = tempfile.TemporaryDirectory(prefix="usbcopy_compare_test_")
        self.root = self._tmp.name
        self.src = os.path.join(self.root, "src")
        self.dst_cs = os.path.join(self.root, "dst_cs")
        self.dst_cpp = os.path.join(self.root, "dst_cpp")
        for path in (self.src, self.dst_cs, self.dst_cpp):
            os.makedirs(path)

    def close(self):
        self._tmp.cleanup()

    @staticmethod
    def _write(path, data):
        parent = os.path.dirname(path)
        if parent and not os.path.isdir(parent):
            os.makedirs(parent)
        with open(path, "wb") as handle:
            handle.write(data)

    def src_file(self, relative, data=b"abc"):
        self._write(os.path.join(self.src, relative.replace("/", os.sep)), data)

    def dst_file(self, side, relative, data=b"abc"):
        root = self.dst_cs if side == "cs" else self.dst_cpp
        self._write(os.path.join(root, relative.replace("/", os.sep)), data)

    def write_text(self, name, text):
        Case._counter += 1
        path = os.path.join(self.root, "%s_%d.txt" % (name, Case._counter))
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        return path

    def write_jsonl(self, rows, name):
        text = "".join(json.dumps(row, ensure_ascii=False,
                                  separators=(",", ":")) + "\n" for row in rows)
        return self.write_text(name, text)

    def run(self, cs_rows, cpp_rows, **options):
        cs_path = self.write_jsonl(cs_rows, "cs")
        cpp_path = self.write_jsonl(cpp_rows, "cpp")
        opts = compare.CompareOptions(
            src_root=self.src, dst_cs=self.dst_cs, dst_cpp=self.dst_cpp,
            **options)
        stream = io.StringIO()
        code = compare.run_compare(cs_path, cpp_path, opts, stream=stream)
        return code, stream.getvalue()


class CompareTestBase(unittest.TestCase):
    def setUp(self):
        self.case = Case()

    def tearDown(self):
        self.case.close()

    def make_base_files(self):
        """与 base_*_rows() 对应的磁盘状态。"""
        self.case.src_file("a.txt", b"abc")
        self.case.src_file("b.log", b"log")
        self.case.dst_file("cs", "a.txt", b"abc")
        self.case.dst_file("cpp", "a.txt", b"abc")

    def assert_pass(self, code, output):
        self.assertEqual(code, 0, output)
        self.assertIn("RESULT: PASS", output)

    def assert_fail(self, code, output, rule=None):
        self.assertEqual(code, 1, output)
        self.assertIn("RESULT: FAIL", output)
        if rule is not None:
            self.assertIn("FAIL] 规则%s" % rule, output)


# ---------------------------------------------------------------------------
# 测试
# ---------------------------------------------------------------------------


class CompareTests(CompareTestBase):

    # ---- 入口 / 读取 ----

    def test_help_exits_zero(self):
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            with self.assertRaises(SystemExit) as raised:
                compare.main(["--help"])
        self.assertEqual(raised.exception.code, 0)
        self.assertIn("--expect-cancel", stream.getvalue())

    def test_load_jsonl_ignores_blank_lines_and_keeps_line_numbers(self):
        path = self.case.write_text("rows", '\n{"a":1}\n\n{"b":2}\n\n')
        rows = compare.load_jsonl(path)
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0].line, 2)
        self.assertEqual(rows[1].line, 4)
        self.assertEqual(rows[0], {"a": 1})

    def test_load_jsonl_invalid_json_raises(self):
        path = self.case.write_text("bad", "{not json}\n")
        with self.assertRaises(compare.JsonlError):
            compare.load_jsonl(path)

    def test_load_jsonl_non_object_flagged_as_schema_error(self):
        path = self.case.write_text("array", "[1,2]\n")
        rows = compare.load_jsonl(path)
        errors, warnings = compare.validate_records(rows, "C#")
        self.assertTrue(errors, "非 object 行必须报 schema error")
        self.assertIn("不是 JSON object", errors[0])

    def test_run_compare_invalid_json_returns_exit_2(self):
        bad = self.case.write_text("bad", "{oops}\n")
        cpp = self.case.write_jsonl(base_cpp_rows(), "cpp")
        opts = compare.CompareOptions(self.case.src, self.case.dst_cs,
                                      self.case.dst_cpp)
        stream = io.StringIO()
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code = compare.run_compare(bad, cpp, opts, stream=stream)
        self.assertEqual(code, 2)

    # ---- 合法样例 ----

    def test_valid_sample_passes(self):
        self.make_base_files()
        code, out = self.case.run(base_cs_rows(), base_cpp_rows())
        self.assert_pass(code, out)
        self.assertIn("[PASS] 规则4", out)
        self.assertIn("配对 file 行数: 2", out)
        self.assertIn("dir 行: C# 1 / C++ 1", out)

    def test_emit_detail_lists_pairs(self):
        self.make_base_files()
        code, out = self.case.run(base_cs_rows(), base_cpp_rows(),
                                  emit_detail=True)
        self.assert_pass(code, out)
        self.assertIn("配对明细", out)
        self.assertIn("C# : action='copy'", out)

    # ---- 规则 0：schema ----

    def test_missing_key_fails(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        del find_file(cs_rows, "a.txt")["dstFt"]
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("缺少：dstFt", out)

    def test_wrong_key_order_fails(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        row = find_file(cs_rows, "a.txt")
        items = list(row.items())
        items[2], items[3] = items[3], items[2]  # 交换 action / skipReason
        row.clear()
        row.update(items)
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("键序/字段不符", out)

    def test_int_field_rejects_bool(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "a.txt")["dstFt"] = True
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("类型应为 int", out)

    def test_dstexists_must_be_bool(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        find_file(cs_rows, "a.txt")["dstExists"] = 1
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("类型应为 bool", out)

    def test_first_line_must_be_batch(self):
        self.make_base_files()
        cs_rows = base_cs_rows()[1:]
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("首行必须是 kind=batch", out)

    def test_last_line_must_be_summary(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()[:-1]
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("末行必须是 kind=summary", out)

    def test_duplicate_file_path_fails(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        cs_rows.insert(-1, file_row("a.txt"))
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "3")
        self.assertIn("重复 file path", out)

    # ---- 规则 4–8 ----

    def test_action_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "b.log")["action"] = "fail"
        find_file(cpp_rows, "b.log")["skipReason"] = "copy-error"
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "4")
        self.assertIn("action 不同", out)

    def test_skipreason_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "b.log")["skipReason"] = "size-limit"
        find_file(cpp_rows, "b.log")["finalSkipReason"] = "size-limit"
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "4")
        self.assertIn("skipReason 不同", out)

    def test_cpp_attempts_two_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "a.txt")["attempts"] = 2
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "5")
        self.assertIn("C++ attempts=2", out)

    def test_csharp_attempts_two_passes(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        find_file(cs_rows, "a.txt")["attempts"] = 2
        code, out = self.case.run(cs_rows, base_cpp_rows())
        self.assert_pass(code, out)

    def test_bytes_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "a.txt")["bytes"] = 4
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "6")
        self.assertIn("bytes 不同", out)

    def test_fail_pair_compares_only_dstexists(self):
        self.make_base_files()
        self.case.src_file("a.txt", b"abc")
        # dstBytes / dstFt 两侧不同也无所谓（fail 只比 dstExists）
        code, out = self.case.run(fail_cs_rows(dst_exists=True, dst_bytes=50,
                                               dst_ft=600),
                                  fail_cpp_rows(dst_exists=True, dst_bytes=60,
                                                dst_ft=700))
        self.assert_pass(code, out)

    def test_fail_pair_dstexists_mismatch_fails(self):
        self.make_base_files()
        code, out = self.case.run(fail_cs_rows(dst_exists=True),
                                  fail_cpp_rows(dst_exists=False))
        self.assert_fail(code, out, "7")
        self.assertIn("dstExists 不同", out)

    def test_skip_pair_compares_all_dst_fields(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "b.log")["dstFt"] = 9
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "7")
        self.assertIn("dstFt 不同", out)

    def test_d8_csharp_dstft_must_differ(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        find_file(cs_rows, "a.txt")["dstFt"] = 100  # == srcFt
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "8")
        self.assertIn("C# 未偏离 D8", out)

    def test_d8_cpp_dstft_must_equal_src(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_file(cpp_rows, "a.txt")["dstFt"] = 999
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "8")
        self.assertIn("C++ 未写回源 mtime", out)

    def test_d8_copy_with_missing_target_fails(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        cpp_rows = base_cpp_rows()
        find_file(cs_rows, "a.txt")["dstExists"] = False
        find_file(cs_rows, "a.txt")["dstBytes"] = 0
        find_file(cpp_rows, "a.txt")["dstExists"] = False
        find_file(cpp_rows, "a.txt")["dstBytes"] = 0
        code, out = self.case.run(cs_rows, cpp_rows, no_dst_listing=True)
        self.assert_fail(code, out, "8")
        self.assertIn("目标不存在", out)

    # ---- 规则 9：not-visited 重算 ----

    def test_not_visited_recomputed_sets_equal(self):
        self.make_base_files()
        self.case.src_file("c.txt", b"ccc")  # 两侧都没输出 c.txt
        code, out = self.case.run(base_cs_rows(), base_cpp_rows())
        self.assert_pass(code, out)
        self.assertIn("两侧都未访问 1", out)

    def test_csharp_not_visited_row_is_stripped(self):
        self.make_base_files()
        self.case.src_file("c.txt", b"ccc")
        cs_rows = base_cs_rows()
        cs_rows.insert(-1, file_row("c.txt", action="not-visited",
                                    dst_exists=False, dst_bytes=0, dst_ft=0,
                                    attempts=0))
        code, out = self.case.run(cs_rows, base_cpp_rows())
        self.assert_pass(code, out)
        self.assertIn("C# 未访问 1 / C++ 未访问 1", out)

    def test_not_visited_set_mismatch_fails(self):
        self.make_base_files()
        self.case.src_file("c.txt", b"ccc")
        cs_rows = base_cs_rows()
        cs_rows.insert(-1, file_row("c.txt", dst_ft=200))
        self.case.dst_file("cs", "c.txt", b"ccc")
        # C++ 不输出 c.txt → C# 已决策、C++ 未访问
        code, out = self.case.run(cs_rows, base_cpp_rows())
        self.assert_fail(code, out, "9")
        self.assertIn("C++ 未访问 / C# 已访问", out)

    # ---- 规则 18 / 15 / 16 / 17 ----

    def test_dir_skipreason_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_dir = None
        for row in cpp_rows:
            if row.get("kind") == "dir":
                find_dir = row
        self.assertIsNotNone(find_dir)
        find_dir["skipReason"] = "system-dir"
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "18")
        self.assertIn("dir.skipReason 不同", out)

    def test_counts_derived_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_summary(cpp_rows)["countsDerived"]["copy"] = 2
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "15")
        self.assertIn("countsDerived.copy 不同", out)

    def test_source_files_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        find_summary(cpp_rows)["sourceFiles"] = 3
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "15")
        self.assertIn("sourceFiles 不同", out)

    def test_batch_index_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        cpp_rows[0]["index"] = 2
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "16")
        self.assertIn("batch.index 不同", out)

    def test_batch_direction_mismatch_fails(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        cpp_rows[0]["direction"] = "reverse"
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "16")
        self.assertIn("batch.direction 不同", out)

    def test_cpp_exitcode_must_match_derived(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        cpp_rows = base_cpp_rows()
        # 两侧 countsDerived 相同且 countedAsFailure=1 → C++ 应 exitCode 1
        for rows in (cs_rows, cpp_rows):
            summary = find_summary(rows)
            summary["countsDerived"]["copy"] = 1
            summary["countsDerived"]["skip"] = 0
            summary["countsDerived"]["fail"] = 1
            summary["countsDerived"]["countedAsFailure"] = 1
            summary["countsDerived"]["totalCounted"] = 2
        find_summary(cpp_rows)["exitCode"] = 0
        code, out = self.case.run(cs_rows, cpp_rows, no_dst_listing=True)
        self.assert_fail(code, out, "17")
        self.assertIn("推导 1", out)

    def test_csharp_exitcode_must_be_zero(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        find_summary(cs_rows)["exitCode"] = 1
        code, out = self.case.run(cs_rows, base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_fail(code, out, "17")
        self.assertIn("C# summary.exitCode 必须为 0", out)

    def test_non_cancel_cancelled_must_be_false(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        cpp_rows = base_cpp_rows()
        find_summary(cs_rows)["cancelled"] = True
        summary = find_summary(cpp_rows)
        summary["cancelled"] = True
        summary["exitCode"] = 3  # 自身自洽，但非取消场景不允许 cancelled=true
        code, out = self.case.run(cs_rows, cpp_rows, no_dst_listing=True)
        self.assert_fail(code, out, "17")
        self.assertIn("cancelled 必须为 false", out)

    # ---- 取消场景 ----

    def _make_cancel_files(self):
        self.case.src_file("big.bin", b"x" * 100)
        self.case.dst_file("cs", "big.bin", b"x" * 50)
        self.case.dst_file("cpp", "big.bin", b"x" * 40)

    def test_cancel_scenario_passes(self):
        self._make_cancel_files()
        code, out = self.case.run(cancel_cs_rows(), cancel_cpp_rows(),
                                  expect_cancel=True)
        self.assert_pass(code, out)
        self.assertIn("[PASS] 规则11", out)
        self.assertIn("[PASS] 规则12", out)
        self.assertIn("fail ↔ C++ cancel 映射：1 处", out)

    def test_cancel_without_expect_flag_fails(self):
        self._make_cancel_files()
        code, out = self.case.run(cancel_cs_rows(), cancel_cpp_rows())
        self.assert_fail(code, out, "11")
        self.assertIn("未传 --expect-cancel", out)

    def test_cancel_without_cancel_action_fails(self):
        self._make_cancel_files()
        code, out = self.case.run(cancel_cs_rows(),
                                  cancel_cpp_rows(action="fail"),
                                  expect_cancel=True)
        self.assert_fail(code, out, "11")
        self.assertIn("没有任何 action=cancel", out)

    def test_cancel_dstbytes_out_of_range_fails(self):
        self._make_cancel_files()
        code, out = self.case.run(cancel_cs_rows(),
                                  cancel_cpp_rows(dst_bytes=100),
                                  expect_cancel=True)
        self.assert_fail(code, out, "12")
        self.assertIn("半成品", out)

    def test_cancel_ignores_cross_side_bytes(self):
        self._make_cancel_files()
        # bytes / dstBytes 两侧故意不同：取消场景不跨侧比
        code, out = self.case.run(
            cancel_cs_rows(bytes_=101, dst_bytes=70),
            cancel_cpp_rows(bytes_=100, dst_bytes=40),
            expect_cancel=True)
        self.assert_pass(code, out)

    def test_cancel_auto_skips_dst_listing(self):
        self._make_cancel_files()  # 目标半成品大小本来就不同（50 vs 40）
        code, out = self.case.run(cancel_cs_rows(), cancel_cpp_rows(),
                                  expect_cancel=True, no_dst_listing=False)
        self.assert_pass(code, out)
        self.assertIn("[SKIP] 规则14", out)
        self.assertIn("取消场景", out)

    # ---- 规则 14：目标目录清单 ----

    def test_target_csharp_extra_allowed_with_flag(self):
        self.make_base_files()
        self.case.dst_file("cs", "a (1).docx", b"docx")
        code, out = self.case.run(base_cs_rows(), base_cpp_rows(),
                                  allow_extra_cs_files=True)
        self.assert_pass(code, out)
        self.assertIn("a (1).docx", out)
        self.assertIn("允许", out)

    def test_target_csharp_extra_fails_without_flag(self):
        self.make_base_files()
        self.case.dst_file("cs", "a (1).docx", b"docx")
        code, out = self.case.run(base_cs_rows(), base_cpp_rows())
        self.assert_fail(code, out, "14")
        self.assertIn("C# 目标多出文件", out)

    def test_target_cpp_extra_fails_even_with_flag(self):
        self.make_base_files()
        self.case.dst_file("cpp", "extra.bin", b"x")
        code, out = self.case.run(base_cs_rows(), base_cpp_rows(),
                                  allow_extra_cs_files=True)
        self.assert_fail(code, out, "14")
        self.assertIn("C++ 目标多出文件", out)

    def test_target_size_mismatch_fails(self):
        self.make_base_files()
        self.case.dst_file("cpp", "a.txt", b"abcd")  # 4B != 3B
        code, out = self.case.run(base_cs_rows(), base_cpp_rows())
        self.assert_fail(code, out, "14")
        self.assertIn("大小不同", out)

    def test_copy_row_missing_target_fails(self):
        self.case.src_file("a.txt", b"abc")
        self.case.src_file("b.log", b"log")
        self.case.dst_file("cs", "a.txt", b"abc")
        # C++ 目标目录里没有 a.txt
        code, out = self.case.run(base_cs_rows(), base_cpp_rows())
        self.assert_fail(code, out, "14")
        self.assertIn("action=copy 但目标不存在", out)

    def test_no_dst_listing_skips_target_checks(self):
        self.make_base_files()
        self.case.dst_file("cs", "a (1).docx", b"docx")
        code, out = self.case.run(base_cs_rows(), base_cpp_rows(),
                                  no_dst_listing=True)
        self.assert_pass(code, out)
        self.assertIn("[SKIP] 规则14", out)

    # ---- 已知生产端偏差：harness 的 summary 键序 ----

    def test_csharp_harness_summary_order_accepted_with_warning(self):
        self.make_base_files()
        cs_rows = base_cs_rows()
        cs_rows[-1] = summary_row(target="C:\\dst-cs", harness_order=True)
        code, out = self.case.run(cs_rows, base_cpp_rows())
        self.assert_pass(code, out)
        self.assertIn("[WARN]", out)
        self.assertIn("harness 历史键序", out)

    def test_cpp_summary_order_must_follow_table(self):
        self.make_base_files()
        cpp_rows = base_cpp_rows()
        cpp_rows[-1] = summary_row(target="C:\\dst-cpp", harness_order=True)
        code, out = self.case.run(base_cs_rows(), cpp_rows,
                                  no_dst_listing=True)
        self.assert_fail(code, out, "0")
        self.assertIn("键序/字段不符", out)

    # ---- 磁盘枚举（规则 9/14 的底层） ----

    def test_enumerate_source_files_normalizes_and_skips_nothing(self):
        self.case.src_file("a.txt", b"1")
        self.case.src_file("sub/b.log", b"22")
        files, reparse, errors = compare.enumerate_source_files(self.case.src)
        self.assertFalse(errors)
        self.assertEqual(files, {"a.txt", "sub/b.log"})
        self.assertEqual(reparse, [])

    def test_list_target_files_relative_and_sizes(self):
        self.case.dst_file("cs", "a.txt", b"abc")
        self.case.dst_file("cs", "deep/b.bin", b"12345")
        listing, errors = compare.list_target_files(self.case.dst_cs)
        self.assertFalse(errors)
        self.assertEqual(listing, {"a.txt": 3, "deep/b.bin": 5})


if __name__ == "__main__":
    program = unittest.main(exit=False, verbosity=2)
    if program.result.wasSuccessful():
        print("OK")
        sys.exit(0)
    sys.exit(1)
