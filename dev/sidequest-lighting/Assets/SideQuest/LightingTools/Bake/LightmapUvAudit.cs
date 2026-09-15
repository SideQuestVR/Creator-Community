// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// Finds geometry that is set to be lightmapped but cannot be.
    ///
    /// A mesh with no second UV channel has nowhere to store baked light. Unity does not
    /// refuse the bake - it bakes everything else and leaves that object unlit or wrongly
    /// lit, which is easy to mistake for a lighting problem and hard to trace to a missing
    /// import checkbox.
    ///
    /// Splitting the fixable case from the unfixable one matters. An imported model has a
    /// ModelImporter with a Generate Lightmap UVs option that can simply be switched on; a
    /// procedural or ProBuilder mesh has no importer at all, so the only honest answer is
    /// to say so and let the author decide.
    /// </summary>
    public sealed class LightmapUvAudit
    {
        public const string CodeMissingUv2 = "LB021_MISSING_UV2";
        public const string CodeNoImporter = "LB020_NO_UV2_NO_IMPORTER";
        public const string CodePackMargin = "LB022_PACK_MARGIN";

        /// <summary>Model asset paths whose importer can generate the missing UVs.</summary>
        public List<string> FixableModelPaths = new List<string>();

        /// <summary>Renderers with no UV2 and no importer to generate one.</summary>
        public List<int> UnfixableIndices = new List<int>();

        public int MissingCount;

        public static LightmapUvAudit Run(SceneScan scan, SqProblemList problems)
        {
            var audit = new LightmapUvAudit();
            var seenPaths = new HashSet<string>();
            var fixableSamples = new List<int>();

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.ContributeGI || r.SharedMesh == null) continue;
                if (!LightmapBudget.ReceivesLightmap(r.Renderer)) continue;
                if (r.HasUv2) continue;

                audit.MissingCount++;

                string assetPath = AssetDatabase.GetAssetPath(r.SharedMesh);
                var importer = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetImporter.GetAtPath(assetPath) as ModelImporter;

                if (importer == null)
                {
                    if (audit.UnfixableIndices.Count < SqProblem.MaxSamples) audit.UnfixableIndices.Add(r.Index);
                    continue;
                }

                if (seenPaths.Add(assetPath)) audit.FixableModelPaths.Add(assetPath);
                if (fixableSamples.Count < SqProblem.MaxSamples) fixableSamples.Add(r.Index);
            }

            audit.Report(problems, fixableSamples);
            return audit;
        }

        void Report(SqProblemList problems, List<int> fixableSamples)
        {
            if (problems == null) return;

            if (FixableModelPaths.Count > 0)
            {
                problems.Add(CodeMissingUv2, SqSeverity.Warn, string.Format(
                    "{0} renderer(s) across {1} model(s) are set to be lightmapped but have no lightmap UVs. They will bake to nothing.",
                    MissingCount - UnfixableIndices.Count, FixableModelPaths.Count))
                    .WithCount(FixableModelPaths.Count)
                    .WithSamples(fixableSamples)
                    .WithAction("Run Generate Missing Lightmap UVs, which switches on Generate Lightmap UVs for those models and reimports them.");
            }

            if (UnfixableIndices.Count > 0)
            {
                problems.Add(CodeNoImporter, SqSeverity.Warn, string.Format(
                    "{0} renderer(s) have no lightmap UVs and no model importer to generate them, which usually means a procedural or ProBuilder mesh.",
                    UnfixableIndices.Count))
                    .WithCount(UnfixableIndices.Count)
                    .WithSamples(UnfixableIndices)
                    .WithAction("Generate UV2 in the tool that created the mesh, or set these to Receive GI from Light Probes so they are lit without a lightmap.");
            }
        }

        /// <summary>
        /// Switches on Generate Lightmap UVs for the models that need it and reimports them.
        ///
        /// Kept as a separate, explicit action rather than folded into Apply: this modifies
        /// asset importers rather than the scene, so it changes files outside the scene's
        /// undo stack and affects every other scene using the same models.
        /// </summary>
        public int GenerateMissingUvs()
        {
            if (FixableModelPaths.Count == 0) return 0;

            int changed = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < FixableModelPaths.Count; i++)
                {
                    string path = FixableModelPaths[i];

                    EditorUtility.DisplayProgressBar("Generating Lightmap UVs",
                        path, (float)i / FixableModelPaths.Count);

                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.generateSecondaryUV) continue;

                    importer.generateSecondaryUV = true;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            return changed;
        }

        /// <summary>
        /// Warns when UV chart padding is too small for the chosen resolution.
        ///
        /// Unity packs charts with a margin measured in UV space, so the same margin is
        /// fewer texels at a lower resolution. Once charts sit closer than a texel apart
        /// their light bleeds into each other, which shows up as bright or dark seams along
        /// edges - and looks like a bake quality problem rather than a packing one.
        /// </summary>
        public static void CheckPackMargin(SceneScan scan, float resolution, SqProblemList problems)
        {
            if (problems == null || resolution <= 0f) return;

            var tight = new List<int>();

            for (int i = 0; i < scan.Renderers.Count && tight.Count < SqProblem.MaxSamples; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.ContributeGI || r.SharedMesh == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(r.SharedMesh);
                if (string.IsNullOrEmpty(assetPath)) continue;

                var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null || !importer.generateSecondaryUV) continue;

                // Pack margin is in UV units of the largest chart; at typical chart sizes
                // roughly this many texels of separation.
                float texelsOfMargin = importer.secondaryUVPackMargin * resolution;
                if (texelsOfMargin < 2f) tight.Add(r.Index);
            }

            if (tight.Count == 0) return;

            problems.Add(CodePackMargin, SqSeverity.Info,
                "Some models have a lightmap UV pack margin that works out to under two texels at this resolution, which can bleed light between charts and show as seams.")
                .WithCount(tight.Count)
                .WithSamples(tight)
                .WithAction("Raise Pack Margin on those models, or raise Lightmap Resolution, if seams appear after baking.");
        }
    }
}
