// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Menu entry points for the light probe tool.
    ///
    /// These are the MCP surface. execute_editor_menu_item can invoke a parameterless
    /// static and nothing else - no arguments in, no value out - so each of these reads its
    /// input from a file, writes its output to a file, and logs one machine-readable line.
    ///
    /// Apply exists twice on purpose. The attended version shows a confirmation dialog,
    /// which is right for a person and fatal for an agent: a modal dialog blocks the Editor,
    /// and an MCP call waiting on it times out with no way to answer. The unattended
    /// version is a separate menu item rather than an automatic suppression, so skipping
    /// the prompt is always a deliberate choice rather than something that quietly happens
    /// because a tool noticed it was being driven.
    /// </summary>
    public static class LightProbeTool
    {
        public const string MenuRoot = "Tools/SideQuest/Lighting/Light Probes/";

        public const string CodeApvActive = "LP010_APV_ACTIVE";
        public const string CodeApvBundleRisk = "LP011_APV_BUNDLE_RISK";
        public const string CodeExistingGroup = "LP012_EXISTING_GROUP";

        [InitializeOnLoadMethod]
        static void Register()
        {
            SqLightingCore.RegisterTool(LightProbePlan.ToolId, LightProbePlan.ToolVersion, 1);
        }

        // ---------------------------------------------------------------- analyze

        [MenuItem(MenuRoot + "Analyze Scene", false, 100)]
        public static void AnalyzeScene()
        {
            SqToolContext context = SqToolContext.Begin(LightProbePlan.ToolId, LightProbePlan.ToolVersion, "analyze");
            if (context == null) return;

            SceneScannerCache.Store(context.Scan);

            LightProbePlan recommendation = LightProbePlan.Recommend(context.Scan, SqSettings.instance);

            CheckProbeSystem(context, recommendation);
            CheckExistingGroups(context);
            ContributeGiAssigner.Plan(context.Scan, recommendation, context.Problems);

            context.WriteReport(w =>
            {
                w.BeginObject();
                recommendation.WriteBody(w);
                w.EndObject();
            });
        }

        /// <summary>
        /// Classic probe groups and Adaptive Probe Volumes are mutually exclusive, and the
        /// URP asset decides which one the renderer reads.
        ///
        /// If APV is active, a LightProbeGroup is simply ignored - so placing one would
        /// report success and change nothing visible, which is the worst possible outcome.
        /// The check runs during analysis so the answer arrives before any work is done.
        /// </summary>
        static void CheckProbeSystem(SqToolContext context, LightProbePlan recommendation)
        {
            if (!context.Scan.Urp.UsesAdaptiveProbeVolumes) return;

            context.Problems.Add(CodeApvActive, SqSeverity.Error,
                "This project's URP asset uses Adaptive Probe Volumes, so a Light Probe Group would be ignored at render time.")
                .WithAction("Switch the URP asset's Light Probe System to Legacy to use this tool, or place Probe Volumes instead.")
                .ClientDependent();

            context.Problems.Add(CodeApvBundleRisk, SqSeverity.Warn,
                "Adaptive Probe Volume data lives in baking-set assets and streaming blobs rather than in the scene, so it may not travel with a Banter world bundle - and in Kit mode there is no scene to carry it at all.")
                .WithAction("For a shipped Banter world, prefer the classic Light Probe Group, whose data is stored in the scene's lighting data asset.")
                .ClientDependent();
        }

        /// <summary>Warns before adopting a group somebody else made, rather than silently rewriting it.</summary>
        static void CheckExistingGroups(SqToolContext context)
        {
            int groups = context.Scan.Existing.ProbeGroups.Count;
            if (groups == 0) return;

            bool ours = false;
            for (int i = 0; i < groups; i++)
            {
                LightProbeGroup group = context.Scan.Existing.ProbeGroups[i];
                if (group != null && group.gameObject.name == ProbeGroupWriter.DefaultGroupName) ours = true;
            }

            if (ours && groups == 1) return;

            context.Problems.Add(CodeExistingGroup, SqSeverity.Info, string.Format(
                "The scene already has {0} Light Probe Group(s) holding {1} probe(s). Applying with replaceExisting will overwrite the first one.",
                groups, context.Scan.Existing.TotalProbePositions))
                .WithCount(groups)
                .WithAction("Set replaceExisting to false in the plan to add to the existing probes instead of replacing them.");
        }

        // ---------------------------------------------------------------- accept

        [MenuItem(MenuRoot + "Accept Recommendations", false, 101)]
        public static void AcceptRecommendations()
        {
            SqToolContext context = SqToolContext.Begin(LightProbePlan.ToolId, LightProbePlan.ToolVersion, "accept");
            if (context == null) return;

            string reportId = PlanValidator.ReadReportId(context.ReportPath);
            if (string.IsNullOrEmpty(reportId))
            {
                SqLog.Fail(LightProbePlan.ToolId, "accept", "E_NO_REPORT",
                    "no analysis report for this scene - run Analyze Scene first");
                StatusFile.Write(LightProbePlan.ToolId, "accept", SqState.Error, context.Scan.SceneName,
                    message: "Run Analyze Scene before accepting recommendations.");
                return;
            }

            LightProbePlan recommendation = LightProbePlan.Recommend(context.Scan, SqSettings.instance);

            var w = new SqJsonWriter();
            recommendation.Write(w, context.Scan, reportId);
            ReportWriter.WriteAtomic(context.PlanPath, w.Finish());

            StatusFile.Write(LightProbePlan.ToolId, "accept", SqState.Idle, context.Scan.SceneName,
                reportPath: context.ReportPath, planPath: context.PlanPath, reportId: reportId);

            SqLog.Ok(LightProbePlan.ToolId, "accept",
                "plan", ReportPaths.ToProjectRelative(context.PlanPath),
                "strategy", LightProbePlan.StrategyName(recommendation.Strategy),
                "id", reportId);
        }

        // ---------------------------------------------------------------- preview / apply

        [MenuItem(MenuRoot + "Preview Plan", false, 200)]
        public static void PreviewPlan() { Execute("preview", false); }

        [MenuItem(MenuRoot + "Apply Plan", false, 201)]
        public static void ApplyPlan() { Execute("apply", true); }

        [MenuItem(MenuRoot + "Apply Plan (No Prompt)", false, 202)]
        public static void ApplyPlanNoPrompt() { Execute("apply", false); }

        /// <summary>
        /// Preview and apply run the same code down to the last decision.
        ///
        /// If preview used a separate path it would eventually drift from apply, and a
        /// preview that does not predict the apply is worse than none - it would be trusted.
        /// </summary>
        static void Execute(string action, bool confirm)
        {
            bool isPreview = action == "preview";

            SqToolContext context = SqToolContext.Begin(LightProbePlan.ToolId, LightProbePlan.ToolVersion, action);
            if (context == null) return;

            DecisionPlan source;
            PlanValidation validation;
            if (!context.TryLoadPlan(action, out source, out validation)) return;

            var settings = SqSettings.instance;
            LightProbePlan plan = LightProbePlan.Read(source, settings, validation);

            if (validation.Rejected) { context.FailApply(action, validation); return; }

            if (!validation.RequireBudget("maxProbes", plan.MaxProbes, settings.maxLightProbes, PlanValidator.ErrBudget))
            {
                context.FailApply(action, validation);
                return;
            }

            List<Vector3> positions = RunSampler(context, plan, validation);
            if (validation.Rejected) { context.FailApply(action, validation); return; }

            List<ContributeGiAssigner.Decision> giDecisions =
                ContributeGiAssigner.Plan(context.Scan, plan, context.Problems);

            var result = new ApplyResult
            {
                ReportId = source.ReportId,
                Validation = validation
            };

            result.Stat("strategy", LightProbePlan.StrategyName(plan.Strategy));
            result.Stat("probes", positions.Count);
            result.Stat("layers", plan.LayerHeights.Length);
            result.Stat("spacing", plan.Spacing);
            result.Stat("contributeGiChanges", giDecisions.Count);

            if (isPreview)
            {
                DrawPreview(context, positions, giDecisions);
                result.Created = positions.Count;
                result.Applied = false;
                context.FinishApply(action, result);
                return;
            }

            if (confirm && !SqToolContext.Confirm("Apply Light Probe Plan", BuildConfirmMessage(context, plan, positions, giDecisions)))
            {
                validation.Note(SqSeverity.Info, "apply", "cancelled by the user");
                result.Skipped = positions.Count;
                context.FinishApply(action, result);
                return;
            }

            ApplyToScene(context, plan, positions, giDecisions, result);
            context.FinishApply(action, result);
        }

        static List<Vector3> RunSampler(SqToolContext context, LightProbePlan plan, PlanValidation validation)
        {
            ProbeSamplerSettings samplerSettings = ProbeSamplerSettings.FromSettings(SqSettings.instance);
            samplerSettings.Spacing = plan.Spacing;
            samplerSettings.MinSpacing = plan.MinSpacing;
            samplerSettings.MergeDistance = plan.MergeDistance;
            samplerSettings.EdgeBand = plan.EdgeBand;
            samplerSettings.LayerHeights = plan.LayerHeights;
            samplerSettings.MaxProbes = plan.MaxProbes;

            IProbeSampler sampler = CreateSampler(plan, samplerSettings, context.Scan, validation);
            if (sampler == null) return new List<Vector3>();

            string reason;
            if (!sampler.IsAvailable(context.Scan, samplerSettings, out reason))
            {
                validation.Reject("E_STRATEGY_UNAVAILABLE", reason);
                return new List<Vector3>();
            }

            var stats = new SamplerStats();
            List<Vector3> positions = sampler.Sample(context.Scan, samplerSettings, context.Problems, stats);

            SqLog.Detail(string.Format("sampler {0}: {1} raw, {2} kept ({3})",
                sampler.Id, stats.RawSamples, stats.AfterDecimation, stats.Notes));

            return positions;
        }

        static IProbeSampler CreateSampler(LightProbePlan plan, ProbeSamplerSettings settings, SceneScan scan, PlanValidation validation)
        {
            switch (plan.Strategy)
            {
                case ProbeStrategy.NavMesh:
                    return new NavMeshSampler();

                case ProbeStrategy.MeshVolume:
                    settings.VolumeObject = ResolveVolumeObject(plan, validation);
                    return new MeshVolumeSampler();

                case ProbeStrategy.Agent:
                    return new AgentSampler { Positions = plan.Positions, EmitColumns = plan.EmitColumns };

                default:
                    return new AdaptiveSampler();
            }
        }

        static GameObject ResolveVolumeObject(LightProbePlan plan, PlanValidation validation)
        {
            if (string.IsNullOrEmpty(plan.VolumeObjectId))
            {
                // Falling back to the selection keeps the window usable; a plan-driven run
                // has no selection, so it fails the availability check with a clear reason.
                return Selection.activeGameObject;
            }

            var id = new SqObjectId(plan.VolumeObjectId);
            GameObject resolved = id.Resolve<GameObject>();

            if (resolved == null)
            {
                validation.Note(SqSeverity.Warn, "volumeObjectId",
                    "the volume object could not be resolved in the open scene; the current selection was used instead");
                return Selection.activeGameObject;
            }

            return resolved;
        }

        static void ApplyToScene(
            SqToolContext context,
            LightProbePlan plan,
            List<Vector3> positions,
            List<ContributeGiAssigner.Decision> giDecisions,
            ApplyResult result)
        {
            // One undo group for the whole apply. Four hundred probes and a dozen flag
            // changes as four hundred and twelve undo steps is the same as no undo at all.
            using (SqUndo.Scope scope = SqUndo.Group("Apply Light Probe Plan"))
            {
                bool created;
                LightProbeGroup group = ProbeGroupWriter.FindOrCreateGroup(context.Scan, out created);

                List<Vector3> final = positions;
                if (!plan.ReplaceExisting)
                {
                    List<Vector3> existing = ProbeGroupWriter.ReadWorldPositions(group);
                    existing.AddRange(positions);
                    final = SpatialHash.DecimateMedoid(existing, plan.MergeDistance);
                }

                int before = group.probePositions != null ? group.probePositions.Length : 0;
                ProbeGroupWriter.CommitWorldPositions(group, final, "Apply Light Probe Plan");

                result.Created = created ? final.Count : Mathf.Max(0, final.Count - before);
                result.Updated = created ? 0 : Mathf.Min(before, final.Count);
                result.Deleted = Mathf.Max(0, before - final.Count);
                result.UndoGroup = scope.GroupId;
                result.Applied = true;

                if (created) result.Own(SqObjectId.Of(group.gameObject));

                int giChanged = ContributeGiAssigner.Apply(giDecisions);
                result.Stat("contributeGiApplied", giChanged);
                result.Stat("probesBefore", before);
                result.Stat("probesAfter", final.Count);
            }

            context.MarkSceneDirty();
            SqPreviewDraw.instance.Clear();
        }

        static void DrawPreview(SqToolContext context, List<Vector3> positions, List<ContributeGiAssigner.Decision> giDecisions)
        {
            SqPreviewDraw preview = SqPreviewDraw.instance;
            preview.Begin(LightProbePlan.ToolId);

            for (int i = 0; i < positions.Count; i++)
                preview.AddPoint(positions[i], SqPreviewDraw.ColorCreate, 0.06f);

            for (int i = 0; i < giDecisions.Count; i++)
            {
                ContributeGiAssigner.Decision d = giDecisions[i];
                if (d.Renderer == null) continue;

                preview.AddTint(d.Renderer.Renderer,
                    d.ClearContributeGI ? SqPreviewDraw.ColorDelete : SqPreviewDraw.ColorUpdate);
            }

            preview.Commit();
        }

        static string BuildConfirmMessage(
            SqToolContext context, LightProbePlan plan, List<Vector3> positions, List<ContributeGiAssigner.Decision> giDecisions)
        {
            int existing = context.Scan.Existing.TotalProbePositions;

            return string.Format(
                "Strategy: {0}\nProbes: {1} (scene currently has {2})\nLayers: {3}\nSpacing: {4:0.##}m\nContribute GI changes: {5}\n\nThis can be undone with a single Ctrl+Z.",
                LightProbePlan.StrategyName(plan.Strategy),
                positions.Count,
                existing,
                plan.LayerHeights.Length,
                plan.Spacing,
                giDecisions.Count);
        }

        // ---------------------------------------------------------------- revert

        [MenuItem(MenuRoot + "Revert Last Apply", false, 300)]
        public static void RevertLastApply()
        {
            // Undo.PerformUndo rather than reconstructing the previous state: the undo
            // stack already holds exactly what changed, and a reconstruction would be a
            // second implementation of the apply with its own chances to be wrong.
            Undo.PerformUndo();
            SqPreviewDraw.instance.Clear();

            SqLog.Ok(LightProbePlan.ToolId, "revert", "msg", "performed one undo step");
        }

        [MenuItem(MenuRoot + "Clear Preview", false, 301)]
        public static void ClearPreview()
        {
            SqPreviewDraw.instance.Clear();
            SqLog.Ok(LightProbePlan.ToolId, "clear-preview");
        }
    }
}
