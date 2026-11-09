#pragma once

// ============================================================================
//  copy_callbacks —— 引擎对外的唯一出口（实测：零 HWND / 零 UI 类型）
//
//  为什么单独定义：同一份 `CopyEngine` 要同时服务 GUI（M3）与 `--headless`（M2），
//  后者没有消息循环。所以引擎不得弹窗、不得直连控件，一切外部可见行为都走这四个回调。
//
//  D30 裁决（已定）：**每个最终决策后立即 `onCounts`**，不再沿用 C# 那个
//  「只在 attempt>=maxRetries || success 时通知」的漏洞。
// ============================================================================

#include <atomic>
#include <functional>
#include <string>

#include "copy_options.h"

namespace usbcopy::copy {

// 单个文件的**完整处理结果**（M2-A 起新增）。
//
//  为什么不能只靠 `onDecision`：基线 §13 的 B 表（kind=file）有 13 个字段，
//  其中 `bytes/srcFt/dstExists/dstBytes/dstFt/error` 只有引擎在「决策那一刻」才知道
//  （KeepBoth 的目标路径、取消留下的半成品都依赖它）。让 headless 事后自己回读文件，
//  在 M2-B 的 KeepBoth / 内容去重场景就会算错，所以由引擎直接给出。
//
//  字段取值规则见 §17.48：dst* 一律是**处理完该文件那一刻**目标盘上的真实状态；
//  `action=copy` 时复制已完成，因此与「运行结束时」等价。
struct FileReport {
    std::wstring path;              // 相对源根，'/' 分隔（§13 通则 2）
    DecisionKind kind = DecisionKind::Copy;
    const wchar_t* reason = reason::kNone; // 规范枚举（§13 skipReason 表）
    long long bytes = 0;            // 源文件字节数
    long long srcFt = 0;            // 源 mtime（FILETIME int64）
    bool dstExists = false;         // 目标真实状态（见 §17.48）
    long long dstBytes = 0;
    long long dstFt = 0;
    int attempts = 1;               // C++ 恒为 1（§17.61）
    std::wstring error;             // 仅 Fail / Cancelled 非空
};

struct CopyCallbacks {
    // 日志。文案与 isError 语义见 兼容性基线.md §17.11
    std::function<void(const std::wstring& message, bool isError)> onLog;

    // 字节级进度（C# 没有，见 §17.3）。
    // bytes = 当前文件已写字节；total = 当前文件大小（不是全局累计）
    std::function<void(const std::wstring& relativePath, long long bytes, long long total)> onProgress;

    // 文件计数（对应 C# CountsChanged），每个最终决策后立即触发
    std::function<void(int total, int success, int failure)> onCounts;

    // 轻量决策通知（附录 B 的原始契约；由 `Counters` 内部触发，GUI 可忽略）
    std::function<void(const std::wstring& relativePath, DecisionKind kind, const wchar_t* reason)>
        onDecision;

    // 完整文件事件（M2-A 起 headless 用它输出 kind=file 行）。
    // 与 onDecision 并存：Counters 只负责计数与轻量通知，引擎负责补全落盘结果。
    std::function<void(const FileReport& report)> onFileHandled;

    // 目录被剪枝（§17.67：只有被跳过的目录才产生 dir 行）。
    // reason 取 system-dir / attr-dir / folder-filter / depth-limit / reparse-point
    std::function<void(const std::wstring& relativePath, const wchar_t* reason)>
        onDirectorySkipped;
};

// 计数 + 通知（把 §17.2 的语义与 D30 的修复封在一处，引擎与测试共用）
//
//   Copy           → total+1, success+1
//   Skip(普通)     → 不计数（C# 如此：系统文件/隐藏/关键词/重复/去重都不计）
//   Skip(size-limit→ total+1, failure+1（唯一例外，决策仍是 Skip）
//   Fail           → total+1, failure+1
//   Cancel(D38)    → total+1, failure+1（数字与 C# 对齐），但 action=cancel
//   每次计数变化后立即 onCounts（D30）
//
//  **total 的定义（P0-5）**：total **不是**独立的计数器，恒等于
//      copy + sizeLimitSkip + fail + cancel
//  也就是"得出最终结论的文件数"，不含普通 Skip。三个原子量一起自增，
//  任何时候 `total() == 已发生的 Copy/Skip(size)/Fail/Cancel 次数之和`；
//  测试里有这条不变量断言，别把 total 改成"发现的文件数"。
class Counters {
public:
    explicit Counters(const CopyCallbacks* callbacks = nullptr) : callbacks_(callbacks) {}

    void Bind(const CopyCallbacks* callbacks) { callbacks_ = callbacks; }

    void Reset();

    void Copy(const std::wstring& relativePath);
    void Skip(const std::wstring& relativePath, const wchar_t* reason,
              bool countedAsFailure = false);
    void Fail(const std::wstring& relativePath, const wchar_t* reason);
    // D38：用户取消导致的未完成文件。计数与 Fail 相同，但 action 区分为 cancel
    void Cancel(const std::wstring& relativePath);

    int total() const { return total_.load(); }
    int success() const { return success_.load(); }
    int failure() const { return failure_.load(); }

private:
    void Emit(DecisionKind kind, const std::wstring& relativePath, const wchar_t* reason);
    void Notify();

    const CopyCallbacks* callbacks_ = nullptr;
    std::atomic<int> total_{0};
    std::atomic<int> success_{0};
    std::atomic<int> failure_{0};
};

} // namespace usbcopy::copy
