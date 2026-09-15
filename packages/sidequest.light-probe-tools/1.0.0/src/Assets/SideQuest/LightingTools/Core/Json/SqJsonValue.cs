// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    public enum SqJsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Immutable parsed JSON node.
    ///
    /// Every accessor takes a fallback and never throws: a decision plan arrives from
    /// outside the Editor, so "missing" and "wrong type" are normal inputs, not
    /// exceptional ones. Callers that need a value to be present ask via TryGet and
    /// record a ValidationIssue instead of relying on an exception.
    /// </summary>
    public sealed class SqJsonValue
    {
        public static readonly SqJsonValue Null = new SqJsonValue();

        public SqJsonType Type { get; private set; }

        readonly bool _bool;
        readonly double _number;
        readonly string _string;
        readonly List<SqJsonValue> _array;
        readonly Dictionary<string, SqJsonValue> _object;

        SqJsonValue() { Type = SqJsonType.Null; }
        public SqJsonValue(bool v) { Type = SqJsonType.Bool; _bool = v; }
        public SqJsonValue(double v) { Type = SqJsonType.Number; _number = v; }
        public SqJsonValue(string v) { Type = SqJsonType.String; _string = v; }
        public SqJsonValue(List<SqJsonValue> v) { Type = SqJsonType.Array; _array = v; }
        public SqJsonValue(Dictionary<string, SqJsonValue> v) { Type = SqJsonType.Object; _object = v; }

        public bool IsNull { get { return Type == SqJsonType.Null; } }
        public bool IsObject { get { return Type == SqJsonType.Object; } }
        public bool IsArray { get { return Type == SqJsonType.Array; } }
        public bool IsNumber { get { return Type == SqJsonType.Number; } }
        public bool IsString { get { return Type == SqJsonType.String; } }
        public bool IsBool { get { return Type == SqJsonType.Bool; } }

        /// <summary>Element count for arrays and objects; 0 for scalars.</summary>
        public int Count
        {
            get
            {
                if (Type == SqJsonType.Array) return _array.Count;
                if (Type == SqJsonType.Object) return _object.Count;
                return 0;
            }
        }

        public IEnumerable<string> Keys
        {
            get
            {
                if (Type != SqJsonType.Object) yield break;
                foreach (var k in _object.Keys) yield return k;
            }
        }

        /// <summary>Array indexer. Out of range yields Null rather than throwing.</summary>
        public SqJsonValue this[int index]
        {
            get
            {
                if (Type != SqJsonType.Array || index < 0 || index >= _array.Count) return Null;
                return _array[index];
            }
        }

        /// <summary>Object indexer. A missing key yields Null rather than throwing.</summary>
        public SqJsonValue this[string key]
        {
            get
            {
                if (Type != SqJsonType.Object || key == null) return Null;
                SqJsonValue v;
                return _object.TryGetValue(key, out v) ? v : Null;
            }
        }

        public bool Has(string key)
        {
            return Type == SqJsonType.Object && key != null && _object.ContainsKey(key);
        }

        // ---- scalar reads ----

        public string AsString(string fallback)
        {
            return Type == SqJsonType.String ? _string : fallback;
        }

        public bool AsBool(bool fallback)
        {
            return Type == SqJsonType.Bool ? _bool : fallback;
        }

        public double AsDouble(double fallback)
        {
            if (Type != SqJsonType.Number) return fallback;
            if (double.IsNaN(_number) || double.IsInfinity(_number)) return fallback;
            return _number;
        }

        public float AsFloat(float fallback)
        {
            double d = AsDouble(double.NaN);
            if (double.IsNaN(d) || d > float.MaxValue || d < float.MinValue) return fallback;
            return (float)d;
        }

        public int AsInt(int fallback)
        {
            double d = AsDouble(double.NaN);
            if (double.IsNaN(d) || d > int.MaxValue || d < int.MinValue) return fallback;
            return (int)Math.Round(d);
        }

        /// <summary>
        /// Reads a 3-element numeric array. Returns false unless all three components are
        /// present and finite - a half-parsed position is worse than no position.
        /// </summary>
        public bool TryGetVector3(out Vector3 result)
        {
            result = Vector3.zero;
            if (Type != SqJsonType.Array || _array.Count != 3) return false;

            float x = _array[0].AsFloat(float.NaN);
            float y = _array[1].AsFloat(float.NaN);
            float z = _array[2].AsFloat(float.NaN);
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) return false;
            if (float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return false;

            result = new Vector3(x, y, z);
            return true;
        }

        /// <summary>True if this subtree contains any non-finite number.</summary>
        public bool ContainsNonFinite()
        {
            switch (Type)
            {
                case SqJsonType.Number:
                    return double.IsNaN(_number) || double.IsInfinity(_number);
                case SqJsonType.Array:
                    for (int i = 0; i < _array.Count; i++)
                        if (_array[i].ContainsNonFinite()) return true;
                    return false;
                case SqJsonType.Object:
                    foreach (var kv in _object)
                        if (kv.Value.ContainsNonFinite()) return true;
                    return false;
                default:
                    return false;
            }
        }
    }
}
