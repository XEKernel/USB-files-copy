# tools/interop

跨语言一致性的**可复现证据**目录。这里的脚本不参与构建、不是部署依赖，
只用于「改动公共库后证明没破坏与 C# 版的兼容性」。

## verify_dpapi_settings.py

证明 **C# v1.7.x 的 `settings.xml` 与 C++ 版导入、DPAPI 加解密双向兼容**。

| 项 | 说明 |
|---|---|
| 依赖 | Python 3（仅标准库）+ 已构建的 C# 客户端 + `build.ps1 -Tests` 产物 |
| 改动范围 | 临时改写 `Client/bin/Release/settings.xml`，脚本结束**必定还原**（失败也会提示备份位置） |
| 退出码 | 0 全部一致 / 1 字段不一致 / 2 环境缺失 |

```powershell
.\build.ps1 -Tests                         # 需要 m1_tests.exe（含 --import-xml 探针）
python tools\interop\verify_dpapi_settings.py
```

它验证的三件事，缺一不可：

1. **entropy 字节**：脚本先用 Python + ctypes 独立实现 `CryptProtectData`，entropy 用
   `UTF-8("U盘文件复制器_SALT_2024")`（断言 29 字节）→ C# 必须能解开
2. **明文/密文编码**：明文 UTF-8、密文 Base64 —— C# 重新加密后 C++ 必须能解开
3. **字段映射**：C# 写出的 XML 元素名 → C++ `AppSettings` 字段，逐项比对（28 个关键字段）

> 只做「自己加密自己解」的单元测试无法证明兼容性：entropy 写错时两边仍然自洽。
> 所以每次改 `src/common/settings.cpp` 的 DPAPI / XML 导入逻辑后，必须重跑本脚本。

## 注意

- 脚本会让 C# 客户端短暂启动并关闭一次（用于触发 `FormClosing` 写盘），
  期间**不要**手动操作那个窗口。
- 注入值里 `AutoStart=false`、`SaveLocation=0`，避免写注册表自启动项或尝试连服务器。
