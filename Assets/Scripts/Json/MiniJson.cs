using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Whiteboard.Json
{
    /// <summary>
    /// 軽量JSONパーサ/ライタ（MiniJSON相当）。外部パッケージに依存せず、
    /// Dictionary&lt;string,object&gt; / List&lt;object&gt; ベースで異種メッセージ（型ごとにフィールドが違う）を扱う。
    /// JsonUtility は静的な型を要求するため WebSocket メッセージのような可変形状の JSON に不向き。
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            var parser = new Parser(json);
            return parser.ParseValue();
        }

        public static string Serialize(object obj)
        {
            var sb = new StringBuilder();
            WriteValue(sb, obj);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case IDictionary dict:
                    WriteObject(sb, dict);
                    return;
                case IEnumerable enumerable:
                    WriteArray(sb, enumerable);
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                default:
                    // int/long/その他数値
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                sb.Append(':');
                WriteValue(sb, entry.Value);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable enumerable)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\f':
                            sb.Append("\\f");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u").Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            // セキュリティレビュー（2026-09-17）：受信メッセージは全て信頼できない入力として扱う。
            // このパーサは再帰下降式のため、深くネストした配列/オブジェクト（例："[[[[...]]]]"）を
            // 制限なく渡すと C# の呼び出しスタックを使い果たし、catch不可能な StackOverflowException で
            // Unity WebGLランタイム全体が落ちる（他の全参加者のセッションにも影響する）。
            // ネスト深さに上限を設け、超過時は通常の（呼び出し側でcatchできる）FormatExceptionとする。
            private const int MaxDepth = 64;

            private readonly string _json;
            private int _index;
            private int _depth;

            public Parser(string json)
            {
                _json = json;
                _index = 0;
                _depth = 0;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length)
                {
                    return null;
                }

                char c = _json[_index];
                switch (c)
                {
                    case '{':
                    case '[':
                        _depth++;
                        if (_depth > MaxDepth)
                        {
                            throw new FormatException("JSONのネストが深すぎます（上限 " + MaxDepth + "）");
                        }
                        try
                        {
                            return c == '{' ? (object)ParseObject() : (object)ParseArray();
                        }
                        finally
                        {
                            _depth--;
                        }
                    case '"':
                        return ParseString();
                    case 't':
                        Expect("true");
                        return true;
                    case 'f':
                        Expect("false");
                        return false;
                    case 'n':
                        Expect("null");
                        return null;
                    default:
                        return ParseNumber();
                }
            }

            private void Expect(string literal)
            {
                if (_index + literal.Length > _json.Length || _json.Substring(_index, literal.Length) != literal)
                {
                    throw new FormatException("JSONの解析に失敗しました（位置 " + _index + "）");
                }
                _index += literal.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                _index++; // '{'
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':')
                    {
                        throw new FormatException("JSONオブジェクトの ':' が見つかりません");
                    }
                    _index++;
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    char next = Peek();
                    if (next == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (next == '}')
                    {
                        _index++;
                        break;
                    }
                    throw new FormatException("JSONオブジェクトの終端が不正です");
                }
                return result;
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _index++; // '['
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    object value = ParseValue();
                    result.Add(value);
                    SkipWhitespace();
                    char next = Peek();
                    if (next == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (next == ']')
                    {
                        _index++;
                        break;
                    }
                    throw new FormatException("JSON配列の終端が不正です");
                }
                return result;
            }

            private string ParseString()
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw new FormatException("JSON文字列の開始 '\"' が見つかりません");
                }
                _index++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (_index >= _json.Length)
                    {
                        throw new FormatException("JSON文字列が終端していません");
                    }
                    char c = _json[_index++];
                    if (c == '"')
                    {
                        break;
                    }
                    if (c == '\\')
                    {
                        char esc = _json[_index++];
                        switch (esc)
                        {
                            case '"':
                                sb.Append('"');
                                break;
                            case '\\':
                                sb.Append('\\');
                                break;
                            case '/':
                                sb.Append('/');
                                break;
                            case 'b':
                                sb.Append('\b');
                                break;
                            case 'f':
                                sb.Append('\f');
                                break;
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            case 't':
                                sb.Append('\t');
                                break;
                            case 'u':
                                string hex = _json.Substring(_index, 4);
                                _index += 4;
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                break;
                            default:
                                sb.Append(esc);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = _index;
                while (_index < _json.Length && (char.IsDigit(_json[_index]) || _json[_index] == '-' || _json[_index] == '+' || _json[_index] == '.' || _json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                }
                string numStr = _json.Substring(start, _index - start);
                if (numStr.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
                {
                    return double.Parse(numStr, CultureInfo.InvariantCulture);
                }
                if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                {
                    return (double)l;
                }
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            }

            private char Peek()
            {
                return _index < _json.Length ? _json[_index] : '\0';
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }
        }
    }
}
