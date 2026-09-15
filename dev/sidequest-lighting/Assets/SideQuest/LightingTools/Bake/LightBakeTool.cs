// SideQuest Lighting Tools - MIT
using System;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// Menu entry points for the light bake tool.
    ///
    /// A lightmap bake is the longest and least reversible thing in the suite, so the
    /// protocol is stricter than the others: the scene must be saved before a bake starts,
    /// the bake runs asynchronously and is polled through a status file, and the result
    /// records what was predicted against what actually came out.
    ///
    /// That last part matters. The atlas estimate is the riskiest arithmetic here, and
    /// comparing it to the real lightmap count after every bake is the only way it ever
    /// gets calibrated.
    /// </summary>
    public static class LightBakeTool
    {
        public const string MenuRoot = "Tools/SideQuest/Lighting/Light Bake/";

        public const string CodePredictionMiss = "LB050_PREDICTION_MISS";

        const string BakeStartedKey = "SideQuest.LightingTools.Bake.StartedTicks";
        const string PredictedAtlasKey = "SideQuest.LightingTools.Bake.PredictedAtlases";

        [InitializeOnLoadMethod]
        static void Register()
        {
            SqLightingCore.RegisterTool(LightBakePlan.ToolId, LightBakePlan.ToolVersion, 1);
        }

        static string StatusPath(SceneScan scan)
        {
            return ReportPaths.Result(scan.SceneName, LightBakePlan.ToolId + ".bake");
        }

        // ---------------------------------------------------------------- analyze

        [MenuItem(MenuRoot + "Analyze Scene", false, 100)]
        public static void AnalyzeScene()
        {
            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, "analyze");
            if (context == null) return;

            LightmapBudget budget;
            LightmapUvAudit uvAudit;
            LightBakePlan plan = LightBakePlan.Recommend(
                context.Scan, SqSettings.instance, context.Problems, out budget, out uvAudit);

            context.WriteReport(w =>
            {
                w.BeginObject();
                plan.WriteBody(w);
                budget.Write(w);
                w.Prop("missingLightmapUvs", uvAudit.MissingCount);
                w.Prop("fixableModels", uvAudit.FixableModelPaths.Count);
                w.EndObject();
            });
        }

        // ---------------------------------------------------------------- accept

        [MenuItem(MenuRoot + "Accept Recommendations", false, 101)]
        public static void AcceptRecommendations()
        {
            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, "accept");
            if (context == null) return;

            string reportId = PlanValidator.ReadReportId(context.ReportPath);
            if (string.IsNullOrEmpty(reportId))
            {
                SqLog.Fail(LightBakePlan.ToolId, "accept", "E_NO_REPORT",
                    "no analysis report for this scene - run Analyze Scene first");
                StatusFile.Write(LightBakePlan.ToolId, "accept", SqState.Error, context.Scan.SceneName,
                    message: "Run Analyze Scene before accepting recommendations.");
                return;
            }

            LightmapBudget budget;
            LightmapUvAudit uvAudit;
            LightBakePlan plan = LightBakePlan.Recommend(
                context.Scan, SqSettings.instance, context.Problems, out budget, out uvAudit);

            var w = new SqJsonWriter();
            plan.Write(w, context.Scan, reportId);
            ReportWriter.WriteAtomic(context.PlanPath, w.Finish());

            StatusFile.Write(LightBakePlan.ToolId, "accept", SqState.Idle, context.Scan.SceneName,
                reportPath: context.ReportPath, planPath: context.PlanPath, reportId: reportId);

            SqLog.Ok(LightBakePlan.ToolId, "accept",
                "plan", ReportPaths.ToProjectRelative(context.PlanPath),
                "resolution", SqFormat.Num(plan.LightmapResolution),
                "atlases", SqFormat.Num(budget.Atlases),
                "id", reportId);
        }

        // ---------------------------------------------------------------- apply

        [MenuItem(MenuRoot + "Apply Plan", false, 200)]
        public static void ApplyPlan() { Execute(true); }

        [MenuItem(MenuRoot + "Apply Plan (No Prompt)", false, 201)]
        public static void ApplyPlanNoPrompt() { Execute(false); }

        static void Execute(bool confirm)
        {
            const string action = "apply";

            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, action);
            if (context == null) return;

            DecisionPlan source;
            PlanValidation validation;
            if (!context.TryLoadPlan(action, out source, out validation)) return;

            var settings = SqSettings.instance;
            LightBakePlan plan = LightBakePlan.Read(source, settings, validation);
            if (validation.Rejected) { context.FailApply(action, validation); return; }

            LightmapBudget budget = LightmapBudget.Compute(context.Scan, plan.LightmapResolution, settings);

            if (!validation.RequireBudget("lightmap atlases", Mathf.CeilToInt(budget.Atlases),
                    settings.maxLightmapAtlases, PlanValidator.ErrBudget))
            {
                context.FailApply(action, validation);
                return;
            }

            var result = new ApplyResult { ReportId = source.ReportId, Validation = validation };

            if (confirm && !SqToolContext.Confirm("Apply Light Bake Settings", BuildConfirmMessage(plan, budget)))
            {
                validation.Note(SqSeverity.Info, action, "cancelled by the user");
                result.Skipped = 1;
                context.FinishApply(action, result);
                return;
            }

            using (SqUndo.Scope scope = SqUndo.Group("Apply Light Bake Plan"))
            {
                if (plan.ApplyLightingSettings)
                {
                    LightingSettings applied = plan.ApplySettings();
                    result.Stat("settingsAsset", AssetDatabase.GetAssetPath(applied));
                    result.Updated++;
                }
                else
                {
                    result.Skipped++;
                    validation.Note(SqSeverity.Info, "applyLightingSettings",
                        "left Unity's lighting settings untouched, as requested");
                }

                int scaled = plan.TuneScales(context.Scan, budget, settings);
                result.Stat("scaleInLightmapChanged", scaled);
                result.Updated += scaled;

                result.UndoGroup = scope.GroupId;
                result.Applied = true;
            }

            LightmapBudget after = LightmapBudget.Compute(context.Scan, plan.LightmapResolution, settings);
            result.Stat("resolution", plan.LightmapResolution);
            result.Stat("estimatedAtlases", after.Atlases);
            result.Stat("directional", plan.Directional ? "combined" : "non-directional");

            SessionState.SetFloat(PredictedAtlasKey, after.Atlases);

            context.MarkSceneDirty();
            context.FinishApply(action, result);
        }

        static string BuildConfirmMessage(LightBakePlan plan, LightmapBudget budget)
        {
            return string.Format(
                "Lightmap resolution: {0} texels per unit\nEstimated atlases: {1} x {2}px\nDirectionality: {3}\nBounces: {4}\n\nThis writes a LightingSettings asset and may reduce Scale In Lightmap on large surfaces. It does not start a bake.",
                SqFormat.Num(plan.LightmapResolution),
                SqFormat.Num(budget.Atlases),
                plan.LightmapMaxSize,
                plan.Directional ? "Combined directional" : "Non-directional (half the memory)",
                plan.Bounces);
        }

        // ---------------------------------------------------------------- lightmap UVs

        [MenuItem(MenuRoot + "Generate Missing Lightmap UVs", false, 250)]
        public static void GenerateMissingUvs()
        {
            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, "generate-uvs");
            if (context == null) return;

            LightmapUvAudit audit = LightmapUvAudit.Run(context.Scan, context.Problems);

            if (audit.FixableModelPaths.Count == 0)
            {
                SqLog.Ok(LightBakePlan.ToolId, "generate-uvs", "changed", "0",
                    "msg", "every lightmapped mesh already has lightmap UVs");
                return;
            }

            // Asset importers, not scene objects: this changes files on disk and affects
            // every other scene using the same models, so it is never folded into Apply.
            if (!EditorUtility.DisplayDialog("Generate Lightmap UVs",
                    string.Format(
                        "Switch on Generate Lightmap UVs for {0} model(s) and reimport them?\n\nThis modifies model import settings, which affects every scene that uses them, and cannot be undone with Ctrl+Z.",
                        audit.FixableModelPaths.Count),
                    "Generate", "Cancel"))
            {
                SqLog.Ok(LightBakePlan.ToolId, "generate-uvs", "changed", "0", "msg", "cancelled");
                return;
            }

            int changed = audit.GenerateMissingUvs();

            SqLog.Ok(LightBakePlan.ToolId, "generate-uvs",
                "changed", changed.ToString(),
                "unfixable", audit.UnfixableIndices.Count.ToString());
        }

        // ---------------------------------------------------------------- bake

        [MenuItem(MenuRoot + "Start Bake", false, 300)]
        public static void StartBake()
        {
            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, "bake", false);
            if (context == null) return;

            if (!context.RequireSavedScene("bake")) return;

            if (Lightmapping.isRunning)
            {
                SqLog.Fail(LightBakePlan.ToolId, "bake", "E_ALREADY_RUNNING", "a bake is already in progress");
                return;
            }

            if (BakeryDetector.IsPresent)
            {
                SqLog.Warn(LightBakePlan.ToolId, "bake",
                    "code", BakeryDetector.CodeBakeryPresent,
                    "msg", "Bakery is installed - this starts Unity's own bake, which will produce a second set of lightmaps");
            }

            SessionState.SetString(BakeStartedKey, DateTime.UtcNow.Ticks.ToString());

            // BakeAsync, not Bake: the blocking version holds the Editor for the whole
            // bake, which on any real scene is far past an MCP call timeout.
            Lightmapping.BakeAsync();

            StatusFile.Write(LightBakePlan.ToolId, "bake", SqState.Running, context.Scan.SceneName,
                message: "Lightmap bake started. Poll with Bake Status.", progress: 0f);

            SqLog.Ok(LightBakePlan.ToolId, "bake-start", "msg", "running in background - poll Bake Status");
        }

        [MenuItem(MenuRoot + "Bake Status", false, 301)]
        public static void BakeStatus()
        {
            SqToolContext context = SqToolContext.Begin(LightBakePlan.ToolId, LightBakePlan.ToolVersion, "bake-status", false);
            if (context == null) return;

            bool running = Lightmapping.isRunning;
            float progress = Lightmapping.buildProgress;
            double elapsed = ElapsedSeconds();

            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            int atlasCount = lightmaps != null ? lightmaps.Length : 0;
            long bytes = EstimateLightmapBytes(lightmaps);

            float predicted = SessionState.GetFloat(PredictedAtlasKey, -1f);

            var w = new SqJsonWriter();
            w.BeginObject();
            w.Prop("schemaVersion", 1);
            w.Prop("tool", LightBakePlan.ToolId);
            w.Prop("scene", context.Scan.SceneName);
            w.Prop("utc", ReportWriter.UtcNow());
            w.Prop("running", running);
            w.Prop("progress", progress);
            w.Prop("elapsedSeconds", (float)elapsed);
            w.Prop("atlasCount", atlasCount);
            w.Prop("lightmapBytes", bytes);
            w.Prop("lightmapMB", bytes / 1048576f);
            w.Prop("hasLightingDataAsset", Lightmapping.lightingDataAsset != null);
            if (predicted >= 0f) w.Prop("predictedAtlases", predicted);
            w.EndObject();

            string path = StatusPath(context.Scan);
            ReportWriter.WriteAtomic(path, w.Finish());

            StatusFile.Write(LightBakePlan.ToolId, "bake-status",
                running ? SqState.Running : SqState.Idle, context.Scan.SceneName,
                resultPath: path,
                message: running ? "bake in progress" : "bake idle",
                progress: running ? progress : 1f);

            SqLog.Ok(LightBakePlan.ToolId, "bake-status",
                "running", running ? "true" : "false",
                "progress", SqFormat.Num(progress),
                "elapsed", SqFormat.Num((float)elapsed),
                "atlases", atlasCount.ToString(),
                "lightmapMB", SqFormat.Num(bytes / 1048576f),
                "file", ReportPaths.ToProjectRelative(path));

            if (!running && atlasCount > 0 && predicted >= 0f) ComparePrediction(predicted, atlasCount);
        }

        /// <summary>
        /// Checks the atlas estimate against what the bake actually produced.
        ///
        /// The estimator is the least trustworthy arithmetic in this tool - it works from
        /// bounding-box area rather than real UV charts, so it can only ever be
        /// approximately right. Saying so out loud after every bake is what keeps it
        /// honest, and gives anyone tuning it real numbers to tune against.
        /// </summary>
        static void ComparePrediction(float predicted, int actual)
        {
            float predictedPages = Mathf.Max(1f, Mathf.Ceil(predicted));
            float error = Mathf.Abs(predictedPages - actual) / Mathf.Max(actual, 1);

            if (error <= 0.5f)
            {
                SqLog.Ok(LightBakePlan.ToolId, "bake-prediction",
                    "predicted", SqFormat.Num(predictedPages),
                    "actual", actual.ToString());
                return;
            }

            SqLog.Warn(LightBakePlan.ToolId, "bake-prediction",
                "code", CodePredictionMiss,
                "predicted", SqFormat.Num(predictedPages),
                "actual", actual.ToString(),
                "msg", "the atlas estimate was well off; it works from bounding-box area, not real UV charts");
        }

        static long EstimateLightmapBytes(LightmapData[] lightmaps)
        {
            if (lightmaps == null) return 0;

            long total = 0;
            for (int i = 0; i < lightmaps.Length; i++)
            {
                Texture2D color = lightmaps[i].lightmapColor;
                if (color == null) continue;

                total += (long)(color.width * color.height * 4 * 1.33f);
            }
            return total;
        }

        static double ElapsedSeconds()
        {
            string raw = SessionState.GetString(BakeStartedKey, null);
            if (string.IsNullOrEmpty(raw)) return 0.0;

            long ticks;
            if (!long.TryParse(raw, out ticks)) return 0.0;

            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
        }

        [MenuItem(MenuRoot + "Cancel Bake", false, 302)]
        public static void CancelBake()
        {
            if (!Lightmapping.isRunning)
            {
                SqLog.Ok(LightBakePlan.ToolId, "bake-cancel", "msg", "no bake was running");
                return;
            }

            Lightmapping.Cancel();
            SqLog.Ok(LightBakePlan.ToolId, "bake-cancel", "msg", "cancelled");
        }

        [MenuItem(MenuRoot + "Clear Baked Lighting", false, 303)]
        public static void ClearBakedLighting()
        {
            Lightmapping.Clear();
            Lightmapping.ClearLightingDataAsset();
            SqLog.Ok(LightBakePlan.ToolId, "clear", "msg", "baked lighting and lighting data asset cleared");
        }

        [MenuItem(MenuRoot + "Revert Last Apply", false, 400)]
        public static void RevertLastApply()
        {
            Undo.PerformUndo();
            SqLog.Ok(LightBakePlan.ToolId, "revert", "msg", "performed one undo step");
        }
    }
}
