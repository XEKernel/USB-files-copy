#pragma once

// ============================================================================
//  copy_engine —— 复制引擎主体（M2-A：遍历 + 真复制 + 计数 + 落盘结果）
//
//  行为契约（逐条来自源码，不是设计选择）：
//    · 遍历顺序：先本目录文件，再子目录递归（CopyEngine.cs:320-338）
//    · 文件判定顺序：见 file_filter.h（系统文件 → 标记 → 属性 → reparse →
//      文件名关键词 → 大小限制）
//    · 目录判定顺序：系统目录名 → System|Hidden → reparse；然后目录关键词过滤
//      （C# ProcessSubdirectoriesAsync）
//    · 深度 guard：在**要不要递归**之前判 `currentDepth >= MaxDirectoryDepth`
//      （§17.24）；根目录 currentDepth=0
//    · 通配符：同一目录内对整个模式集**取并集去重**（D37 修复，§17.28）
//    · 计数：用 `Counters`（D30 语义），total == success + failure 不变量（§17.31）
//    · 复制：`CopyFileExW` + `SetFileTime` 写回源 mtime（D8 裁决）
//    · 空目录：不建（D7；只有写文件时才建父目录）
//
//  M2-A 的边界（交接文档 §6.4，**不要**顺手做）：
//    · 冲突策略只实现 Skip（duplicate-skip）；其余三种由调用方校验并拒绝
//    · 不做内容去重（compareContent）、不做限速、不做反向复制、不做目录树
//    · `dryRun=true` 只判定不落盘（§17.41），dst* 读运行前状态
//    · 取消：`cancelAfterMs > 0` 时由 CopyFileExW 的进度回调中断当前文件，
//      已复制的字节保留（半成品不删，§17.4 / D38）；其余检查点见 §17.4
//
//  M2-B/C 会在此文件继续加：冲突策略、内容去重、限速、目录树。
// ============================================================================

#include <string>

#include "copy_callbacks.h"
#include "copy_options.h"

namespace usbcopy::copy {

struct EngineParams {
    // 源根。绝对路径（末尾带不带分隔符都可以，引擎自己归一化）
    std::wstring sourceRoot;
    // 目标根。绝对路径；文件落点是 targetRoot\[relativeTargetDir\]\path
    std::wstring targetRoot;
    // 正向产品传「卷标_序列号」；`--headless` / harness 传空串（文件直接落在目标根）
    std::wstring relativeTargetDir;
    // 反向复制（M2-E）：判定与目标路径语义见 §17.7/§17.59；M2-A 传 false
    bool reverseCopy = false;
    // 只判定不落盘（§17.41）
    bool dryRun = false;
    // >0：从 RunCopyEngine 进入开始计时，到点取消（§17.33 计时起点）
    long long cancelAfterMs = 0;
};

struct EngineStats {
    int total = 0;        // 引擎上报值（Counters）
    int success = 0;
    int failure = 0;
    bool cancelled = false;
    int filesDecided = 0; // 产生过 onFileHandled 的文件数（不含 not-visited）
    int sourceFiles = 0;  // 源树文件总数（独立扫描，只跳过 reparse 目录；§13 D 表）
    int logLines = 0;     // onLog 触发次数
};

// 同步执行一次复制。不抛异常；失败通过 onFileHandled(kind=Fail) + onLog 表达。
EngineStats RunCopyEngine(const EngineParams& params, const CopyOptions& options,
                          const CopyCallbacks& callbacks);

// ---- 限速窗口（§17.49 / D36）----
//
// M2-A 还不会真正限速（M2-C），但**开跑时序**必须保留：headless 在遍历前
// 调 RecordDriveInsertion，否则 M2-C 一接上限速就是「静默失效」。
// 表放在进程级（等价 C# 的 `_driveInsertionTimes` 实例字段）。
void RecordDriveInsertion(const std::wstring& driveId);
void ForgetDriveInsertion(const std::wstring& driveId);
bool HasDriveInsertion(const std::wstring& driveId);
void ResetDriveInsertions();

} // namespace usbcopy::copy
