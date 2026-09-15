// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Decides which renderers contribute to baked GI, and which read their lighting from
    /// probes instead of owning lightmap texels.
    ///
    /// This has more effect on a Quest scene than probe placement does. Lightmap atlas
    /// space is the scarce resource, and it is routinely spent on objects that cannot
    /// benefit: a chair leg gets the same texel density as the wall behind it, and a prop
    /// that moves gets baked into the shadow it was standing in when the bake ran.
    ///
    /// Marking small static objects Contribute GI but Receive GI from Light Probes keeps
    /// their bounced light in the bake while giving their own shading back to the probe
    /// volume - typically the single biggest atlas saving available, and one the prior-art
    /// tools never touched.
    /// </summary>
    public static class ContributeGiAssigner
    {
        public const string CodeMovingContributor = "LP050_MOVING_CONTRIBUTOR";
        public const string CodeNoUv2 = "LP051_NO_UV2";

        public sealed class Decision
        {
            public RendererFacts Renderer;
            public bool SetContributeGI;
            public bool ClearContributeGI;
            public bool SetReceiveFromProbes;
            public string Reason;
        }

        /// <summary>
        /// Works out what should change, without changing anything.
        ///
        /// Separated from the apply so preview and apply cannot disagree: the same list
        /// drives the Scene view overlay, the preview counts and the actual mutation.
        /// </summary>
        public static List<Decision> Plan(SceneScan scan, LightProbePlan plan, SqProblemList problems)
        {
            var decisions = new List<Decision>();
            if (!plan.AssignContributeGI) return decisions;

            // Anything under about four times the threshold is "small" - big enough to
            // bounce light worth baking, too small to deserve its own texels.
            float smallThreshold = plan.ContributeGIMinExtent * 4f;

            int movingContributors = 0;
            var missingUv2 = new List<int>();

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.HasBounds) continue;

                float extent = Mathf.Max(r.WorldBounds.size.x, Mathf.Max(r.WorldBounds.size.y, r.WorldBounds.size.z));

                if (r.MayMove || r.IsSkinned)
                {
                    if (r.ContributeGI)
                    {
                        movingContributors++;
                        decisions.Add(new Decision
                        {
                            Renderer = r,
                            ClearContributeGI = true,
                            Reason = "moves at runtime, so baking it into the lightmap would leave its shadow behind"
                        });
                    }
                    continue;
                }

                if (!r.IsStatic) continue;

                if (extent < plan.ContributeGIMinExtent)
                {
                    if (r.ContributeGI)
                    {
                        decisions.Add(new Decision
                        {
                            Renderer = r,
                            ClearContributeGI = true,
                            Reason = "too small to contribute meaningful bounced light"
                        });
                    }
                    continue;
                }

                bool wantsProbeLighting = plan.SmallObjectsReceiveFromProbes && extent < smallThreshold;
                bool needsChange = !r.ContributeGI ||
                    (wantsProbeLighting && !ReceivesFromProbes(r.Renderer));

                if (!needsChange) continue;

                decisions.Add(new Decision
                {
                    Renderer = r,
                    SetContributeGI = !r.ContributeGI,
                    SetReceiveFromProbes = wantsProbeLighting,
                    Reason = wantsProbeLighting
                        ? "contributes bounced light but is small enough to read its own shading from probes"
                        : "large static opaque geometry that should be lightmapped"
                });

                if (!wantsProbeLighting && !r.HasUv2 && missingUv2.Count < SqProblem.MaxSamples)
                    missingUv2.Add(r.Index);
            }

            Report(problems, movingContributors, missingUv2);
            return decisions;
        }

        static void Report(SqProblemList problems, int movingContributors, List<int> missingUv2)
        {
            if (problems == null) return;

            if (movingContributors > 0)
            {
                problems.Add(CodeMovingContributor, SqSeverity.Warn, string.Format(
                    "{0} renderer(s) are marked Contribute GI but can move at runtime; their baked shadows would stay behind.",
                    movingContributors))
                    .WithCount(movingContributors)
                    .WithAction("Apply the plan to clear Contribute GI on them, or remove the Rigidbody/Animator if they do not really move.");
            }

            if (missingUv2.Count > 0)
            {
                problems.Add(CodeNoUv2, SqSeverity.Warn,
                    "Some renderers selected for lightmapping have no lightmap UVs, so they cannot be baked.")
                    .WithCount(missingUv2.Count)
                    .WithSamples(missingUv2)
                    .WithAction("Enable Generate Lightmap UVs on the model importer, or let them read lighting from probes instead.");
            }
        }

        /// <summary>
        /// Applies the decisions. Caller owns the undo group, so this folds into one Ctrl+Z
        /// along with the probe placement it accompanies.
        /// </summary>
        public static int Apply(List<Decision> decisions)
        {
            int changed = 0;

            for (int i = 0; i < decisions.Count; i++)
            {
                Decision d = decisions[i];
                if (d.Renderer == null || d.Renderer.GameObject == null) continue;

                SqUndo.ModifyGameObject(d.Renderer.GameObject, "Assign Contribute GI");

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(d.Renderer.GameObject);

                // Masked write, never a whole-enum assignment: these flags also carry
                // batching, occlusion, navigation and reflection settings that belong to
                // other tools and to the user.
                if (d.SetContributeGI) flags |= StaticEditorFlags.ContributeGI;
                if (d.ClearContributeGI) flags &= ~StaticEditorFlags.ContributeGI;

                GameObjectUtility.SetStaticEditorFlags(d.Renderer.GameObject, flags);

                if (d.SetReceiveFromProbes) SetReceivesFromProbes(d.Renderer.Renderer);

                changed++;
            }

            return changed;
        }

        // Receive GI is not a plain Renderer property - it is an Editor-only serialized
        // field, and which concrete Renderer subclass exposes it has moved between Unity
        // versions. Reading and writing m_ReceiveGI directly works on any renderer and does
        // not break when that moves again.
        //
        // Values follow UnityEngine.ReceiveGI: 1 = Lightmaps, 2 = Light Probes.
        const int ReceiveGiLightmaps = 1;
        const int ReceiveGiLightProbes = 2;
        const string ReceiveGiProperty = "m_ReceiveGI";

        static bool ReceivesFromProbes(Renderer renderer)
        {
            if (renderer == null) return false;

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty(ReceiveGiProperty);

            // A renderer type without the field cannot be switched, so report it as already
            // satisfied rather than proposing a change that would silently do nothing.
            if (property == null) return true;

            return property.intValue == ReceiveGiLightProbes;
        }

        static void SetReceivesFromProbes(Renderer renderer)
        {
            if (renderer == null) return;

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty(ReceiveGiProperty);
            if (property == null) return;

            SqUndo.Modify(renderer, "Assign Receive GI");
            property.intValue = ReceiveGiLightProbes;
            so.ApplyModifiedProperties();
        }
    }
}
