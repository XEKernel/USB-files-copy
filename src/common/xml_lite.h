#pragma once

// ============================================================================
//  xml_lite —— 只够解析 C# XmlSerializer 输出的最小 XML 解析器
//
//  支持：元素嵌套、文本节点、5 个预定义实体 + 数字实体、XML 声明、注释、CDATA、
//        自闭合元素、属性（解析后丢弃，因为 XmlSerializer 会输出 xmlns:xsi 等）
//  不支持：DTD、处理指令（声明除外）、命名空间语义、XPath、实体声明
//
//  用途：M1 一次性导入 C# 版 settings.xml（见 兼容性基线.md §1.4(d)）
// ============================================================================

#include <string>
#include <vector>

namespace usbcopy::xml {

struct Node {
    std::wstring name;
    std::wstring text; // 直接文本（不含子元素文本），已解实体
    std::vector<Node> children;

    // 按名字找第一个直接子元素（大小写敏感，与 XmlSerializer 一致）
    const Node* Child(const std::wstring& childName) const;
    // 子元素文本（去首尾空白）；不存在返回空串
    std::wstring ChildText(const std::wstring& childName) const;
};

// 解析整篇文档，root 为根元素
bool Parse(const std::wstring& xml, Node& root, std::wstring* error);

// 从文件读取（UTF-8 / UTF-8 BOM / UTF-16 LE BOM 自动识别）
bool ParseFile(const std::wstring& filePath, Node& root, std::wstring* error);

// 去首尾空白（供上层复用）
std::wstring Trim(const std::wstring& text);

} // namespace usbcopy::xml
