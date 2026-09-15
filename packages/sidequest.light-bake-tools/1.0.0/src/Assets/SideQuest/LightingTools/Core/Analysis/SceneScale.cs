// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Scene-wide size metrics that every heuristic in the suite is expressed in terms of.
    ///
    /// Absolute constants do not survive contact with real content: 2 metre probe spacing
    /// is sensible in a corridor and absurd in a landscape. Deriving voxel size, occluder
    /// thresholds and probe density from percentiles of actual renderer sizes is what lets
    /// one set of defaults work across wildly different scenes.
    ///
    /// Percentiles rather than mean or max, because a single skybox-sized backdrop or one
    /// stray far-off object would drag both of those somewhere useless.
    /// </summary>
    public sealed class SceneScale
    {
        public Bounds WorldBounds;
        public bool HasBounds;

        public int RendererCount;
        public long TotalTriangles;

        public float ExtentP10, ExtentP25, ExtentP50, ExtentP75, ExtentP90, ExtentMax;

        /// <summary>Voxel edge length for the occupancy grid and zone segmentation.</summary>
        public float CellSize = 2f;

        /// <summary>Rough floor area, from the world bounds footprint.</summary>
        public float FloorAreaEstimate;

        /// <summary>Estimated interior ceiling height, used to clamp probe layer heights.</summary>
        public float CeilingHeightEstimate = 3f;

        public static SceneScale Compute(IList<RendererFacts> renderers)
        {
            var scale = new SceneScale();
            if (renderers == null || renderers.Count == 0) return scale;

            var extents = new List<float>(renderers.Count);
            Bounds bounds = default(Bounds);
            bool first = true;

            for (int i = 0; i < renderers.Count; i++)
            {
                RendererFacts r = renderers[i];
                if (!r.HasBounds) continue;

                if (first) { bounds = r.WorldBounds; first = false; }
                else bounds.Encapsulate(r.WorldBounds);

                scale.RendererCount++;
                scale.TotalTriangles += r.TriangleCount;

                // Largest axis, not the diagonal: it is what decides whether an object
                // reads as a wall, a prop or a backdrop.
                Vector3 s = r.WorldBounds.size;
                extents.Add(Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }

            if (first) return scale;

            scale.WorldBounds = bounds;
            scale.HasBounds = true;
            scale.FloorAreaEstimate = bounds.size.x * bounds.size.z;

            extents.Sort();
            scale.ExtentP10 = Percentile.OfSorted(extents, 0.10f);
            scale.ExtentP25 = Percentile.OfSorted(extents, 0.25f);
            scale.ExtentP50 = Percentile.OfSorted(extents, 0.50f);
            scale.ExtentP75 = Percentile.OfSorted(extents, 0.75f);
            scale.ExtentP90 = Percentile.OfSorted(extents, 0.90f);
            scale.ExtentMax = extents[extents.Count - 1];

            // Clamped hard at both ends: too fine and the occupancy grid explodes on a
            // large scene, too coarse and doorways vanish so every room merges into one.
            scale.CellSize = Mathf.Clamp(2f * scale.ExtentP50, 1.0f, 8.0f);

            // Interiors are rarely taller than a few metres; a tall world bounds usually
            // means an outdoor shell or a skybox proxy rather than a tall room.
            scale.CeilingHeightEstimate = Mathf.Clamp(bounds.size.y, 2.2f, 6f);

            return scale;
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject("scale");
            if (HasBounds) w.Prop("worldBounds", WorldBounds);
            w.Prop("rendererCount", RendererCount);
            w.Prop("totalTris", TotalTriangles);
            w.Prop("floorArea", FloorAreaEstimate);
            w.Prop("cellSize", CellSize);
            w.Prop("ceilingHeight", CeilingHeightEstimate);

            w.BeginObject("rendererExtent");
            w.Prop("p10", ExtentP10);
            w.Prop("p25", ExtentP25);
            w.Prop("p50", ExtentP50);
            w.Prop("p75", ExtentP75);
            w.Prop("p90", ExtentP90);
            w.Prop("max", ExtentMax);
            w.EndObject();

            w.EndObject();
        }
    }
}
