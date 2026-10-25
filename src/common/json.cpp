#include "json.h"

#include <windows.h>

#include <algorithm>
#include <cerrno>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cwctype>

#include "path_util.h"
#include "win_util.h"

namespace usbcopy::json {

namespace {

constexpr int kMaxDepth = 64;

const Value& NullValue() {
    static const Value kNull;
    return kNull;
}

// ---- 序列化 ----

void AppendEscaped(std::wstring& out, const std::wstring& text) {
    out.push_back(L'"');
    for (wchar_t c : text) {
        switch (c) {
        case L'"': out.append(L"\\\""); break;
        case L'\\': out.append(L"\\\\"); break;
        case L'\b': out.append(L"\\b"); break;
        case L'\f': out.append(L"\\f"); break;
        case L'\n': out.append(L"\\n"); break;
        case L'\r': out.append(L"\\r"); break;
        case L'\t': out.append(L"\\t"); break;
        default:
            if (c < 0x20) {
                wchar_t buffer[8]{};
                swprintf_s(buffer, L"\\u%04X", static_cast<unsigned>(c));
                out.append(buffer);
            } else {
                out.push_back(c); // 非 ASCII 原样输出，最终由 ToUtf8 转成 UTF-8
            }
            break;
        }
    }
    out.push_back(L'"');
}

void AppendNumber(std::wstring& out, bool intLike, long long intValue, double doubleValue) {
    if (intLike) {
        out.append(std::to_wstring(intValue));
        return;
    }
    if (!std::isfinite(doubleValue)) {
        out.append(L"null"); // JSON 没有 NaN/Infinity
        return;
    }
    wchar_t buffer[64]{};
    swprintf_s(buffer, L"%.17g", doubleValue);
    std::wstring text = buffer;
    // 保证看起来仍是数字（"1" 或 "1e+300" 都是合法 JSON，无需补 .0）
    out.append(text);
}

void SerializeInner(const Value& value, std::wstring& out, int depth) {
    if (depth > kMaxDepth) {
        out.append(L"null"); // 防御：正常数据到不了这里
        return;
    }
    switch (value.type()) {
    case Type::Null:
        out.append(L"null");
        break;
    case Type::Bool:
        out.append(value.AsBool() ? L"true" : L"false");
        break;
    case Type::Number:
        AppendNumber(out, value.IsIntLike(), value.Int64Raw(), value.AsDouble());
        break;
    case Type::String:
        AppendEscaped(out, value.AsString());
        break;
    case Type::Array:
        out.push_back(L'[');
        for (size_t i = 0; i < value.Size(); ++i) {
            if (i > 0) {
                out.push_back(L',');
            }
            SerializeInner(value.At(i), out, depth + 1);
        }
        out.push_back(L']');
        break;
    case Type::Object:
        out.push_back(L'{');
        for (size_t i = 0; i < value.Size(); ++i) {
            if (i > 0) {
                out.push_back(L',');
            }
            AppendEscaped(out, value.KeyAt(i));
            out.push_back(L':');
            SerializeInner(value.At(i), out, depth + 1);
        }
        out.push_back(L'}');
        break;
    }
}

// ---- 解析 ----

class Parser {
public:
    explicit Parser(const std::wstring& text) : text_(text) {}

    bool ParseDocument(Value& out, std::wstring* error) {
        SkipWhitespace();
        if (!ParseValue(out, 0, error)) {
            return false;
        }
        SkipWhitespace();
        if (pos_ != text_.size()) {
            return Fail(error, L"JSON 尾部存在多余字符");
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

    void SkipWhitespace() {
        while (pos_ < text_.size()) {
            const wchar_t c = text_[pos_];
            if (c == L' ' || c == L'\t' || c == L'\r' || c == L'\n') {
                ++pos_;
            } else {
                break;
            }
        }
    }

    bool Expect(wchar_t expected, std::wstring* error) {
        if (AtEnd() || text_[pos_] != expected) {
            return Fail(error, L"缺少预期的字符");
        }
        ++pos_;
        return true;
    }

    bool ParseLiteral(const wchar_t* literal, std::wstring* error) {
        const size_t length = wcslen(literal);
        if (text_.compare(pos_, length, literal) != 0) {
            return Fail(error, L"无法识别的字面量");
        }
        pos_ += length;
        return true;
    }

    bool ParseValue(Value& out, int depth, std::wstring* error) {
        if (depth > kMaxDepth) {
            return Fail(error, L"嵌套层数超过 64");
        }
        if (AtEnd()) {
            return Fail(error, L"内容意外结束");
        }
        switch (Peek()) {
        case L'{': return ParseObject(out, depth, error);
        case L'[': return ParseArray(out, depth, error);
        case L'"': {
            std::wstring text;
            if (!ParseString(text, error)) {
                return false;
            }
            out = Value(std::move(text));
            return true;
        }
        case L't': {
            if (!ParseLiteral(L"true", error)) {
                return false;
            }
            out = Value(true);
            return true;
        }
        case L'f': {
            if (!ParseLiteral(L"false", error)) {
                return false;
            }
            out = Value(false);
            return true;
        }
        case L'n': {
            if (!ParseLiteral(L"null", error)) {
                return false;
            }
            out = Value();
            return true;
        }
        default:
            return ParseNumber(out, error);
        }
    }

    bool ParseObject(Value& out, int depth, std::wstring* error) {
        if (!Expect(L'{', error)) {
            return false;
        }
        out = Value::MakeObject();
        SkipWhitespace();
        if (!AtEnd() && Peek() == L'}') {
            ++pos_;
            return true;
        }
        for (;;) {
            SkipWhitespace();
            std::wstring key;
            if (!ParseString(key, error)) {
                return false;
            }
            SkipWhitespace();
            if (!Expect(L':', error)) {
                return false;
            }
            SkipWhitespace();
            Value item;
            if (!ParseValue(item, depth + 1, error)) {
                return false;
            }
            out.Set(key, std::move(item));
            SkipWhitespace();
            if (AtEnd()) {
                return Fail(error, L"对象未闭合");
            }
            if (Peek() == L',') {
                ++pos_;
                continue;
            }
            if (Peek() == L'}') {
                ++pos_;
                return true;
            }
            return Fail(error, L"对象成员之间缺少逗号");
        }
    }

    bool ParseArray(Value& out, int depth, std::wstring* error) {
        if (!Expect(L'[', error)) {
            return false;
        }
        out = Value::MakeArray();
        SkipWhitespace();
        if (!AtEnd() && Peek() == L']') {
            ++pos_;
            return true;
        }
        for (;;) {
            SkipWhitespace();
            Value item;
            if (!ParseValue(item, depth + 1, error)) {
                return false;
            }
            out.Push(std::move(item));
            SkipWhitespace();
            if (AtEnd()) {
                return Fail(error, L"数组未闭合");
            }
            if (Peek() == L',') {
                ++pos_;
                continue;
            }
            if (Peek() == L']') {
                ++pos_;
                return true;
            }
            return Fail(error, L"数组元素之间缺少逗号");
        }
    }

    bool ParseHex4(unsigned& value, std::wstring* error) {
        if (pos_ + 4 > text_.size()) {
            return Fail(error, L"\\u 转义不完整");
        }
        unsigned result = 0;
        for (int i = 0; i < 4; ++i) {
            const wchar_t c = text_[pos_ + static_cast<size_t>(i)];
            unsigned digit = 0;
            if (c >= L'0' && c <= L'9') {
                digit = static_cast<unsigned>(c - L'0');
            } else if (c >= L'a' && c <= L'f') {
                digit = static_cast<unsigned>(c - L'a') + 10u;
            } else if (c >= L'A' && c <= L'F') {
                digit = static_cast<unsigned>(c - L'A') + 10u;
            } else {
                return Fail(error, L"\\u 转义含非十六进制字符");
            }
            result = result * 16u + digit;
        }
        pos_ += 4;
        value = result;
        return true;
    }

    bool ParseString(std::wstring& out, std::wstring* error) {
        if (!Expect(L'"', error)) {
            return false;
        }
        out.clear();
        for (;;) {
            if (AtEnd()) {
                return Fail(error, L"字符串未闭合");
            }
            const wchar_t c = text_[pos_++];
            if (c == L'"') {
                return true;
            }
            if (c < 0x20) {
                return Fail(error, L"字符串内含未转义的控制字符");
            }
            if (c != L'\\') {
                out.push_back(c);
                continue;
            }
            if (AtEnd()) {
                return Fail(error, L"转义符后内容缺失");
            }
            const wchar_t escape = text_[pos_++];
            switch (escape) {
            case L'"': out.push_back(L'"'); break;
            case L'\\': out.push_back(L'\\'); break;
            case L'/': out.push_back(L'/'); break;
            case L'b': out.push_back(L'\b'); break;
            case L'f': out.push_back(L'\f'); break;
            case L'n': out.push_back(L'\n'); break;
            case L'r': out.push_back(L'\r'); break;
            case L't': out.push_back(L'\t'); break;
            case L'u': {
                unsigned code = 0;
                if (!ParseHex4(code, error)) {
                    return false;
                }
                if (code >= 0xD800 && code <= 0xDBFF) {
                    // 高代理：必须跟一个低位 \uDC00-\uDFFF 才能组成完整字符
                    if (pos_ + 1 < text_.size() && text_[pos_] == L'\\' && text_[pos_ + 1] == L'u') {
                        const size_t save = pos_;
                        pos_ += 2;
                        unsigned low = 0;
                        if (ParseHex4(low, error) && low >= 0xDC00 && low <= 0xDFFF) {
                            // wchar_t 是 16 位：补充平面字符必须以代理对形式写入
                            out.push_back(static_cast<wchar_t>(code));
                            out.push_back(static_cast<wchar_t>(low));
                            break;
                        }
                        pos_ = save;
                    }
                    out.push_back(static_cast<wchar_t>(code)); // 孤立代理，原样保留
                } else {
                    out.push_back(static_cast<wchar_t>(code));
                }
                break;
            }
            default:
                return Fail(error, L"未知的转义序列");
            }
        }
    }

    bool ParseNumber(Value& out, std::wstring* error) {
        const size_t start = pos_;
        if (Peek() == L'-') {
            ++pos_;
        }
        if (AtEnd() || std::iswdigit(Peek()) == 0) {
            return Fail(error, L"数字格式非法");
        }
        if (Peek() == L'0') {
            ++pos_;
            if (!AtEnd() && std::iswdigit(Peek()) != 0) {
                return Fail(error, L"数字不允许前导零");
            }
        } else {
            while (!AtEnd() && std::iswdigit(Peek()) != 0) {
                ++pos_;
            }
        }

        bool intLike = true;
        if (!AtEnd() && Peek() == L'.') {
            intLike = false;
            ++pos_;
            if (AtEnd() || std::iswdigit(Peek()) == 0) {
                return Fail(error, L"小数点后缺少数字");
            }
            while (!AtEnd() && std::iswdigit(Peek()) != 0) {
                ++pos_;
            }
        }
        if (!AtEnd() && (Peek() == L'e' || Peek() == L'E')) {
            intLike = false;
            ++pos_;
            if (!AtEnd() && (Peek() == L'+' || Peek() == L'-')) {
                ++pos_;
            }
            if (AtEnd() || std::iswdigit(Peek()) == 0) {
                return Fail(error, L"指数部分缺少数字");
            }
            while (!AtEnd() && std::iswdigit(Peek()) != 0) {
                ++pos_;
            }
        }

        const std::wstring token = text_.substr(start, pos_ - start);
        if (intLike) {
            errno = 0;
            wchar_t* end = nullptr;
            const long long value = wcstoll(token.c_str(), &end, 10);
            if (errno != ERANGE && end != nullptr && *end == L'\0') {
                out = Value::MakeInt(value);
                return true;
            }
            // 超出 int64：退化为浮点（与 STJ 的 TryGetInt64 失败语义一致）
        }
        out = Value::MakeDouble(wcstod(token.c_str(), nullptr));
        return true;
    }

    const std::wstring& text_;
    size_t pos_ = 0;
};

bool ReadAllBytes(const std::wstring& filePath, std::string& bytes, std::wstring* error) {
    HANDLE handle = CreateFileW(path::ToExtended(filePath).c_str(), GENERIC_READ,
                                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        if (error != nullptr) {
            *error = L"打开文件失败：" + util::Win32Message(GetLastError());
        }
        return false;
    }
    util::HandleGuard guard(handle);

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(handle, &size)) {
        if (error != nullptr) {
            *error = L"读取文件大小失败：" + util::Win32Message(GetLastError());
        }
        return false;
    }
    if (size.QuadPart > 64ll * 1024 * 1024) {
        if (error != nullptr) {
            *error = L"文件过大（>64MB），拒绝解析";
        }
        return false;
    }

    bytes.clear();
    bytes.resize(static_cast<size_t>(size.QuadPart));
    size_t offset = 0;
    while (offset < bytes.size()) {
        DWORD read = 0;
        const DWORD want = static_cast<DWORD>(
            (std::min)(static_cast<size_t>(1u << 20), bytes.size() - offset));
        if (!ReadFile(handle, bytes.data() + offset, want, &read, nullptr)) {
            if (error != nullptr) {
                *error = L"读取文件失败：" + util::Win32Message(GetLastError());
            }
            return false;
        }
        if (read == 0) {
            break;
        }
        offset += read;
    }
    bytes.resize(offset);
    return true;
}

} // namespace

// ---- Value ----

Value::Value(bool value) : type_(Type::Bool), bool_(value) {}
Value::Value(int value) : type_(Type::Number), int_(value) {}
Value::Value(long long value) : type_(Type::Number), int_(value) {}
Value::Value(double value)
    : type_(Type::Number), intLike_(false), double_(value) {}
Value::Value(const wchar_t* value) : type_(Type::String), string_(value == nullptr ? L"" : value) {}
Value::Value(const std::wstring& value) : type_(Type::String), string_(value) {}
Value::Value(std::wstring&& value) : type_(Type::String), string_(std::move(value)) {}

Value Value::MakeArray() {
    Value value;
    value.type_ = Type::Array;
    return value;
}

Value Value::MakeObject() {
    Value value;
    value.type_ = Type::Object;
    return value;
}

Value Value::MakeInt(long long value) {
    Value result;
    result.type_ = Type::Number;
    result.intLike_ = true;
    result.int_ = value;
    return result;
}

Value Value::MakeDouble(double value) {
    Value result;
    result.type_ = Type::Number;
    result.intLike_ = false;
    result.double_ = value;
    return result;
}

bool Value::AsBool(bool fallback) const {
    return type_ == Type::Bool ? bool_ : fallback;
}

long long Value::AsInt64(long long fallback) const {
    if (type_ != Type::Number) {
        return fallback;
    }
    if (intLike_) {
        return int_;
    }
    if (!std::isfinite(double_) || double_ > 9.2e18 || double_ < -9.2e18) {
        return fallback;
    }
    return static_cast<long long>(double_);
}

int Value::AsInt(int fallback) const {
    if (type_ != Type::Number) {
        return fallback;
    }
    const long long value = AsInt64(fallback);
    if (value > 2147483647ll || value < -2147483648ll) {
        return fallback;
    }
    return static_cast<int>(value);
}

double Value::AsDouble(double fallback) const {
    if (type_ != Type::Number) {
        return fallback;
    }
    return intLike_ ? static_cast<double>(int_) : double_;
}

const std::wstring& Value::AsString() const {
    if (type_ == Type::String) {
        return string_;
    }
    return NullValue().string_;
}

size_t Value::Size() const {
    if (type_ == Type::Array || type_ == Type::Object) {
        return items_.size();
    }
    return 0;
}

const Value& Value::At(size_t index) const {
    if ((type_ == Type::Array || type_ == Type::Object) && index < items_.size()) {
        return items_[index];
    }
    return NullValue();
}

const std::wstring& Value::KeyAt(size_t index) const {
    if (type_ == Type::Object && index < keys_.size()) {
        return keys_[index];
    }
    return NullValue().string_;
}

void Value::Push(Value value) {
    if (type_ == Type::Null) {
        type_ = Type::Array;
    }
    if (type_ == Type::Array) {
        items_.push_back(std::move(value));
    }
}

void Value::Set(const std::wstring& key, Value value) {
    if (type_ == Type::Null) {
        type_ = Type::Object;
    }
    if (type_ != Type::Object) {
        return;
    }
    for (size_t i = 0; i < keys_.size(); ++i) {
        if (keys_[i] == key) {
            items_[i] = std::move(value); // 保持原位置
            return;
        }
    }
    keys_.push_back(key);
    items_.push_back(std::move(value));
}

void Value::Set(const wchar_t* key, Value value) {
    Set(std::wstring(key == nullptr ? L"" : key), std::move(value));
}

const Value* Value::Find(const std::wstring& key) const {
    if (type_ != Type::Object) {
        return nullptr;
    }
    for (size_t i = 0; i < keys_.size(); ++i) {
        if (keys_[i] == key) {
            return &items_[i];
        }
    }
    return nullptr;
}

const Value* Value::FindIgnoreCase(const std::wstring& key) const {
    if (type_ != Type::Object) {
        return nullptr;
    }
    for (size_t i = 0; i < keys_.size(); ++i) {
        if (CompareStringOrdinal(keys_[i].c_str(), static_cast<int>(keys_[i].size()), key.c_str(),
                                 static_cast<int>(key.size()), TRUE) == CSTR_EQUAL) {
            return &items_[i];
        }
    }
    return nullptr;
}

bool Value::GetBool(const std::wstring& key, bool fallback) const {
    const Value* item = Find(key);
    return item != nullptr ? item->AsBool(fallback) : fallback;
}

int Value::GetInt(const std::wstring& key, int fallback) const {
    const Value* item = Find(key);
    return item != nullptr ? item->AsInt(fallback) : fallback;
}

long long Value::GetInt64(const std::wstring& key, long long fallback) const {
    const Value* item = Find(key);
    return item != nullptr ? item->AsInt64(fallback) : fallback;
}

double Value::GetDouble(const std::wstring& key, double fallback) const {
    const Value* item = Find(key);
    return item != nullptr ? item->AsDouble(fallback) : fallback;
}

std::wstring Value::GetString(const std::wstring& key, const std::wstring& fallback) const {
    const Value* item = Find(key);
    if (item == nullptr || !item->IsString()) {
        return fallback;
    }
    return item->AsString();
}

// ---- 对外接口 ----

bool Parse(const std::wstring& text, Value& out, std::wstring* error) {
    if (error != nullptr) {
        error->clear();
    }
    Parser parser(text);
    return parser.ParseDocument(out, error);
}

bool ParseUtf8(const std::string& text, Value& out, std::wstring* error) {
    std::string body = text;
    if (body.size() >= 3 && static_cast<unsigned char>(body[0]) == 0xEF &&
        static_cast<unsigned char>(body[1]) == 0xBB && static_cast<unsigned char>(body[2]) == 0xBF) {
        body.erase(0, 3); // 容忍 BOM
    }
    return Parse(util::FromUtf8(body), out, error);
}

std::wstring SerializeText(const Value& value) {
    std::wstring out;
    out.reserve(256);
    SerializeInner(value, out, 0);
    return out;
}

std::string SerializeUtf8(const Value& value) {
    return util::ToUtf8(SerializeText(value));
}

bool LoadFile(const std::wstring& filePath, Value& out, std::wstring* error) {
    std::string bytes;
    if (!ReadAllBytes(filePath, bytes, error)) {
        return false;
    }
    return ParseUtf8(bytes, out, error);
}

bool SaveFile(const std::wstring& filePath, const Value& value, std::wstring* error) {
    const std::string bytes = SerializeUtf8(value);
    HANDLE handle = CreateFileW(path::ToExtended(filePath).c_str(), GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        if (error != nullptr) {
            *error = L"创建文件失败：" + util::Win32Message(GetLastError());
        }
        return false;
    }
    util::HandleGuard guard(handle);

    size_t offset = 0;
    while (offset < bytes.size()) {
        DWORD written = 0;
        const DWORD want = static_cast<DWORD>(
            (std::min)(static_cast<size_t>(1u << 20), bytes.size() - offset));
        if (!WriteFile(handle, bytes.data() + offset, want, &written, nullptr) || written == 0) {
            if (error != nullptr) {
                *error = L"写入文件失败：" + util::Win32Message(GetLastError());
            }
            return false;
        }
        offset += written;
    }
    FlushFileBuffers(handle);
    return true;
}

} // namespace usbcopy::json
