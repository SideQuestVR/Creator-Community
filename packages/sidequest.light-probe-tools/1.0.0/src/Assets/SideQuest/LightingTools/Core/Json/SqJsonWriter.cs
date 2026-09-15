// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Compact JSON writer. No dependency on Newtonsoft: the Creator SDK path may not
    /// provide it, and the matching parser is deliberately ours (see SqJsonParser).
    /// Numbers are written invariant-culture with trailing zeros trimmed, because these
    /// reports are read by an LLM and every wasted byte is a wasted token.
    /// </summary>
    public sealed class SqJsonWriter
    {
        const int MaxDepth = 32;

        readonly StringBuilder _sb = new StringBuilder(8192);
        readonly Stack<bool> _isArray = new Stack<bool>();
        bool _needComma;

        public int Length { get { return _sb.Length; } }
        public int Depth { get { return _isArray.Count; } }

        public override string ToString() { return _sb.ToString(); }

        void Separate()
        {
            if (_needComma) _sb.Append(',');
            _needComma = true;
        }

        void PushScope(bool isArray)
        {
            if (_isArray.Count >= MaxDepth)
                throw new InvalidOperationException("SqJsonWriter exceeded max depth " + MaxDepth);
            _isArray.Push(isArray);
            _needComma = false;
        }

        void PopScope(bool expectArray)
        {
            if (_isArray.Count == 0)
                throw new InvalidOperationException("SqJsonWriter scope underflow");
            bool wasArray = _isArray.Pop();
            if (wasArray != expectArray)
                throw new InvalidOperationException("SqJsonWriter scope mismatch");
            _needComma = true;
        }

        public SqJsonWriter BeginObject() { Separate(); _sb.Append('{'); PushScope(false); return this; }
        public SqJsonWriter EndObject() { PopScope(false); _sb.Append('}'); return this; }
        public SqJsonWriter BeginArray() { Separate(); _sb.Append('['); PushScope(true); return this; }
        public SqJsonWriter EndArray() { PopScope(true); _sb.Append(']'); return this; }

        /// <summary>Writes a key and opens an object as its value.</summary>
        public SqJsonWriter BeginObject(string key) { Key(key); _sb.Append('{'); PushScope(false); return this; }
        public SqJsonWriter BeginArray(string key) { Key(key); _sb.Append('['); PushScope(true); return this; }

        public SqJsonWriter Key(string key)
        {
            Separate();
            WriteString(key);
            _sb.Append(':');
            _needComma = false;
            return this;
        }

        // ---- scalar values (array element form) ----

        public SqJsonWriter Value(string v)
        {
            Separate();
            if (v == null) _sb.Append("null"); else WriteString(v);
            return this;
        }

        public SqJsonWriter Value(bool v) { Separate(); _sb.Append(v ? "true" : "false"); return this; }
        public SqJsonWriter Value(int v) { Separate(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public SqJsonWriter Value(long v) { Separate(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public SqJsonWriter Value(float v) { Separate(); WriteNumber(v); return this; }
        public SqJsonWriter Value(double v) { Separate(); WriteNumber(v); return this; }
        public SqJsonWriter Null() { Separate(); _sb.Append("null"); return this; }

        // ---- key/value pair form ----

        public SqJsonWriter Prop(string key, string v)
        {
            Key(key);
            if (v == null) _sb.Append("null"); else WriteString(v);
            _needComma = true;
            return this;
        }

        public SqJsonWriter Prop(string key, bool v) { Key(key); _sb.Append(v ? "true" : "false"); _needComma = true; return this; }
        public SqJsonWriter Prop(string key, int v) { Key(key); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; return this; }
        public SqJsonWriter Prop(string key, long v) { Key(key); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; return this; }
        public SqJsonWriter Prop(string key, float v) { Key(key); WriteNumber(v); _needComma = true; return this; }
        public SqJsonWriter Prop(string key, double v) { Key(key); WriteNumber(v); _needComma = true; return this; }

        /// <summary>Vectors are arrays, never objects - three keys per vector adds up fast.</summary>
        public SqJsonWriter Prop(string key, Vector3 v)
        {
            Key(key);
            WriteVector(v);
            _needComma = true;
            return this;
        }

        public SqJsonWriter Value(Vector3 v)
        {
            Separate();
            WriteVector(v);
            return this;
        }

        public SqJsonWriter Prop(string key, Bounds b)
        {
            BeginObject(key);
            Prop("c", b.center);
            Prop("s", b.size);
            EndObject();
            return this;
        }

        public SqJsonWriter Prop(string key, Color c)
        {
            Key(key);
            _sb.Append('[');
            WriteNumber(c.r); _sb.Append(',');
            WriteNumber(c.g); _sb.Append(',');
            WriteNumber(c.b);
            _sb.Append(']');
            _needComma = true;
            return this;
        }

        public SqJsonWriter Prop(string key, IList<int> values)
        {
            BeginArray(key);
            for (int i = 0; i < values.Count; i++) Value(values[i]);
            EndArray();
            return this;
        }

        public SqJsonWriter Prop(string key, IList<string> values)
        {
            BeginArray(key);
            for (int i = 0; i < values.Count; i++) Value(values[i]);
            EndArray();
            return this;
        }

        // ---- primitives ----

        void WriteVector(Vector3 v)
        {
            _sb.Append('[');
            WriteNumber(v.x); _sb.Append(',');
            WriteNumber(v.y); _sb.Append(',');
            WriteNumber(v.z);
            _sb.Append(']');
        }

        void WriteNumber(double v)
        {
            // NaN and Infinity are not legal JSON. They are also the signature of a bad
            // calculation upstream, so emit null rather than garbage a parser would accept.
            if (double.IsNaN(v) || double.IsInfinity(v)) { _sb.Append("null"); return; }

            if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
            {
                _sb.Append(((long)v).ToString(CultureInfo.InvariantCulture));
                return;
            }

            string s = v.ToString("0.###", CultureInfo.InvariantCulture);
            _sb.Append(s.Length == 0 || s == "-0" ? "0" : s);
        }

        void WriteString(string s)
        {
            _sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == 0x7f)
                            _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        /// <summary>Throws if any scope is still open - catches a malformed report at the source.</summary>
        public string Finish()
        {
            if (_isArray.Count != 0)
                throw new InvalidOperationException("SqJsonWriter finished with " + _isArray.Count + " scope(s) still open");
            return _sb.ToString();
        }
    }
}
