#include "xml_lite.h"

#include <windows.h>

#include <cwctype>

#include "path_util.h"
#include "win_util.h"

namespace usbcopy::xml {

namespace {

constexpr int kMaxDepth = 64;

bool IsSpace(wchar_t c) {
    return c == L' ' || c == L'\t' || c == L'\r' || c == L'\n';
}

std::wstring DecodeEntities(const std::wstring& raw) {
    std::wstring out;
    out.reserve(raw.size());
    size_t pos = 0;
    while (pos < raw.size()) {
        const wchar_t c = raw[pos];
        if (c != L'&') {
            out.push_back(c);
            ++pos;
            continue;
        }
        const size_t semi = raw.find(L';', pos + 1);
        if (semi == std::wstring::npos || semi - pos > 12) {
            out.push_back(c); // 不是实体，原样保留
            ++pos;
            continue;
        }
        const std::wstring entity = raw.substr(pos + 1, semi - pos - 1);
        if (entity == L"lt") {
            out.push_back(L'<');
        } else if (entity == L"gt") {
            out.push_back(L'>');
        } else if (entity == L"amp") {
            out.push_back(L'&');
        } else if (entity == L"quot") {
            out.push_back(L'"');
        } else if (entity == L"apos") {
            out.push_back(L'\'');
        } else if (!entity.empty() && entity[0] == L'#') {
            unsigned code = 0;
            bool ok = true;
            if (entity.size() > 2 && (entity[1] == L'x' || entity[1] == L'X')) {
                for (size_t i = 2; i < entity.size(); ++i) {
                    const wchar_t digit = entity[i];
                    unsigned value = 0;
                    if (digit >= L'0' && digit <= L'9') {
                        value = static_cast<unsigned>(digit - L'0');
                    } else if (digit >= L'a' && digit <= L'f') {
                        value = static_cast<unsigned>(digit - L'a') + 10u;
                    } else if (digit >= L'A' && digit <= L'F') {
                        value = static_cast<unsigned>(digit - L'A') + 10u;
                    } else {
                        ok = false;
                        break;
                    }
                    code = code * 16u + value;
                }
            } else {
                for (size_t i = 1; i < entity.size(); ++i) {
                    if (entity[i] < L'0' || entity[i] > L'9') {
                        ok = false;
                        break;
                    }
                    code = code * 10u + static_cast<unsigned>(entity[i] - L'0');
                }
            }
            if (ok && code > 0 && code <= 0x10FFFF) {
                if (code <= 0xFFFF) {
                    out.push_back(static_cast<wchar_t>(code));
                } else {
                    // 补充平面：拆成代理对
                    const unsigned value = code - 0x10000u;
                    out.push_back(static_cast<wchar_t>(0xD800u + (value >> 10)));
                    out.push_back(static_cast<wchar_t>(0xDC00u + (value & 0x3FFu)));
                }
            } else {
                out.append(raw, pos, semi - pos + 1);
            }
        } else {
            out.append(raw, pos, semi - pos + 1); // 未知实体原样保留
        }
        pos = semi + 1;
    }
    return out;
}

class Parser {
public:
    explicit Parser(const std::wstring& text) : text_(text) {}

    bool ParseDocument(Node& root, std::wstring* error) {
        // BOM
        if (!text_.empty() && text_[0] == 0xFEFF) {
            pos_ = 1;
        }
        SkipMisc();
        if (AtEnd() || Peek() != L'<') {
            return Fail(error, L"缺少根元素");
        }
        if (!ParseElement(root, 0, error)) {
            return false;
        }
        SkipMisc();
        if (!AtEnd()) {
            return Fail(error, L"根元素之后存在多余内容");
        }
        return true;
    }

private:
    bool Fail(std::wstring* error, const wchar_t* message) {
        if (error != nullptr) {
            *error = std::wstring(message) + L"（位置 " + std::to_wstring(pos_) + L"）";
        }
        return false;
    }

    bool AtEnd() const { return pos_ >= text_.size(); }
    wchar_t Peek() const { return pos_ < text_.size() ? text_[pos_] : L'\0'; }

    bool StartsWith(const wchar_t* literal) const {
        const size_t length = wcslen(literal);
        return text_.compare(pos_, length, literal) == 0;
    }

    void SkipWhitespace() {
        while (!AtEnd() && IsSpace(text_[pos_])) {
            ++pos_;
        }
    }

    // 跳过声明、注释、DOCTYPE 之类的杂项
    bool SkipMisc() {
        for (;;) {
            SkipWhitespace();
            if (StartsWith(L"<?")) {
                const size_t end = text_.find(L"?>", pos_);
                if (end == std::wstring::npos) {
                    pos_ = text_.size();
                    return true; // 允许被截断的声明（宽容处理）
                }
                pos_ = end + 2;
                continue;
            }
            if (StartsWith(L"<!--")) {
                const size_t end = text_.find(L"-->", pos_);
                pos_ = (end == std::wstring::npos) ? text_.size() : end + 3;
                continue;
            }
            if (StartsWith(L"<![CDATA[")) {
                return true; // 由 ParseContent 处理
            }
            return true;
        }
    }

    bool ReadName(std::wstring& name) {
        name.clear();
        while (!AtEnd()) {
            const wchar_t c = text_[pos_];
            if (IsSpace(c) || c == L'>' || c == L'/' || c == L'=') {
                break;
            }
            name.push_back(c);
            ++pos_;
        }
        return !name.empty();
    }

    // 跳过属性（含引号内的 '>'），停在 '>' 或 "/>"
    bool SkipAttributes(std::wstring* error) {
        for (;;) {
            SkipWhitespace();
            if (AtEnd()) {
                return Fail(error, L"开始标签未闭合");
            }
            if (Peek() == L'>' || Peek() == L'/') {
                return true;
            }
            std::wstring attribute;
            if (!ReadName(attribute)) {
                return Fail(error, L"属性名非法");
            }
            SkipWhitespace();
            if (AtEnd() || Peek() != L'=') {
                return Fail(error, L"属性缺少等号");
            }
            ++pos_;
            SkipWhitespace();
            if (AtEnd() || (Peek() != L'"' && Peek() != L'\'')) {
                return Fail(error, L"属性值缺少引号");
            }
            const wchar_t quote = text_[pos_++];
            while (!AtEnd() && text_[pos_] != quote) {
                ++pos_;
            }
            if (AtEnd()) {
                return Fail(error, L"属性值未闭合");
            }
            ++pos_; // 跳过结束引号
        }
    }

    bool ParseElement(Node& node, int depth, std::wstring* error) {
        if (depth > kMaxDepth) {
            return Fail(error, L"元素嵌套超过 64 层");
        }
        if (AtEnd() || Peek() != L'<') {
            return Fail(error, L"缺少开始标签");
        }
        ++pos_;
        if (!ReadName(node.name)) {
            return Fail(error, L"元素名非法");
        }
        if (!SkipAttributes(error)) {
            return false;
        }
        if (AtEnd()) {
            return Fail(error, L"开始标签未闭合");
        }
        if (Peek() == L'/') { // 自闭合
            ++pos_;
            if (AtEnd() || Peek() != L'>') {
                return Fail(error, L"自闭合标签格式错误");
            }
            ++pos_;
            return true;
        }
        if (Peek() != L'>') {
            return Fail(error, L"开始标签格式错误");
        }
        ++pos_;

        if (!ParseContent(node, depth, error)) {
            return false;
        }

        // 结束标签
        if (!StartsWith(L"</")) {
            return Fail(error, L"缺少结束标签");
        }
        pos_ += 2;
        std::wstring closing;
        if (!ReadName(closing)) {
            return Fail(error, L"结束标签非法");
        }
        if (closing != node.name) {
            return Fail(error, L"结束标签与开始标签不匹配");
        }
        SkipWhitespace();
        if (AtEnd() || Peek() != L'>') {
            return Fail(error, L"结束标签未闭合");
        }
        ++pos_;
        return true;
    }

    bool ParseContent(Node& node, int depth, std::wstring* error) {
        std::wstring text;
        for (;;) {
            if (AtEnd()) {
                return Fail(error, L"内容未闭合");
            }
            if (StartsWith(L"</")) {
                node.text = DecodeEntities(text);
                return true;
            }
            if (StartsWith(L"<!--")) {
                const size_t end = text_.find(L"-->", pos_);
                pos_ = (end == std::wstring::npos) ? text_.size() : end + 3;
                continue;
            }
            if (StartsWith(L"<![CDATA[")) {
                const size_t start = pos_ + 9;
                const size_t end = text_.find(L"]]>", start);
                if (end == std::wstring::npos) {
                    return Fail(error, L"CDATA 未闭合");
                }
                text.append(text_, start, end - start);
                pos_ = end + 3;
                continue;
            }
            if (Peek() == L'<') {
                Node child;
                if (!ParseElement(child, depth + 1, error)) {
                    return false;
                }
                node.children.push_back(std::move(child));
                continue;
            }
            text.push_back(text_[pos_++]);
        }
    }

    const std::wstring& text_;
    size_t pos_ = 0;
};

} // namespace

const Node* Node::Child(const std::wstring& childName) const {
    for (const Node& child : children) {
        if (child.name == childName) {
            return &child;
        }
    }
    return nullptr;
}

std::wstring Node::ChildText(const std::wstring& childName) const {
    const Node* child = Child(childName);
    return child == nullptr ? std::wstring() : Trim(child->text);
}

std::wstring Trim(const std::wstring& text) {
    size_t begin = 0;
    size_t end = text.size();
    while (begin < end && IsSpace(text[begin])) {
        ++begin;
    }
    while (end > begin && IsSpace(text[end - 1])) {
        --end;
    }
    return text.substr(begin, end - begin);
}

bool Parse(const std::wstring& xml, Node& root, std::wstring* error) {
    if (error != nullptr) {
        error->clear();
    }
    root = Node();
    Parser parser(xml);
    return parser.ParseDocument(root, error);
}

bool ParseFile(const std::wstring& filePath, Node& root, std::wstring* error) {
    HANDLE handle = CreateFileW(path::ToExtended(filePath).c_str(), GENERIC_READ,
                                FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
                                FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        if (error != nullptr) {
            *error = L"打开文件失败：" + util::Win32Message(GetLastError());
        }
        return false;
    }
    util::HandleGuard guard(handle);

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(handle, &size) || size.QuadPart > 16ll * 1024 * 1024) {
        if (error != nullptr) {
            *error = L"文件为空或过大（>16MB）";
        }
        return false;
    }

    std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
    size_t offset = 0;
    while (offset < bytes.size()) {
        DWORD read = 0;
        const DWORD want =
            static_cast<DWORD>((std::min)(static_cast<size_t>(1u << 20), bytes.size() - offset));
        if (!ReadFile(handle, bytes.data() + offset, want, &read, nullptr) || read == 0) {
            break;
        }
        offset += read;
    }
    bytes.resize(offset);

    std::wstring text;
    if (bytes.size() >= 2 && static_cast<unsigned char>(bytes[0]) == 0xFF &&
        static_cast<unsigned char>(bytes[1]) == 0xFE) {
        // UTF-16 LE（XmlSerializer 默认 StreamWriter(Encoding.UTF8)，但容忍手改文件）
        const size_t count = (bytes.size() - 2) / 2;
        text.resize(count);
        for (size_t i = 0; i < count; ++i) {
            text[i] = static_cast<wchar_t>(static_cast<unsigned char>(bytes[2 + i * 2]) |
                                          (static_cast<unsigned char>(bytes[2 + i * 2 + 1]) << 8));
        }
    } else {
        std::string body = bytes;
        if (body.size() >= 3 && static_cast<unsigned char>(body[0]) == 0xEF &&
            static_cast<unsigned char>(body[1]) == 0xBB &&
            static_cast<unsigned char>(body[2]) == 0xBF) {
            body.erase(0, 3);
        }
        text = util::FromUtf8(body);
    }

    return Parse(text, root, error);
}

} // namespace usbcopy::xml
