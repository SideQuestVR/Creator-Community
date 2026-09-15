// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// How probe positions are chosen. Three first-class strategies, not one with modes.
    ///
    /// They answer genuinely different questions. NavMesh knows where a player can stand.
    /// Adaptive knows where lighting actually changes. An agent knows what the scene is
    /// for. None subsumes the others, so the tool recommends one and the user picks.
    /// </summary>
    public enum ProbeStrategy
    {
        /// <summary>Density driven by lights, edges and irradiance gradient.</summary>
        Adaptive,

        /// <summary>Grid over the NavMesh plus its boundary edges - where players walk.</summary>
        NavMesh,

        /// <summary>Fill inside a chosen collider volume.</summary>
        MeshVolume,

        /// <summary>Explicit positions from a decision plan, or hand-painted in the Scene view.</summary>
        Agent
    }

    public sealed class ProbeSamplerSettings
    {
        /// <summary>Base spacing in metres. Smaller means more probes - one semantic in every mode.</summary>
        public float Spacing = 2f;

        /// <summary>Density heuristics never subdivide below this.</summary>
        public float MinSpacing = 0.5f;

        public float MergeDistance = 0.5f;
        public float EdgeBand = 0.6f;

        /// <summary>Heights above the floor at which probes are layered.</summary>
        public float[] LayerHeights = { 0.3f, 1.6f, 2.6f };

        public int MaxProbes = 2000;

        /// <summary>Shadow rays make the gradient estimate far better and far slower.</summary>
        public bool UseShadowRays = true;

        /// <summary>Volume mode only: the object whose collider bounds the fill.</summary>
        public GameObject VolumeObject;

        public static ProbeSamplerSettings FromSettings(SqSettings s)
        {
            return new ProbeSamplerSettings
            {
                Spacing = s.probeSpacing,
                MinSpacing = s.probeMinSpacing,
                MergeDistance = s.probeMergeDistance,
                EdgeBand = s.probeEdgeBand,
                LayerHeights = s.probeLayerHeights != null && s.probeLayerHeights.Length > 0
                    ? s.probeLayerHeights
                    : new[] { 0.3f, 1.6f, 2.6f },
                MaxProbes = s.maxLightProbes
            };
        }
    }

    public sealed class SamplerStats
    {
        public int RawSamples;
        public int AfterDecimation;
        public int Layers;
        public bool HitProbeCap;
        public string Notes;
    }

    public interface IProbeSampler
    {
        ProbeStrategy Strategy { get; }
        string Id { get; }
        string DisplayName { get; }

        /// <summary>Whether this strategy can run on this scene, and if not, why not in plain words.</summary>
        bool IsAvailable(SceneScan scan, ProbeSamplerSettings settings, out string reason);

        /// <summary>Returns WORLD positions. Conversion to group-local space happens only in ProbeGroupWriter.</summary>
        List<Vector3> Sample(SceneScan scan, ProbeSamplerSettings settings, SqProblemList problems, SamplerStats stats);
    }

    /// <summary>Helpers every sampler shares.</summary>
    public static class ProbeSampling
    {
        public const string CodeCoplanar = "LP020_COPLANAR";
        public const string CodeProbeCap = "LP021_PROBE_CAP";
        public const string CodeNoFloor = "LP022_NO_FLOOR";

        /// <summary>
        /// Emits a vertical column of probes above a floor point.
        ///
        /// This is the single biggest quality difference from the prior art, which placed
        /// one horizontal sheet at a fixed offset. In VR the viewer's head is at 1.6m, so a
        /// sheet at 0.25m lights the floor correctly and the face not at all - and a flat
        /// set tetrahedralises into slivers, which interpolates badly in every direction.
        /// </summary>
        public static void EmitColumn(
            Vector3 floorPoint,
            float[] layerHeights,
            float ceilingHeight,
            List<Vector3> output,
            System.Func<Vector3, bool> isValid = null)
        {
            if (layerHeights == null || layerHeights.Length == 0)
            {
                Vector3 single = floorPoint + Vector3.up * 0.3f;
                if (isValid == null || isValid(single)) output.Add(single);
                return;
            }

            // Keep the lowest layer even if the ceiling estimate is wrong or the space is
            // low: a column of nothing is worse than a column of one.
            bool emitted = false;

            for (int i = 0; i < layerHeights.Length; i++)
            {
                float height = layerHeights[i];
                if (emitted && height > ceilingHeight) break;

                Vector3 position = floorPoint + Vector3.up * height;

                // A "floor" is any surface with space above it, which includes the top of
                // every crate and pane in the scene. Stacking a full column on a 2.5m prop
                // puts the upper layer through the ceiling, so each layer is checked
                // against real space rather than trusted because the one below it fitted.
                if (isValid != null && !isValid(position))
                {
                    if (emitted) break; // the column has left usable space; stop climbing
                    continue;
                }

                output.Add(position);
                emitted = true;
            }
        }

        /// <summary>
        /// Drops a ray to find the floor under a point.
        ///
        /// Colliders rather than renderers, because a probe should sit above the surface a
        /// player stands on, and that is what colliders describe.
        /// </summary>
        public static bool TryFindFloor(Vector3 from, float maxDistance, out Vector3 floorPoint)
        {
            RaycastHit hit;
            if (Physics.Raycast(from, Vector3.down, out hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                floorPoint = hit.point;
                return true;
            }

            floorPoint = from;
            return false;
        }

        /// <summary>
        /// Thins to the merge distance and enforces the probe cap.
        ///
        /// The cap is enforced by increasing the merge distance and thinning again, not by
        /// truncating the list. Truncation would keep whichever probes happened to be
        /// generated first - typically one corner of the scene - and leave the rest unlit.
        /// </summary>
        public static List<Vector3> Finalise(
            List<Vector3> raw,
            ProbeSamplerSettings settings,
            SqProblemList problems,
            SamplerStats stats)
        {
            stats.RawSamples = raw.Count;

            List<Vector3> result = SpatialHash.DecimateMedoid(raw, settings.MergeDistance);

            float mergeDistance = settings.MergeDistance;
            int guard = 0;
            while (result.Count > settings.MaxProbes && guard++ < 12)
            {
                mergeDistance *= 1.35f;
                result = SpatialHash.DecimateMedoid(raw, mergeDistance);
                stats.HitProbeCap = true;
            }

            if (stats.HitProbeCap && problems != null)
            {
                problems.Add(CodeProbeCap, SqSeverity.Warn, string.Format(
                    "Sampling produced more than the {0} probe budget, so spacing was widened to {1:0.##}m (from {2:0.##}m).",
                    settings.MaxProbes, mergeDistance, settings.MergeDistance))
                    .WithAction("Raise Max Light Probes in the tool settings, or increase Spacing, if the denser set is wanted.");
            }

            if (problems != null && result.Count >= 4 && ProbeGroupWriter.IsCoplanar(result))
            {
                problems.Add(CodeCoplanar, SqSeverity.Warn,
                    "All probes ended up at effectively the same height. Unity's tetrahedralisation degenerates for a flat set, and lighting will not interpolate correctly above or below it.")
                    .WithAction("Check that Layer Heights has more than one entry and that the ceiling estimate is not collapsing them.");
            }

            stats.AfterDecimation = result.Count;
            return result;
        }
    }
}
