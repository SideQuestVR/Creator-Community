// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.Occlusion
{
    /// <summary>
    /// The occlusion decision plan.
    ///
    /// Unlike the probe tools, this one does not place objects - it changes flags on
    /// existing ones and sets three bake parameters. So the plan carries the parameters
    /// plus an optional list of per-renderer overrides, and everything not overridden
    /// follows the classifier.
    ///
    /// Overrides matter here more than elsewhere. Whether something should occlude is
    /// partly an authoring decision: a creator may know that a particular partition is
    /// meant to be see-through later, or that a decorative arch should never cull the room
    /// behind it. The classifier cannot know either.
    /// </summary>
    public sealed class OcclusionPlan
    {
        public const string ToolId = "occlusion";
        public const string ToolVersion = "1.0.0";

        public const int MaxOverrides = 20000;

        public float MinOccluderFaceSize = 1.5f;
        public float MinOccluderThickness = 0.05f;
        public OcclusionParameters Parameters = new OcclusionParameters();

        /// <summary>True to write static flags; false to set bake parameters only.</summary>
        public bool AssignFlags = true;

        /// <summary>
        /// False keeps whatever Smallest Occluder / Smallest Hole / Backface Threshold are
        /// already set. Once someone has tuned these against their own world by eye, an
        /// apply that silently reset them would undo the only reliable information anyone
        /// has about that scene.
        /// </summary>
        public bool AssignBakeParameters = true;

        /// <summary>GlobalObjectId to forced occluder state. Absent means follow the classifier.</summary>
        public Dictionary<string, bool> OccluderOverrides = new Dictionary<string, bool>();

        /// <summary>GlobalObjectId to forced occludee state.</summary>
        public Dictionary<string, bool> OccludeeOverrides = new Dictionary<string, bool>();

        public static OcclusionPlan Recommend(SceneScan scan, SqSettings settings, SqProblemList problems,
            out List<OcclusionDecision> decisions)
        {
            var plan = new OcclusionPlan
            {
                MinOccluderFaceSize = ResolveFaceThreshold(scan, settings),
                MinOccluderThickness = settings.minOccluderThickness,
                AssignBakeParameters = settings.writeBakeParametersOnApply
            };

            decisions = OccluderClassifier.Classify(
                scan, plan.MinOccluderFaceSize, plan.MinOccluderThickness, problems);
            plan.Parameters = OcclusionParameters.Solve(scan, decisions, plan.MinOccluderFaceSize, problems);

            return plan;
        }

        /// <summary>
        /// How large a face has to be before the object is allowed to occlude.
        ///
        /// Occluders should be a small set of big solid things - walls, floors, ceilings,
        /// large structural masses. An absolute default cannot express that, because
        /// "big" only means anything relative to the space: 1.5m is a wall in a corridor
        /// and a crate in a warehouse.
        ///
        /// Sizing it against ceiling height gets the intent right in both. An occluder has
        /// to block a room-sized sight line, so anything much shorter than the room is
        /// furniture and should be culled rather than do the culling.
        ///
        /// Being too permissive here is what makes occlusion look broken: every marginal
        /// prop becomes an occluder, Umbra fattens each one to the voxel grid, and the
        /// accumulated phantom occlusion hides geometry the player is standing in front of.
        /// </summary>
        public static float ResolveFaceThreshold(SceneScan scan, SqSettings settings)
        {
            float fromCeiling = scan.Scale.CeilingHeightEstimate * Mathf.Max(settings.occluderCeilingFraction, 0f);
            return Mathf.Max(settings.minOccluderFaceSize, fromCeiling);
        }

        public void Write(SqJsonWriter w, SceneScan scan, string reportId)
        {
            w.BeginObject();
            w.Prop("schemaVersion", DecisionPlan.SchemaVersion);
            w.Prop("tool", ToolId);
            w.Prop("toolVersion", ToolVersion);
            w.Prop("reportId", reportId);
            w.Prop("scene", scan.SceneName);
            w.Prop("sceneGuid", scan.SceneGuid);
            WriteBody(w, null);
            w.EndObject();
        }

        /// <summary>
        /// Body only. Decisions are summarised rather than listed: a scene of 600 renderers
        /// would otherwise produce 600 entries that all say the same thing, and the useful
        /// content - what changes and why - fits in counts plus a handful of examples.
        /// </summary>
        public void WriteBody(SqJsonWriter w, List<OcclusionDecision> decisions)
        {
            w.Prop("assignFlags", AssignFlags);
            w.Prop("assignBakeParameters", AssignBakeParameters);
            w.Prop("minOccluderFaceSize", MinOccluderFaceSize);
            w.Prop("minOccluderThickness", MinOccluderThickness);
            Parameters.Write(w);

            if (decisions != null) WriteSummary(w, decisions);

            w.BeginObject("overrides");
            w.Prop("note", "Optional. Map a GlobalObjectId to true or false to force a renderer's role; anything not listed follows the classifier.");
            WriteOverrides(w, "occluder", OccluderOverrides);
            WriteOverrides(w, "occludee", OccludeeOverrides);
            w.EndObject();
        }

        static void WriteOverrides(SqJsonWriter w, string key, Dictionary<string, bool> overrides)
        {
            w.BeginObject(key);
            foreach (var pair in overrides) w.Prop(pair.Key, pair.Value);
            w.EndObject();
        }

        static void WriteSummary(SqJsonWriter w, List<OcclusionDecision> decisions)
        {
            int occluders = 0, occludees = 0, changes = 0, cleared = 0;
            var demoted = new List<int>();
            var promoted = new List<int>();

            for (int i = 0; i < decisions.Count; i++)
            {
                OcclusionDecision d = decisions[i];

                if (d.WantOccluder) occluders++;
                if (d.WantOccludee) occludees++;
                if (!d.Changed) continue;

                changes++;
                if (!d.WantOccluder && !d.WantOccludee) cleared++;
                else if (d.WantOccluder && !d.HasOccluder && promoted.Count < 8) promoted.Add(d.Renderer.Index);
                else if (!d.WantOccluder && d.HasOccluder && demoted.Count < 8) demoted.Add(d.Renderer.Index);
            }

            w.BeginObject("summary");
            w.Prop("renderers", decisions.Count);
            w.Prop("occluders", occluders);
            w.Prop("occludees", occludees);
            w.Prop("changes", changes);
            w.Prop("clearedEntirely", cleared);
            if (promoted.Count > 0) w.Prop("promotedToOccluderIdx", promoted);
            if (demoted.Count > 0) w.Prop("demotedFromOccluderIdx", demoted);
            w.EndObject();
        }

        public static OcclusionPlan Read(DecisionPlan source, SqSettings settings, PlanValidation validation)
        {
            var plan = new OcclusionPlan();

            plan.AssignFlags = source["assignFlags"].AsBool(true);
            plan.AssignBakeParameters = source["assignBakeParameters"].AsBool(true);
            plan.MinOccluderFaceSize = validation.Clamp("minOccluderFaceSize",
                source["minOccluderFaceSize"].AsFloat(settings.minOccluderFaceSize), 0.05f, 50f);

            plan.MinOccluderThickness = validation.Clamp("minOccluderThickness",
                source["minOccluderThickness"].AsFloat(settings.minOccluderThickness), 0f, 10f);

            plan.Parameters = OcclusionParameters.Read(source["parameters"], validation);

            SqJsonValue overrides = source["overrides"];
            ReadOverrides(overrides["occluder"], plan.OccluderOverrides, "overrides.occluder", validation);
            ReadOverrides(overrides["occludee"], plan.OccludeeOverrides, "overrides.occludee", validation);

            return plan;
        }

        static void ReadOverrides(SqJsonValue source, Dictionary<string, bool> target, string field, PlanValidation validation)
        {
            if (!source.IsObject) return;

            if (source.Count > MaxOverrides)
            {
                validation.Reject(PlanValidator.ErrBudget, string.Format(
                    "{0} lists {1} entries; the structural limit is {2}", field, source.Count, MaxOverrides));
                return;
            }

            foreach (string key in source.Keys)
            {
                SqJsonValue value = source[key];
                if (!value.IsBool)
                {
                    validation.Note(SqSeverity.Warn, field + "." + key, "override must be true or false; entry skipped");
                    continue;
                }

                target[key] = value.AsBool(false);
            }
        }

        /// <summary>
        /// Applies plan overrides on top of the classifier's decisions.
        ///
        /// An override that names an object not in this scene is reported rather than
        /// ignored: it usually means the plan was built against a different scene or an
        /// older version of this one, and silently doing nothing would look like success.
        /// </summary>
        public void ApplyOverrides(List<OcclusionDecision> decisions, PlanValidation validation)
        {
            if (OccluderOverrides.Count == 0 && OccludeeOverrides.Count == 0) return;

            var byId = new Dictionary<string, OcclusionDecision>();
            for (int i = 0; i < decisions.Count; i++)
            {
                SqObjectId id = decisions[i].Renderer.Id;
                if (id.IsValid) byId[id.Value] = decisions[i];
            }

            int unresolved = 0;
            unresolved += ApplyOverrideSet(byId, OccluderOverrides, true);
            unresolved += ApplyOverrideSet(byId, OccludeeOverrides, false);

            if (unresolved > 0)
            {
                validation.Note(SqSeverity.Warn, "overrides", string.Format(
                    "{0} override(s) name objects that are not in this scene and had no effect", unresolved));
            }
        }

        static int ApplyOverrideSet(Dictionary<string, OcclusionDecision> byId, Dictionary<string, bool> overrides, bool isOccluder)
        {
            int unresolved = 0;

            foreach (var pair in overrides)
            {
                OcclusionDecision decision;
                if (!byId.TryGetValue(pair.Key, out decision)) { unresolved++; continue; }

                if (isOccluder)
                {
                    decision.WantOccluder = pair.Value;
                    decision.Reason = "forced by the plan";
                }
                else
                {
                    decision.WantOccludee = pair.Value;
                    decision.Reason = "forced by the plan";
                }
            }

            return unresolved;
        }
    }
}
