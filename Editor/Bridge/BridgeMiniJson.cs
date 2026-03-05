using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Blanketmen.UnityMcp.Bridge.Editor
{
    internal static class BridgeMiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            return Parser.Parse(json);
        }

        private sealed class Parser : IDisposable
        {
            private const string WordBreak = "{}[],:\"";

            private readonly string _json;
            private int _index;

            private Parser(string json)
            {
                _json = json;
            }

            public static object Parse(string json)
            {
                using (var parser = new Parser(json))
                {
                    return parser.ParseValue();
                }
            }

            public void Dispose()
            {
            }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>(StringComparer.Ordinal);
                ConsumeChar();

                while (true)
                {
                    Token token = NextToken;
                    if (token == Token.None)
                    {
                        return null;
                    }

                    if (token == Token.CurlyClose)
                    {
                        ConsumeChar();
                        return table;
                    }

                    if (token != Token.String)
                    {
                        return null;
                    }

                    string name = ParseString();
                    if (name == null)
                    {
                        return null;
                    }

                    if (NextToken != Token.Colon)
                    {
                        return null;
                    }

                    ConsumeChar();
                    object value = ParseValue();
                    table[name] = value;

                    token = NextToken;
                    if (token == Token.Comma)
                    {
                        ConsumeChar();
                        continue;
                    }

                    if (token == Token.CurlyClose)
                    {
                        ConsumeChar();
                        return table;
                    }

                    return null;
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();
                ConsumeChar();

                bool parsing = true;
                while (parsing)
                {
                    Token token = NextToken;
                    if (token == Token.None)
                    {
                        break;
                    }

                    if (token == Token.SquareClose)
                    {
                        ConsumeChar();
                        break;
                    }

                    if (token == Token.Comma)
                    {
                        ConsumeChar();
                        continue;
                    }

                    object value = ParseValue();
                    array.Add(value);
                }

                return array;
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case Token.String:
                        return ParseString();
                    case Token.Number:
                        return ParseNumber();
                    case Token.CurlyOpen:
                        return ParseObject();
                    case Token.SquareOpen:
                        return ParseArray();
                    case Token.True:
                        return true;
                    case Token.False:
                        return false;
                    case Token.Null:
                        return null;
                    default:
                        return null;
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                ConsumeChar();

                while (_index < _json.Length)
                {
                    char c = ConsumeChar();
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_index >= _json.Length)
                    {
                        break;
                    }

                    c = ConsumeChar();
                    switch (c)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append(c);
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
                            if (_index + 4 <= _json.Length)
                            {
                                string hex = _json.Substring(_index, 4);
                                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint codePoint))
                                {
                                    sb.Append((char)codePoint);
                                    _index += 4;
                                }
                            }

                            break;
                    }
                }

                return null;
            }

            private object ParseNumber()
            {
                string number = NextWord;
                if (number.IndexOf('.') >= 0 || number.IndexOf('e') >= 0 || number.IndexOf('E') >= 0)
                {
                    if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
                    {
                        return dbl;
                    }
                }
                else
                {
                    if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lng))
                    {
                        return lng;
                    }
                }

                return 0d;
            }

            private char PeekChar
            {
                get
                {
                    if (_index >= _json.Length)
                    {
                        return '\0';
                    }

                    return _json[_index];
                }
            }

            private char ConsumeChar()
            {
                if (_index >= _json.Length)
                {
                    return '\0';
                }

                return _json[_index++];
            }

            private string NextWord
            {
                get
                {
                    var sb = new StringBuilder();
                    while (_index < _json.Length && !IsWordBreak(PeekChar))
                    {
                        sb.Append(ConsumeChar());
                    }

                    return sb.ToString();
                }
            }

            private Token NextToken
            {
                get
                {
                    EatWhitespace();

                    if (_index >= _json.Length)
                    {
                        return Token.None;
                    }

                    switch (PeekChar)
                    {
                        case '{':
                            return Token.CurlyOpen;
                        case '}':
                            return Token.CurlyClose;
                        case '[':
                            return Token.SquareOpen;
                        case ']':
                            return Token.SquareClose;
                        case ',':
                            return Token.Comma;
                        case '"':
                            return Token.String;
                        case ':':
                            return Token.Colon;
                        case '-':
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                            return Token.Number;
                    }

                    string word = NextWord;
                    if (word == "false")
                    {
                        return Token.False;
                    }

                    if (word == "true")
                    {
                        return Token.True;
                    }

                    if (word == "null")
                    {
                        return Token.Null;
                    }

                    return Token.None;
                }
            }

            private void EatWhitespace()
            {
                while (_index < _json.Length)
                {
                    char c = PeekChar;
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }
            }

            private static bool IsWordBreak(char c)
            {
                return char.IsWhiteSpace(c) || WordBreak.IndexOf(c) != -1;
            }

            private enum Token
            {
                None,
                CurlyOpen,
                CurlyClose,
                SquareOpen,
                SquareClose,
                Colon,
                Comma,
                String,
                Number,
                True,
                False,
                Null,
            }
        }
    }
}
