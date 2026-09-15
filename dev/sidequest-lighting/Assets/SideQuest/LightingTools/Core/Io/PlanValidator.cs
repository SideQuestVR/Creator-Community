// SideQuest Lighting Tools - MIT
using System;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Loads a decision plan and checks everything that is true of every plan, whatever
    /// tool wrote it.
    ///
    /// A plan is untrusted input. It may have been written by a model, hand-edited, left
    /// over from a different scene, or produced against a report from before the scene was
    /// rebuilt. None of those are attacks, and all of them silently ruin a scene if applied.
    /// So nothing here trusts the plan for anything except its own content, and every
    /// check runs to completion before a single object is touched.
    /// </summary>
    public static class PlanValidator
    {
        public const string ErrNotFound = "E_PLAN_NOT_FOUND";
        public const string ErrTooLarge = "E_PLAN_TOO_LARGE";
        public const string ErrMalformed = "E_PLAN_MALFORMED";
        public const string ErrSchema = "E_PLAN_SCHEMA";
        public const string ErrWrongTool = "E_WRONG_TOOL";
        public const string ErrWrongScene = "E_WRONG_SCENE";
        public const string ErrStalePlan = "E_STALE_PLAN";
        public const string ErrNonFinite = "E_NON_FINITE";
        public const string ErrMultiScene = "E_MULTI_SCENE";
        public const string ErrOutOfBounds = "E_OUT_OF_BOUNDS";
        public const string ErrBudget = "E_BUDGET";
        public const string ErrSceneDirty = "E_SCENE_DIRTY";

        /// <summary>
        /// Reads, parses and envelope-checks a plan.
        ///
        /// Returns false when the plan must not be applied at all. The validation object
        /// always carries the reason, so the caller logs one line and stops rather than
        /// deciding for itself how bad the problem was.
        /// </summary>
        public static bool TryLoad(
            string planPath,
            string expectedTool,
            SceneScan scan,
            string expectedReportId,
            out DecisionPlan plan,
            out PlanValidation validation)
        {
            plan = null;
            validation = new PlanValidation();

            string text, readError;
            if (!ReportWriter.TryReadBounded(planPath, SqJsonParser.MaxBytes, out text, out readError))
            {
                bool tooLarge = readError != null && readError.Contains("limit is");
                validation.Reject(tooLarge ? ErrTooLarge : ErrNotFound, readError);
                return false;
            }

            SqJsonValue root;
            string parseError;
            if (!SqJsonParser.TryParse(text, out root, out parseError))
            {
                validation.Reject(ErrMalformed, parseError);
                return false;
            }

            if (!root.IsObject)
            {
                validation.Reject(ErrSchema, "plan root must be a JSON object");
                return false;
            }

            // A single NaN anywhere rejects the whole plan rather than the one field.
            // Non-finite numbers do not arrive alone - they mean an upstream calculation
            // went wrong, and the neighbouring values are no more trustworthy.
            if (root.ContainsNonFinite())
            {
                validation.Reject(ErrNonFinite, "plan contains NaN or Infinity");
                return false;
            }

            var loaded = new DecisionPlan
            {
                Root = root,
                Tool = root["tool"].AsString(null),
                ToolVersion = root["toolVersion"].AsString(null),
                ReportId = root["reportId"].AsString(null),
                SceneGuid = root["sceneGuid"].AsString(null),
                Scene = root["scene"].AsString(null),
                Notes = root["notes"].AsString(null)
            };

            int schemaVersion = root["schemaVersion"].AsInt(-1);
            if (schemaVersion != DecisionPlan.SchemaVersion)
            {
                validation.Reject(ErrSchema, string.Format(
                    "plan schemaVersion is {0}, expected {1}", schemaVersion, DecisionPlan.SchemaVersion));
                return false;
            }

            if (!string.Equals(loaded.Tool, expectedTool, StringComparison.Ordinal))
            {
                validation.Reject(ErrWrongTool, string.Format(
                    "plan targets tool '{0}' but this is '{1}'", loaded.Tool ?? "(none)", expectedTool));
                return false;
            }

            if (!CheckScene(loaded, scan, validation)) return false;
            if (!CheckFreshness(loaded, expectedReportId, validation)) return false;

            plan = loaded;
            return true;
        }

        /// <summary>
        /// The plan must name the scene that is actually open.
        ///
        /// Without this, a plan built for a four-room interior applied to whatever scene
        /// happened to be open would place probes and probes boxes at coordinates that mean
        /// nothing there - and it would look like it worked.
        /// </summary>
        static bool CheckScene(DecisionPlan plan, SceneScan scan, PlanValidation validation)
        {
            if (string.IsNullOrEmpty(scan.SceneGuid))
            {
                // An unsaved scene has no GUID to match against, so fall back to the name
                // and say so rather than skipping the check in silence.
                validation.Note(SqSeverity.Warn, "sceneGuid",
                    "the open scene has never been saved, so the plan was matched by name only");

                if (!string.Equals(plan.Scene, scan.SceneName, StringComparison.Ordinal))
                {
                    validation.Reject(ErrWrongScene, string.Format(
                        "plan targets scene '{0}' but '{1}' is open", plan.Scene ?? "(none)", scan.SceneName));
                    return false;
                }
                return true;
            }

            if (!string.Equals(plan.SceneGuid, scan.SceneGuid, StringComparison.Ordinal))
            {
                validation.Reject(ErrWrongScene, string.Format(
                    "plan targets scene '{0}' ({1}) but '{2}' is open",
                    plan.Scene ?? "(unknown)", plan.SceneGuid ?? "(none)", scan.SceneName));
                return false;
            }

            return true;
        }

        /// <summary>
        /// The plan must be built from the newest analysis of this scene.
        ///
        /// Report indices refer to renderers by position in the last scan. If the scene
        /// changed and was re-scanned, index 47 is a different object - so applying an old
        /// plan retargets every decision onto whatever now sits at those indices.
        /// </summary>
        static bool CheckFreshness(DecisionPlan plan, string expectedReportId, PlanValidation validation)
        {
            if (string.IsNullOrEmpty(expectedReportId))
            {
                validation.Note(SqSeverity.Warn, "reportId",
                    "no analysis report was found for this scene, so plan freshness could not be verified");
                return true;
            }

            if (string.IsNullOrEmpty(plan.ReportId))
            {
                validation.Reject(ErrStalePlan,
                    "plan has no reportId. Run Analyze Scene and build the plan from its report.");
                return false;
            }

            if (!string.Equals(plan.ReportId, expectedReportId, StringComparison.Ordinal))
            {
                validation.Reject(ErrStalePlan, string.Format(
                    "plan was built from report {0} but the newest report for this scene is {1}. Re-run Analyze Scene and rebuild the plan.",
                    plan.ReportId, expectedReportId));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads just the reportId out of an existing report file.
        ///
        /// Parsing the whole report to read one field is wasteful but simple and exact,
        /// and this runs once per apply rather than per entry.
        /// </summary>
        public static string ReadReportId(string reportPath)
        {
            string text, error;
            if (!ReportWriter.TryReadBounded(reportPath, SqJsonParser.MaxBytes, out text, out error)) return null;

            SqJsonValue root;
            string parseError;
            if (!SqJsonParser.TryParse(text, out root, out parseError)) return null;

            return root["reportId"].AsString(null);
        }

        /// <summary>
        /// Rejects a position that is nowhere near the scene.
        ///
        /// Bounds are inflated generously because a probe legitimately sits outside the
        /// renderer bounds it was derived from. The check is aimed at values that are
        /// wrong by orders of magnitude, not at a metre of slack.
        /// </summary>
        public static bool IsPositionPlausible(Vector3 position, Bounds sceneBounds)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return false;
            if (float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z)) return false;

            Bounds inflated = sceneBounds;
            inflated.Expand(sceneBounds.size);
            return inflated.Contains(position);
        }

        /// <summary>
        /// v1 applies to the active scene only.
        ///
        /// With several scenes loaded, "the scene" is ambiguous: GlobalObjectIds resolve
        /// across all of them, and an apply could write into a scene the user never
        /// analysed and might not notice was dirty.
        /// </summary>
        public static bool CheckSingleScene(PlanValidation validation)
        {
            int loaded = SceneManager.loadedSceneCount;
            if (loaded <= 1) return true;

            validation.Reject(ErrMultiScene, string.Format(
                "{0} scenes are loaded. Close the others and re-run: this version applies to the active scene only.", loaded));
            return false;
        }
    }
}
