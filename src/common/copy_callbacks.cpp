#include "copy_callbacks.h"

namespace usbcopy::copy {

const wchar_t* DecisionKindText(DecisionKind kind) {
    switch (kind) {
    case DecisionKind::Copy: return L"copy";
    case DecisionKind::Skip: return L"skip";
    case DecisionKind::Fail: return L"fail";
    case DecisionKind::NotVisited: return L"not-visited";
    }
    return L"not-visited";
}

void Counters::Reset() {
    total_.store(0);
    success_.store(0);
    failure_.store(0);
    Notify(); // C# ResetCounters 也发一次
}

void Counters::Copy(const std::wstring& relativePath) {
    total_.fetch_add(1);
    success_.fetch_add(1);
    Emit(DecisionKind::Copy, relativePath, reason::kNone);
    Notify();
}

void Counters::Skip(const std::wstring& relativePath, const wchar_t* reason,
                    bool countedAsFailure) {
    if (countedAsFailure) {
        // 超大文件：决策是 Skip，但 C# 计入 total 与 failure（§17.2 唯一例外）
        total_.fetch_add(1);
        failure_.fetch_add(1);
        Emit(DecisionKind::Skip, relativePath, reason);
        Notify();
        return;
    }
    // 其余跳过：不计任何数，也不通知（与 C# 一致——否则 UI 会无端刷新）
    Emit(DecisionKind::Skip, relativePath, reason);
}

void Counters::Fail(const std::wstring& relativePath, const wchar_t* reason) {
    total_.fetch_add(1);
    failure_.fetch_add(1);
    Emit(DecisionKind::Fail, relativePath, reason);
    // D30：C# 在「硬错误第一次就 break」「取消导致的失败」时只加计数器不通知，
    // 这里一律通知，保证 UI 数字与真实结果一致
    Notify();
}

void Counters::Emit(DecisionKind kind, const std::wstring& relativePath, const wchar_t* reason) {
    if (callbacks_ != nullptr && callbacks_->onDecision) {
        callbacks_->onDecision(relativePath, kind, reason == nullptr ? reason::kNone : reason);
    }
}

void Counters::Notify() {
    if (callbacks_ != nullptr && callbacks_->onCounts) {
        callbacks_->onCounts(total_.load(), success_.load(), failure_.load());
    }
}

} // namespace usbcopy::copy
