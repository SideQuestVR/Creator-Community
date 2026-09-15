// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// The light bake decision plan: a lighting settings preset, a solved resolution, and
    /// whatever per-renderer scaling is needed to make it fit.
    ///
    /// The settings are written to a LightingSettings ASSET rather than into the scene's
    /// embedded lighting data. An asset is diffable in version control, can be shared
    /// between scenes, and can be inspected without opening the scene it belongs to -
    /// none of which is true of the embedded settings that Unity uses by default.
    /// </summary>
    public sealed class LightBakePlan
    {
        public const string ToolId = "light-bake";
        public const string ToolVersion = "1.0.0";

        public const string CodeTooManyRealtime = "LB010_TOO_MANY_REALTIME";
        public const string CodeNoLights = "LB011_NO_LIGHTS";
        public const string CodeSubtractive = "LB012_SUBTRACTIVE";

        public const string SettingsAssetFolder = "Assets/Settings";

        /// <summary>Quest-oriented defaults. Every one of these is a compromise for a 72Hz mobile GPU.</summary>
        public int DirectSamples = 32;
        public int IndirectSamples = 256;
        public int EnvironmentSamples = 256;
        public int Bounces = 2;

        public float LightmapResolution = 10f;
        public int LightmapPadding = 4;
        public int LightmapMaxSize = 1024;
        public bool CompressLightmaps = true;

        /// <summary>
        /// Non-directional halves lightmap memory by dropping the dominant-direction map.
        /// Directional lightmaps buy normal-mapped detail in baked light, which is a poor
        /// trade on a device where memory bandwidth is the binding constraint.
        /// </summary>
        public bool Directional;

        public bool AmbientOcclusion = true;
        public float AoMaxDistance = 0.6f;

        public bool ApplyLightingSettings = true;
        public bool TuneScaleInLightmap = true;

        /// <summary>Solved so the atlas budget is met; 0 leaves the resolution alone.</summary>
        public float SolvedResolution;

        public static LightBakePlan Recommend(SceneScan scan, SqSettings settings, SqProblemList problems,
            out LightmapBudget budget, out LightmapUvAudit uvAudit)
        {
            var plan = new LightBakePlan();

            // Bakery drives its own bake. Leave Unity's settings alone by default so the
            // two do not produce competing lightmaps.
            plan.ApplyLightingSettings = !BakeryDetector.IsPresent;
            BakeryDetector.Report(problems);

            AuditLights(scan, problems);
            UrpGuards.CheckBake(scan.Urp, problems);

            uvAudit = LightmapUvAudit.Run(scan, problems);

            budget = LightmapBudget.Compute(scan, plan.LightmapResolution, settings);
            budget.Report(problems);

            // Solve rather than merely warn. "Too big" is not actionable; "use 6.2 texels
            // per unit" is, and the arithmetic to get there is a square root.
            plan.SolvedResolution = budget.OverBudget
                ? Mathf.Max(1f, Mathf.Floor(budget.FittingResolution * 10f) / 10f)
                : plan.LightmapResolution;

            plan.LightmapResolution = plan.SolvedResolution;
            plan.LightmapMaxSize = settings.lightmapAtlasSize;

            LightmapUvAudit.CheckPackMargin(scan, plan.LightmapResolution, problems);

            return plan;
        }

        /// <summary>
        /// Checks the light setup for things that will not survive a mobile build.
        /// </summary>
        static void AuditLights(SceneScan scan, SqProblemList problems)
        {
            if (problems == null) return;

            int realtime = 0;
            int baked = 0;
            int realtimeDirectional = 0;

            for (int i = 0; i < scan.Lights.Count; i++)
            {
                LightFacts light = scan.Lights[i];
                if (light.Light == null || !light.Light.enabled) continue;

                if (light.IsRealtime)
                {
                    realtime++;
                    if (light.Type == LightType.Directional) realtimeDirectional++;
                }
                else if (light.IsBaked)
                {
                    baked++;
                }
            }

            if (scan.Lights.Count == 0)
            {
                problems.Add(CodeNoLights, SqSeverity.Warn,
                    "The scene has no lights, so a bake would produce only ambient contribution.")
                    .WithAction("Add at least one light, or rely on the environment lighting deliberately.");
                return;
            }

            // One realtime directional is the normal budget on Quest. Everything beyond
            // that is per-pixel work every frame for light that could have been baked once.
            int realtimeBeyondBudget = realtime - Mathf.Min(realtimeDirectional, 1);

            if (realtimeBeyondBudget > 0)
            {
                problems.Add(CodeTooManyRealtime, SqSeverity.Warn, string.Format(
                    "{0} realtime light(s) beyond a single directional. On a standalone headset each one costs per-pixel work every frame for light that a bake computes once.",
                    realtimeBeyondBudget))
                    .WithCount(realtimeBeyondBudget)
                    .WithAction("Set them to Baked, or to Mixed if they need to affect moving objects.");
            }

            if (baked == 0 && realtime > 0)
            {
                problems.Add(CodeSubtractive, SqSeverity.Info,
                    "No light is set to Baked, so a bake will produce little beyond ambient occlusion and environment light.")
                    .WithAction("Set the lights that do not need to move to Baked before baking.");
            }
        }

        // ---- applying ----

        /// <summary>
        /// Creates or updates the LightingSettings asset and assigns it to the scene.
        /// </summary>
        public LightingSettings ApplySettings()
        {
            LightingSettings settings = LoadOrCreateAsset();

            Undo.RecordObject(settings, "Apply Lighting Settings");

            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.bakedGI = true;
            settings.realtimeGI = false;

            settings.directSampleCount = DirectSamples;
            settings.indirectSampleCount = IndirectSamples;
            settings.environmentSampleCount = EnvironmentSamples;
            settings.maxBounces = Bounces;

            settings.lightmapResolution = LightmapResolution;
            settings.lightmapPadding = LightmapPadding;
            settings.lightmapMaxSize = LightmapMaxSize;
            // lightmapCompression, not the deprecated compressLightmaps bool. Normal
            // quality is the right default on a standalone headset: lightmaps are low
            // frequency, so compression artefacts are nearly invisible while the memory
            // saving is not.
            settings.lightmapCompression = CompressLightmaps
                ? LightmapCompression.NormalQuality
                : LightmapCompression.None;
            settings.directionalityMode = Directional ? LightmapsMode.CombinedDirectional : LightmapsMode.NonDirectional;

            settings.ao = AmbientOcclusion;
            settings.aoMaxDistance = AoMaxDistance;

            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            return settings;
        }

        static LightingSettings LoadOrCreateAsset()
        {
            string path = SettingsAssetFolder + "/SideQuestBakeSettings.lighting";

            var existing = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(SettingsAssetFolder))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var created = new LightingSettings { name = "SideQuestBakeSettings" };
            AssetDatabase.CreateAsset(created, path);

            return created;
        }

        /// <summary>
        /// Reduces Scale In Lightmap on the largest surfaces until the budget fits.
        ///
        /// Preferred over lowering the global resolution, because texel density only needs
        /// to come down where it is being spent: a 200 square metre floor and a doorframe
        /// get the same texels per unit by default, and only one of them shows the
        /// difference. Largest first, so the fewest objects are touched.
        /// </summary>
        public int TuneScales(SceneScan scan, LightmapBudget budget, SqSettings settings)
        {
            if (!TuneScaleInLightmap || !budget.OverBudget) return 0;

            var candidates = new List<RendererFacts>();
            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.ContributeGI || !r.HasBounds) continue;
                if (!LightmapBudget.ReceivesLightmap(r.Renderer)) continue;
                candidates.Add(r);
            }

            candidates.Sort((a, b) => b.SurfaceArea.CompareTo(a.SurfaceArea));

            double target = budget.WeightedArea * (budget.MaxAtlases / Mathf.Max(budget.Atlases, 0.0001f));
            double current = budget.WeightedArea;

            int changed = 0;

            for (int i = 0; i < candidates.Count && current > target; i++)
            {
                RendererFacts r = candidates[i];

                float scale = LightmapBudget.ReadScaleInLightmap(r.Renderer);
                if (scale <= 0.26f) continue;

                float reduced = Mathf.Max(0.25f, scale * 0.5f);

                current -= (double)r.SurfaceArea * (scale * scale - reduced * reduced);
                LightmapBudget.WriteScaleInLightmap(r.Renderer, reduced);
                changed++;
            }

            return changed;
        }

        // ---- serialisation ----

        public void Write(SqJsonWriter w, SceneScan scan, string reportId)
        {
            w.BeginObject();
            w.Prop("schemaVersion", DecisionPlan.SchemaVersion);
            w.Prop("tool", ToolId);
            w.Prop("toolVersion", ToolVersion);
            w.Prop("reportId", reportId);
            w.Prop("scene", scan.SceneName);
            w.Prop("sceneGuid", scan.SceneGuid);
            WriteBody(w);
            w.EndObject();
        }

        public void WriteBody(SqJsonWriter w)
        {
            w.Prop("applyLightingSettings", ApplyLightingSettings);
            w.Prop("tuneScaleInLightmap", TuneScaleInLightmap);

            w.BeginObject("lightingSettings");
            w.Prop("lightmapper", "ProgressiveGPU");
            w.Prop("directSamples", DirectSamples);
            w.Prop("indirectSamples", IndirectSamples);
            w.Prop("environmentSamples", EnvironmentSamples);
            w.Prop("bounces", Bounces);
            w.Prop("lightmapResolution", LightmapResolution);
            w.Prop("lightmapPadding", LightmapPadding);
            w.Prop("lightmapMaxSize", LightmapMaxSize);
            w.Prop("compressLightmaps", CompressLightmaps);
            w.Prop("directional", Directional);
            w.Prop("ambientOcclusion", AmbientOcclusion);
            w.Prop("aoMaxDistance", AoMaxDistance);
            w.EndObject();
        }

        public static LightBakePlan Read(DecisionPlan source, SqSettings settings, PlanValidation validation)
        {
            var plan = new LightBakePlan();

            plan.ApplyLightingSettings = source["applyLightingSettings"].AsBool(true);
            plan.TuneScaleInLightmap = source["tuneScaleInLightmap"].AsBool(true);

            SqJsonValue s = source["lightingSettings"];

            plan.DirectSamples = validation.ClampInt("lightingSettings.directSamples", s["directSamples"].AsInt(32), 8, 1024);
            plan.IndirectSamples = validation.ClampInt("lightingSettings.indirectSamples", s["indirectSamples"].AsInt(256), 8, 8192);
            plan.EnvironmentSamples = validation.ClampInt("lightingSettings.environmentSamples", s["environmentSamples"].AsInt(256), 8, 8192);
            plan.Bounces = validation.ClampInt("lightingSettings.bounces", s["bounces"].AsInt(2), 0, 4);

            // The upper bound is the point of this clamp. A resolution of 200 texels per
            // unit is a plausible typo and an overnight bake that fills a disk.
            plan.LightmapResolution = validation.Clamp("lightingSettings.lightmapResolution",
                s["lightmapResolution"].AsFloat(10f), 0.5f, 100f);

            plan.LightmapPadding = validation.ClampInt("lightingSettings.lightmapPadding", s["lightmapPadding"].AsInt(4), 2, 64);
            plan.LightmapMaxSize = validation.Snap("lightingSettings.lightmapMaxSize",
                s["lightmapMaxSize"].AsInt(settings.lightmapAtlasSize), new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096 });

            plan.CompressLightmaps = s["compressLightmaps"].AsBool(true);
            plan.Directional = s["directional"].AsBool(false);
            plan.AmbientOcclusion = s["ambientOcclusion"].AsBool(true);
            plan.AoMaxDistance = validation.Clamp("lightingSettings.aoMaxDistance", s["aoMaxDistance"].AsFloat(0.6f), 0.01f, 20f);

            return plan;
        }
    }
}
