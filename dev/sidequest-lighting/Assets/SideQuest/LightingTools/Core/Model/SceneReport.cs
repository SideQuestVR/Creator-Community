// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    public enum ReportDetail { Summary, Zones, Full }

    /// <summary>
    /// Writes the scene analysis report.
    ///
    /// Token efficiency is a correctness requirement here, not an optimisation. These
    /// reports are read by an LLM, and a naive per-renderer dump of a 500-object scene
    /// runs to megabytes - which costs real money, crowds out the reasoning it was meant
    /// to inform, and buries the handful of facts that actually decide the plan.
    ///
    /// So the default detail level carries no per-renderer array at all: zones,
    /// histograms, outliers, problems and a ready-made recommended plan. The full dump
    /// exists for debugging and is never the default.
    /// </summary>
    public static class SceneReport
    {
        public const int SchemaVersion = 1;

        /// <summary>Per-zone renderer rows written at Zones detail.</summary>
        public const int TopRenderersPerZone = 20;

        public delegate void WriteSection(SqJsonWriter writer);

        /// <summary>
        /// Builds the report, degrading detail until it fits the byte budget.
        ///
        /// Degrading beats hard-truncating: a report cut off mid-array is not valid JSON
        /// and tells the reader nothing, whereas the same scene described in less detail
        /// still answers the question.
        /// </summary>
        public static string BuildWithBudget(
            SceneScan scan,
            SqProblemList problems,
            string tool,
            string toolVersion,
            string reportId,
            ReportDetail requested,
            int maxBytes,
            WriteSection writeRecommendations,
            out ReportDetail actualDetail,
            out bool truncated)
        {
            actualDetail = requested;
            truncated = false;

            ReportDetail detail = requested;
            string json = Build(scan, problems, tool, toolVersion, reportId, detail, false, writeRecommendations);

            while (json.Length > maxBytes && detail != ReportDetail.Summary)
            {
                detail = detail == ReportDetail.Full ? ReportDetail.Zones : ReportDetail.Summary;
                truncated = true;
                json = Build(scan, problems, tool, toolVersion, reportId, detail, true, writeRecommendations);
            }

            actualDetail = detail;
            return json;
        }

        public static string Build(
            SceneScan scan,
            SqProblemList problems,
            string tool,
            string toolVersion,
            string reportId,
            ReportDetail detail,
            bool truncated,
            WriteSection writeRecommendations)
        {
            var w = new SqJsonWriter();
            w.BeginObject();

            WriteEnvelope(w, scan, tool, toolVersion, reportId, detail, truncated);
            scan.Urp.Write(w);
            scan.Scale.Write(w);
            WriteGrid(w, scan);
            WriteRenderers(w, scan);
            WriteMaterials(w, scan);
            WriteLights(w, scan);
            WriteExisting(w, scan);
            WriteNavMesh(w, scan);
            WriteZones(w, scan, detail);

            if (detail == ReportDetail.Full) WriteObjectTable(w, scan);

            if (problems != null) problems.Write(w, "problems");

            if (writeRecommendations != null)
            {
                w.Key("recommendations");
                writeRecommendations(w);
            }

            w.EndObject();
            return w.Finish();
        }

        static void WriteEnvelope(SqJsonWriter w, SceneScan scan, string tool, string toolVersion,
            string reportId, ReportDetail detail, bool truncated)
        {
            w.Prop("schemaVersion", SchemaVersion);
            w.Prop("reportId", reportId);
            w.Prop("tool", tool);
            w.Prop("toolVersion", toolVersion);
            w.Prop("coreVersion", SqLightingCore.Version);
            w.Prop("unityVersion", Application.unityVersion);
            w.Prop("scene", scan.SceneName);
            w.Prop("scenePath", scan.ScenePath);
            w.Prop("sceneGuid", scan.SceneGuid);
            w.Prop("generatedUtc", ReportWriter.UtcNow());
            w.Prop("detail", DetailName(detail));
            w.Prop("scanSeconds", (float)scan.ScanSeconds);
            if (truncated)
            {
                w.Prop("truncated", true);
                w.Prop("truncationReason", "report exceeded the configured byte budget; detail was reduced");
            }
        }

        /// <summary>
        /// The voxelisation the zones came from.
        ///
        /// Cheap to write and the first thing worth checking when a zone list looks wrong:
        /// a coarse cell size cannot resolve a doorway, and an interior count near zero
        /// means the scene has no enclosed space for rooms to be found in.
        /// </summary>
        static void WriteGrid(SqJsonWriter w, SceneScan scan)
        {
            if (scan.Grid == null) return;

            w.BeginObject("grid");
            w.Prop("cellSize", scan.Grid.CellSize);
            w.Prop("cells", scan.Grid.CellCount);
            w.Prop("solid", scan.Grid.SolidCount);
            w.Prop("exterior", scan.Grid.ExteriorCount);
            w.Prop("interior", scan.Grid.InteriorCount);
            w.Prop("source", scan.Grid.UsedRendererFallback ? "renderer-bounds" : "colliders");
            w.Prop("erosionPasses", ZoneSegmenter.ErosionPassesFor(scan.Grid.CellSize));
            w.EndObject();
        }

        static void WriteRenderers(SqJsonWriter w, SceneScan scan)
        {
            int contributeGi = 0, occluder = 0, occludee = 0, batching = 0;
            int opaque = 0, cutout = 0, transparent = 0;
            int withUv2 = 0, skinned = 0, mayMove = 0, doubleSided = 0, missingMaterial = 0;

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];

                if (r.ContributeGI) contributeGi++;
                if (r.OccluderStatic) occluder++;
                if (r.OccludeeStatic) occludee++;
                if (r.BatchingStatic) batching++;
                if (r.HasUv2) withUv2++;
                if (r.IsSkinned) skinned++;
                if (r.MayMove) mayMove++;
                if (r.AnyDoubleSided) doubleSided++;
                if (r.AnyMissingMaterial) missingMaterial++;

                MaterialFacts dominant = r.Dominant;
                if (dominant == null) continue;
                if (dominant.IsTransparent) transparent++;
                else if (string.Equals(dominant.SurfaceType, "cutout", StringComparison.Ordinal)) cutout++;
                else opaque++;
            }

            w.BeginObject("renderers");
            w.Prop("total", scan.Renderers.Count);
            w.Prop("contributeGI", contributeGi);
            w.Prop("occluderStatic", occluder);
            w.Prop("occludeeStatic", occludee);
            w.Prop("batchingStatic", batching);
            w.Prop("opaque", opaque);
            w.Prop("cutout", cutout);
            w.Prop("transparent", transparent);
            w.Prop("withUv2", withUv2);
            w.Prop("skinned", skinned);
            w.Prop("mayMove", mayMove);
            w.Prop("doubleSided", doubleSided);
            if (missingMaterial > 0) w.Prop("missingMaterial", missingMaterial);
            w.EndObject();
        }

        static void WriteMaterials(SqJsonWriter w, SceneScan scan)
        {
            w.BeginObject("materials");
            w.Prop("uniqueCount", scan.UniqueMaterialCount);
            scan.SmoothnessHistogram.Write(w, "smoothnessHist");
            scan.MetallicHistogram.Write(w, "metallicHist");
            w.Prop("glossyFraction", scan.SmoothnessHistogram.FractionAtOrAbove(0.7f));

            if (scan.UnknownShaders.Count > 0)
            {
                w.BeginArray("unknownShaders");
                int written = 0;
                foreach (var kv in scan.UnknownShaders)
                {
                    if (written++ >= 20) break;
                    w.BeginObject();
                    w.Prop("shader", kv.Key);
                    w.Prop("slots", kv.Value);
                    w.EndObject();
                }
                w.EndArray();
            }

            w.EndObject();
        }

        static void WriteLights(SqJsonWriter w, SceneScan scan)
        {
            // Lights are written in full: a scene with more than a few dozen is rare, and
            // each one materially changes probe density and bake advice.
            w.BeginArray("lights");
            for (int i = 0; i < scan.Lights.Count && i < 128; i++)
            {
                LightFacts light = scan.Lights[i];
                w.BeginObject();
                w.Prop("name", light.Name);
                w.Prop("type", light.Type.ToString());
                w.Prop("mode", light.Mode.ToString());
                w.Prop("intensity", light.Intensity);
                w.Prop("color", light.Color);
                if (light.Type != LightType.Directional) w.Prop("range", light.Range);
                if (light.Type == LightType.Spot) w.Prop("spotAngle", light.SpotAngle);
                w.Prop("bounce", light.BounceIntensity);
                w.Prop("shadows", light.CastsShadows);
                w.Prop("pos", light.Position);
                if (light.ZoneId >= 0) w.Prop("zone", light.ZoneId);
                w.EndObject();
            }
            w.EndArray();
        }

        static void WriteExisting(SqJsonWriter w, SceneScan scan)
        {
            ExistingLighting e = scan.Existing;

            w.BeginObject("existing");
            w.Prop("lightProbeGroups", e.ProbeGroups.Count);
            w.Prop("lightProbePositions", e.TotalProbePositions);
            w.Prop("reflectionProbes", e.ReflectionProbes.Count);
            w.Prop("lightmapCount", e.LightmapCount);
            w.Prop("lightmapBytes", e.LightmapBytes);
            w.Prop("hasLightingDataAsset", e.HasLightingDataAsset);
            w.Prop("occlusionDataBytes", e.OcclusionDataBytes);
            w.EndObject();
        }

        static void WriteNavMesh(SqJsonWriter w, SceneScan scan)
        {
            w.BeginObject("navmesh");
            w.Prop("present", scan.NavMesh.Present);
            if (scan.NavMesh.Present)
            {
                w.Prop("vertexCount", scan.NavMesh.VertexCount);
                w.Prop("triCount", scan.NavMesh.TriangleCount);
                w.Prop("bounds", scan.NavMesh.Bounds);
            }
            w.EndObject();
        }

        static void WriteZones(SqJsonWriter w, SceneScan scan, ReportDetail detail)
        {
            w.BeginArray("zones");

            for (int i = 0; i < scan.Zones.Count; i++)
            {
                Zone zone = scan.Zones[i];

                w.BeginObject();
                w.Prop("id", zone.Id);
                w.Prop("bounds", zone.Bounds);
                w.Prop("renderers", zone.RendererCount);
                w.Prop("tris", zone.TriangleCount);
                w.Prop("floorArea", zone.FloorArea);
                w.Prop("openness", zone.Openness);
                w.Prop("meanSmoothness", zone.MeanSmoothness);
                w.Prop("maxSmoothness", zone.MaxSmoothness);
                w.Prop("metallicFrac", zone.MetallicFraction);
                w.Prop("reflectionWeight", zone.ReflectionWeight);
                w.Prop("gradientScore", zone.GradientScore);
                w.Prop("hasNavMesh", zone.HasNavMesh);

                w.BeginObject("lights");
                w.Prop("dir", zone.DirectionalLights);
                w.Prop("point", zone.PointLights);
                w.Prop("spot", zone.SpotLights);
                if (zone.AreaLights > 0) w.Prop("area", zone.AreaLights);
                w.EndObject();

                if (zone.Connections.Count > 0)
                {
                    w.BeginArray("connections");
                    for (int c = 0; c < zone.Connections.Count; c++)
                    {
                        w.BeginObject();
                        w.Prop("zoneId", zone.Connections[c].ZoneId);
                        w.Prop("gapWidth", zone.Connections[c].GapWidth);
                        w.EndObject();
                    }
                    w.EndArray();
                }

                if (detail != ReportDetail.Summary) WriteZoneRenderers(w, scan, zone);

                w.EndObject();
            }

            w.EndArray();
        }

        /// <summary>
        /// The heaviest renderers in a zone, by reflection weight then triangle count.
        ///
        /// Ranked rather than listed: the twenty objects that dominate a room's look are
        /// what a decision needs, and the remaining four hundred would only dilute them.
        /// </summary>
        static void WriteZoneRenderers(SqJsonWriter w, SceneScan scan, Zone zone)
        {
            var members = new List<RendererFacts>();
            for (int i = 0; i < scan.Renderers.Count; i++)
                if (scan.Renderers[i].ZoneId == zone.Id) members.Add(scan.Renderers[i]);

            members.Sort((a, b) =>
            {
                int byWeight = b.ReflectionWeight.CompareTo(a.ReflectionWeight);
                return byWeight != 0 ? byWeight : b.TriangleCount.CompareTo(a.TriangleCount);
            });

            int limit = Mathf.Min(members.Count, TopRenderersPerZone);
            if (limit == 0) return;

            w.BeginArray("topRenderers");
            for (int i = 0; i < limit; i++)
            {
                RendererFacts r = members[i];
                w.BeginObject();
                w.Prop("idx", r.Index);
                w.Prop("name", r.Name);
                w.Prop("tris", r.TriangleCount);
                w.Prop("area", r.SurfaceArea);
                w.Prop("smoothness", r.MaxSmoothness);
                w.Prop("reflWeight", r.ReflectionWeight);
                w.Prop("surface", r.Dominant != null ? r.Dominant.SurfaceType : "unknown");
                w.Prop("uv2", r.HasUv2);
                w.Prop("gi", r.ContributeGI);
                w.Prop("occluder", r.OccluderStatic);
                w.Prop("occludee", r.OccludeeStatic);
                w.EndObject();
            }
            w.EndArray();
        }

        /// <summary>
        /// Index to GlobalObjectId table. Full detail only.
        ///
        /// Roughly eighty characters per entry, so for 500 renderers this table alone is
        /// larger than everything else in the report put together. A plan built from a
        /// summary report addresses objects by index instead.
        /// </summary>
        static void WriteObjectTable(SqJsonWriter w, SceneScan scan)
        {
            w.BeginArray("objects");
            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                w.BeginObject();
                w.Prop("idx", r.Index);
                w.Prop("n", r.Name);
                w.Prop("gid", r.Id.Value);
                w.EndObject();
            }
            w.EndArray();
        }

        public static string DetailName(ReportDetail detail)
        {
            switch (detail)
            {
                case ReportDetail.Full: return "full";
                case ReportDetail.Zones: return "zones";
                default: return "summary";
            }
        }

        public static ReportDetail ParseDetail(string name)
        {
            if (string.Equals(name, "full", StringComparison.OrdinalIgnoreCase)) return ReportDetail.Full;
            if (string.Equals(name, "zones", StringComparison.OrdinalIgnoreCase)) return ReportDetail.Zones;
            return ReportDetail.Summary;
        }
    }
}
