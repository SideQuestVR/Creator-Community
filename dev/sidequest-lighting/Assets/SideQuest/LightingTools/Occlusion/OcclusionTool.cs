// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Occlusion
{
    /// <summary>
    /// Menu entry points for the occlusion culling tool.
    ///
    /// The bake is the part that shapes this API. StaticOcclusionCulling.Compute blocks
    /// the Editor for as long as it takes, which for a large scene is far past any MCP
    /// call timeout, so the bake is started in the background and polled through a status
    /// file instead. Start Bake returns immediately; Bake Status writes what it knows.
    /// </summary>
    public static class OcclusionTool
    {
        public const string MenuRoot = "Tools/SideQuest/Lighting/Occlusion/";

        public const string CodeBanterKitMode = "OC040_KIT_MODE_NO_OCCLUSION";

        const string BakeStartedKey = "SideQuest.LightingTools.Occlusion.BakeStartedTicks";

        [InitializeOnLoadMethod]
        static void Register()
        {
            SqLightingCore.RegisterTool(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, 1);
        }

        static string StatusPath(SceneScan scan)
        {
            return ReportPaths.Result(scan.SceneName, OcclusionPlan.ToolId + ".bake");
        }

        // ---------------------------------------------------------------- analyze

        [MenuItem(MenuRoot + "Analyze Scene", false, 100)]
        public static void AnalyzeScene()
        {
            SqToolContext context = SqToolContext.Begin(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, "analyze");
            if (context == null) return;

            CheckBanterBundleMode(context.Problems);
            CheckExistingData(context);

            List<OcclusionDecision> decisions;
            OcclusionPlan plan = OcclusionPlan.Recommend(context.Scan, SqSettings.instance, context.Problems, out decisions);

            context.WriteReport(w =>
            {
                w.BeginObject();
                plan.WriteBody(w, decisions);
                w.EndObject();
            });
        }

        /// <summary>
        /// Occlusion data is stored in the scene, so a Banter world built as a Kit - which
        /// has no scene - carries none of it.
        ///
        /// Detected by reflection so this compiles and runs in a project without the Banter
        /// SDK, and says nothing at all when the SDK is absent.
        /// </summary>
        static void CheckBanterBundleMode(SqProblemList problems)
        {
            if (!BanterSdkPresent()) return;

            problems.Add(CodeBanterKitMode, SqSeverity.Info,
                "Occlusion data is stored in the scene. A Banter world exported as a Kit has no scene, so occlusion culling will not travel with it.")
                .WithAction("Export as Scene if the world should ship with baked occlusion.");
        }

        static bool BanterSdkPresent()
        {
            try
            {
                System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    if (assemblies[i].GetName().Name.IndexOf("Banter", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception e)
            {
                SqLog.Detail("banter sdk probe failed: " + e.Message);
            }

            return false;
        }

        static void CheckExistingData(SqToolContext context)
        {
            int bytes = context.Scan.Existing.OcclusionDataBytes;
            if (bytes <= 0) return;

            float megabytes = bytes / 1048576f;
            float budget = SqSettings.instance.occlusionDataBudgetMB;

            if (megabytes <= budget) return;

            context.Problems.Add(OcclusionParameters.CodeDataBudget, SqSeverity.Warn, string.Format(
                "The existing occlusion data is {0}MB, over the {1}MB budget. On Quest that is memory taken from everything else.",
                SqFormat.Num(megabytes), SqFormat.Num(budget)))
                .WithAction("Raise Smallest Hole and re-bake, or raise the budget in the tool settings.");
        }

        // ---------------------------------------------------------------- accept

        [MenuItem(MenuRoot + "Accept Recommendations", false, 101)]
        public static void AcceptRecommendations()
        {
            SqToolContext context = SqToolContext.Begin(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, "accept");
            if (context == null) return;

            string reportId = PlanValidator.ReadReportId(context.ReportPath);
            if (string.IsNullOrEmpty(reportId))
            {
                SqLog.Fail(OcclusionPlan.ToolId, "accept", "E_NO_REPORT",
                    "no analysis report for this scene - run Analyze Scene first");
                StatusFile.Write(OcclusionPlan.ToolId, "accept", SqState.Error, context.Scan.SceneName,
                    message: "Run Analyze Scene before accepting recommendations.");
                return;
            }

            List<OcclusionDecision> decisions;
            OcclusionPlan plan = OcclusionPlan.Recommend(context.Scan, SqSettings.instance, context.Problems, out decisions);

            var w = new SqJsonWriter();
            plan.Write(w, context.Scan, reportId);
            ReportWriter.WriteAtomic(context.PlanPath, w.Finish());

            StatusFile.Write(OcclusionPlan.ToolId, "accept", SqState.Idle, context.Scan.SceneName,
                reportPath: context.ReportPath, planPath: context.PlanPath, reportId: reportId);

            SqLog.Ok(OcclusionPlan.ToolId, "accept",
                "plan", ReportPaths.ToProjectRelative(context.PlanPath),
                "smallestHole", SqFormat.Num(plan.Parameters.SmallestHole),
                "smallestOccluder", SqFormat.Num(plan.Parameters.SmallestOccluder),
                "id", reportId);
        }

        // ---------------------------------------------------------------- preview / apply

        [MenuItem(MenuRoot + "Preview Plan", false, 200)]
        public static void PreviewPlan() { Execute("preview", false); }

        [MenuItem(MenuRoot + "Apply Plan", false, 201)]
        public static void ApplyPlan() { Execute("apply", true); }

        [MenuItem(MenuRoot + "Apply Plan (No Prompt)", false, 202)]
        public static void ApplyPlanNoPrompt() { Execute("apply", false); }

        static void Execute(string action, bool confirm)
        {
            bool isPreview = action == "preview";

            SqToolContext context = SqToolContext.Begin(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, action);
            if (context == null) return;

            DecisionPlan source;
            PlanValidation validation;
            if (!context.TryLoadPlan(action, out source, out validation)) return;

            OcclusionPlan plan = OcclusionPlan.Read(source, SqSettings.instance, validation);
            if (validation.Rejected) { context.FailApply(action, validation); return; }

            List<OcclusionDecision> decisions = OccluderClassifier.Classify(
                context.Scan, plan.MinOccluderFaceSize, plan.MinOccluderThickness, context.Problems);
            plan.ApplyOverrides(decisions, validation);

            var result = new ApplyResult { ReportId = source.ReportId, Validation = validation };
            Summarise(plan, decisions, result);

            if (isPreview)
            {
                DrawPreview(decisions);
                CountChanges(decisions, result);
                result.Applied = false;
                context.FinishApply(action, result);
                return;
            }

            if (confirm && !SqToolContext.Confirm("Apply Occlusion Plan", BuildConfirmMessage(plan, decisions)))
            {
                validation.Note(SqSeverity.Info, "apply", "cancelled by the user");
                result.Skipped = decisions.Count;
                context.FinishApply(action, result);
                return;
            }

            using (SqUndo.Scope scope = SqUndo.Group("Apply Occlusion Plan"))
            {
                int changed = plan.AssignFlags ? OccluderClassifier.Apply(decisions) : 0;
                ApplyBakeParameters(plan.Parameters);

                result.Updated = changed;
                result.UndoGroup = scope.GroupId;
                result.Applied = true;
            }

            context.MarkSceneDirty();
            SqPreviewDraw.instance.Clear();
            context.FinishApply(action, result);
        }

        /// <summary>
        /// Bake parameters are project-level statics, not scene data, so they are not part
        /// of the undo group and do not dirty the scene.
        /// </summary>
        static void ApplyBakeParameters(OcclusionParameters parameters)
        {
            StaticOcclusionCulling.smallestOccluder = parameters.SmallestOccluder;
            StaticOcclusionCulling.smallestHole = parameters.SmallestHole;
            StaticOcclusionCulling.backfaceThreshold = parameters.BackfaceThreshold;
        }

        static void Summarise(OcclusionPlan plan, List<OcclusionDecision> decisions, ApplyResult result)
        {
            int occluders = 0, occludees = 0;
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i].WantOccluder) occluders++;
                if (decisions[i].WantOccludee) occludees++;
            }

            result.Stat("occluders", occluders);
            result.Stat("occludees", occludees);
            result.Stat("smallestOccluder", plan.Parameters.SmallestOccluder);
            result.Stat("smallestHole", plan.Parameters.SmallestHole);
            result.Stat("backfaceThreshold", plan.Parameters.BackfaceThreshold);
            result.Stat("estimatedCells", (float)plan.Parameters.EstimatedCells);
        }

        static void CountChanges(List<OcclusionDecision> decisions, ApplyResult result)
        {
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i].Changed) result.Updated++;
                else result.Kept++;
            }
        }

        static void DrawPreview(List<OcclusionDecision> decisions)
        {
            SqPreviewDraw preview = SqPreviewDraw.instance;
            preview.Begin(OcclusionPlan.ToolId);

            for (int i = 0; i < decisions.Count; i++)
            {
                OcclusionDecision d = decisions[i];
                if (d.Renderer == null || d.Renderer.Renderer == null) continue;

                // Occluders and occludees are tinted differently because the mistake this
                // tool exists to catch - a scene of occludees with no occluders, or walls
                // demoted by a size test - is obvious at a glance in colour and invisible
                // in a list.
                if (d.WantOccluder) preview.AddTint(d.Renderer.Renderer, SqPreviewDraw.ColorOccluder);
                else if (d.WantOccludee) preview.AddTint(d.Renderer.Renderer, SqPreviewDraw.ColorOccludee);
                else preview.AddTint(d.Renderer.Renderer, SqPreviewDraw.ColorDelete);
            }

            preview.Commit();
        }

        static string BuildConfirmMessage(OcclusionPlan plan, List<OcclusionDecision> decisions)
        {
            int occluders = 0, occludees = 0, changes = 0;
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i].WantOccluder) occluders++;
                if (decisions[i].WantOccludee) occludees++;
                if (decisions[i].Changed) changes++;
            }

            return string.Format(
                "Occluders: {0}\nOccludees: {1}\nFlag changes: {2}\n\nSmallest occluder: {3}m\nSmallest hole: {4}m\nBackface threshold: {5}\n\nFlags can be undone with a single Ctrl+Z. Bake parameters are project settings and are not undone.",
                occluders, occludees, changes,
                SqFormat.Num(plan.Parameters.SmallestOccluder),
                SqFormat.Num(plan.Parameters.SmallestHole),
                SqFormat.Num(plan.Parameters.BackfaceThreshold));
        }

        // ---------------------------------------------------------------- bake

        [MenuItem(MenuRoot + "Start Bake", false, 300)]
        public static void StartBake()
        {
            SqToolContext context = SqToolContext.Begin(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, "bake", false);
            if (context == null) return;

            // A bake writes into the scene's occlusion data. If the scene has unsaved
            // changes and the result is bad, there is nothing clean to go back to.
            if (!context.RequireSavedScene("bake")) return;

            if (StaticOcclusionCulling.isRunning)
            {
                SqLog.Fail(OcclusionPlan.ToolId, "bake", "E_ALREADY_RUNNING", "an occlusion bake is already in progress");
                return;
            }

            SessionState.SetString(BakeStartedKey, DateTime.UtcNow.Ticks.ToString());

            // GenerateInBackground rather than Compute: Compute blocks the Editor until it
            // finishes, which no MCP call can wait out, and gives no way to report progress.
            StaticOcclusionCulling.GenerateInBackground();

            StatusFile.Write(OcclusionPlan.ToolId, "bake", SqState.Running, context.Scan.SceneName,
                message: "Occlusion bake started. Poll with Bake Status.");

            SqLog.Ok(OcclusionPlan.ToolId, "bake-start",
                "smallestOccluder", SqFormat.Num(StaticOcclusionCulling.smallestOccluder),
                "smallestHole", SqFormat.Num(StaticOcclusionCulling.smallestHole),
                "msg", "running in background - poll Bake Status");
        }

        /// <summary>
        /// Writes the current bake state to a file an agent can read.
        ///
        /// This is the whole reason the bake is asynchronous: an MCP call cannot sit on a
        /// long bake, so progress has to be pulled rather than waited for.
        /// </summary>
        [MenuItem(MenuRoot + "Bake Status", false, 301)]
        public static void BakeStatus()
        {
            SqToolContext context = SqToolContext.Begin(OcclusionPlan.ToolId, OcclusionPlan.ToolVersion, "bake-status", false);
            if (context == null) return;

            bool running = StaticOcclusionCulling.isRunning;
            int dataSize = (int)StaticOcclusionCulling.umbraDataSize;
            double elapsed = ElapsedSeconds();

            var w = new SqJsonWriter();
            w.BeginObject();
            w.Prop("schemaVersion", 1);
            w.Prop("tool", OcclusionPlan.ToolId);
            w.Prop("scene", context.Scan.SceneName);
            w.Prop("utc", ReportWriter.UtcNow());
            w.Prop("running", running);
            w.Prop("elapsedSeconds", (float)elapsed);
            w.Prop("dataSizeBytes", dataSize);
            w.Prop("dataSizeMB", dataSize / 1048576f);
            w.Prop("smallestOccluder", StaticOcclusionCulling.smallestOccluder);
            w.Prop("smallestHole", StaticOcclusionCulling.smallestHole);
            w.Prop("backfaceThreshold", StaticOcclusionCulling.backfaceThreshold);
            w.EndObject();

            string path = StatusPath(context.Scan);
            ReportWriter.WriteAtomic(path, w.Finish());

            StatusFile.Write(OcclusionPlan.ToolId, "bake-status",
                running ? SqState.Running : SqState.Idle, context.Scan.SceneName,
                resultPath: path,
                message: running ? "bake in progress" : "bake idle",
                progress: running ? 0.5f : 1f);

            SqLog.Ok(OcclusionPlan.ToolId, "bake-status",
                "running", running ? "true" : "false",
                "elapsed", SqFormat.Num((float)elapsed),
                "dataMB", SqFormat.Num(dataSize / 1048576f),
                "file", ReportPaths.ToProjectRelative(path));

            if (!running && dataSize > 0) CheckDataBudget(dataSize);
        }

        static double ElapsedSeconds()
        {
            string raw = SessionState.GetString(BakeStartedKey, null);
            if (string.IsNullOrEmpty(raw)) return 0.0;

            long ticks;
            if (!long.TryParse(raw, out ticks)) return 0.0;

            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
        }

        static void CheckDataBudget(int dataSize)
        {
            float megabytes = dataSize / 1048576f;
            float budget = SqSettings.instance.occlusionDataBudgetMB;
            if (megabytes <= budget) return;

            SqLog.Warn(OcclusionPlan.ToolId, "bake-status",
                "code", OcclusionParameters.CodeDataBudget,
                "msg", string.Format("occlusion data is {0}MB against a {1}MB budget - raise Smallest Hole and re-bake",
                    SqFormat.Num(megabytes), SqFormat.Num(budget)));
        }

        [MenuItem(MenuRoot + "Cancel Bake", false, 302)]
        public static void CancelBake()
        {
            if (!StaticOcclusionCulling.isRunning)
            {
                SqLog.Ok(OcclusionPlan.ToolId, "bake-cancel", "msg", "no bake was running");
                return;
            }

            StaticOcclusionCulling.Cancel();
            SqLog.Ok(OcclusionPlan.ToolId, "bake-cancel", "msg", "cancelled");
        }

        [MenuItem(MenuRoot + "Clear Occlusion Data", false, 303)]
        public static void ClearData()
        {
            StaticOcclusionCulling.Clear();
            SqLog.Ok(OcclusionPlan.ToolId, "clear-data", "msg", "occlusion data cleared");
        }

        // ---------------------------------------------------------------- misc

        [MenuItem(MenuRoot + "Revert Last Apply", false, 400)]
        public static void RevertLastApply()
        {
            Undo.PerformUndo();
            SqPreviewDraw.instance.Clear();
            SqLog.Ok(OcclusionPlan.ToolId, "revert", "msg", "performed one undo step");
        }

        [MenuItem(MenuRoot + "Clear Preview", false, 401)]
        public static void ClearPreview()
        {
            SqPreviewDraw.instance.Clear();
            SqLog.Ok(OcclusionPlan.ToolId, "clear-preview");
        }
    }
}
