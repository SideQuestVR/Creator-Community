// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// Works out how much lightmap a scene will actually need, and what resolution fits.
    ///
    /// Lightmap resolution is the setting creators most often discover the hard way. It is
    /// entered as texels per world unit, which says nothing about the number that matters -
    /// how many atlas pages come out the other end. A value that bakes in two minutes on a
    /// small scene produces eleven 1024 atlases on a large one, and nothing warns you until
    /// the bake has finished and the headset is out of memory.
    ///
    /// The relationship is simple enough to solve directly: texel count grows with surface
    /// area and with the square of resolution, so the resolution that fits a given atlas
    /// budget can be computed rather than found by bisection over hour-long bakes.
    /// </summary>
    public sealed class LightmapBudget
    {
        public const string CodeAtlasBudget = "LB030_ATLAS_BUDGET";
        public const string CodeNoLightmappedGeometry = "LB031_NOTHING_TO_BAKE";

        /// <summary>
        /// Atlas packing never reaches 100%. A third again is a realistic allowance for
        /// charts that do not tessellate neatly and for the padding between them.
        /// </summary>
        public const float PackingOverhead = 1.3f;

        /// <summary>Sum of area times scale squared - everything about the scene that resolution multiplies.</summary>
        public double WeightedArea;

        public int LightmappedRenderers;
        public int ProbeLitRenderers;

        public float Resolution;
        public double Texels;
        public float Atlases;

        /// <summary>The resolution that would exactly fill the atlas budget.</summary>
        public float FittingResolution;

        public bool OverBudget { get { return Atlases > MaxAtlases; } }

        public int MaxAtlases;
        public int AtlasSize;

        public static LightmapBudget Compute(SceneScan scan, float resolution, SqSettings settings)
        {
            var budget = new LightmapBudget
            {
                Resolution = resolution,
                MaxAtlases = settings.maxLightmapAtlases,
                AtlasSize = settings.lightmapAtlasSize
            };

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.ContributeGI || !r.HasBounds) continue;

                if (!ReceivesLightmap(r.Renderer)) { budget.ProbeLitRenderers++; continue; }

                budget.LightmappedRenderers++;

                float scale = ReadScaleInLightmap(r.Renderer);
                budget.WeightedArea += (double)r.SurfaceArea * scale * scale;
            }

            budget.Recalculate();
            return budget;
        }

        void Recalculate()
        {
            Texels = WeightedArea * Resolution * Resolution;

            double atlasTexels = (double)AtlasSize * AtlasSize;
            Atlases = atlasTexels <= 0 ? 0f : (float)(Texels * PackingOverhead / atlasTexels);

            // Invert the same relationship: texels grow with the square of resolution, so
            // the resolution that exactly fills the budget is a square root away. This is
            // the number worth reporting - it turns "too big" into "use this instead".
            double budgetTexels = MaxAtlases * atlasTexels / PackingOverhead;
            FittingResolution = WeightedArea <= 0.0 ? Resolution : (float)System.Math.Sqrt(budgetTexels / WeightedArea);
        }

        /// <summary>
        /// Receive GI is an Editor-only serialized field whose owning type has moved
        /// between Unity versions, so it is read through SerializedObject rather than a
        /// property that may not exist. 2 is Light Probes; anything else is Lightmaps.
        /// </summary>
        public static bool ReceivesLightmap(Renderer renderer)
        {
            if (renderer == null) return false;

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty("m_ReceiveGI");
            if (property == null) return true;

            return property.intValue != 2;
        }

        public static float ReadScaleInLightmap(Renderer renderer)
        {
            if (renderer == null) return 1f;

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty("m_ScaleInLightmap");
            return property == null ? 1f : property.floatValue;
        }

        public static void WriteScaleInLightmap(Renderer renderer, float scale)
        {
            if (renderer == null) return;

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty("m_ScaleInLightmap");
            if (property == null) return;

            SqUndo.Modify(renderer, "Set Lightmap Scale");
            property.floatValue = Mathf.Clamp(scale, 0.01f, 10f);
            so.ApplyModifiedProperties();
        }

        public void Report(SqProblemList problems)
        {
            if (problems == null) return;

            if (LightmappedRenderers == 0)
            {
                problems.Add(CodeNoLightmappedGeometry, SqSeverity.Warn,
                    "Nothing in this scene is set to receive lightmaps, so a bake would produce no lightmap data.")
                    .WithAction("Mark static geometry Contribute GI, or accept that the scene is lit entirely by probes.");
                return;
            }

            if (!OverBudget)
            {
                problems.Add(CodeAtlasBudget, SqSeverity.Info, string.Format(
                    "At {0} texels per unit this scene needs about {1} atlas page(s) of {2}px, within the budget of {3}.",
                    SqFormat.Num(Resolution), SqFormat.Num(Atlases), AtlasSize, MaxAtlases));
                return;
            }

            problems.Add(CodeAtlasBudget, SqSeverity.Warn, string.Format(
                "At {0} texels per unit this scene needs about {1} atlas page(s) of {2}px against a budget of {3}. Dropping to {4} texels per unit fits.",
                SqFormat.Num(Resolution), SqFormat.Num(Atlases), AtlasSize, MaxAtlases, SqFormat.Num(FittingResolution)))
                .WithAction("Lower Lightmap Resolution to the fitting value, reduce Scale In Lightmap on large surfaces, or switch small objects to Receive GI from Light Probes.");
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject("lightmapBudget");
            w.Prop("resolution", Resolution);
            w.Prop("fittingResolution", FittingResolution);
            w.Prop("weightedArea", (float)WeightedArea);
            w.Prop("estimatedTexels", (float)Texels);
            w.Prop("estimatedAtlases", Atlases);
            w.Prop("maxAtlases", MaxAtlases);
            w.Prop("atlasSize", AtlasSize);
            w.Prop("lightmappedRenderers", LightmappedRenderers);
            w.Prop("probeLitRenderers", ProbeLitRenderers);
            w.Prop("overBudget", OverBudget);
            w.EndObject();
        }
    }
}
