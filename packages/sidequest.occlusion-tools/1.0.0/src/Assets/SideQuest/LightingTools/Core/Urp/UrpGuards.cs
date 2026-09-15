// SideQuest Lighting Tools - MIT
namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Turns a UrpFacts snapshot into findings.
    ///
    /// Every guard here is advisory about the shipped world, because the Banter client
    /// supplies its own URP asset at runtime. The tools still report them: a creator who
    /// knows box projection is off in their own project has a chance to ask why their
    /// reflections look wrong, whereas silence just wastes their afternoon.
    /// </summary>
    public static class UrpGuards
    {
        public const string NotUrp = "URP001_NOT_URP";
        public const string BoxProjectionDisabled = "URP002_BOX_PROJECTION_DISABLED";
        public const string BlendingDisabled = "URP003_BLENDING_DISABLED";
        public const string AdditionalLightsDisabled = "URP004_ADDITIONAL_LIGHTS_DISABLED";
        public const string ProbeSystemUnknown = "URP005_PROBE_SYSTEM_UNKNOWN";

        /// <summary>
        /// Raises URP001 if this is not a URP project. Returns false when the caller
        /// should stop: the suite targets URP only, and Built-in-shaped advice produced
        /// from URP heuristics would be confidently wrong.
        /// </summary>
        public static bool RequireUrp(UrpFacts facts, SqProblemList problems)
        {
            if (facts.HasUrp) return true;

            problems.Add(NotUrp, SqSeverity.Error,
                "This project does not use the Universal Render Pipeline. SideQuest Lighting Tools targets URP only.")
                .WithAction("Assign a Universal Render Pipeline Asset in Project Settings > Graphics.");

            return false;
        }

        /// <summary>Checks that apply to every tool in the suite.</summary>
        public static void CheckCommon(UrpFacts facts, SqProblemList problems)
        {
            if (string.Equals(facts.LightProbeSystem, "unknown"))
            {
                problems.Add(ProbeSystemUnknown, SqSeverity.Info,
                    "Could not read the URP asset's light probe system, so probe advice assumes the classic system.")
                    .WithAction("Check Lighting > Light Probe System in the URP Asset if probe results look wrong.")
                    .ClientDependent();
            }
        }

        /// <summary>Checks specific to reflection probe work.</summary>
        public static void CheckReflection(UrpFacts facts, SqProblemList problems)
        {
            if (facts.ReflectionProbeBoxProjection.HasValue && !facts.ReflectionProbeBoxProjection.Value)
            {
                problems.Add(BoxProjectionDisabled, SqSeverity.Warn,
                    "Reflection Probe Box Projection is disabled on this project's URP asset, so box-projected probes will look like distant cubemaps.")
                    .WithAction("Enable Reflection Probe Box Projection on the URP Asset. In a Banter world the client's URP asset decides this at runtime.")
                    .ClientDependent();
            }

            if (facts.ReflectionProbeBlending.HasValue && !facts.ReflectionProbeBlending.Value)
            {
                problems.Add(BlendingDisabled, SqSeverity.Warn,
                    "Reflection Probe Blending is disabled on this project's URP asset, so reflections will pop when crossing between probes.")
                    .WithAction("Enable Reflection Probe Blending on the URP Asset. In a Banter world the client's URP asset decides this at runtime.")
                    .ClientDependent();
            }
        }

        /// <summary>Checks specific to baking. A disabled additional-light mode is an argument for baking, not against it.</summary>
        public static void CheckBake(UrpFacts facts, SqProblemList problems)
        {
            if (string.Equals(facts.AdditionalLightsMode, "disabled"))
            {
                problems.Add(AdditionalLightsDisabled, SqSeverity.Info,
                    "Additional lights are disabled on this project's URP asset: point and spot lights contribute nothing at runtime unless their light is baked.")
                    .WithAction("Bake point and spot lights, or set them to Baked mode.")
                    .ClientDependent();
            }
        }
    }
}
