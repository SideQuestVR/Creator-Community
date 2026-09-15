// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Uniform spatial hash over world-space points.
    ///
    /// Exists mainly to replace the prior-art merge pass, which was an O(n squared) scan
    /// that compared every candidate probe against every probe kept so far. On a scene
    /// producing 20,000 raw samples that is 200 million distance tests before decimation
    /// even finishes; here it is one bucket lookup per point.
    /// </summary>
    public sealed class SpatialHash
    {
        readonly float _cellSize;
        readonly float _inverseCellSize;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        readonly List<Vector3> _points = new List<Vector3>();

        public SpatialHash(float cellSize)
        {
            _cellSize = Mathf.Max(cellSize, 0.0001f);
            _inverseCellSize = 1f / _cellSize;
        }

        public int Count { get { return _points.Count; } }
        public Vector3 GetPoint(int index) { return _points[index]; }

        public int Insert(Vector3 point)
        {
            int index = _points.Count;
            _points.Add(point);

            long key = KeyOf(point);
            List<int> bucket;
            if (!_cells.TryGetValue(key, out bucket))
            {
                bucket = new List<int>(4);
                _cells[key] = bucket;
            }
            bucket.Add(index);
            return index;
        }

        /// <summary>Indices in the 27 cells surrounding a point. Appends; does not clear.</summary>
        public void QueryNeighbourhood(Vector3 point, List<int> results)
        {
            int cx = Mathf.FloorToInt(point.x * _inverseCellSize);
            int cy = Mathf.FloorToInt(point.y * _inverseCellSize);
            int cz = Mathf.FloorToInt(point.z * _inverseCellSize);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        List<int> bucket;
                        if (_cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out bucket))
                            results.AddRange(bucket);
                    }
        }

        /// <summary>True if any inserted point lies within radius.</summary>
        public bool HasPointWithin(Vector3 point, float radius)
        {
            var scratch = new List<int>(16);
            QueryNeighbourhood(point, scratch);

            float sqr = radius * radius;
            for (int i = 0; i < scratch.Count; i++)
            {
                if ((_points[scratch[i]] - point).sqrMagnitude <= sqr) return true;
            }
            return false;
        }

        long KeyOf(Vector3 p)
        {
            return Key(
                Mathf.FloorToInt(p.x * _inverseCellSize),
                Mathf.FloorToInt(p.y * _inverseCellSize),
                Mathf.FloorToInt(p.z * _inverseCellSize));
        }

        /// <summary>
        /// Packs three cell coordinates into one key. 21 bits each covers +/-1,000,000
        /// cells per axis, far beyond any plausible scene, and collisions between distinct
        /// cells are impossible within that range rather than merely unlikely.
        /// </summary>
        static long Key(int x, int y, int z)
        {
            const long mask = 0x1FFFFF;
            return ((x & mask) << 42) | ((y & mask) << 21) | (z & mask);
        }

        /// <summary>
        /// Thins a point set so no two survivors are closer than minDistance.
        ///
        /// Each occupied cell keeps its medoid - the input point nearest that cell's
        /// centroid - rather than the centroid itself. The prior-art merge averaged pairs
        /// as it went, which drifts a cluster centre toward whichever point happened to
        /// arrive last and, worse, invents positions that were never sampled: a probe
        /// carefully placed 30cm off a wall can end up inside it. Keeping a real sample
        /// cannot do that.
        ///
        /// Deterministic: ties are broken by insertion order, so the same input always
        /// gives the same output.
        /// </summary>
        public static List<Vector3> DecimateMedoid(IList<Vector3> points, float minDistance)
        {
            var result = new List<Vector3>();
            if (points == null || points.Count == 0) return result;

            if (minDistance <= 0.0001f)
            {
                result.AddRange(points);
                return result;
            }

            float inverseCell = 1f / minDistance;
            var buckets = new Dictionary<long, List<int>>();
            var order = new List<long>();

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                long key = Key(
                    Mathf.FloorToInt(p.x * inverseCell),
                    Mathf.FloorToInt(p.y * inverseCell),
                    Mathf.FloorToInt(p.z * inverseCell));

                List<int> bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new List<int>(4);
                    buckets[key] = bucket;
                    order.Add(key); // preserve first-seen order for deterministic output
                }
                bucket.Add(i);
            }

            for (int b = 0; b < order.Count; b++)
            {
                List<int> bucket = buckets[order[b]];

                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < bucket.Count; i++) centroid += points[bucket[i]];
                centroid /= bucket.Count;

                int best = bucket[0];
                float bestSqr = (points[best] - centroid).sqrMagnitude;
                for (int i = 1; i < bucket.Count; i++)
                {
                    float sqr = (points[bucket[i]] - centroid).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = bucket[i]; }
                }

                result.Add(points[best]);
            }

            return result;
        }
    }
}
