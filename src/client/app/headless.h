#pragma once

// ============================================================================
//  headless —— `--headless` 命令行入口（M2-A）
//
//  规格：交接文档 §6.1 / 基线 §17.12 / §17.33 / §17.37 / §17.49–17.50 /
//        §17.58–17.59 / §17.64。
//
//  开跑时序（**不许调换**）：
//      解析参数 → 校验盘/目录（失败 exit 2）→ GetDriveId → RecordDriveInsertion
//      → 遍历 + CopyFileExW 真复制 → 流式输出 JSONL
//  `--dry-run` 也走这条链（RecordDriveInsertion 照调，但不落盘、不限速睡眠）。
//
//  输出：首行 kind=batch、末行 kind=summary；中间 file/dir/log 按访问顺序交错。
// ============================================================================

#include <string>
#include <vector>

namespace usbcopy::app {

// 是否走控制台路径。`--dry-run` 隐含 headless（基线 §17.19）。
bool WantsHeadless(const std::vector<std::wstring>& args);

// 执行一次 headless 复制，返回进程退出码（0/1/2/3/4，§17.33）。
// 不接受命令行以外的输入；不弹窗、不建窗口、不初始化 ComCtl。
int RunHeadless(const std::vector<std::wstring>& args);

} // namespace usbcopy::app
