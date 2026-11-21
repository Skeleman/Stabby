using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Stabby.Content
{
    internal sealed class Json
    {
        enum Kind { Null, Bool, Number, String, Array, Object }

        Kind kind;
        string text;
        bool flag;
        List<Json> items;
        Dictionary<string, Json> fields;
        List<string> order;
        HashSet<string> used;
        string where;

        public static Json Parse(string source, string name)
        {
            var p = new Parser(source, name);
            p.Skip();
            var v = p.Value(name);
            p.Skip();
            if (p.At < source.Length) throw p.Fail("unexpected text after the value");
            return v;
        }

        public bool IsNull => kind == Kind.Null;

        public bool Has(string key) => Field(key) != null;

        Json Field(string key)
        {
            if (kind != Kind.Object) throw Fail("an object");
            used.Add(key);
            return fields.TryGetValue(key, out var v) ? v : null;
        }

        public void End()
        {
            foreach (var k in order)
                if (!used.Contains(k)) throw new InvalidDataException(where + ": unknown field '" + k + "'");
        }

        InvalidDataException Fail(string expected) => new InvalidDataException(where + ": expected " + expected);

        public float Float(string key, float fallback)
        {
            var v = Field(key);
            return v == null ? fallback : v.AsFloat();
        }

        public float? FloatOrNull(string key)
        {
            var v = Field(key);
            return v == null || v.IsNull ? (float?)null : v.AsFloat();
        }

        public int Int(string key, int fallback)
        {
            var v = Field(key);
            return v == null ? fallback : v.AsInt();
        }

        public ulong ULong(string key, ulong fallback)
        {
            var v = Field(key);
            if (v == null) return fallback;
            if (v.kind != Kind.Number || !ulong.TryParse(v.text, NumberStyles.None, CultureInfo.InvariantCulture, out var r)) throw v.Fail("an unsigned integer");
            return r;
        }

        public bool Bool(string key, bool fallback)
        {
            var v = Field(key);
            if (v == null) return fallback;
            if (v.kind != Kind.Bool) throw v.Fail("true or false");
            return v.flag;
        }

        public string String(string key, string fallback)
        {
            var v = Field(key);
            return v == null ? fallback : v.AsString();
        }

        public Json Object(string key)
        {
            var v = Field(key);
            if (v == null || v.IsNull) return null;
            if (v.kind != Kind.Object) throw v.Fail("an object");
            return v;
        }

        public List<Json> Array(string key)
        {
            var v = Field(key);
            if (v == null || v.IsNull) return null;
            if (v.kind != Kind.Array) throw v.Fail("an array");
            return v.items;
        }

        public float[] Floats(string key, float[] fallback)
        {
            if (Field(key) == null) return fallback;
            var a = Array(key);
            if (a == null) return null;
            var r = new float[a.Count];
            for (int k = 0; k < r.Length; k++) r[k] = a[k].AsFloat();
            return r;
        }

        public List<string> Strings(string key, List<string> fallback)
        {
            if (Field(key) == null) return fallback;
            var a = Array(key);
            if (a == null) return null;
            var r = new List<string>(a.Count);
            foreach (var s in a) r.Add(s.AsString());
            return r;
        }

        public List<Json> Objects(string key, bool required = false)
        {
            if (Field(key) == null) return required ? throw Fail("field '" + key + "'") : new List<Json>();
            var a = Array(key);
            if (a == null) return new List<Json>();
            foreach (var o in a)
                if (o.kind != Kind.Object) throw o.Fail("an object");
            return a;
        }

        float AsFloat()
        {
            if (kind != Kind.Number || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) || float.IsInfinity(r)) throw Fail("a number");
            return r;
        }

        int AsInt()
        {
            if (kind != Kind.Number || !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var r)) throw Fail("an integer");
            return r;
        }

        string AsString()
        {
            if (kind == Kind.Null) return null;
            if (kind != Kind.String) throw Fail("a string");
            return text;
        }

        sealed class Parser
        {
            readonly string s;
            readonly string name;
            public int At;

            public Parser(string source, string name)
            {
                s = source ?? throw new InvalidDataException(name + " is empty");
                this.name = name;
            }

            public InvalidDataException Fail(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < At && k < s.Length; k++)
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                return new InvalidDataException(name + " line " + line + ", column " + col + ": " + message);
            }

            public void Skip()
            {
                while (At < s.Length && (s[At] == ' ' || s[At] == '\t' || s[At] == '\n' || s[At] == '\r')) At++;
            }

            public Json Value(string path)
            {
                if (At >= s.Length) throw Fail("unexpected end of text");
                char c = s[At];
                if (c == '{') return Obj(path);
                if (c == '[') return Arr(path);
                if (c == '"') return new Json { kind = Kind.String, text = Str(), where = path };
                if (c == '-' || (c >= '0' && c <= '9')) return new Json { kind = Kind.Number, text = Num(), where = path };
                if (Word("true")) return new Json { kind = Kind.Bool, flag = true, where = path };
                if (Word("false")) return new Json { kind = Kind.Bool, where = path };
                if (Word("null")) return new Json { kind = Kind.Null, where = path };
                throw Fail("unexpected character '" + c + "'");
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(s, At, w, 0, w.Length) != 0) return false;
                At += w.Length;
                return true;
            }

            Json Obj(string path)
            {
                var o = new Json { kind = Kind.Object, fields = new Dictionary<string, Json>(StringComparer.Ordinal), order = new List<string>(), used = new HashSet<string>(StringComparer.Ordinal), where = path };
                At++;
                Skip();
                if (At < s.Length && s[At] == '}') { At++; return o; }
                while (true)
                {
                    Skip();
                    if (At >= s.Length || s[At] != '"') throw Fail("expected a field name");
                    var key = Str();
                    Skip();
                    if (At >= s.Length || s[At] != ':') throw Fail("expected ':'");
                    At++;
                    Skip();
                    var v = Value(path + "." + key);
                    if (!o.fields.ContainsKey(key)) o.order.Add(key);
                    o.fields[key] = v;
                    Skip();
                    if (At < s.Length && s[At] == ',') { At++; continue; }
                    if (At < s.Length && s[At] == '}') { At++; return o; }
                    throw Fail("expected ',' or '}'");
                }
            }

            Json Arr(string path)
            {
                var a = new Json { kind = Kind.Array, items = new List<Json>(), where = path };
                At++;
                Skip();
                if (At < s.Length && s[At] == ']') { At++; return a; }
                while (true)
                {
                    Skip();
                    a.items.Add(Value(path + "[" + a.items.Count.ToString(CultureInfo.InvariantCulture) + "]"));
                    Skip();
                    if (At < s.Length && s[At] == ',') { At++; continue; }
                    if (At < s.Length && s[At] == ']') { At++; return a; }
                    throw Fail("expected ',' or ']'");
                }
            }

            string Num()
            {
                int start = At;
                if (s[At] == '-') At++;
                if (At < s.Length && s[At] == '0') At++;
                else if (!Digits()) throw Fail("bad number");
                if (At < s.Length && s[At] == '.') { At++; if (!Digits()) throw Fail("bad number"); }
                if (At < s.Length && (s[At] == 'e' || s[At] == 'E'))
                {
                    At++;
                    if (At < s.Length && (s[At] == '+' || s[At] == '-')) At++;
                    if (!Digits()) throw Fail("bad number");
                }
                return s.Substring(start, At - start);
            }

            bool Digits()
            {
                int start = At;
                while (At < s.Length && s[At] >= '0' && s[At] <= '9') At++;
                return At > start;
            }

            string Str()
            {
                At++;
                var b = new StringBuilder();
                while (true)
                {
                    if (At >= s.Length) throw Fail("unterminated string");
                    char c = s[At++];
                    if (c == '"') return b.ToString();
                    if (c < ' ') throw Fail("control character in string");
                    if (c != '\\') { b.Append(c); continue; }
                    if (At >= s.Length) throw Fail("unterminated string");
                    char e = s[At++];
                    switch (e)
                    {
                        case '"': b.Append('"'); break;
                        case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            if (At + 4 > s.Length || !int.TryParse(s.AsSpan(At, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int u)) throw Fail("bad \\u escape");
                            b.Append((char)u);
                            At += 4;
                            break;
                        default: throw Fail("bad escape");
                    }
                }
            }
        }
    }

    internal sealed class JsonWriter
    {
        readonly StringBuilder b = new StringBuilder();
        readonly string nl = Environment.NewLine;
        readonly List<bool> first = new List<bool>();

        public override string ToString() => b.ToString();

        void Indent()
        {
            b.Append(nl);
            b.Append(' ', 2 * first.Count);
        }

        void Next()
        {
            if (first.Count == 0) return;
            if (!first[first.Count - 1]) b.Append(',');
            first[first.Count - 1] = false;
            Indent();
        }

        public JsonWriter Key(string k)
        {
            Next();
            Quote(k);
            b.Append(": ");
            return this;
        }

        void Open(char c)
        {
            b.Append(c);
            first.Add(true);
        }

        void Close(char c)
        {
            bool empty = first[first.Count - 1];
            first.RemoveAt(first.Count - 1);
            if (!empty) Indent();
            b.Append(c);
        }

        public JsonWriter BeginObject() { Open('{'); return this; }
        public JsonWriter EndObject() { Close('}'); return this; }
        public JsonWriter BeginArray() { Open('['); return this; }
        public JsonWriter EndArray() { Close(']'); return this; }

        public JsonWriter Item() { Next(); return this; }

        public JsonWriter Value(float v) { b.Append(v.ToString("R", CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(int v) { b.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(ulong v) { b.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(string v) { Quote(v); return this; }

        public JsonWriter Floats(float[] v)
        {
            BeginArray();
            foreach (var f in v) Item().Value(f);
            return EndArray();
        }

        void Quote(string v)
        {
            b.Append('"');
            foreach (char c in v)
            {
                switch (c)
                {
                    case '\\': b.Append("\\\\"); continue;
                    case '\n': b.Append("\\n"); continue;
                    case '\r': b.Append("\\r"); continue;
                    case '\t': b.Append("\\t"); continue;
                    case '\b': b.Append("\\b"); continue;
                    case '\f': b.Append("\\f"); continue;
                }
                if (c < ' ' || c > '~' || c == '"' || c == '<' || c == '>' || c == '&' || c == '\'' || c == '+' || c == '`')
                    b.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            b.Append('"');
        }
    }
}
