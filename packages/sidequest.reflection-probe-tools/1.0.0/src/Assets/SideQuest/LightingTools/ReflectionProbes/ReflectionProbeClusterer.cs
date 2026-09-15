// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// Groups glossy surfaces so each cluster can be served by one probe.
    ///
    /// A room is not always one probe's worth of space. A long hall, an L-shaped area or a
    /// mezzanine over a floor all need more than one capture point, and putting a single
    /// probe at the average of all of them puts it where none of the glossy surfaces
    /// actually are. Weighting by reflection weight rather than counting renderers keeps a
    /// mirrored floor from being outvoted by forty matte crates.
    /// </summary>
    public static class ReflectionProbeClusterer
    {
        public struct Sample
        {
            public Vector3 Position;
            public float Weight;
        }

        public sealed class Cluster
        {
            public Vector3 Centroid;
            public float TotalWeight;
            public Bounds Bounds;
            public int Count;
        }

        /// <summary>
        /// Weighted k-means with k-means++ seeding.
        ///
        /// Seeded from a fixed sequence rather than UnityEngine.Random: the same scene must
        /// produce the same plan every time, or a re-analysis would show spurious probe
        /// moves and a reviewer could never tell a real change from noise.
        /// </summary>
        public static List<Cluster> Build(IList<Sample> samples, int k, int iterations = 12)
        {
            var clusters = new List<Cluster>();
            if (samples == null || samples.Count == 0 || k < 1) return clusters;

            if (k >= samples.Count)
            {
                for (int i = 0; i < samples.Count; i++)
                    clusters.Add(Single(samples[i]));
                return clusters;
            }

            var rng = new DeterministicRandom(0x5EED);
            List<Vector3> centres = SeedPlusPlus(samples, k, rng);
            var assignment = new int[samples.Count];

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool moved = Assign(samples, centres, assignment);
                Recentre(samples, assignment, centres);
                if (!moved) break; // converged: further passes would change nothing
            }

            return Collect(samples, assignment, centres);
        }

        static Cluster Single(Sample sample)
        {
            return new Cluster
            {
                Centroid = sample.Position,
                TotalWeight = sample.Weight,
                Bounds = new Bounds(sample.Position, Vector3.zero),
                Count = 1
            };
        }

        /// <summary>
        /// k-means++ seeding, weighted.
        ///
        /// Picking initial centres at random routinely drops two of them in the same room
        /// and none in the next, and plain k-means never recovers from that. Choosing each
        /// new centre with probability proportional to its distance from the nearest
        /// existing one spreads them out from the start.
        /// </summary>
        static List<Vector3> SeedPlusPlus(IList<Sample> samples, int k, DeterministicRandom rng)
        {
            var centres = new List<Vector3>(k);

            // First centre: the heaviest sample, not a random one. It is the surface most
            // likely to show a wrong reflection, so it should anchor a probe.
            int heaviest = 0;
            for (int i = 1; i < samples.Count; i++)
                if (samples[i].Weight > samples[heaviest].Weight) heaviest = i;
            centres.Add(samples[heaviest].Position);

            var distances = new float[samples.Count];

            while (centres.Count < k)
            {
                double total = 0.0;

                for (int i = 0; i < samples.Count; i++)
                {
                    float nearest = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float sqr = (samples[i].Position - centres[c]).sqrMagnitude;
                        if (sqr < nearest) nearest = sqr;
                    }

                    distances[i] = nearest * Mathf.Max(samples[i].Weight, 0.0001f);
                    total += distances[i];
                }

                if (total <= 0.0) break; // every sample coincides with a centre

                double target = rng.NextDouble() * total;
                double running = 0.0;
                int chosen = samples.Count - 1;

                for (int i = 0; i < samples.Count; i++)
                {
                    running += distances[i];
                    if (running >= target) { chosen = i; break; }
                }

                centres.Add(samples[chosen].Position);
            }

            return centres;
        }

        static bool Assign(IList<Sample> samples, List<Vector3> centres, int[] assignment)
        {
            bool moved = false;

            for (int i = 0; i < samples.Count; i++)
            {
                int best = 0;
                float bestSqr = (samples[i].Position - centres[0]).sqrMagnitude;

                for (int c = 1; c < centres.Count; c++)
                {
                    float sqr = (samples[i].Position - centres[c]).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = c; }
                }

                if (assignment[i] != best) { assignment[i] = best; moved = true; }
            }

            return moved;
        }

        static void Recentre(IList<Sample> samples, int[] assignment, List<Vector3> centres)
        {
            var sums = new Vector3[centres.Count];
            var weights = new float[centres.Count];

            for (int i = 0; i < samples.Count; i++)
            {
                int c = assignment[i];
                float weight = Mathf.Max(samples[i].Weight, 0.0001f);
                sums[c] += samples[i].Position * weight;
                weights[c] += weight;
            }

            for (int c = 0; c < centres.Count; c++)
            {
                // An emptied cluster keeps its previous centre rather than collapsing to
                // the origin, which would drag a probe to world zero.
                if (weights[c] > 0f) centres[c] = sums[c] / weights[c];
            }
        }

        static List<Cluster> Collect(IList<Sample> samples, int[] assignment, List<Vector3> centres)
        {
            var clusters = new List<Cluster>();
            var byIndex = new Dictionary<int, Cluster>();

            for (int i = 0; i < samples.Count; i++)
            {
                int c = assignment[i];

                Cluster cluster;
                if (!byIndex.TryGetValue(c, out cluster))
                {
                    cluster = new Cluster
                    {
                        Centroid = centres[c],
                        Bounds = new Bounds(samples[i].Position, Vector3.zero)
                    };
                    byIndex[c] = cluster;
                    clusters.Add(cluster);
                }

                cluster.Bounds.Encapsulate(samples[i].Position);
                cluster.TotalWeight += samples[i].Weight;
                cluster.Count++;
            }

            clusters.Sort((a, b) => b.TotalWeight.CompareTo(a.TotalWeight));
            return clusters;
        }

        /// <summary>
        /// A small xorshift generator.
        ///
        /// Deliberately not UnityEngine.Random: that shares global state with everything
        /// else in the Editor, so the plan produced would depend on whatever else happened
        /// to draw a random number first.
        /// </summary>
        sealed class DeterministicRandom
        {
            uint _state;

            public DeterministicRandom(uint seed) { _state = seed == 0 ? 1u : seed; }

            public uint NextUInt()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }

            public double NextDouble() { return NextUInt() / (double)uint.MaxValue; }
        }
    }
}
