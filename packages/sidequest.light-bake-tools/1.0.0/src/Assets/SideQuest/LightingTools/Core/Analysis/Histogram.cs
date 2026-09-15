// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Fixed-bucket histogram over a known range.
    ///
    /// A report says "smoothness distribution" in ten integers instead of one float per
    /// material. For 400 materials that is the difference between 30 bytes and 3 KB, and
    /// the ten integers answer the actual question - is this scene glossy? - just as well.
    /// </summary>
    public sealed class Histogram
    {
        readonly int[] _buckets;
        readonly float _min;
        readonly float _max;

        public int Total { get; private set; }

        public Histogram(int bucketCount = 10, float min = 0f, float max = 1f)
        {
            if (bucketCount < 1) bucketCount = 1;
            _buckets = new int[bucketCount];
            _min = min;
            _max = max > min ? max : min + 1f;
        }

        public void Add(float value, int weight = 1)
        {
            if (float.IsNaN(value) || weight <= 0) return;

            float t = (value - _min) / (_max - _min);
            int index = (int)(t * _buckets.Length);
            if (index < 0) index = 0;
            if (index >= _buckets.Length) index = _buckets.Length - 1;

            _buckets[index] += weight;
            Total += weight;
        }

        public IList<int> Buckets { get { return _buckets; } }

        /// <summary>Fraction of samples at or above a value. Bucket-resolution, which is all a report needs.</summary>
        public float FractionAtOrAbove(float value)
        {
            if (Total == 0) return 0f;

            float t = (value - _min) / (_max - _min);
            int start = (int)(t * _buckets.Length);
            if (start < 0) start = 0;

            int count = 0;
            for (int i = start; i < _buckets.Length; i++) count += _buckets[i];
            return (float)count / Total;
        }

        public void Write(SqJsonWriter w, string key)
        {
            w.Prop(key, _buckets);
        }
    }

    /// <summary>Percentiles over a sample list, used everywhere scene scale is derived.</summary>
    public static class Percentile
    {
        /// <summary>
        /// Nearest-rank percentile. Sorts in place - callers pass a scratch list, not
        /// anything they still need in original order.
        /// </summary>
        public static float Of(List<float> samplesSortedInPlace, float fraction)
        {
            if (samplesSortedInPlace == null || samplesSortedInPlace.Count == 0) return 0f;

            samplesSortedInPlace.Sort();
            return OfSorted(samplesSortedInPlace, fraction);
        }

        /// <summary>Percentile of an already-sorted list. Avoids re-sorting for each of p10/p25/p50/...</summary>
        public static float OfSorted(List<float> sorted, float fraction)
        {
            if (sorted == null || sorted.Count == 0) return 0f;

            int index = (int)Math.Round(fraction * (sorted.Count - 1));
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }
    }
}
