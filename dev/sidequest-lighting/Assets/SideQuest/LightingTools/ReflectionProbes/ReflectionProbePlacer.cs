// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using System.IO;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// Works out where reflection probes belong, and how big and how sharp each should be.
    ///
    /// The whole job is spending a fixed cubemap memory budget where it shows. A probe on a
    /// mirrored floor earns its 128 pixels; the same probe in a plaster corridor is a
    /// megabyte of nothing. So placement follows reflection weight - smoothness cubed,
    /// scaled by metallic and surface area - rather than room geometry alone.
    /// </summary>
    public static class ReflectionProbePlacer
    {
        public const string CodeSimpleLit = "RP011_SIMPLE_LIT_NO_REFLECTIONS";
        public const string CodeMemoryBudget = "RP020_MEMORY_BUDGET";
        public const string CodeRealtimeOnMobile = "RP030_REALTIME_ON_MOBILE";
        public const string CodeAuthorProbes = "RP040_AUTHORED_PROBES";
        public const string CodeInsideGeometry = "RP050_INSIDE_GEOMETRY";
        public const string CodeNoGloss = "RP060_NO_GLOSSY_SURFACES";
        public const string CodeStaleCapture = "RP070_PROBES_OLDER_THAN_LIGHTMAPS";
        public const string CodeBakeOrder = "RP071_BAKE_AFTER_LIGHTMAPS";

        /// <summary>A zone contributing less weight than this does not justify a probe.</summary>
        public const float ZoneWeightThreshold = 4f;

        /// <summary>Above this volume in cubic metres, a zone is split between several probes.</summary>
        public const float MaxProbeVolume = 2000f;

        public const int MaxProbesPerZone = 6;

        public sealed class Options
        {
            public float EyeHeight = 1.6f;
            public float BoxPadding = 0.1f;
            public float MemoryBudgetMB = 24f;
            public int MaxProbes = 64;

            public static Options FromSettings(SqSettings s)
            {
                return new Options
                {
                    EyeHeight = s.reflectionProbeEyeHeight,
                    BoxPadding = s.reflectionBoxPadding,
                    MemoryBudgetMB = s.reflectionProbeMemoryBudgetMB,
                    MaxProbes = s.maxReflectionProbes
                };
            }
        }

        public static List<ReflectionProbeSpec> Recommend(SceneScan scan, Options options, SqProblemList problems)
        {
            var specs = new List<ReflectionProbeSpec>();

            CheckSimpleLit(scan, problems);

            for (int i = 0; i < scan.Zones.Count; i++)
                BuildZoneProbes(scan, scan.Zones[i], options, specs, problems);

            if (specs.Count == 0)
            {
                // Falling back to one scene-wide probe rather than none: a scene with no
                // strongly glossy surface still looks better with an ambient capture than
                // with the skybox reflected onto interior walls.
                if (scan.Scale.HasBounds) specs.Add(SceneWideProbe(scan, options));

                if (problems != null)
                {
                    problems.Add(CodeNoGloss, SqSeverity.Info,
                        "No zone had enough glossy surface area to justify its own reflection probe.")
                        .WithAction("A single scene-wide probe was recommended instead. Add probes by hand if specific surfaces need them.");
                }
            }

            AssignImportance(specs);
            EnforceMemoryBudget(specs, options, problems);
            CheckExisting(scan, specs, problems);
            CheckCaptureFreshness(scan, problems);

            return specs;
        }

        static void BuildZoneProbes(
            SceneScan scan, Zone zone, Options options, List<ReflectionProbeSpec> specs, SqProblemList problems)
        {
            var samples = new List<ReflectionProbeClusterer.Sample>();
            float totalWeight = 0f;
            float maxSmoothness = 0f;

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (r.ZoneId != zone.Id || !r.HasBounds) continue;
                if (r.ReflectionWeight <= 0f) continue;

                samples.Add(new ReflectionProbeClusterer.Sample
                {
                    Position = r.WorldBounds.center,
                    Weight = r.ReflectionWeight
                });

                totalWeight += r.ReflectionWeight;
                if (r.MaxSmoothness > maxSmoothness) maxSmoothness = r.MaxSmoothness;
            }

            if (totalWeight < ZoneWeightThreshold || samples.Count == 0) return;

            Vector3 zoneSize = zone.Bounds.size;
            float volume = zoneSize.x * zoneSize.y * zoneSize.z;
            int k = Mathf.Clamp(Mathf.CeilToInt(volume / MaxProbeVolume), 1, MaxProbesPerZone);

            List<ReflectionProbeClusterer.Cluster> clusters = ReflectionProbeClusterer.Build(samples, k);

            for (int c = 0; c < clusters.Count; c++)
            {
                ReflectionProbeClusterer.Cluster cluster = clusters[c];

                // With one probe the box is the whole room, so reflections stay correct
                // right up to the walls. With several, each covers its own cluster.
                Bounds box = clusters.Count == 1 ? zone.Bounds : cluster.Bounds;
                box.Expand(options.BoxPadding * 2f);

                // A cluster of coplanar surfaces has near-zero extent on one axis, which
                // would make box projection degenerate.
                box.size = new Vector3(
                    Mathf.Max(box.size.x, 1f),
                    Mathf.Max(box.size.y, 1f),
                    Mathf.Max(box.size.z, 1f));

                bool escaped;
                Vector3 position = ChoosePosition(scan, zone, cluster.Centroid, options, out escaped);

                if (!escaped && problems != null)
                {
                    problems.Add(CodeInsideGeometry, SqSeverity.Warn, string.Format(
                        "A probe for zone {0} could not be moved clear of solid geometry, so it fell back to the zone centre. A capture point inside geometry bakes to a black or garbage cubemap.",
                        zone.Id))
                        .WithAction("Move this probe by hand after applying, or give the room a clear interior volume.");
                }

                var spec = new ReflectionProbeSpec
                {
                    Id = string.Format("zone{0}-{1}", zone.Id, c),
                    Action = ProbeAction.Create,
                    Position = position,
                    BoxOffset = box.center - position,
                    BoxSize = box.size,
                    ZoneId = zone.Id,
                    ServedWeight = cluster.TotalWeight,
                    BlendDistance = ChooseBlendDistance(box),
                    NearClip = ChooseNearClip(box),
                    Resolution = ChooseResolution(maxSmoothness, box),
                    Hdr = true,
                    BoxProjection = true,
                    Reason = string.Format(
                        "{0} glossy renderer(s), weight {1:0.#}, max smoothness {2:0.##}",
                        cluster.Count, cluster.TotalWeight, maxSmoothness)
                };

                specs.Add(spec);
            }
        }

        /// <summary>
        /// Puts the probe at eye height and out of solid geometry.
        ///
        /// A capture point inside a wall returns a black or garbage cubemap, and nothing in
        /// the Editor says so - the probe looks fine until the bake comes back wrong. The
        /// weighted centroid of glossy surfaces lands inside geometry often, because those
        /// surfaces are the walls, so the escape step is not an edge case.
        /// </summary>
        static Vector3 ChoosePosition(SceneScan scan, Zone zone, Vector3 centroid, Options options, out bool escaped)
        {
            escaped = true;
            var position = new Vector3(centroid.x, zone.FloorY + options.EyeHeight, centroid.z);

            // Keep it inside the zone: a centroid between two clusters can fall outside the
            // room the probe is meant to serve.
            position = ClampToBounds(position, zone.Bounds);

            if (scan.Grid != null)
            {
                Vector3 free;
                if (scan.Grid.TryFindNearestFree(position, 6, out free))
                {
                    // The voxel grid is coarse, so confirm against real colliders before
                    // accepting the escape - a free voxel can still clip a thin wall.
                    if (!Physics.CheckSphere(free, 0.15f, ~0, QueryTriggerInteraction.Ignore))
                        return free;
                }
            }

            if (!Physics.CheckSphere(position, 0.15f, ~0, QueryTriggerInteraction.Ignore)) return position;

            // Last resort: the zone centre. Reported rather than silent, because a probe
            // that never got clear of geometry will bake to nothing useful.
            escaped = false;
            return zone.Bounds.center;
        }

        static Vector3 ClampToBounds(Vector3 point, Bounds bounds)
        {
            return new Vector3(
                Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(point.y, bounds.min.y, bounds.max.y),
                Mathf.Clamp(point.z, bounds.min.z, bounds.max.z));
        }

        /// <summary>
        /// Blend distance scaled to the box.
        ///
        /// A fixed value is wrong at both ends: 1m of blending is imperceptible across a
        /// warehouse and swallows a cupboard whole. A tenth of the shortest axis keeps the
        /// transition proportional to the space.
        /// </summary>
        /// <summary>
        /// Near clip scaled to the space, well under Unity's 0.3 default.
        ///
        /// Anything closer to the capture point than this is missing from the cubemap, so
        /// in a small room or a tight alcove the default quietly drops the nearest
        /// surfaces - which reads as seeing through a wall into whatever is behind it.
        /// There is no cost to a small value for a cubemap capture, and the failure it
        /// prevents is invisible until someone stands in the wrong place.
        /// </summary>
        static float ChooseNearClip(Bounds box)
        {
            float minExtent = Mathf.Min(box.size.x, Mathf.Min(box.size.y, box.size.z));
            return Mathf.Clamp(minExtent * 0.02f, 0.01f, 0.3f);
        }

        static float ChooseBlendDistance(Bounds box)
        {
            float minExtent = Mathf.Min(box.size.x, Mathf.Min(box.size.y, box.size.z));
            return Mathf.Clamp(0.1f * minExtent, 0.25f, 2f);
        }

        /// <summary>
        /// Resolution from how sharp the reflections need to be and how much space is shown.
        ///
        /// Smoothness dominates because a rough surface convolves detail away - rendering
        /// 256 pixels to then blur them is pure waste. Box size matters second: a large
        /// space shows more of the world per texel, so it needs more of them for the same
        /// apparent sharpness.
        /// </summary>
        static int ChooseResolution(float maxSmoothness, Bounds box)
        {
            float diagonal = box.size.magnitude;

            int resolution = 64;

            if (maxSmoothness < 0.5f || diagonal < 4f) resolution = 32;
            if (maxSmoothness > 0.75f || diagonal > 12f) resolution = 128;
            if (maxSmoothness > 0.92f && diagonal > 6f) resolution = 256;

            return resolution;
        }

        static ReflectionProbeSpec SceneWideProbe(SceneScan scan, Options options)
        {
            Bounds bounds = scan.Scale.WorldBounds;

            return new ReflectionProbeSpec
            {
                Id = "scene-ambient",
                Action = ProbeAction.Create,
                Position = new Vector3(bounds.center.x, bounds.min.y + options.EyeHeight, bounds.center.z),
                BoxOffset = new Vector3(0f, bounds.center.y - (bounds.min.y + options.EyeHeight), 0f),
                BoxSize = bounds.size,
                Resolution = 64,
                BlendDistance = 1f,
                Hdr = true,
                BoxProjection = true,
                Reason = "scene-wide ambient capture; no zone had enough glossy surface for its own probe"
            };
        }

        /// <summary>
        /// Importance by nesting depth.
        ///
        /// Unity picks the highest-importance probe whose box contains the renderer. A
        /// detail probe inside a room probe inside a scene probe therefore has to outrank
        /// both, or the largest and least specific capture wins everywhere.
        /// </summary>
        static void AssignImportance(List<ReflectionProbeSpec> specs)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                Vector3 centre = specs[i].WorldBox.center;
                int depth = 0;

                for (int j = 0; j < specs.Count; j++)
                {
                    if (i == j) continue;
                    if (specs[j].WorldBox.Contains(centre)) depth++;
                }

                specs[i].Importance = Mathf.Clamp(1 + depth, 1, 5);
            }
        }

        /// <summary>
        /// Brings total cubemap memory under budget by lowering resolutions.
        ///
        /// Resolution is reduced before probes are dropped: a slightly blurrier reflection
        /// in every room reads far better than a correct one in some rooms and the skybox
        /// in the rest. The least glossy probes give up their pixels first.
        /// </summary>
        static void EnforceMemoryBudget(List<ReflectionProbeSpec> specs, Options options, SqProblemList problems)
        {
            long budget = (long)(options.MemoryBudgetMB * 1024 * 1024);
            long total = TotalBytes(specs);
            if (total <= budget) return;

            long original = total;

            var ordered = new List<ReflectionProbeSpec>(specs);
            ordered.Sort((a, b) => a.ServedWeight.CompareTo(b.ServedWeight));

            int guard = 0;
            while (total > budget && guard++ < 64)
            {
                bool reduced = false;

                for (int i = 0; i < ordered.Count && total > budget; i++)
                {
                    if (ordered[i].Resolution <= 32) continue;

                    ordered[i].Resolution /= 2;
                    reduced = true;
                    total = TotalBytes(specs);
                }

                if (!reduced) break; // everything is already at the floor
            }

            if (problems == null) return;

            problems.Add(CodeMemoryBudget, total > budget ? SqSeverity.Warn : SqSeverity.Info, string.Format(
                "Reflection cubemaps needed {0:0.#}MB against a {1:0.#}MB budget, so resolutions were reduced to {2:0.#}MB.",
                original / 1048576f, options.MemoryBudgetMB, total / 1048576f))
                .WithAction("Raise Reflection Probe Memory Budget in the tool settings, or reduce the number of probes, if the sharper capture is wanted.");
        }

        static long TotalBytes(List<ReflectionProbeSpec> specs)
        {
            long total = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Action == ProbeAction.Delete) continue;
                total += specs[i].EstimatedBytes;
            }
            return total;
        }

        /// <summary>
        /// Warns when a scene's gloss sits on shaders that never sample a reflection probe.
        ///
        /// Simple Lit has a smoothness slider and uses it for a Blinn-Phong highlight only.
        /// A creator who turns that up is asking for reflections they will never get, and
        /// no number of probes will change it - so say so instead of placing probes to
        /// serve surfaces that cannot use them.
        /// </summary>
        static void CheckSimpleLit(SceneScan scan, SqProblemList problems)
        {
            if (problems == null) return;

            int glossySimpleLit = 0;
            var samples = new List<int>();

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];

                for (int m = 0; m < r.Materials.Length; m++)
                {
                    MaterialFacts material = r.Materials[m];
                    if (material == null) continue;
                    if (material.SamplesReflectionProbes) continue;
                    if (material.Smoothness < 0.6f) continue;

                    glossySimpleLit++;
                    if (samples.Count < SqProblem.MaxSamples) samples.Add(r.Index);
                    break;
                }
            }

            if (glossySimpleLit == 0) return;

            problems.Add(CodeSimpleLit, SqSeverity.Warn, string.Format(
                "{0} renderer(s) have high smoothness on a shader that never samples reflection probes, such as Simple Lit or Unlit. They will not reflect anything however many probes are placed.",
                glossySimpleLit))
                .WithCount(glossySimpleLit)
                .WithSamples(samples)
                .WithAction("Switch those materials to Universal Render Pipeline/Lit if they are meant to reflect.");
        }

        /// <summary>
        /// Reconciles against probes already in the scene.
        ///
        /// Probes a person placed are never modified or deleted. The tool has no way to
        /// know why a hand-placed probe is where it is, and overwriting one would destroy
        /// work that the plan file gives no way to recover.
        /// </summary>
        /// <summary>
        /// Warns when a probe's cubemap was captured before the lightmaps that light it.
        ///
        /// A reflection probe captures the scene as currently lit, so its cubemap is only
        /// as correct as the lighting at the moment it was baked. Bake probes and lightmaps
        /// in one pass and the capture can see the scene before the lightmaps land, leaving
        /// reflections that are dark, flat, or missing nearby surfaces entirely - and
        /// baking a second time with identical settings fixes it, which makes the cause
        /// look like anything except an ordering problem.
        ///
        /// Comparing file times is crude, but it is the one signal that distinguishes
        /// "these were captured under the current lighting" from "these were not".
        /// </summary>
        static void CheckCaptureFreshness(SceneScan scan, SqProblemList problems)
        {
            if (problems == null) return;

            problems.Add(CodeBakeOrder, SqSeverity.Info,
                "Reflection probes capture the scene as it is currently lit, so they are only correct if they were baked after the lightmaps.")
                .WithAction("Bake lightmaps first, then Bake Probes. Re-bake probes after any lighting change.");

            System.DateTime newestLightmap = NewestAssetTime(LightmapPaths());
            if (newestLightmap == System.DateTime.MinValue) return;

            System.DateTime oldestProbe = System.DateTime.MaxValue;
            int stale = 0;

            for (int i = 0; i < scan.Existing.ReflectionProbes.Count; i++)
            {
                ReflectionProbe probe = scan.Existing.ReflectionProbes[i];
                if (probe == null || probe.bakedTexture == null) continue;

                string path = AssetDatabase.GetAssetPath(probe.bakedTexture);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                System.DateTime written = File.GetLastWriteTimeUtc(path);
                if (written < oldestProbe) oldestProbe = written;
                if (written < newestLightmap) stale++;
            }

            if (stale == 0) return;

            problems.Add(CodeStaleCapture, SqSeverity.Warn, string.Format(
                "{0} reflection probe cubemap(s) were baked before the current lightmaps, so they reflect the scene as it was lit earlier.",
                stale))
                .WithCount(stale)
                .WithAction("Run Bake Probes again now that the lightmaps exist.");
        }

        static List<string> LightmapPaths()
        {
            var paths = new List<string>();
            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmaps == null) return paths;

            for (int i = 0; i < lightmaps.Length; i++)
            {
                Texture2D color = lightmaps[i].lightmapColor;
                if (color == null) continue;

                string path = AssetDatabase.GetAssetPath(color);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            return paths;
        }

        static System.DateTime NewestAssetTime(List<string> paths)
        {
            System.DateTime newest = System.DateTime.MinValue;

            for (int i = 0; i < paths.Count; i++)
            {
                if (!File.Exists(paths[i])) continue;

                System.DateTime written = File.GetLastWriteTimeUtc(paths[i]);
                if (written > newest) newest = written;
            }

            return newest;
        }

        static void CheckExisting(SceneScan scan, List<ReflectionProbeSpec> specs, SqProblemList problems)
        {
            if (problems == null) return;

            int authored = 0;
            int realtime = 0;

            for (int i = 0; i < scan.Existing.ReflectionProbes.Count; i++)
            {
                ReflectionProbe probe = scan.Existing.ReflectionProbes[i];
                if (probe == null) continue;

                if (!probe.gameObject.name.StartsWith(ReflectionProbeApplier.NamePrefix)) authored++;
                if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) realtime++;
            }

            if (authored > 0)
            {
                problems.Add(CodeAuthorProbes, SqSeverity.Info, string.Format(
                    "{0} reflection probe(s) in this scene were not created by this tool. They are left untouched.", authored))
                    .WithCount(authored)
                    .WithAction("Delete them by hand first if the recommended layout should replace them.");
            }

            if (realtime > 0)
            {
                problems.Add(CodeRealtimeOnMobile, SqSeverity.Warn, string.Format(
                    "{0} reflection probe(s) are set to Realtime. On Quest that re-renders the scene six times per update and is rarely affordable.", realtime))
                    .WithCount(realtime)
                    .WithAction("Set them to Baked unless the reflection genuinely has to track moving geometry.");
            }
        }
    }
}
