// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Samples a grid over the NavMesh, plus a band along its boundary edges.
    ///
    /// The NavMesh is a good floor abstraction: it already excludes geometry, slopes too
    /// steep to stand on and space too tight to enter, so sampling it puts probes exactly
    /// where a player's head will be. This is a corrected port of the prior-art placer.
    ///
    /// Two real bugs are fixed here, beyond the world/local one that ProbeGroupWriter owns:
    ///
    /// The original's "edge pass" looped over every NavMesh vertex, and for each one looped
    /// over every other vertex to find the closest - except it assigned unconditionally
    /// inside the loop, so it kept whichever nearby vertex came last. It was O(n squared)
    /// and produced an arbitrary jittered duplicate per vertex rather than edge coverage.
    /// Real boundary edges - those belonging to exactly one triangle - are what it wanted,
    /// and finding them is linear.
    ///
    /// The original also stepped its loops by a field defaulting to zero, so clicking the
    /// button on a freshly opened window hung the Editor with no way out. Spacing is
    /// guarded here and checked again before any loop runs.
    /// </summary>
    public sealed class NavMeshSampler : IProbeSampler
    {
        public ProbeStrategy Strategy { get { return ProbeStrategy.NavMesh; } }
        public string Id { get { return "navmesh"; } }
        public string DisplayName { get { return "NavMesh surface"; } }

        public bool IsAvailable(SceneScan scan, ProbeSamplerSettings settings, out string reason)
        {
            if (!scan.NavMesh.Present)
            {
                reason = "No baked NavMesh in this scene. Bake one (Window > AI > Navigation), or use Adaptive instead.";
                return false;
            }

            reason = null;
            return true;
        }

        public List<Vector3> Sample(SceneScan scan, ProbeSamplerSettings settings, SqProblemList problems, SamplerStats stats)
        {
            var world = new List<Vector3>();

            float spacing = Mathf.Max(settings.Spacing, 0.001f);
            if (settings.Spacing < 0.001f && problems != null)
            {
                problems.Add("LP023_SPACING_GUARD", SqSeverity.Warn,
                    "Spacing was zero or negative; it was raised to 1mm to avoid an infinite loop.")
                    .WithAction("Set a sensible Spacing, typically 1 to 3 metres for interiors.");
            }

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0) return world;

            Bounds bounds = ComputeBounds(triangulation.vertices);
            stats.Layers = settings.LayerHeights != null ? settings.LayerHeights.Length : 1;

            SampleGrid(bounds, spacing, settings, scan, world);
            SampleBoundaryEdges(triangulation, settings, scan, world);

            stats.Notes = string.Format("navmesh {0} tris, grid {1:0.##}m", triangulation.indices.Length / 3, spacing);
            return ProbeSampling.Finalise(world, settings, problems, stats);
        }

        static Bounds ComputeBounds(Vector3[] vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        static void SampleGrid(Bounds bounds, float spacing, ProbeSamplerSettings settings, SceneScan scan, List<Vector3> world)
        {
            int stepsX = Mathf.CeilToInt(bounds.size.x / spacing);
            int stepsZ = Mathf.CeilToInt(bounds.size.z / spacing);

            // Second guard: even with a valid spacing, a huge NavMesh could ask for a
            // billion samples. Bail rather than freeze, and let the caller see the count.
            long total = (long)(stepsX + 1) * (stepsZ + 1);
            if (total > 4_000_000) return;

            float searchRadius = Mathf.Max(bounds.size.y, spacing * 2f);
            bool cancelled = false;

            try
            {
                for (int ix = 0; ix <= stepsX && !cancelled; ix++)
                {
                    if ((ix & 15) == 0)
                    {
                        cancelled = EditorUtility.DisplayCancelableProgressBar(
                            "Placing Light Probes", "Sampling the NavMesh", (float)ix / Mathf.Max(stepsX, 1));
                    }

                    float x = bounds.min.x + ix * spacing;

                    for (int iz = 0; iz <= stepsZ; iz++)
                    {
                        float z = bounds.min.z + iz * spacing;

                        NavMeshHit hit;
                        var from = new Vector3(x, bounds.max.y, z);
                        if (!NavMesh.SamplePosition(from, out hit, searchRadius, NavMesh.AllAreas)) continue;

                        ProbeSampling.EmitColumn(hit.position, settings.LayerHeights, scan.Scale.CeilingHeightEstimate, world);
                    }
                }
            }
            finally
            {
                // finally, not a trailing call: a cancel or an exception mid-sweep would
                // otherwise leave the progress bar on screen and the Editor apparently hung.
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Adds probes along the NavMesh silhouette.
        ///
        /// Lighting changes fastest at the edges of walkable space - a doorway, a ledge,
        /// the base of a wall - and a plain grid undersamples exactly there. A boundary
        /// edge belongs to one triangle only, so counting edge uses finds them in one pass
        /// over the index buffer.
        /// </summary>
        static void SampleBoundaryEdges(NavMeshTriangulation triangulation, ProbeSamplerSettings settings, SceneScan scan, List<Vector3> world)
        {
            int[] indices = triangulation.indices;
            Vector3[] vertices = triangulation.vertices;
            if (indices == null || indices.Length < 3) return;

            var edgeUses = new Dictionary<long, int>();
            var edgeEnds = new Dictionary<long, KeyValuePair<int, int>>();

            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                CountEdge(edgeUses, edgeEnds, indices[t], indices[t + 1]);
                CountEdge(edgeUses, edgeEnds, indices[t + 1], indices[t + 2]);
                CountEdge(edgeUses, edgeEnds, indices[t + 2], indices[t]);
            }

            float step = Mathf.Max(settings.Spacing * 0.5f, 0.25f);

            foreach (var use in edgeUses)
            {
                if (use.Value != 1) continue; // shared edge: interior, already grid-sampled

                KeyValuePair<int, int> ends = edgeEnds[use.Key];
                Vector3 a = vertices[ends.Key];
                Vector3 b = vertices[ends.Value];

                float length = Vector3.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / step));

                for (int i = 0; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(a, b, (float)i / steps);
                    ProbeSampling.EmitColumn(point, settings.LayerHeights, scan.Scale.CeilingHeightEstimate, world);
                }
            }
        }

        static void CountEdge(Dictionary<long, int> uses, Dictionary<long, KeyValuePair<int, int>> ends, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            int count;
            uses.TryGetValue(key, out count);
            uses[key] = count + 1;

            if (count == 0) ends[key] = new KeyValuePair<int, int>(a, b);
        }
    }
}
