// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Strict recursive-descent JSON parser.
    ///
    /// This is the first line of defence on the untrusted-plan path, which is why it is
    /// ours rather than a library's. It is deliberately less permissive than most
    /// parsers: no comments, no trailing commas, no single quotes, no NaN/Infinity
    /// literals, no duplicate keys, no unescaped control characters, bounded depth and
    /// bounded input size. A plan that needs any of those is a plan we do not want to run.
    /// </summary>
    public static class SqJsonParser
    {
        public const int MaxDepth = 32;
        public const int MaxBytes = 4 * 1024 * 1024;

        /// <summary>
        /// Parses JSON text. Returns false and sets <paramref name="error"/> on any
        /// violation; never throws for malformed input.
        /// </summary>
        public static bool TryParse(string text, out SqJsonValue value, out string error)
        {
            value = SqJsonValue.Null;
            error = null;

            if (text == null) { error = "input is null"; return false; }
            if (text.Length > MaxBytes)
            {
                error = "input exceeds " + MaxBytes + " bytes (" + text.Length + ")";
                return false;
            }

            var p = new Parser(text);
            try
            {
                p.SkipWhitespace();
                SqJsonValue root = p.ParseValue(0);
                p.SkipWhitespace();
                if (!p.AtEnd)
                {
                    error = p.Describe("unexpected trailing content");
                    return false;
                }
                value = root;
                return true;
            }
            catch (JsonError e)
            {
                error = e.Message;
                return false;
            }
        }

        sealed class JsonError : Exception
        {
            public JsonError(string message) : base(message) { }
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; _i = 0; }

            public bool AtEnd { get { return _i >= _s.Length; } }

            public string Describe(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; } else col++;
                }
                return message + " at line " + line + " column " + col;
            }

            JsonError Fail(string message) { return new JsonError(Describe(message)); }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            char Peek()
            {
                if (_i >= _s.Length) throw Fail("unexpected end of input");
                return _s[_i];
            }

            void Expect(char c)
            {
                if (_i >= _s.Length || _s[_i] != c)
                    throw Fail("expected '" + c + "'");
                _i++;
            }

            public SqJsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Fail("nesting exceeds max depth " + MaxDepth);

                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return new SqJsonValue(ParseString());
                    case 't': ParseLiteral("true"); return new SqJsonValue(true);
                    case 'f': ParseLiteral("false"); return new SqJsonValue(false);
                    case 'n': ParseLiteral("null"); return SqJsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        // NaN / Infinity / +1 / .5 / 'quoted' all land here on purpose.
                        throw Fail("unexpected character '" + c + "'");
                }
            }

            void ParseLiteral(string literal)
            {
                if (_i + literal.Length > _s.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw Fail("invalid literal, expected '" + literal + "'");
                _i += literal.Length;
            }

            SqJsonValue ParseObject(int depth)
            {
                Expect('{');
                var map = new Dictionary<string, SqJsonValue>(StringComparer.Ordinal);

                SkipWhitespace();
                if (Peek() == '}') { _i++; return new SqJsonValue(map); }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Fail("object key must be a string");
                    string key = ParseString();

                    // Duplicate keys are how a plan smuggles a second value past a
                    // validator that only looked at the first one. Reject outright.
                    if (map.ContainsKey(key)) throw Fail("duplicate object key '" + key + "'");

                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    map[key] = ParseValue(depth + 1);

                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; break; }
                    throw Fail("expected ',' or '}' in object");
                }

                return new SqJsonValue(map);
            }

            SqJsonValue ParseArray(int depth)
            {
                Expect('[');
                var list = new List<SqJsonValue>();

                SkipWhitespace();
                if (Peek() == ']') { _i++; return new SqJsonValue(list); }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue(depth + 1));

                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; break; }
                    throw Fail("expected ',' or ']' in array");
                }

                return new SqJsonValue(list);
            }

            string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();

                while (true)
                {
                    if (_i >= _s.Length) throw Fail("unterminated string");
                    char c = _s[_i++];

                    if (c == '"') break;

                    if (c < 0x20) throw Fail("unescaped control character in string");

                    if (c != '\\') { sb.Append(c); continue; }

                    if (_i >= _s.Length) throw Fail("unterminated escape sequence");
                    char e = _s[_i++];
                    switch (e)
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
                            if (_i + 4 > _s.Length) throw Fail("truncated \\u escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int d = HexDigit(_s[_i + k]);
                                if (d < 0) throw Fail("invalid hex digit in \\u escape");
                                code = (code << 4) | d;
                            }
                            _i += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            throw Fail("invalid escape character '\\" + e + "'");
                    }
                }

                return sb.ToString();
            }

            static int HexDigit(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            SqJsonValue ParseNumber()
            {
                int start = _i;

                if (_i < _s.Length && _s[_i] == '-') _i++;

                // Integer part: either a single 0 or [1-9][0-9]*. Leading zeros rejected.
                if (_i >= _s.Length) throw Fail("truncated number");
                if (_s[_i] == '0')
                {
                    _i++;
                }
                else if (_s[_i] >= '1' && _s[_i] <= '9')
                {
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
                else
                {
                    throw Fail("invalid number");
                }

                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    int digits = 0;
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') { _i++; digits++; }
                    if (digits == 0) throw Fail("number has no digits after decimal point");
                }

                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    int digits = 0;
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') { _i++; digits++; }
                    if (digits == 0) throw Fail("number has no digits in exponent");
                }

                string token = _s.Substring(start, _i - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw Fail("number '" + token + "' is not representable");

                // 1e400 parses without error and yields Infinity. Treat it as malformed
                // here so nothing downstream has to defend against it again.
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw Fail("number '" + token + "' is not finite");

                return new SqJsonValue(value);
            }
        }
    }
}
