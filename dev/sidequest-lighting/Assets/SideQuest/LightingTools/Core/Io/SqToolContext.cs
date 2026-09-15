// SideQuest Lighting Tools - MIT
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// The analyze / preview / apply lifecycle, in one place.
    ///
    /// All four tools speak the same protocol to the outside world: one machine-readable
    /// console line per action, a status.json pointing at whatever was produced, and a
    /// report, plan and result file in known locations. Implementing that four times would
    /// guarantee four subtly different dialects, and an agent driving the suite would have
    /// to learn each one.
    ///
    /// Menu items invoked over MCP are parameterless statics that cannot return anything,
    /// so these files and log lines are the entire return channel. They are the contract.
    /// </summary>
    public sealed class SqToolContext
    {
        public string Tool { get; private set; }
        public string ToolVersion { get; private set; }
        public SceneScan Scan { get; private set; }
        public SqProblemList Problems { get; private set; }

        public string ReportPath { get { return ReportPaths.Report(Scan.SceneName, Tool); } }
        public string PlanPath { get { return ReportPaths.Plan(Scan.SceneName, Tool); } }
        public string ResultPath { get { return ReportPaths.Result(Scan.SceneName, Tool); } }
        public string PreviewPath { get { return ReportPaths.Preview(Scan.SceneName, Tool); } }

        SqToolContext() { }

        /// <summary>
        /// Scans the scene and prepares an action. Returns null if the project is not URP,
        /// having already reported why - the suite is URP-only, and heuristics built on URP
        /// material properties produce confident nonsense anywhere else.
        /// </summary>
        public static SqToolContext Begin(string tool, string toolVersion, string action, bool needsGrid = true)
        {
            ReportPaths.EnsureDataFolders();

            var context = new SqToolContext { Tool = tool, ToolVersion = toolVersion };
            context.Problems = new SqProblemList();

            StatusFile.Write(tool, action, SqState.Running, "(scanning)");

            try
            {
                context.Scan = SceneScanner.Scan(context.Problems, needsGrid);
            }
            catch (Exception e)
            {
                SqLog.Fail(tool, action, "E_SCAN_FAILED", e.Message);
                StatusFile.Write(tool, action, SqState.Error, "(unknown)", message: e.Message);
                Debug.LogException(e);
                return null;
            }

            if (!UrpGuards.RequireUrp(context.Scan.Urp, context.Problems))
            {
                SqLog.Fail(tool, action, UrpGuards.NotUrp, "project is not using URP");
                StatusFile.Write(tool, action, SqState.Error, context.Scan.SceneName,
                    message: "This project does not use URP. SideQuest Lighting Tools targets URP only.");
                return null;
            }

            UrpGuards.CheckCommon(context.Scan.Urp, context.Problems);
            return context;
        }

        /// <summary>
        /// Writes the analysis report and logs the line an agent matches on.
        ///
        /// The report id is fresh every time. A plan carries the id it was built from, so
        /// re-analysing a scene invalidates any plan written against the previous state -
        /// which is the point, because renderer indices shift when the scene changes.
        /// </summary>
        public string WriteReport(SceneReport.WriteSection writeRecommendations)
        {
            string reportId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var settings = SqSettings.instance;

            ReportDetail requested = SceneReport.ParseDetail(settings.defaultReportDetail);
            ReportDetail actual;
            bool truncated;

            string json = SceneReport.BuildWithBudget(
                Scan, Problems, Tool, ToolVersion, reportId,
                requested, Mathf.Max(4096, settings.reportMaxBytes),
                writeRecommendations, out actual, out truncated);

            int bytes = ReportWriter.WriteAtomic(ReportPath, json);

            StatusFile.Write(Tool, "analyze", SqState.Idle, Scan.SceneName,
                reportPath: ReportPath, reportId: reportId);

            SqLog.Ok(Tool, "analyze",
                "report", ReportPaths.ToProjectRelative(ReportPath),
                "id", reportId,
                "bytes", bytes.ToString(),
                "detail", SceneReport.DetailName(actual),
                "zones", Scan.Zones.Count.ToString(),
                "renderers", Scan.Renderers.Count.ToString(),
                "problems", Problems.Count.ToString());

            if (truncated)
            {
                SqLog.Warn(Tool, "analyze",
                    "truncated", "true",
                    "msg", "report exceeded the byte budget and was written at reduced detail");
            }

            return reportId;
        }

        /// <summary>
        /// Loads and envelope-checks the plan for this tool and scene.
        ///
        /// On refusal this writes the result file and logs before returning false, so a
        /// caller only has to check the bool. Every refusal reaches the agent the same way
        /// a success would.
        /// </summary>
        public bool TryLoadPlan(string action, out DecisionPlan plan, out PlanValidation validation)
        {
            string expectedReportId = PlanValidator.ReadReportId(ReportPath);

            if (!PlanValidator.TryLoad(PlanPath, Tool, Scan, expectedReportId, out plan, out validation))
            {
                FailApply(action, validation);
                return false;
            }

            if (!PlanValidator.CheckSingleScene(validation))
            {
                FailApply(action, validation);
                return false;
            }

            return true;
        }

        /// <summary>Writes the result file, status and console line for a refused plan.</summary>
        public void FailApply(string action, PlanValidation validation)
        {
            var result = new ApplyResult
            {
                Tool = Tool,
                Action = action,
                Scene = Scan.SceneName,
                SceneGuid = Scan.SceneGuid,
                PlanPath = ReportPaths.ToProjectRelative(PlanPath),
                Applied = false,
                RejectCode = validation.RejectCode,
                RejectMessage = validation.RejectMessage,
                Validation = validation
            };

            ReportWriter.WriteAtomic(ResultPath, result.ToJson());

            StatusFile.Write(Tool, action, SqState.Error, Scan.SceneName,
                planPath: PlanPath, resultPath: ResultPath, message: validation.RejectMessage);

            SqLog.Fail(Tool, action, validation.RejectCode ?? "E_REJECTED", validation.RejectMessage ?? "plan refused");
        }

        /// <summary>Writes the result file, status and console line for a completed apply or preview.</summary>
        public void FinishApply(string action, ApplyResult result)
        {
            result.Tool = Tool;
            result.Action = action;
            result.Scene = Scan.SceneName;
            result.SceneGuid = Scan.SceneGuid;
            result.PlanPath = ReportPaths.ToProjectRelative(PlanPath);

            bool isPreview = string.Equals(action, "preview", StringComparison.Ordinal);
            string path = isPreview ? PreviewPath : ResultPath;

            ReportWriter.WriteAtomic(path, result.ToJson());

            StatusFile.Write(Tool, action, SqState.Idle, Scan.SceneName,
                planPath: PlanPath,
                resultPath: isPreview ? null : path,
                previewPath: isPreview ? path : null,
                reportId: result.ReportId);

            SqLog.Ok(Tool, action,
                "created", result.Created.ToString(),
                "updated", result.Updated.ToString(),
                "deleted", result.Deleted.ToString(),
                "kept", result.Kept.ToString(),
                "skipped", result.Skipped.ToString(),
                "warn", (result.Validation != null ? result.Validation.WarnCount : 0).ToString(),
                "file", ReportPaths.ToProjectRelative(path));
        }

        /// <summary>Objects created by this tool's previous run, for ownership checks.</summary>
        public System.Collections.Generic.HashSet<string> PreviouslyOwned()
        {
            return ApplyResult.ReadOwnedIds(ResultPath);
        }

        /// <summary>
        /// Refuses to run when the scene has unsaved changes.
        ///
        /// Used before anything slow and hard to undo - a lightmap or occlusion bake. If
        /// the result is bad, the way back should be reverting a saved file, not hoping the
        /// undo stack survived a forty-minute bake.
        /// </summary>
        public bool RequireSavedScene(string action)
        {
            if (!Scan.Scene.isDirty) return true;

            var validation = new PlanValidation();
            validation.Reject(PlanValidator.ErrSceneDirty,
                "The scene has unsaved changes. Save it first so a bad bake can be reverted from version control.");
            FailApply(action, validation);
            return false;
        }

        /// <summary>
        /// Confirmation dialog for the attended path.
        ///
        /// Every tool ships two menu items: one that asks, and one that does not. A modal
        /// dialog blocks the Editor, and an MCP call waiting on it times out with no way to
        /// answer - so an unattended path has to exist. It is a separate menu item rather
        /// than an automatic suppression, so the choice to skip the prompt is always
        /// someone's deliberate act.
        /// </summary>
        public static bool Confirm(string title, string message)
        {
            return EditorUtility.DisplayDialog(title, message, "Apply", "Cancel");
        }

        public void MarkSceneDirty()
        {
            if (Scan.Scene.IsValid() && Scan.Scene.isLoaded) EditorSceneManager.MarkSceneDirty(Scan.Scene);
        }
    }
}
