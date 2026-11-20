using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTP.Treatment
{
    /// <summary>
    /// A small recursive-descent JSON reader and writer helpers for the treatment API. JsonUtility cannot read the
    /// profile: the water-delivery subfactor's "start" is a string while every other "start" is a number, and several
    /// fields are null. Objects become <see cref="Dictionary{TKey,TValue}"/> (string to object), arrays
    /// <see cref="List{T}"/> of object, numbers double, and strings, booleans and null keep their type.
    /// </summary>
    public static class SimpleJson
    {
        /// <summary>Parses <paramref name="json"/>; returns false with <paramref name="error"/> for anything malformed.</summary>
        public static bool TryParse(string json, out object value, out string error)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The response was empty.";
                return false;
            }

            var reader = new Reader(json);
            try
            {
                reader.SkipWhitespace();
                value = reader.ReadValue(0);
                reader.SkipWhitespace();
                if (!reader.AtEnd)
                    throw reader.Fail("unexpected text after the value");
                error = null;
                return true;
            }
            catch (FormatException exception)
            {
                value = null;
                error = "The response was not valid JSON: " + exception.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ typed access

        public static Dictionary<string, object> Object(object value) => value as Dictionary<string, object>;

        public static List<object> Array(object value) => value as List<object>;

        public static object Get(Dictionary<string, object> obj, string key) =>
            obj != null && key != null && obj.TryGetValue(key, out var value) ? value : null;

        public static string String(Dictionary<string, object> obj, string key) => Get(obj, key) as string;

        public static bool TryNumber(Dictionary<string, object> obj, string key, out double number)
        {
            if (Get(obj, key) is double d)
            {
                number = d;
                return true;
            }

            number = 0d;
            return false;
        }

        public static double Number(Dictionary<string, object> obj, string key, double fallback = 0d) =>
            TryNumber(obj, key, out var number) ? number : fallback;

        public static bool Bool(Dictionary<string, object> obj, string key, bool fallback = false) =>
            Get(obj, key) is bool b ? b : fallback;

        public static bool IsNull(Dictionary<string, object> obj, string key) => obj != null && obj.ContainsKey(key) && obj[key] == null;

        // ------------------------------------------------------------------ writing

        /// <summary>A JSON string literal (quotes, escapes and control characters handled).</summary>
        public static string Quote(string text)
        {
            if (text == null)
                return "null";
            var builder = new StringBuilder(text.Length + 2);
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// A JSON number in the invariant culture. Whole values are written without a fraction ("25", not "25.0"), so a
        /// strict integer field accepts them; NaN and infinity (not JSON) become 0.
        /// </summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "0";
            var rounded = Math.Round(value, 6);
            if (Math.Abs(rounded - Math.Round(rounded)) < 1e-9 && Math.Abs(rounded) < 1e15)
                return ((long)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);
            return rounded.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>A JSON integer (rounded) for fields the server types as int.</summary>
        public static string Integer(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? "0" : ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ reader

        sealed class Reader
        {
            const int MaxDepth = 64;
            readonly string text;
            int position;

            public Reader(string json) => text = json;

            public bool AtEnd => position >= text.Length;

            public FormatException Fail(string message) => new FormatException($"{message} at character {position}");

            public void SkipWhitespace()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                    position++;
            }

            char Peek() => position < text.Length ? text[position] : '\0';

            void Expect(char c)
            {
                if (Peek() != c)
                    throw Fail($"expected '{c}'");
                position++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth)
                    throw Fail("nesting too deep");
                SkipWhitespace();
                var c = Peek();
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadWord("true"); return true;
                    case 'f': ReadWord("false"); return false;
                    case 'n': ReadWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Fail(AtEnd ? "unexpected end" : $"unexpected '{c}'");
                }
            }

            void ReadWord(string word)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
                    throw Fail($"expected '{word}'");
                position += word.Length;
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                Expect('{');
                var result = new Dictionary<string, object>();
                SkipWhitespace();
                if (Peek() == '}')
                {
                    position++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        position++;
                        continue;
                    }

                    Expect('}');
                    return result;
                }
            }

            List<object> ReadArray(int depth)
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    position++;
                    return result;
                }

                while (true)
                {
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        position++;
                        continue;
                    }

                    Expect(']');
                    return result;
                }
            }

            string ReadString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Fail("unterminated string");
                    var c = text[position++];
                    if (c == '"')
                        return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        throw Fail("unterminated escape");
                    var e = text[position++];
                    switch (e)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length ||
                                !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw Fail("bad unicode escape");
                            builder.Append((char)code);
                            position += 4;
                            break;
                        default:
                            throw Fail($"bad escape '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                var start = position;
                if (Peek() == '-')
                    position++;
                while (position < text.Length)
                {
                    var c = text[position];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                        position++;
                    else
                        break;
                }

                var token = text.Substring(start, position - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Fail($"bad number '{token}'");
                return number;
            }
        }
    }
}
