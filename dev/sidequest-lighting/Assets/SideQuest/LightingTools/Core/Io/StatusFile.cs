// SideQuest Lighting Tools - MIT
using System;

namespace SideQuest.LightingTools.Core
{
    public enum SqState { Idle, Running, Error }

    /// <summary>
    /// status.json is the one file an agent reads to orient itself.
    ///
    /// Without it, driving the suite means guessing filenames from scene names and
    /// system names and hoping the sanitiser agreed. With it, every action leaves a
    /// pointer to exactly what it produced, so the next step is always a single Read.
    /// Every action rewrites it - including failures, which is the case that matters most.
    /// </summary>
    public static class StatusFile
    {
        public static void Write(
            string tool,
            string action,
            SqState state,
            string sceneName,
            string reportPath = null,
            string planPath = null,
            string resultPath = null,
            string previewPath = null,
            string reportId = null,
            string message = null,
            float progress = -1f)
        {
            try
            {
                ReportPaths.EnsureDataFolders();

                var w = new SqJsonWriter();
                w.BeginObject();
                w.Prop("schemaVersion", 1);
                w.Prop("coreVersion", SqLightingCore.Version);
                w.Prop("tool", tool);
                w.Prop("lastAction", action);
                w.Prop("state", StateName(state));
                w.Prop("scene", sceneName);
                w.Prop("utc", ReportWriter.UtcNow());

                if (reportId != null) w.Prop("reportId", reportId);
                if (message != null) w.Prop("message", message);
                if (progress >= 0f) w.Prop("progress", progress);

                // Paths are project-relative so they can be pasted straight into a Read.
                if (reportPath != null) w.Prop("reportPath", ReportPaths.ToProjectRelative(reportPath));
                if (planPath != null) w.Prop("planPath", ReportPaths.ToProjectRelative(planPath));
                if (resultPath != null) w.Prop("resultPath", ReportPaths.ToProjectRelative(resultPath));
                if (previewPath != null) w.Prop("previewPath", ReportPaths.ToProjectRelative(previewPath));

                w.EndObject();

                ReportWriter.WriteAtomic(ReportPaths.StatusPath, w.Finish());
            }
            catch (Exception e)
            {
                // A failure to write the status file must never mask the actual result of
                // the action that was running.
                SqLog.Detail("status write failed: " + e.Message);
            }
        }

        static string StateName(SqState state)
        {
            switch (state)
            {
                case SqState.Running: return "running";
                case SqState.Error: return "error";
                default: return "idle";
            }
        }
    }
}
