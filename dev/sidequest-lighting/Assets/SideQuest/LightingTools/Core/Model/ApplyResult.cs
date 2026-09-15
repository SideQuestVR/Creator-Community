// SideQuest Lighting Tools - MIT
using System.Collections.Generic;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// What an apply or preview actually did.
    ///
    /// Two jobs. First, it tells the agent the outcome in numbers it can check against
    /// what it asked for, without needing a screenshot. Second - and this is the part that
    /// matters later - it records which objects the tool created, so a future run knows
    /// what it owns. A delete is honoured only for objects listed here by a previous run;
    /// probes a person placed by hand are never touched.
    /// </summary>
    public sealed class ApplyResult
    {
        public const int SchemaVersion = 1;

        public string Tool;
        public string Action;
        public string Scene;
        public string SceneGuid;
        public string ReportId;
        public string PlanPath;

        public int Created;
        public int Updated;
        public int Deleted;
        public int Skipped;
        public int Kept;

        /// <summary>Undo group for this apply. Recorded so Revert Last Apply can target it.</summary>
        public int UndoGroup = -1;

        public bool Applied;
        public string RejectCode;
        public string RejectMessage;

        /// <summary>GlobalObjectIds of objects this run created, for ownership on the next run.</summary>
        public List<string> OwnedIds = new List<string>();

        /// <summary>Free-form per-tool counters, so a tool need not extend this class to report one number.</summary>
        public List<KeyValuePair<string, string>> Stats = new List<KeyValuePair<string, string>>();

        public PlanValidation Validation;

        public void Stat(string key, string value)
        {
            Stats.Add(new KeyValuePair<string, string>(key, value));
        }

        public void Stat(string key, int value) { Stat(key, value.ToString()); }
        public void Stat(string key, float value) { Stat(key, value.ToString("0.###")); }

        public void Own(SqObjectId id)
        {
            if (id.IsValid) OwnedIds.Add(id.Value);
        }

        public string ToJson()
        {
            var w = new SqJsonWriter();
            w.BeginObject();

            w.Prop("schemaVersion", SchemaVersion);
            w.Prop("coreVersion", SqLightingCore.Version);
            w.Prop("tool", Tool);
            w.Prop("action", Action);
            w.Prop("scene", Scene);
            if (SceneGuid != null) w.Prop("sceneGuid", SceneGuid);
            if (ReportId != null) w.Prop("reportId", ReportId);
            if (PlanPath != null) w.Prop("planPath", PlanPath);
            w.Prop("utc", ReportWriter.UtcNow());
            w.Prop("applied", Applied);

            if (RejectCode != null)
            {
                w.BeginObject("rejected");
                w.Prop("code", RejectCode);
                w.Prop("message", RejectMessage);
                w.EndObject();
            }

            w.BeginObject("counts");
            w.Prop("created", Created);
            w.Prop("updated", Updated);
            w.Prop("deleted", Deleted);
            w.Prop("kept", Kept);
            w.Prop("skipped", Skipped);
            w.EndObject();

            if (UndoGroup >= 0) w.Prop("undoGroup", UndoGroup);

            if (Stats.Count > 0)
            {
                w.BeginObject("stats");
                for (int i = 0; i < Stats.Count; i++) w.Prop(Stats[i].Key, Stats[i].Value);
                w.EndObject();
            }

            if (Validation != null) Validation.Write(w, "issues");

            if (OwnedIds.Count > 0)
            {
                w.BeginArray("ownedIds");
                for (int i = 0; i < OwnedIds.Count; i++) w.Value(OwnedIds[i]);
                w.EndArray();
            }

            w.EndObject();
            return w.Finish();
        }

        /// <summary>Reads back the ownership list from a previous run. Missing file means owning nothing.</summary>
        public static HashSet<string> ReadOwnedIds(string resultPath)
        {
            var owned = new HashSet<string>();

            string text, error;
            if (!ReportWriter.TryReadBounded(resultPath, SqJsonParser.MaxBytes, out text, out error)) return owned;

            SqJsonValue root;
            string parseError;
            if (!SqJsonParser.TryParse(text, out root, out parseError)) return owned;

            SqJsonValue ids = root["ownedIds"];
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i].AsString(null);
                if (!string.IsNullOrEmpty(id)) owned.Add(id);
            }

            return owned;
        }
    }
}
