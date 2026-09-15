// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// The reflection probe decision plan.
    ///
    /// The tool writes its own recommendation into the report in exactly this shape, so
    /// accepting it is a copy and adjusting it is an edit. An agent's useful contribution
    /// here is usually not placing probes from scratch but overriding a handful of
    /// judgements the heuristics cannot make - that this mirror deserves 256 while the
    /// corridor can drop to 32, or that a probe belongs on the other side of a pillar.
    /// </summary>
    public static class ReflectionProbePlan
    {
        public const string ToolId = "reflection-probes";
        public const string ToolVersion = "1.0.0";

        /// <summary>Structural ceiling, checked before the configured budget.</summary>
        public const int MaxProbeEntries = 256;

        public static void Write(SqJsonWriter w, List<ReflectionProbeSpec> specs, SceneScan scan, string reportId)
        {
            w.BeginObject();
            w.Prop("schemaVersion", DecisionPlan.SchemaVersion);
            w.Prop("tool", ToolId);
            w.Prop("toolVersion", ToolVersion);
            w.Prop("reportId", reportId);
            w.Prop("scene", scan.SceneName);
            w.Prop("sceneGuid", scan.SceneGuid);
            WriteBody(w, specs);
            w.EndObject();
        }

        public static void WriteBody(SqJsonWriter w, List<ReflectionProbeSpec> specs)
        {
            long totalBytes = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Action != ProbeAction.Delete) totalBytes += specs[i].EstimatedBytes;
            }

            w.Prop("probeCount", specs.Count);
            w.Prop("estimatedMemoryMB", totalBytes / 1048576f);

            w.BeginArray("probes");
            for (int i = 0; i < specs.Count; i++) specs[i].Write(w);
            w.EndArray();
        }

        public static List<ReflectionProbeSpec> Read(
            DecisionPlan source, SceneScan scan, SqSettings settings, PlanValidation validation)
        {
            var specs = new List<ReflectionProbeSpec>();

            SqJsonValue probes = source["probes"];
            if (!probes.IsArray)
            {
                validation.Reject(PlanValidator.ErrSchema, "plan has no probes array");
                return specs;
            }

            if (probes.Count > MaxProbeEntries)
            {
                validation.Reject(PlanValidator.ErrBudget, string.Format(
                    "plan lists {0} probes; the structural limit is {1}", probes.Count, MaxProbeEntries));
                return specs;
            }

            var seenIds = new HashSet<string>();

            for (int i = 0; i < probes.Count; i++)
            {
                ReflectionProbeSpec spec = ReflectionProbeSpec.Read(probes[i], i, scan, validation);
                if (spec == null) continue;

                // Two entries with the same id would fight over the same probe, with the
                // outcome decided by array order. Refuse rather than pick one.
                if (!seenIds.Add(spec.Id))
                {
                    validation.Note(SqSeverity.Warn, "probes[" + i + "].id",
                        "duplicate probe id; entry skipped", spec.Id, null);
                    continue;
                }

                specs.Add(spec);
            }

            CheckBudgets(specs, settings, validation);
            return specs;
        }

        /// <summary>
        /// Enforces the ceilings from settings.
        ///
        /// Read from SqSettings, never from the plan. A plan that could raise its own
        /// limits is not a limit, and cubemap memory is the one budget in this tool that a
        /// mistake spends invisibly - nothing in the Editor shows the total until the
        /// device runs out.
        /// </summary>
        static void CheckBudgets(List<ReflectionProbeSpec> specs, SqSettings settings, PlanValidation validation)
        {
            int live = 0;
            long totalBytes = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Action == ProbeAction.Delete) continue;
                live++;
                totalBytes += specs[i].EstimatedBytes;
            }

            if (!validation.RequireBudget("probes", live, settings.maxReflectionProbes, PlanValidator.ErrBudget))
                return;

            float megabytes = totalBytes / 1048576f;
            validation.RequireBudget("reflection cubemap memory", megabytes,
                settings.reflectionProbeMemoryBudgetMB, PlanValidator.ErrBudget, "MB");
        }
    }
}
