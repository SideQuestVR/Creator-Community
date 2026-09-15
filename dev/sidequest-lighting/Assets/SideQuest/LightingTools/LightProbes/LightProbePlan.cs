// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// The light probe decision plan: what the tool recommends, and what it will accept back.
    ///
    /// The recommendation is written into the report in exactly this shape, so accepting it
    /// is a byte copy and adjusting it is an edit rather than an authoring exercise. That
    /// framing is the point: reviewing a concrete plan is a far more reliable task for an
    /// agent - or a person - than composing one from a pile of scene statistics.
    /// </summary>
    public sealed class LightProbePlan
    {
        public const string ToolId = "light-probes";
        public const string ToolVersion = "1.0.0";

        public ProbeStrategy Strategy = ProbeStrategy.Adaptive;

        public float Spacing = 2f;
        public float MinSpacing = 0.5f;
        public float MergeDistance = 0.5f;
        public float EdgeBand = 0.6f;
        public float[] LayerHeights = { 0.3f, 1.6f, 2.6f };
        public int MaxProbes = 2000;

        /// <summary>False keeps the existing probes and adds to them.</summary>
        public bool ReplaceExisting = true;

        /// <summary>Agent strategy: explicit world positions.</summary>
        public List<Vector3> Positions = new List<Vector3>();

        /// <summary>Agent strategy: treat supplied positions as floor points and layer above them.</summary>
        public bool EmitColumns;

        /// <summary>Volume strategy: GlobalObjectId of the bounding collider's GameObject.</summary>
        public string VolumeObjectId;

        public bool AssignContributeGI = true;
        public float ContributeGIMinExtent = 0.5f;
        public bool SmallObjectsReceiveFromProbes = true;

        // ---- reading ----

        /// <summary>
        /// Reads a plan body, clamping every scalar into a documented range.
        ///
        /// Clamped rather than refused: a blend distance of 40 is a typo, not an attack,
        /// and refusing the whole plan over it would waste a full analyse-plan round trip.
        /// Every clamp is recorded in the validation and ends up in the result file, so the
        /// difference between what was asked and what ran is never invisible.
        /// </summary>
        public static LightProbePlan Read(DecisionPlan source, SqSettings settings, PlanValidation validation)
        {
            var plan = new LightProbePlan();

            plan.Strategy = ParseStrategy(source["strategy"].AsString("adaptive"), validation);

            plan.Spacing = validation.Clamp("spacing", source["spacing"].AsFloat(settings.probeSpacing), 0.05f, 50f);
            plan.MinSpacing = validation.Clamp("minSpacing", source["minSpacing"].AsFloat(settings.probeMinSpacing), 0.05f, 50f);
            plan.MergeDistance = validation.Clamp("mergeDistance", source["mergeDistance"].AsFloat(settings.probeMergeDistance), 0.01f, 50f);
            plan.EdgeBand = validation.Clamp("edgeBand", source["edgeBand"].AsFloat(settings.probeEdgeBand), 0f, 20f);

            if (plan.MinSpacing > plan.Spacing)
            {
                validation.Note(SqSeverity.Warn, "minSpacing",
                    "minSpacing was larger than spacing, so it was lowered to match",
                    SqFormat.Num(plan.MinSpacing), SqFormat.Num(plan.Spacing));
                plan.MinSpacing = plan.Spacing;
            }

            plan.MaxProbes = validation.ClampInt("maxProbes", source["maxProbes"].AsInt(settings.maxLightProbes), 1, settings.maxLightProbes);
            plan.ReplaceExisting = source["replaceExisting"].AsBool(true);
            plan.EmitColumns = source["emitColumns"].AsBool(false);
            plan.VolumeObjectId = source["volumeObjectId"].AsString(null);

            ReadLayerHeights(source, settings, plan, validation);
            ReadPositions(source, settings, plan, validation);

            SqJsonValue gi = source["contributeGI"];
            if (gi.IsObject)
            {
                plan.AssignContributeGI = gi["enabled"].AsBool(true);
                plan.ContributeGIMinExtent = validation.Clamp("contributeGI.minExtent", gi["minExtent"].AsFloat(0.5f), 0f, 100f);
                plan.SmallObjectsReceiveFromProbes = gi["smallObjectsReceiveFromProbes"].AsBool(true);
            }

            return plan;
        }

        static void ReadLayerHeights(DecisionPlan source, SqSettings settings, LightProbePlan plan, PlanValidation validation)
        {
            SqJsonValue heights = source["layerHeights"];
            if (!heights.IsArray || heights.Count == 0)
            {
                plan.LayerHeights = settings.probeLayerHeights;
                return;
            }

            if (heights.Count > 16)
            {
                validation.Note(SqSeverity.Warn, "layerHeights",
                    "more than 16 layers were requested; the first 16 were used",
                    heights.Count.ToString(), "16");
            }

            var list = new List<float>();
            int limit = Mathf.Min(heights.Count, 16);
            for (int i = 0; i < limit; i++)
            {
                float height = heights[i].AsFloat(float.NaN);
                if (float.IsNaN(height)) continue;
                list.Add(Mathf.Clamp(height, -50f, 50f));
            }

            if (list.Count == 0)
            {
                validation.Note(SqSeverity.Warn, "layerHeights", "no usable layer heights; the defaults were used");
                plan.LayerHeights = settings.probeLayerHeights;
                return;
            }

            list.Sort();
            plan.LayerHeights = list.ToArray();
        }

        static void ReadPositions(DecisionPlan source, SqSettings settings, LightProbePlan plan, PlanValidation validation)
        {
            SqJsonValue positions = source["positions"];
            if (!positions.IsArray || positions.Count == 0) return;

            // A hard cap before the budget check: parsing 10 million entries to then refuse
            // them costs the same as accepting them.
            const int hardCap = 50000;
            if (positions.Count > hardCap)
            {
                validation.Reject(PlanValidator.ErrBudget, string.Format(
                    "plan lists {0} positions; the structural limit is {1}", positions.Count, hardCap));
                return;
            }

            int malformed = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 position;
                if (!positions[i].TryGetVector3(out position)) { malformed++; continue; }
                plan.Positions.Add(position);
            }

            if (malformed > 0)
            {
                validation.Note(SqSeverity.Warn, "positions", string.Format(
                    "{0} entries were not three finite numbers and were skipped", malformed),
                    positions.Count.ToString(), plan.Positions.Count.ToString());
            }
        }

        static ProbeStrategy ParseStrategy(string name, PlanValidation validation)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "adaptive": return ProbeStrategy.Adaptive;
                case "navmesh": return ProbeStrategy.NavMesh;
                case "volume": return ProbeStrategy.MeshVolume;
                case "agent": return ProbeStrategy.Agent;
                default:
                    validation.Note(SqSeverity.Warn, "strategy",
                        "unrecognised strategy; adaptive was used", name, "adaptive");
                    return ProbeStrategy.Adaptive;
            }
        }

        public static string StrategyName(ProbeStrategy strategy)
        {
            switch (strategy)
            {
                case ProbeStrategy.NavMesh: return "navmesh";
                case ProbeStrategy.MeshVolume: return "volume";
                case ProbeStrategy.Agent: return "agent";
                default: return "adaptive";
            }
        }

        // ---- writing ----

        public void Write(SqJsonWriter w, SceneScan scan, string reportId)
        {
            w.BeginObject();
            w.Prop("schemaVersion", DecisionPlan.SchemaVersion);
            w.Prop("tool", ToolId);
            w.Prop("toolVersion", ToolVersion);
            w.Prop("reportId", reportId);
            w.Prop("scene", scan.SceneName);
            w.Prop("sceneGuid", scan.SceneGuid);
            WriteBody(w);
            w.EndObject();
        }

        /// <summary>Body only - used for the recommendations block inside a report.</summary>
        public void WriteBody(SqJsonWriter w)
        {
            w.Prop("strategy", StrategyName(Strategy));
            w.Prop("spacing", Spacing);
            w.Prop("minSpacing", MinSpacing);
            w.Prop("mergeDistance", MergeDistance);
            w.Prop("edgeBand", EdgeBand);
            w.Prop("maxProbes", MaxProbes);
            w.Prop("replaceExisting", ReplaceExisting);

            w.BeginArray("layerHeights");
            for (int i = 0; i < LayerHeights.Length; i++) w.Value(LayerHeights[i]);
            w.EndArray();

            if (Strategy == ProbeStrategy.Agent)
            {
                w.Prop("emitColumns", EmitColumns);
                w.BeginArray("positions");
                for (int i = 0; i < Positions.Count; i++) w.Value(Positions[i]);
                w.EndArray();
            }

            if (VolumeObjectId != null) w.Prop("volumeObjectId", VolumeObjectId);

            w.BeginObject("contributeGI");
            w.Prop("enabled", AssignContributeGI);
            w.Prop("minExtent", ContributeGIMinExtent);
            w.Prop("smallObjectsReceiveFromProbes", SmallObjectsReceiveFromProbes);
            w.EndObject();
        }

        /// <summary>
        /// The deterministic recommendation for this scene.
        ///
        /// Strategy choice follows what the scene can actually support, in order of how
        /// much it knows about the space: a NavMesh is an explicit statement about where
        /// players go, so it wins when present; otherwise density is derived from the
        /// geometry itself.
        /// </summary>
        public static LightProbePlan Recommend(SceneScan scan, SqSettings settings)
        {
            var plan = new LightProbePlan
            {
                Spacing = settings.probeSpacing,
                MinSpacing = settings.probeMinSpacing,
                MergeDistance = settings.probeMergeDistance,
                EdgeBand = settings.probeEdgeBand,
                LayerHeights = settings.probeLayerHeights,
                MaxProbes = settings.maxLightProbes,
                ContributeGIMinExtent = Mathf.Max(0.3f, scan.Scale.ExtentP25)
            };

            plan.Strategy = scan.NavMesh.Present ? ProbeStrategy.NavMesh : ProbeStrategy.Adaptive;

            // Scale spacing to the scene. A 2m default is right for a room and far too fine
            // for a landscape, where it would burn the whole probe budget on open ground.
            if (scan.Scale.HasBounds)
            {
                float footprint = Mathf.Sqrt(Mathf.Max(scan.Scale.FloorAreaEstimate, 1f));
                float suggested = Mathf.Clamp(footprint / 24f, 1f, 8f);
                plan.Spacing = Mathf.Max(settings.probeSpacing, suggested);
                plan.MergeDistance = Mathf.Clamp(plan.Spacing * 0.25f, 0.2f, 2f);
            }

            // Layers above the estimated ceiling would be placed inside it and then merged
            // away, so trim them rather than emitting probes that cannot survive.
            var usable = new List<float>();
            for (int i = 0; i < plan.LayerHeights.Length; i++)
            {
                if (usable.Count > 0 && plan.LayerHeights[i] > scan.Scale.CeilingHeightEstimate) break;
                usable.Add(plan.LayerHeights[i]);
            }
            if (usable.Count > 0) plan.LayerHeights = usable.ToArray();

            return plan;
        }
    }
}
