// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// One adjustment the validator made to a plan, or one reason it refused part of it.
    ///
    /// Clamping silently would be the worst of both worlds: the plan appears to have been
    /// followed while the scene got something else. Every adjustment is recorded, surfaced
    /// in the result file and summarised on the console, so an agent that asked for a 4096
    /// cubemap learns it got 512 and why.
    /// </summary>
    public sealed class ValidationIssue
    {
        public SqSeverity Severity;
        public string Field;
        public string Given;
        public string Used;
        public string Message;

        public void Write(SqJsonWriter w)
        {
            w.BeginObject();
            w.Prop("severity", SqProblem.SeverityName(Severity));
            w.Prop("field", Field);
            if (Given != null) w.Prop("given", Given);
            if (Used != null) w.Prop("used", Used);
            if (Message != null) w.Prop("message", Message);
            w.EndObject();
        }
    }

    /// <summary>
    /// The envelope of a decision plan, plus the raw body for the owning tool to read.
    ///
    /// Core validates identity and shape; only the tool knows what a probe entry means.
    /// That split is deliberate - it keeps the security-relevant checks in one place
    /// rather than repeated, differently, in four.
    /// </summary>
    public sealed class DecisionPlan
    {
        public const int SchemaVersion = 1;

        public string Tool;
        public string ToolVersion;
        public string ReportId;
        public string SceneGuid;
        public string Scene;
        public string Notes;

        /// <summary>The whole parsed document.</summary>
        public SqJsonValue Root = SqJsonValue.Null;

        public SqJsonValue this[string key] { get { return Root[key]; } }
    }

    /// <summary>
    /// Accumulates validation outcomes for one apply.
    ///
    /// Two distinct outcomes live here. A <em>rejection</em> stops the whole apply: the
    /// plan is stale, targets another scene, or asks for something outside a budget the
    /// user set. An <em>issue</em> is a value that was pulled into range and then used.
    /// Mixing the two would mean either refusing a plan over a typo'd blend distance, or
    /// quietly running one aimed at a different scene.
    /// </summary>
    public sealed class PlanValidation
    {
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

        public bool Rejected { get; private set; }
        public string RejectCode { get; private set; }
        public string RejectMessage { get; private set; }

        public int WarnCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Issues.Count; i++) if (Issues[i].Severity == SqSeverity.Warn) n++;
                return n;
            }
        }

        /// <summary>Refuses the whole plan. The first rejection wins, so the cause stays the root cause.</summary>
        public void Reject(string code, string message)
        {
            if (Rejected) return;
            Rejected = true;
            RejectCode = code;
            RejectMessage = message;
        }

        public void Note(SqSeverity severity, string field, string message, string given = null, string used = null)
        {
            Issues.Add(new ValidationIssue
            {
                Severity = severity,
                Field = field,
                Given = given,
                Used = used,
                Message = message
            });
        }

        // ---- clamping ----

        public float Clamp(string field, float given, float min, float max)
        {
            if (float.IsNaN(given) || float.IsInfinity(given))
            {
                Note(SqSeverity.Warn, field, "value was not finite; used the minimum",
                    given.ToString(), min.ToString());
                return min;
            }

            float used = Mathf.Clamp(given, min, max);
            if (!Mathf.Approximately(used, given))
            {
                Note(SqSeverity.Warn, field, string.Format("clamped to [{0}, {1}]", min, max),
                    given.ToString("0.###"), used.ToString("0.###"));
            }
            return used;
        }

        public int ClampInt(string field, int given, int min, int max)
        {
            int used = Mathf.Clamp(given, min, max);
            if (used != given)
            {
                Note(SqSeverity.Warn, field, string.Format("clamped to [{0}, {1}]", min, max),
                    given.ToString(), used.ToString());
            }
            return used;
        }

        /// <summary>Snaps to the nearest allowed value - cubemap resolutions, atlas sizes.</summary>
        public int Snap(string field, int given, int[] allowed)
        {
            if (allowed == null || allowed.Length == 0) return given;

            int best = allowed[0];
            int bestDistance = Mathf.Abs(given - best);

            for (int i = 1; i < allowed.Length; i++)
            {
                int distance = Mathf.Abs(given - allowed[i]);
                if (distance < bestDistance) { bestDistance = distance; best = allowed[i]; }
            }

            if (best != given)
                Note(SqSeverity.Warn, field, "snapped to the nearest supported value", given.ToString(), best.ToString());

            return best;
        }

        /// <summary>
        /// Enforces a ceiling from SqSettings. Returns false and rejects on overage.
        ///
        /// Budgets are never read from the plan. A plan that could raise its own limits is
        /// not a limit, and "place 100,000 probes" is exactly the mistake this is here to
        /// stop - whether it came from a bad heuristic, a typo, or a confused model.
        /// </summary>
        public bool RequireBudget(string field, int requested, int budget, string code)
        {
            if (requested <= budget) return true;

            Reject(code, string.Format(
                "{0} requests {1} but the configured budget is {2}. Raise it in the tool settings if that is intended.",
                field, requested, budget));
            return false;
        }

        public bool RequireBudget(string field, float requested, float budget, string code, string unit)
        {
            if (requested <= budget) return true;

            Reject(code, string.Format(
                "{0} requests {1:0.##}{3} but the configured budget is {2:0.##}{3}. Raise it in the tool settings if that is intended.",
                field, requested, budget, unit));
            return false;
        }

        public void Write(SqJsonWriter w, string key)
        {
            w.BeginArray(key);
            for (int i = 0; i < Issues.Count; i++) Issues[i].Write(w);
            w.EndArray();
        }

        /// <summary>Short console summary: the counts an agent checks before reading the result file.</summary>
        public string Summary()
        {
            if (Rejected) return RejectCode + ": " + RejectMessage;
            return string.Format("{0} adjustment(s)", Issues.Count);
        }
    }
}
