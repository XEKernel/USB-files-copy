#pragma once

// ============================================================================
//  json —— 极简 JSON（只做设置/索引/日志需要的那部分）
//
//  支持：object / array / string / number（整数与浮点分别保真）/ true / false / null
//  对象键保持插入顺序（不排序）——与 System.Text.Json 一致，便于人工 diff
//  不支持：注释、NaN / Infinity、单引号、尾逗号、无引号键
//
//  与 System.Text.Json 的差异（已记入 兼容性基线.md §15）：
//    · 序列化时非 ASCII 原样输出 UTF-8（STJ 默认转义为 \uXXXX）——两者互可解析
//    · 深度上限 64（STJ 默认 64），超限视为非法输入，防止栈溢出
//    · 整数用 int64 保真（STJ 读成 JsonElement 再取，同样不丢精度）
// ============================================================================

#include <string>
#include <vector>

namespace usbcopy::json {

enum class Type { Null, Bool, Number, String, Array, Object };

class Value {
public:
    Value() = default;
    explicit Value(bool value);
    explicit Value(int value);
    explicit Value(long long value);
    explicit Value(double value);
    explicit Value(const wchar_t* value);
    explicit Value(const std::wstring& value);
    explicit Value(std::wstring&& value);

    static Value MakeArray();
    static Value MakeObject();
    static Value MakeInt(long long value);
    static Value MakeDouble(double value);

    Type type() const { return type_; }
    bool IsNull() const { return type_ == Type::Null; }
    bool IsBool() const { return type_ == Type::Bool; }
    bool IsNumber() const { return type_ == Type::Number; }
    bool IsString() const { return type_ == Type::String; }
    bool IsArray() const { return type_ == Type::Array; }
    bool IsObject() const { return type_ == Type::Object; }

    // 类型不符时返回 fallback（不抛异常）
    bool AsBool(bool fallback = false) const;
    long long AsInt64(long long fallback = 0) const;
    int AsInt(int fallback = 0) const;
    double AsDouble(double fallback = 0.0) const;
    const std::wstring& AsString() const; // 非字符串时返回空串

    // Number 专用：整数形态（序列化时按整数输出，不做浮点格式化）
    bool IsIntLike() const { return type_ == Type::Number && intLike_; }
    long long Int64Raw() const { return int_; }

    // ---- array / object 通用 ----
    size_t Size() const;
    bool Empty() const { return Size() == 0; }
    const Value& At(size_t index) const;      // 越界返回 null 单例
    const std::wstring& KeyAt(size_t index) const;

    void Push(Value value);                   // 仅对 Array 有效
    void Set(const std::wstring& key, Value value);          // 已存在则替换（位置不变）
    void Set(const wchar_t* key, Value value);

    const Value* Find(const std::wstring& key) const;        // 大小写敏感
    const Value* FindIgnoreCase(const std::wstring& key) const;

    // 便捷读取（键不存在或类型不符时返回 fallback）
    bool GetBool(const std::wstring& key, bool fallback = false) const;
    int GetInt(const std::wstring& key, int fallback = 0) const;
    long long GetInt64(const std::wstring& key, long long fallback = 0) const;
    double GetDouble(const std::wstring& key, double fallback = 0.0) const;
    std::wstring GetString(const std::wstring& key, const std::wstring& fallback = {}) const;

private:
    Type type_ = Type::Null;
    bool bool_ = false;
    bool intLike_ = true; // Number 专用：true=整数（保真 int64），false=浮点
    long long int_ = 0;
    double double_ = 0.0;
    std::wstring string_;
    std::vector<std::wstring> keys_; // 仅 Object；与 items_ 平行
    std::vector<Value> items_;       // Array 元素 / Object 值
};

// 解析 UTF-16 文本；失败时写 error（含位置），out 不确定（不要用）
bool Parse(const std::wstring& text, Value& out, std::wstring* error);
bool ParseUtf8(const std::string& text, Value& out, std::wstring* error);

// 序列化：紧凑格式，键按插入顺序
std::wstring SerializeText(const Value& value);
std::string SerializeUtf8(const Value& value);

// 文件读写（UTF-8；写入不加 BOM，读取容忍 BOM）
bool LoadFile(const std::wstring& filePath, Value& out, std::wstring* error);
bool SaveFile(const std::wstring& filePath, const Value& value, std::wstring* error);

} // namespace usbcopy::json
