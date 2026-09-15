// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SideQuest.LightingTools.Core
{
    /// <summary>One light, flattened for reporting. Scenes rarely have enough to need summarising.</summary>
    public sealed class LightFacts
    {
        public Light Light;
        public string Name;
        public LightType Type;
        public LightmapBakeType Mode;
        public Color Color;
        public float Intensity;
        public float Range;
        public float SpotAngle;
        public float BounceIntensity;
        public bool CastsShadows;
        public Vector3 Position;
        public int ZoneId = -1;

        public bool IsRealtime { get { return Mode == LightmapBakeType.Realtime; } }
        public bool IsBaked { get { return Mode == LightmapBakeType.Baked; } }
    }

    /// <summary>What already exists in the scene, so a tool can preserve rather than clobber it.</summary>
    public sealed class ExistingLighting
    {
        public List<LightProbeGroup> ProbeGroups = new List<LightProbeGroup>();
        public int TotalProbePositions;
        public List<ReflectionProbe> ReflectionProbes = new List<ReflectionProbe>();
        public int LightmapCount;
        public long LightmapBytes;
        public bool HasLightingDataAsset;
        public int OcclusionDataBytes;
    }

    public sealed class NavMeshFacts
    {
        public bool Present;
        public int VertexCount;
        public int TriangleCount;
        public Bounds Bounds;
    }

    /// <summary>
    /// The single scene pass every tool in the suite shares.
    ///
    /// Four tools each walking the hierarchy, each reading materials, each voxelising
    /// space would be four times the cost for identical data. Scanning once also means all
    /// four tools reason about exactly the same zones - so a reflection probe and a light
    /// probe cluster agree about where a room is, which they would not if each derived its
    /// own partition.
    /// </summary>
    public sealed class SceneScan
    {
        public Scene Scene;
        public string SceneName = "Untitled";
        public string ScenePath = string.Empty;
        public string SceneGuid = string.Empty;

        public List<RendererFacts> Renderers = new List<RendererFacts>();
        public List<LightFacts> Lights = new List<LightFacts>();
        public SceneScale Scale = new SceneScale();
        public OccupancyGrid Grid;
        public List<Zone> Zones = new List<Zone>();
        public ExistingLighting Existing = new ExistingLighting();
        public NavMeshFacts NavMesh = new NavMeshFacts();
        public UrpFacts Urp = new UrpFacts();

        public Histogram SmoothnessHistogram = new Histogram();
        public Histogram MetallicHistogram = new Histogram();

        /// <summary>Shader name to renderer count, for shaders whose gloss could not be read.</summary>
        public Dictionary<string, int> UnknownShaders = new Dictionary<string, int>(StringComparer.Ordinal);

        public int UniqueMaterialCount;
        public double ScanSeconds;

        public Zone FindZone(int id)
        {
            for (int i = 0; i < Zones.Count; i++) if (Zones[i].Id == id) return Zones[i];
            return null;
        }
    }

    public static class SceneScanner
    {
        /// <summary>How many voxels the zone grid may use. Roughly a second of CheckBox calls.</summary>
        public const int ZoneCellBudget = 1000000;

        /// <summary>
        /// Voxel size for zone segmentation: the finest the cell budget affords.
        ///
        /// This deliberately ignores renderer size. Deriving it from the median renderer
        /// extent seems reasonable until the renderers ARE the architecture - a test scene
        /// of 8m wall and floor slabs reports a median extent of 8m and asks for voxels
        /// coarser than the rooms it is meant to resolve, so every room merges into one
        /// zone and every per-room decision in the suite collapses.
        ///
        /// Scene volume is the honest input: it is what actually decides how many voxels a
        /// given resolution costs. The floor of 0.25m is what resolves a doorway; the
        /// ceiling of 4m keeps a landscape from pretending to room-scale precision.
        /// </summary>
        public static float ZoneCellSize(SceneScale scale)
        {
            if (!scale.HasBounds) return 1f;

            Vector3 size = scale.WorldBounds.size;
            double volume = (double)Mathf.Max(size.x, 1f) * Mathf.Max(size.y, 1f) * Mathf.Max(size.z, 1f);

            float affordable = (float)System.Math.Pow(volume / ZoneCellBudget, 1.0 / 3.0);
            return Mathf.Clamp(affordable, 0.25f, 4f);
        }

        public static SceneScan Scan(SqProblemList problems, bool includeGrid = true)
        {
            var started = DateTime.UtcNow;
            var scan = new SceneScan();

            MaterialFacts.ResetCache();

            // Everything downstream that asks a physics question - the occupancy grid,
            // floor raycasts, the reflection probe escape test - reads the physics scene,
            // which lags the transform hierarchy until it is synced. Doing it once here
            // means no individual query has to remember.
            Physics.SyncTransforms();

            scan.Scene = SceneManager.GetActiveScene();
            scan.SceneName = string.IsNullOrEmpty(scan.Scene.name) ? "Untitled" : scan.Scene.name;
            scan.ScenePath = scan.Scene.path ?? string.Empty;
            scan.SceneGuid = string.IsNullOrEmpty(scan.ScenePath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(scan.ScenePath);

            scan.Urp = UrpFacts.Read();

            ScanRenderers(scan);
            ScanLights(scan);
            ScanExisting(scan);
            ScanNavMesh(scan);

            scan.Scale = SceneScale.Compute(scan.Renderers);

            if (includeGrid && scan.Scale.HasBounds)
            {
                scan.Grid = OccupancyGrid.Build(scan.Scale.WorldBounds, ZoneCellSize(scan.Scale), scan.Renderers);
                scan.Zones = ZoneSegmenter.Segment(scan.Grid);

                AssignRenderersToZones(scan);
                AssignLightsToZones(scan);
                ComputeZoneAggregates(scan);
                RefineCeilingHeight(scan);

                if (scan.Grid.UsedRendererFallback && problems != null)
                {
                    problems.Add("AN010_NO_COLLIDERS", SqSeverity.Warn,
                        "No colliders were found, so rooms were derived from renderer bounding boxes. Concave rooms will read as filled boxes.")
                        .WithAction("Add colliders to walls and floors, or review the zone list before applying a plan.");
                }

                if (scan.Zones.Count == 0 && problems != null)
                {
                    problems.Add("AN011_NO_ZONES", SqSeverity.Warn,
                        "No enclosed zones were found. The scene may be fully open, or too small for the voxel size.")
                        .WithAction("Heuristics will fall back to whole-scene bounds.");
                }
            }

            ReportUnknownShaders(scan, problems);

            scan.ScanSeconds = (DateTime.UtcNow - started).TotalSeconds;
            return scan;
        }

        static void ScanRenderers(SceneScan scan)
        {
            // FindObjectsByType, not the deprecated FindObjectsOfType, and renderers
            // rather than every GameObject: the prior-art tool walked all GameObjects and
            // filtered afterwards, which is far more expensive on a large scene.
            //
            // The two-argument overload is deliberate. Unity 6000.4 added a single-argument
            // form and deprecated this one, but 6000.3 does not have it - so the newer call
            // is a hard compile error there, while this one is merely a warning on 6000.4.
            // Portability wins over a clean warning list.
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            var uniqueMaterials = new HashSet<int>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                // Particles and lines have no meaningful surface to light or occlude.
                if (renderer is ParticleSystemRenderer) continue;
                if (renderer is LineRenderer || renderer is TrailRenderer) continue;

                RendererFacts facts = RendererFacts.Read(renderer, scan.Renderers.Count);
                scan.Renderers.Add(facts);

                Material[] shared = renderer.sharedMaterials;
                for (int m = 0; m < shared.Length; m++)
                    if (shared[m] != null) uniqueMaterials.Add(shared[m].GetInstanceID());

                for (int m = 0; m < facts.Materials.Length; m++)
                {
                    MaterialFacts material = facts.Materials[m];
                    if (material == null) continue;

                    scan.SmoothnessHistogram.Add(material.Smoothness);
                    scan.MetallicHistogram.Add(material.Metallic);

                    if (string.Equals(material.GlossSource, "unknown", StringComparison.Ordinal))
                    {
                        int count;
                        scan.UnknownShaders.TryGetValue(material.ShaderName, out count);
                        scan.UnknownShaders[material.ShaderName] = count + 1;
                    }
                }
            }

            scan.UniqueMaterialCount = uniqueMaterials.Count;
        }

        static void ScanLights(SceneScan scan)
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;

                scan.Lights.Add(new LightFacts
                {
                    Light = light,
                    Name = light.gameObject.name,
                    Type = light.type,
                    Mode = light.lightmapBakeType,
                    Color = light.color,
                    Intensity = light.intensity,
                    Range = light.range,
                    SpotAngle = light.spotAngle,
                    BounceIntensity = light.bounceIntensity,
                    CastsShadows = light.shadows != LightShadows.None,
                    Position = light.transform.position
                });
            }
        }

        static void ScanExisting(SceneScan scan)
        {
            LightProbeGroup[] groups = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < groups.Length; i++)
            {
                scan.Existing.ProbeGroups.Add(groups[i]);
                Vector3[] positions = groups[i].probePositions;
                if (positions != null) scan.Existing.TotalProbePositions += positions.Length;
            }

            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            scan.Existing.ReflectionProbes.AddRange(probes);

            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmaps != null)
            {
                scan.Existing.LightmapCount = lightmaps.Length;
                for (int i = 0; i < lightmaps.Length; i++)
                {
                    Texture2D color = lightmaps[i].lightmapColor;
                    if (color == null) continue;

                    // Rough: 4 bytes per texel plus a third for mips. Exact size depends on
                    // the compression format, which is not worth reading for a budget check.
                    scan.Existing.LightmapBytes += (long)(color.width * color.height * 4 * 1.33f);
                }
            }

            scan.Existing.HasLightingDataAsset = Lightmapping.lightingDataAsset != null;
            scan.Existing.OcclusionDataBytes = (int)StaticOcclusionCulling.umbraDataSize;
        }

        static void ScanNavMesh(SceneScan scan)
        {
            try
            {
                NavMeshTriangulation triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
                if (triangulation.vertices == null || triangulation.vertices.Length == 0) return;

                scan.NavMesh.Present = true;
                scan.NavMesh.VertexCount = triangulation.vertices.Length;
                scan.NavMesh.TriangleCount = triangulation.indices != null ? triangulation.indices.Length / 3 : 0;

                var bounds = new Bounds(triangulation.vertices[0], Vector3.zero);
                for (int i = 1; i < triangulation.vertices.Length; i++)
                    bounds.Encapsulate(triangulation.vertices[i]);
                scan.NavMesh.Bounds = bounds;
            }
            catch (Exception e)
            {
                SqLog.Detail("navmesh read failed: " + e.Message);
            }
        }

        /// <summary>
        /// Replaces the scene-bounds ceiling guess with the median room height.
        ///
        /// Scene bounds Y is a poor proxy for how tall a room is: one tall atrium, a
        /// skybox proxy or a stray far-off object pushes it to the clamp, and anything
        /// derived from it then describes a building nobody is standing in. Once zones
        /// exist, each one's height is the actual height of a room, and the median of
        /// those is what "ceiling height" was always meant to mean.
        ///
        /// This matters most for occlusion, where the occluder size threshold is expressed
        /// as a fraction of it.
        /// </summary>
        static void RefineCeilingHeight(SceneScan scan)
        {
            if (scan.Zones.Count == 0) return;

            var heights = new List<float>(scan.Zones.Count);
            for (int i = 0; i < scan.Zones.Count; i++)
            {
                if (scan.Zones[i].IsExterior) continue;
                heights.Add(scan.Zones[i].Bounds.size.y);
            }

            if (heights.Count == 0) return;

            heights.Sort();
            scan.Scale.CeilingHeightEstimate = Mathf.Clamp(Percentile.OfSorted(heights, 0.5f), 2.2f, 12f);
        }

        static void AssignRenderersToZones(SceneScan scan)
        {
            if (scan.Grid == null || scan.Zones.Count == 0) return;

            // Zone membership by cell lookup at the renderer's centre. A renderer whose
            // centre sits inside a wall - which is normal for the wall itself - falls back
            // to the nearest zone by bounds distance.
            var cellToZone = new Dictionary<int, int>();
            for (int z = 0; z < scan.Zones.Count; z++)
            {
                Zone zone = scan.Zones[z];
                for (int c = 0; c < zone.Cells.Count; c++)
                {
                    Vector3Int cell = zone.Cells[c];
                    cellToZone[scan.Grid.IndexOf(cell.x, cell.y, cell.z)] = zone.Id;
                }
            }

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts renderer = scan.Renderers[i];
                if (!renderer.HasBounds) continue;

                Vector3Int cell = scan.Grid.WorldToCell(renderer.WorldBounds.center);

                int zoneId;
                if (cellToZone.TryGetValue(scan.Grid.IndexOf(cell.x, cell.y, cell.z), out zoneId))
                {
                    renderer.ZoneId = zoneId;
                    continue;
                }

                renderer.ZoneId = NearestZoneId(scan.Zones, renderer.WorldBounds.center);
            }
        }

        static void AssignLightsToZones(SceneScan scan)
        {
            for (int i = 0; i < scan.Lights.Count; i++)
                scan.Lights[i].ZoneId = NearestZoneId(scan.Zones, scan.Lights[i].Position);
        }

        static int NearestZoneId(List<Zone> zones, Vector3 point)
        {
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < zones.Count; i++)
            {
                float sqr = zones[i].Bounds.SqrDistance(point);
                if (sqr < bestSqr) { bestSqr = sqr; best = zones[i].Id; }
            }

            return best;
        }

        static void ComputeZoneAggregates(SceneScan scan)
        {
            var smoothnessSum = new Dictionary<int, float>();
            var smoothnessCount = new Dictionary<int, int>();
            var metallicCount = new Dictionary<int, int>();

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts renderer = scan.Renderers[i];
                Zone zone = scan.FindZone(renderer.ZoneId);
                if (zone == null) continue;

                zone.RendererCount++;
                zone.TriangleCount += renderer.TriangleCount;
                zone.ReflectionWeight += renderer.ReflectionWeight;
                if (renderer.MaxSmoothness > zone.MaxSmoothness) zone.MaxSmoothness = renderer.MaxSmoothness;

                for (int m = 0; m < renderer.Materials.Length; m++)
                {
                    MaterialFacts material = renderer.Materials[m];
                    if (material == null) continue;

                    float sum;
                    smoothnessSum.TryGetValue(zone.Id, out sum);
                    smoothnessSum[zone.Id] = sum + material.Smoothness;

                    int count;
                    smoothnessCount.TryGetValue(zone.Id, out count);
                    smoothnessCount[zone.Id] = count + 1;

                    if (material.Metallic > 0.5f)
                    {
                        int metals;
                        metallicCount.TryGetValue(zone.Id, out metals);
                        metallicCount[zone.Id] = metals + 1;
                    }
                }
            }

            for (int i = 0; i < scan.Lights.Count; i++)
            {
                LightFacts light = scan.Lights[i];
                Zone zone = scan.FindZone(light.ZoneId);
                if (zone == null) continue;

                switch (light.Type)
                {
                    case LightType.Directional: zone.DirectionalLights++; break;
                    case LightType.Point: zone.PointLights++; break;
                    case LightType.Spot: zone.SpotLights++; break;
                    default: zone.AreaLights++; break;
                }
            }

            var lights = new List<Light>(scan.Lights.Count);
            for (int i = 0; i < scan.Lights.Count; i++) lights.Add(scan.Lights[i].Light);

            // Shadow rays off: this runs once per zone over the whole scene and the
            // ranking between zones does not need them. The probe placer turns them on
            // for the far smaller set of candidate positions it actually cares about.
            var irradiance = new IrradianceProbe(lights, false);

            for (int i = 0; i < scan.Zones.Count; i++)
            {
                Zone zone = scan.Zones[i];

                int count;
                if (smoothnessCount.TryGetValue(zone.Id, out count) && count > 0)
                    zone.MeanSmoothness = smoothnessSum[zone.Id] / count;

                int metals;
                if (metallicCount.TryGetValue(zone.Id, out metals) && count > 0)
                    zone.MetallicFraction = (float)metals / count;

                zone.GradientScore = irradiance.GradientScore(zone.Bounds.center, scan.Scale.CellSize);

                if (scan.NavMesh.Present)
                    zone.HasNavMesh = scan.NavMesh.Bounds.Intersects(zone.Bounds);
            }
        }

        static void ReportUnknownShaders(SceneScan scan, SqProblemList problems)
        {
            if (problems == null || scan.UnknownShaders.Count == 0) return;

            int affected = 0;
            foreach (var kv in scan.UnknownShaders) affected += kv.Value;

            var names = new List<string>();
            foreach (var kv in scan.UnknownShaders)
            {
                if (names.Count >= 5) break;
                names.Add(kv.Key);
            }

            problems.Add("RP010_UNKNOWN_SHADER", SqSeverity.Warn, string.Format(
                "{0} material slot(s) use shaders whose smoothness could not be read; a neutral value was assumed at reduced weight. Shaders: {1}",
                affected, string.Join(", ", names.ToArray())))
                .WithCount(affected)
                .WithAction("Add an entry to Shader Gloss Overrides in the tool settings for each shader, so reflection placement stops guessing.");
        }
    }
}
