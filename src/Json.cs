using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BuildManager
{
    /// <summary>
    /// 极简 JSON 解析/转义。游戏内嵌 Mono 的 DataContractJsonSerializer 行为不可靠
    /// （未知字段/ulong 可能抛异常），静态表和方案文件统一走这里，零外部依赖。
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var parser = new Parser(text);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Unexpected trailing characters at position " + parser.Position);
            return value;
        }

        public static string Escape(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static object Get(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value : null;
        }

        public static int ToInt(object value, int fallback = 0)
        {
            if (value is int) return (int)value;
            if (value is long)
            {
                long l = (long)value;
                return l > int.MaxValue || l < int.MinValue ? fallback : (int)l;
            }
            if (value is ulong)
            {
                ulong u = (ulong)value;
                return u > int.MaxValue ? fallback : (int)u;
            }
            if (value is double) return (int)(double)value;
            if (value is string)
            {
                int parsed;
                if (int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return fallback;
        }

        public static ulong ToUlong(object value, ulong fallback = 0)
        {
            if (value is ulong) return (ulong)value;
            if (value is long) return (long)value < 0 ? fallback : (ulong)(long)value;
            if (value is int) return (int)value < 0 ? fallback : (ulong)(int)value;
            if (value is double) return (double)value < 0 ? fallback : (ulong)(double)value;
            if (value is string)
            {
                ulong parsed;
                if (ulong.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return fallback;
        }

        public static string ToStringValue(object value)
        {
            return value as string;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text)
            {
                _text = text;
                _pos = 0;
            }

            public int Position { get { return _pos; } }

            public bool AtEnd { get { return _pos >= _text.Length; } }

            public void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == '\uFEFF' || char.IsWhiteSpace(c))
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");

                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++; // '{'
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("Expected string key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("Expected ':'");
                    _pos++;
                    obj[key] = ParseValue();
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _pos++;
                        return obj;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _pos++; // '['
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _pos++;
                        return list;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                _pos++; // opening quote
                var sb = new StringBuilder();

                while (_pos < _text.Length)
                {
                    char c = _text[_pos++];
                    if (c == '"') return sb.ToString();

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_pos >= _text.Length) throw Error("Unterminated escape");
                    char escape = _text[_pos++];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Error("Bad \\u escape");
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                code = (code << 4) | HexValue(_text[_pos++]);
                            }
                            sb.Append((char)code);
                            break;
                        default:
                            throw Error("Bad escape '\\" + escape + "'");
                    }
                }

                throw Error("Unterminated string");
            }

            private object ParseNumber()
            {
                int start = _pos;
                if (Peek() == '-') _pos++;

                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                string raw = _text.Substring(start, _pos - start);
                if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0)
                {
                    if (raw.StartsWith("-", StringComparison.Ordinal))
                    {
                        long l;
                        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                    }
                    else
                    {
                        ulong u;
                        if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) return u;
                        long l2;
                        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out l2)) return l2;
                    }
                }

                double d;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw Error("Bad number '" + raw + "'");
            }

            private void Expect(string literal)
            {
                if (_pos + literal.Length > _text.Length ||
                    string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                {
                    throw Error("Expected '" + literal + "'");
                }
                _pos += literal.Length;
            }

            private char Peek()
            {
                return _pos < _text.Length ? _text[_pos] : '\0';
            }

            private int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                throw Error("Bad hex digit '" + c + "'");
            }

            private FormatException Error(string message)
            {
                return new FormatException(message + " at position " + _pos);
            }
        }
    }
}
