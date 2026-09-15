// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Per-project settings for the whole suite.
    ///
    /// The prior-art tools kept every knob as a private field on an EditorWindow, so each
    /// value reset the moment the window closed. Storing them once here fixes that for all
    /// four tools at the same time.
    ///
    /// Lives in ProjectSettings/, not Assets/: it is project configuration, it should be
    /// diffable in version control, and it must never end up inside a shipped AssetBundle.
    /// </summary>
    [FilePath("ProjectSettings/SideQuestLightingTools.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SqSettings : ScriptableSingleton<SqSettings>
    {
        // ---- budgets: the ceilings a decision plan can never raise for itself ----

        [Tooltip("Hard ceiling on probes a single apply may place. Quest-oriented default.")]
        public int maxLightProbes = 2000;

        [Tooltip("Hard ceiling on reflection probes in one scene.")]
        public int maxReflectionProbes = 64;

        [Tooltip("Total baked reflection cubemap memory before RP020_MEMORY_BUDGET is raised, in MB.")]
        public float reflectionProbeMemoryBudgetMB = 24f;

        [Tooltip("Lightmap atlases allowed before LB030_ATLAS_BUDGET is raised.")]
        public int maxLightmapAtlases = 2;

        [Tooltip("Atlas edge length the budget estimate assumes.")]
        public int lightmapAtlasSize = 1024;

        [Tooltip("Occlusion umbra data size that triggers a warning, in MB.")]
        public float occlusionDataBudgetMB = 8f;

        // ---- light probe defaults ----

        [Tooltip("Base probe spacing in metres. Smaller means more probes - one semantic everywhere.")]
        public float probeSpacing = 2.0f;

        [Tooltip("Probes closer together than this are merged, keeping the medoid of each cell.")]
        public float probeMergeDistance = 0.5f;

        [Tooltip("Heights above the floor at which probe layers are placed. A single layer lights VR badly.")]
        public float[] probeLayerHeights = { 0.3f, 1.6f, 2.6f };

        [Tooltip("Extra probes are placed within this distance of a NavMesh or geometry edge.")]
        public float probeEdgeBand = 0.6f;

        [Tooltip("Probe spacing is never reduced below this, whatever the density heuristics ask for.")]
        public float probeMinSpacing = 0.5f;

        // ---- reflection probe defaults ----

        [Tooltip("Height above the zone floor at which a reflection probe is centred.")]
        public float reflectionProbeEyeHeight = 1.6f;

        [Tooltip("Reflection box bounds are inflated by this much beyond the zone bounds.")]
        public float reflectionBoxPadding = 0.1f;

        // ---- occlusion defaults ----

        [Tooltip("Absolute floor on occluder face size. The effective threshold is usually higher - it scales with the scene so that only structural geometry qualifies.")]
        public float minOccluderFaceSize = 2f;

        [Tooltip("Occluder face size as a multiple of ceiling height. An occluder has to be able to block a room-sized sight line, so it is sized against the room rather than by an absolute guess.")]
        public float occluderCeilingFraction = 1f;

        [Tooltip("Ceiling on the solved Smallest Hole. Raising this makes occluders more solid and can make geometry vanish when the camera is close to it. Unity's own default is 0.25.")]
        public float occlusionMaxSmallestHole = 0.25f;

        [Tooltip("Geometry thinner than this never becomes an occluder. Paper-thin surfaces cannot be voxelised reliably and produce phantom occlusion.")]
        public float minOccluderThickness = 0.05f;

        [Tooltip("Untick to keep the Smallest Occluder / Smallest Hole / Backface Threshold you set by hand. Applying a plan will then change static flags only. Values tuned against a real world by eye beat anything this tool infers.")]
        public bool writeBakeParametersOnApply = true;

        // ---- behaviour ----

        [Tooltip("Log extra non-machine-readable detail to the console.")]
        public bool verboseDetail;

        [Tooltip("Report detail level written by Analyze: summary, zones or full.")]
        public string defaultReportDetail = "summary";

        [Tooltip("Byte ceiling for a written report before it degrades to a lower detail level.")]
        public int reportMaxBytes = 65536;

        // ---- shader gloss overrides ----

        [Serializable]
        public struct ShaderGlossOverride
        {
            public string shaderName;
            public float smoothness;
            public float metallic;
            public bool usesEnvironmentReflections;
        }

        [Tooltip("Manual answers for shaders whose gloss cannot be read automatically. Never guess silently.")]
        public List<ShaderGlossOverride> shaderGlossOverrides = new List<ShaderGlossOverride>();

        public bool TryGetGlossOverride(string shaderName, out ShaderGlossOverride result)
        {
            result = default(ShaderGlossOverride);
            if (string.IsNullOrEmpty(shaderName)) return false;

            for (int i = 0; i < shaderGlossOverrides.Count; i++)
            {
                if (string.Equals(shaderGlossOverrides[i].shaderName, shaderName, StringComparison.Ordinal))
                {
                    result = shaderGlossOverrides[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>Persists to ProjectSettings/. Call after any mutation.</summary>
        public void Persist()
        {
            SqLog.VerboseDetail = verboseDetail;
            Save(true);
        }

        /// <summary>
        /// Writes the settings file if it does not exist yet.
        ///
        /// Without this the file only appeared once somebody moved a slider, so a project
        /// that had only ever run Analyze had no settings file at all - nothing to inspect,
        /// nothing to diff, and nothing to edit by hand or check into version control,
        /// despite that being the entire reason these live in ProjectSettings.
        ///
        /// It also makes the values explicit rather than implied. A ScriptableSingleton
        /// survives domain reloads in memory, so an instance created before a default
        /// changed keeps the old value for the rest of the session while the source says
        /// otherwise - which is confusing enough to debug once, let alone in someone
        /// else's project.
        /// </summary>
        public static void EnsurePersisted()
        {
            const string path = "ProjectSettings/SideQuestLightingTools.asset";
            if (System.IO.File.Exists(path)) return;

            instance.Persist();
        }

        void OnEnable()
        {
            SqLog.VerboseDetail = verboseDetail;
        }
    }
}
