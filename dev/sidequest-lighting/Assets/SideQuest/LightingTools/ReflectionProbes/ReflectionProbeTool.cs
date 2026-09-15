// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// Menu entry points for the reflection probe tool.
    ///
    /// Same contract as the light probe tool: parameterless statics, input from a plan
    /// file, output to a result file, one machine-readable console line per action, and a
    /// separate no-prompt apply so an MCP-driven run never blocks on a modal dialog.
    /// </summary>
    public static class ReflectionProbeTool
    {
        public const string MenuRoot = "Tools/SideQuest/Lighting/Reflection Probes/";

        [InitializeOnLoadMethod]
        static void Register()
        {
            SqLightingCore.RegisterTool(ReflectionProbePlan.ToolId, ReflectionProbePlan.ToolVersion, 1);
        }

        // ---------------------------------------------------------------- analyze

        [MenuItem(MenuRoot + "Analyze Scene", false, 100)]
        public static void AnalyzeScene()
        {
            SqToolContext context = SqToolContext.Begin(
                ReflectionProbePlan.ToolId, ReflectionProbePlan.ToolVersion, "analyze");
            if (context == null) return;

            UrpGuards.CheckReflection(context.Scan.Urp, context.Problems);

            var options = ReflectionProbePlacer.Options.FromSettings(SqSettings.instance);
            List<ReflectionProbeSpec> specs = ReflectionProbePlacer.Recommend(context.Scan, options, context.Problems);
            ReflectionProbeApplier.ReconcileWithScene(context.Scan, specs);

            // WriteBody fills an already-open object, because the same method also writes
            // the body of a standalone plan file. The caller owns the braces.
            context.WriteReport(w =>
            {
                w.BeginObject();
                ReflectionProbePlan.WriteBody(w, specs);
                w.EndObject();
            });
        }

        // ---------------------------------------------------------------- accept

        [MenuItem(MenuRoot + "Accept Recommendations", false, 101)]
        public static void AcceptRecommendations()
        {
            SqToolContext context = SqToolContext.Begin(
                ReflectionProbePlan.ToolId, ReflectionProbePlan.ToolVersion, "accept");
            if (context == null) return;

            string reportId = PlanValidator.ReadReportId(context.ReportPath);
            if (string.IsNullOrEmpty(reportId))
            {
                SqLog.Fail(ReflectionProbePlan.ToolId, "accept", "E_NO_REPORT",
                    "no analysis report for this scene - run Analyze Scene first");
                StatusFile.Write(ReflectionProbePlan.ToolId, "accept", SqState.Error, context.Scan.SceneName,
                    message: "Run Analyze Scene before accepting recommendations.");
                return;
            }

            var options = ReflectionProbePlacer.Options.FromSettings(SqSettings.instance);
            List<ReflectionProbeSpec> specs = ReflectionProbePlacer.Recommend(context.Scan, options, context.Problems);
            ReflectionProbeApplier.ReconcileWithScene(context.Scan, specs);

            var w = new SqJsonWriter();
            ReflectionProbePlan.Write(w, specs, context.Scan, reportId);
            ReportWriter.WriteAtomic(context.PlanPath, w.Finish());

            StatusFile.Write(ReflectionProbePlan.ToolId, "accept", SqState.Idle, context.Scan.SceneName,
                reportPath: context.ReportPath, planPath: context.PlanPath, reportId: reportId);

            SqLog.Ok(ReflectionProbePlan.ToolId, "accept",
                "plan", ReportPaths.ToProjectRelative(context.PlanPath),
                "probes", specs.Count.ToString(),
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

            SqToolContext context = SqToolContext.Begin(
                ReflectionProbePlan.ToolId, ReflectionProbePlan.ToolVersion, action);
            if (context == null) return;

            DecisionPlan source;
            PlanValidation validation;
            if (!context.TryLoadPlan(action, out source, out validation)) return;

            List<ReflectionProbeSpec> specs =
                ReflectionProbePlan.Read(source, context.Scan, SqSettings.instance, validation);

            if (validation.Rejected) { context.FailApply(action, validation); return; }

            var result = new ApplyResult { ReportId = source.ReportId, Validation = validation };
            Summarise(specs, result);

            if (isPreview)
            {
                DrawPreview(specs);
                CountForPreview(specs, result);
                result.Applied = false;
                context.FinishApply(action, result);
                return;
            }

            if (confirm && !SqToolContext.Confirm("Apply Reflection Probe Plan", BuildConfirmMessage(specs)))
            {
                validation.Note(SqSeverity.Info, "apply", "cancelled by the user");
                result.Skipped = specs.Count;
                context.FinishApply(action, result);
                return;
            }

            ReflectionProbeApplier.Apply(context.Scan, specs, context.PreviouslyOwned(), result);
            context.MarkSceneDirty();
            SqPreviewDraw.instance.Clear();

            context.FinishApply(action, result);
        }

        static void Summarise(List<ReflectionProbeSpec> specs, ApplyResult result)
        {
            long bytes = 0;
            int maxResolution = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Action == ProbeAction.Delete) continue;
                bytes += specs[i].EstimatedBytes;
                if (specs[i].Resolution > maxResolution) maxResolution = specs[i].Resolution;
            }

            result.Stat("estimatedMemoryMB", bytes / 1048576f);
            result.Stat("maxResolution", maxResolution);
        }

        static void CountForPreview(List<ReflectionProbeSpec> specs, ApplyResult result)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                switch (specs[i].Action)
                {
                    case ProbeAction.Create: result.Created++; break;
                    case ProbeAction.Update: result.Updated++; break;
                    case ProbeAction.Delete: result.Deleted++; break;
                    default: result.Kept++; break;
                }
            }
        }

        static void DrawPreview(List<ReflectionProbeSpec> specs)
        {
            SqPreviewDraw preview = SqPreviewDraw.instance;
            preview.Begin(ReflectionProbePlan.ToolId);

            for (int i = 0; i < specs.Count; i++)
            {
                ReflectionProbeSpec spec = specs[i];

                Color color;
                switch (spec.Action)
                {
                    case ProbeAction.Create: color = SqPreviewDraw.ColorCreate; break;
                    case ProbeAction.Update: color = SqPreviewDraw.ColorUpdate; break;
                    case ProbeAction.Delete: color = SqPreviewDraw.ColorDelete; break;
                    default: color = SqPreviewDraw.ColorKeep; break;
                }

                preview.AddPoint(spec.Position, color, 0.15f);

                if (spec.Action != ProbeAction.Delete)
                {
                    Bounds box = spec.WorldBox;
                    preview.AddBox(box.center, box.size, color,
                        string.Format("{0}  {1}px  imp {2}", spec.Id, spec.Resolution, spec.Importance));
                }
            }

            preview.Commit();
        }

        static string BuildConfirmMessage(List<ReflectionProbeSpec> specs)
        {
            int create = 0, update = 0, delete = 0, keep = 0;
            long bytes = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                switch (specs[i].Action)
                {
                    case ProbeAction.Create: create++; break;
                    case ProbeAction.Update: update++; break;
                    case ProbeAction.Delete: delete++; break;
                    default: keep++; break;
                }

                if (specs[i].Action != ProbeAction.Delete) bytes += specs[i].EstimatedBytes;
            }

            return string.Format(
                "Create: {0}\nUpdate: {1}\nDelete: {2}\nKeep: {3}\n\nEstimated cubemap memory: {4:0.#}MB\n\nProbes placed by hand are never modified. This can be undone with a single Ctrl+Z.",
                create, update, delete, keep, bytes / 1048576f);
        }

        // ---------------------------------------------------------------- bake

        [MenuItem(MenuRoot + "Bake Probes", false, 300)]
        public static void BakeProbes()
        {
            SqToolContext context = SqToolContext.Begin(
                ReflectionProbePlan.ToolId, ReflectionProbePlan.ToolVersion, "bake", false);
            if (context == null) return;

            List<ReflectionProbe> probes = context.Scan.Existing.ReflectionProbes;
            if (probes.Count == 0)
            {
                SqLog.Fail(ReflectionProbePlan.ToolId, "bake", "E_NO_PROBES",
                    "no reflection probes in this scene - apply a plan first");
                StatusFile.Write(ReflectionProbePlan.ToolId, "bake", SqState.Error, context.Scan.SceneName,
                    message: "No reflection probes to bake.");
                return;
            }

            // A probe captures the scene as currently lit. Baking it before the lightmaps
            // exist gives a cubemap of the unlit scene, and because baking again with
            // identical settings then fixes it, the cause looks like anything except an
            // ordering problem. Cheap to check, and the check is the only warning anyone
            // gets.
            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmaps == null || lightmaps.Length == 0)
            {
                SqLog.Warn(ReflectionProbePlan.ToolId, "bake",
                    "code", ReflectionProbePlacer.CodeBakeOrder,
                    "msg", "no lightmaps in this scene yet - probes baked now will capture the scene unlit; bake lightmaps first, then bake probes again");
            }

            // Baking each probe individually rather than through a full Lightmapping.Bake:
            // the full bake also recomputes lightmaps, which on a large scene is tens of
            // minutes to produce cubemaps that take seconds.
            string folder = "Assets/ReflectionProbes/" + ReportPaths.Sanitize(context.Scan.SceneName);
            EnsureAssetFolder(folder);

            int baked = 0;
            int failed = 0;

            try
            {
                for (int i = 0; i < probes.Count; i++)
                {
                    ReflectionProbe probe = probes[i];
                    if (probe == null) continue;

                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Baking Reflection Probes",
                        string.Format("{0} ({1} of {2})", probe.gameObject.name, i + 1, probes.Count),
                        (float)i / probes.Count))
                        break;

                    string path = string.Format("{0}/{1}.exr", folder, ReportPaths.Sanitize(probe.gameObject.name));

                    if (Lightmapping.BakeReflectionProbe(probe, path)) baked++;
                    else failed++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            var result = new ApplyResult { Applied = true };
            result.Stat("baked", baked);
            result.Stat("failed", failed);
            result.Stat("folder", folder);

            context.FinishApply("bake", result);
        }

        static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string[] parts = assetPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ---------------------------------------------------------------- misc

        [MenuItem(MenuRoot + "Revert Last Apply", false, 400)]
        public static void RevertLastApply()
        {
            Undo.PerformUndo();
            SqPreviewDraw.instance.Clear();
            SqLog.Ok(ReflectionProbePlan.ToolId, "revert", "msg", "performed one undo step");
        }

        [MenuItem(MenuRoot + "Clear Preview", false, 401)]
        public static void ClearPreview()
        {
            SqPreviewDraw.instance.Clear();
            SqLog.Ok(ReflectionProbePlan.ToolId, "clear-preview");
        }
    }
}
