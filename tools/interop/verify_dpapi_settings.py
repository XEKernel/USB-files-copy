#!/usr/bin/env python3
"""跨语言验证：C# v1.7.x 的 settings.xml ↔ C++ 版导入（含 DPAPI 双向兼容）

为什么这个脚本必须入库
----------------------
DPAPI 兼容性只由三处参数决定：**entropy 字节**（UTF-8 of "U盘文件复制器_SALT_2024"，29 字节）、
**明文编码**（UTF-8）、**输出编码**（Base64）+ CurrentUser scope。任何一处写错，用户从 C# 版升级到
C++ 版就会「令牌没了」。这类错误单元测试抓不到（自己加密自己解一定成功），只有让 **C# 版真的
参与一次加解密** 才能证伪。所以这个脚本是 M1 的唯一外部证据，改动 settings.cpp 的 DPAPI 相关
代码后必须重跑。

它做什么
--------
1. 备份 `Client/bin/Release/settings.xml`
2. 用 Python + ctypes **独立实现** DPAPI（只依赖系统 crypt32），生成 Password/ApiToken 密文
   →  证明 C# 能解开「我们算出来的密文」
3. 写入 XML → 启动 C# 版 → 优雅关闭（WM_CLOSE），让它重新写盘（重新加密）
   →  证明 C# 认可我们的 entropy / 编码
4. 用 `m1_tests.exe --import-xml` 导入 C# 写出的 XML，逐字段比对
   →  证明我们能解开「C# 算出来的密文」，且字段映射无误
5. 无论成功失败都还原 settings.xml（失败则保留 .interop_bak 并提示）

用法
----
    # 先构建两个东西（缺一个脚本会直接报错退出）
    .\\build.ps1                 # → out\\bin\\release\\UsbCopy.exe（不需要，只要 C# 版）
    .\\build.ps1 -Tests          # → out\\bin\\release-tests\\m1_tests.exe

    python tools\\interop\\verify_dpapi_settings.py
    python tools\\interop\\verify_dpapi_settings.py --exe "C:\\path\\U盘文件复制器.exe"

退出码：0 = 全部一致；1 = 有字段不一致；2 = 环境缺失（没构建 C# 版或探针程序）
"""

import argparse
import base64
import ctypes
import os
import shutil
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_EXE = REPO / "Client" / "bin" / "Release" / "U盘文件复制器.exe"
DEFAULT_PROBE = REPO / "out" / "bin" / "release-tests" / "m1_tests.exe"

ENTROPY_TEXT = "U盘文件复制器_SALT_2024"
PASSWORD = "跨语言口令-42"
TOKEN = "tok-中文-abc"

WM_CLOSE = 0x0010

# ---------------------------------------------------------------- DPAPI


class DATA_BLOB(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
user32 = ctypes.WinDLL("user32", use_last_error=True)
WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)


def _blob(data: bytes):
    buffer = ctypes.create_string_buffer(data, len(data))
    return DATA_BLOB(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_char))), buffer


def protect(text: str) -> str:
    """CryptProtectData(CurrentUser) + 固定 entropy + Base64，与 C# ProtectedData 等价。"""
    entropy_bytes = ENTROPY_TEXT.encode("utf-8")
    assert len(entropy_bytes) == 29, f"entropy 长度应为 29，实际 {len(entropy_bytes)}"

    plain, keep_plain = _blob(text.encode("utf-8"))
    entropy, keep_entropy = _blob(entropy_bytes)
    output = DATA_BLOB()
    ok = crypt32.CryptProtectData(
        ctypes.byref(plain),           # pDataIn
        "UsbCopy",                     # szDataDescr
        ctypes.byref(entropy),         # pOptionalEntropy
        None,                          # pvReserved
        None,                          # pPromptStruct
        0x1,                           # CRYPTPROTECT_UI_FORBIDDEN
        ctypes.byref(output),
    )
    if not ok:
        raise OSError(ctypes.get_last_error(), "CryptProtectData 失败")
    data = ctypes.string_at(output.pbData, output.cbData)
    kernel32.LocalFree(output.pbData)
    del keep_plain, keep_entropy
    return base64.b64encode(data).decode("ascii")


def windows_of(pid: int):
    found = []

    def callback(hwnd, _):
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True

    user32.EnumWindows(WNDENUMPROC(callback), 0)
    return found


# ---------------------------------------------------------------- XML 模板

XML_TEMPLATE = """<?xml version="1.0" encoding="utf-8"?>
<AppSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <TargetDirectory>{target}</TargetDirectory>
  <CopyPpt>false</CopyPpt>
  <CopyWord>true</CopyWord>
  <CopyExcel>false</CopyExcel>
  <CopyPdf>true</CopyPdf>
  <CopyImage>false</CopyImage>
  <CopyVideo>true</CopyVideo>
  <CopyAudio>false</CopyAudio>
  <CopyCompressed>true</CopyCompressed>
  <CopyAllFiles>false</CopyAllFiles>
  <UseCustomExtensions>true</UseCustomExtensions>
  <CustomExtensions>abc,def</CustomExtensions>
  <DuplicateFileAction>2</DuplicateFileAction>
  <EnableFileSizeLimit>true</EnableFileSizeLimit>
  <MaxFileSizeMB>321</MaxFileSizeMB>
  <EnableFileNameFilter>true</EnableFileNameFilter>
  <FileNameKeywords>kw1,kw2</FileNameKeywords>
  <EnableFolderNameFilter>false</EnableFolderNameFilter>
  <FolderNameKeywords />
  <CreateDirectoryTree>false</CreateDirectoryTree>
  <LimitDirectoryDepth>true</LimitDirectoryDepth>
  <MaxDirectoryDepth>7</MaxDirectoryDepth>
  <LogSuccess>true</LogSuccess>
  <LogErrors>false</LogErrors>
  <LogNeutral>true</LogNeutral>
  <SaveLogToFile>false</SaveLogToFile>
  <ShowLogInWindow>true</ShowLogInWindow>
  <EnableStopCopyFile>true</EnableStopCopyFile>
  <StopCopyFileName>halt.flag</StopCopyFileName>
  <EnableReverseCopyFile>false</EnableReverseCopyFile>
  <ReverseCopyFileName>reverse.copy</ReverseCopyFileName>
  <EnableSpeedLimit>true</EnableSpeedLimit>
  <SpeedLimitMinutes>11</SpeedLimitMinutes>
  <SpeedLimitIndex>2</SpeedLimitIndex>
  <AutoStart>false</AutoStart>
  <AutoStartHidden>false</AutoStartHidden>
  <Server>
    <ServerAddress>192.0.2.77</ServerAddress>
    <Port>8443</Port>
    <UseHttps>false</UseHttps>
    <Password>{password}</Password>
    <ApiToken>{token}</ApiToken>
    <RemoteRootPath>/</RemoteRootPath>
    <ApiBasePath>/api/file</ApiBasePath>
    <ValidateCertificate>true</ValidateCertificate>
    <TimeoutSeconds>30</TimeoutSeconds>
    <ChunkSizeBytes>1048576</ChunkSizeBytes>
    <MaxRetries>3</MaxRetries>
  </Server>
  <SaveLocation>0</SaveLocation>
  <UseChunkedUpload>true</UseChunkedUpload>
  <ShowTrayIcon>true</ShowTrayIcon>
  <ShowCompletionNotify>false</ShowCompletionNotify>
  <EnableWhitelist>true</EnableWhitelist>
  <WhitelistDriveIds>ABCD1234,EFGH5678</WhitelistDriveIds>
</AppSettings>
"""


def expected_fields(target: str) -> dict:
    """注入值 → 期望 C++ 探针输出（key 为 C# 字段名）。"""
    return {
        "TargetDirectory": target,
        "CopyPpt": "0",
        "CopyWord": "1",
        "CopyExcel": "0",
        "CustomExtensions": "abc,def",
        "DuplicateFileAction": "2",
        "MaxFileSizeMB": "321",
        "FileNameKeywords": "kw1,kw2",
        "CreateDirectoryTree": "0",
        "LimitDirectoryDepth": "1",
        "MaxDirectoryDepth": "7",
        "LogErrors": "0",
        "SaveLogToFile": "0",
        "EnableStopCopyFile": "1",
        "StopCopyFileName": "halt.flag",
        "EnableReverseCopyFile": "0",
        "EnableSpeedLimit": "1",
        "SpeedLimitMinutes": "11",
        "SpeedLimitIndex": "2",
        "ServerAddress": "192.0.2.77",
        "Port": "8443",
        "UseHttps": "0",
        "Password": PASSWORD,
        "ApiToken": TOKEN,
        "SaveLocation": "0",
        "ShowCompletionNotify": "0",
        "EnableWhitelist": "1",
        "WhitelistDriveIds": "ABCD1234,EFGH5678",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="验证 C# settings.xml ↔ C++ 导入与 DPAPI 兼容")
    parser.add_argument("--exe", default=str(DEFAULT_EXE), help="C# 版客户端路径")
    parser.add_argument("--probe", default=str(DEFAULT_PROBE), help="m1_tests.exe 路径")
    parser.add_argument("--work", default=os.path.join(os.environ.get("TEMP", "."), "usbcopy_interop"),
                        help="工作目录（存放临时 XML 与目标目录）")
    args = parser.parse_args()

    exe = Path(args.exe)
    probe = Path(args.probe)
    if not exe.is_file():
        print(f"[环境缺失] C# 客户端不存在：{exe}\n  先构建 C# 版，或用 --exe 指定路径")
        return 2
    if not probe.is_file():
        print(f"[环境缺失] 探针不存在：{probe}\n  先运行：.\\build.ps1 -Tests")
        return 2

    config = exe.parent / "settings.xml"
    backup = Path(str(config) + ".interop_bak")
    work = Path(args.work)
    target = work / "target"
    work.mkdir(parents=True, exist_ok=True)
    target.mkdir(parents=True, exist_ok=True)

    if config.exists():
        shutil.copy2(config, backup)
        print(f"[1/5] 已备份 {config.name} → {backup.name}")

    password_cipher = protect(PASSWORD)
    token_cipher = protect(TOKEN)
    print(f"[2/5] Python 独立 DPAPI 生成密文（Password {len(password_cipher)} 字符）")

    config.write_text(
        XML_TEMPLATE.format(target=target, password=password_cipher, token=token_cipher),
        encoding="utf-8-sig",
    )
    print(f"[3/5] 已写入注入值并启动 C# 版：{exe.name}")

    process = subprocess.Popen([str(exe)], cwd=str(exe.parent))
    time.sleep(3.5)
    handles = windows_of(process.pid)
    if not handles:
        process.kill()
        print("[失败] C# 程序没有出现可见窗口，无法触发保存")
        return 1
    for hwnd in handles:
        user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        process.kill()
        print("[失败] C# 程序关闭超时")
        return 1
    print(f"[4/5] C# 已退出（代码 {process.returncode}），settings.xml 已被它重新写盘")

    copy_for_probe = work / "from_csharp.xml"
    shutil.copy2(config, copy_for_probe)
    result = subprocess.run(
        [str(probe), "--import-xml", str(copy_for_probe)],
        capture_output=True,
        cwd=str(work),
    )
    text = result.stdout.decode("utf-8", errors="replace")
    if result.returncode != 0:
        print("[失败] 探针导入失败：")
        print(text)
        return 1

    fields = {}
    for line in text.splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            fields[key.strip()] = value.strip()

    expected = expected_fields(str(target))
    failures = []
    print("[5/5] 逐字段比对：")
    for key, want in expected.items():
        got = fields.get(key, "<缺失>")
        ok = got == want
        print(f"  {'OK  ' if ok else 'FAIL'} {key}={got!r}" + ("" if ok else f"  期望 {want!r}"))
        if not ok:
            failures.append(key)

    if backup.exists():
        shutil.copy2(backup, config)
        backup.unlink()
        print(f"[还原] settings.xml 已还原（删除 {backup.name}）")

    total = len(expected)
    print(f"\n比对 {total} 项，失败 {len(failures)} 项")
    if failures:
        print("失败字段：" + ", ".join(failures))
        return 1
    print("结论：DPAPI entropy / 明文编码 / Base64 / scope 与字段映射双向兼容")
    return 0


if __name__ == "__main__":
    sys.exit(main())
