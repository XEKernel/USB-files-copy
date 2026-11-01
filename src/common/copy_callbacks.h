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

struct CopyCallbacks {
    // 日志。文案与 isError 语义见 兼容性基线.md §17.11
    std::function<void(const std::wstring& message, bool isError)> onLog;

    // 字节级进度（C# 没有，见 §17.3）。
    // bytes = 当前文件已写字节；total = 当前文件大小（不是全局累计）
    std::function<void(const std::wstring& relativePath, long long bytes, long long total)> onProgress;

    // 文件计数（对应 C# CountsChanged），每个最终决策后立即触发
    std::function<void(int total, int success, int failure)> onCounts;

    // 每文件一个最终决策：headless 直接输出 JSONL，GUI 可忽略
    std::function<void(const std::wstring& relativePath, DecisionKind kind, const wchar_t* reason)>
        onDecision;
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
