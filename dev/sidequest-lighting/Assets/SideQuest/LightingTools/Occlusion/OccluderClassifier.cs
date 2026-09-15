// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Occlusion
{
    /// <summary>What should change about one renderer's occlusion flags.</summary>
    public sealed class OcclusionDecision
    {
        public RendererFacts Renderer;

        public bool WantOccluder;
        public bool WantOccludee;

        public bool HasOccluder;
        public bool HasOccludee;

        public string Reason;

        public bool Changed { get { return WantOccluder != HasOccluder || WantOccludee != HasOccludee; } }
    }

    /// <summary>
    /// Decides which renderers can hide others, and which can be hidden.
    ///
    /// Two corrections to the prior-art helper, both of which decide whether occlusion
    /// culling does anything at all.
    ///
    /// First, that script only ever cleared Occluder flags and never set Occludee on
    /// anything. Occlusion culling works by testing occludees against occluders, so a
    /// scene with no occludees culls nothing however good the bake is - the feature
    /// appears to be on and costs bake time for no benefit.
    ///
    /// Second, it disqualified occluders by bounds magnitude. That rejects every wall -
    /// thin in one axis and the single best occluder there is - while accepting a long
    /// pipe, which hides nothing. What matters is having a substantial FACE, so the test
    /// is on the second-largest axis rather than on overall size.
    /// </summary>
    public static class OccluderClassifier
    {
        public const string CodeNoOccluders = "OC010_NO_OCCLUDERS";
        public const string CodeMovingFlagged = "OC020_MOVING_FLAGGED";
        public const string CodeTransparentOccluder = "OC030_TRANSPARENT_OCCLUDER";
        public const string CodeNonStatic = "OC031_NOT_STATIC";
        public const string CodeThinOccluder = "OC032_THIN_OCCLUDER";

        public static List<OcclusionDecision> Classify(
            SceneScan scan, float minOccluderFaceSize, float minOccluderThickness, SqProblemList problems)
        {
            var decisions = new List<OcclusionDecision>();

            int occluders = 0;
            int occludees = 0;
            int movingFlagged = 0;
            int demotedTransparent = 0;
            int tooThin = 0;
            var nonStatic = new List<int>();

            for (int i = 0; i < scan.Renderers.Count; i++)
            {
                RendererFacts r = scan.Renderers[i];
                if (!r.HasBounds) continue;

                var decision = new OcclusionDecision
                {
                    Renderer = r,
                    HasOccluder = r.OccluderStatic,
                    HasOccludee = r.OccludeeStatic
                };

                if (r.MayMove || r.IsSkinned)
                {
                    // Occlusion data records where geometry WAS at bake time. A moving
                    // object flagged either way culls against a memory of itself.
                    decision.WantOccluder = false;
                    decision.WantOccludee = false;
                    decision.Reason = "moves at runtime, so baked occlusion cannot describe it";

                    if (r.OccluderStatic || r.OccludeeStatic) movingFlagged++;
                    decisions.Add(decision);
                    continue;
                }

                if (!r.IsStatic)
                {
                    decision.WantOccluder = false;
                    decision.WantOccludee = false;
                    decision.Reason = "not marked static";
                    if (nonStatic.Count < SqProblem.MaxSamples) nonStatic.Add(r.Index);
                    decisions.Add(decision);
                    continue;
                }

                // Every static renderer is worth culling when something hides it, whatever
                // it is made of - this is the flag the prior art never set.
                decision.WantOccludee = true;
                occludees++;

                decision.WantOccluder = r.IsViableOccluder(minOccluderFaceSize, minOccluderThickness);
                if (decision.WantOccluder)
                {
                    occluders++;
                    decision.Reason = string.Format("solid opaque geometry, {0}m face and {1}m thick",
                        SqFormat.Num(r.OccluderFaceSize), SqFormat.Num(r.OccluderThickness));
                }
                else
                {
                    decision.Reason = DescribeWhyNotOccluder(r, minOccluderFaceSize, minOccluderThickness);
                    if (r.OccluderStatic && !r.AllOpaque) demotedTransparent++;
                    if (r.OccluderThickness < minOccluderThickness && r.OccluderFaceSize >= minOccluderFaceSize) tooThin++;
                }

                decisions.Add(decision);
            }

            Report(problems, occluders, occludees, movingFlagged, demotedTransparent, tooThin, nonStatic);
            return decisions;
        }

        static string DescribeWhyNotOccluder(RendererFacts r, float minOccluderFaceSize, float minOccluderThickness)
        {
            if (r.OccluderThickness < minOccluderThickness)
            {
                return string.Format("only {0}m thick, too flat to voxelise into a solid occluder",
                    SqFormat.Num(r.OccluderThickness));
            }

            if (!r.AllOpaque) return "not fully opaque, so it cannot block sight lines";
            if (r.AnyDoubleSided) return "double-sided, which occlusion treats as having no solid interior";
            if (r.AnyMissingMaterial) return "has a missing material, so its opacity is unknown";

            for (int i = 0; i < r.Materials.Length; i++)
            {
                MaterialFacts material = r.Materials[i];
                if (material != null && material.SurfaceType == "cutout")
                    return "alpha-clipped, so it is full of holes despite being opaque";
            }

            return string.Format("its largest face is {0:0.##}m, below the {1:0.##}m threshold",
                r.OccluderFaceSize, minOccluderFaceSize);
        }

        static void Report(SqProblemList problems, int occluders, int occludees,
            int movingFlagged, int demotedTransparent, int tooThin, List<int> nonStatic)
        {
            if (problems == null) return;

            if (occluders == 0)
            {
                problems.Add(CodeNoOccluders, SqSeverity.Error,
                    "No renderer qualifies as an occluder, so a bake would produce data that culls nothing.")
                    .WithAction("Lower Minimum Occluder Face Size, or check that walls and floors are marked static and use opaque materials.");
            }

            if (movingFlagged > 0)
            {
                problems.Add(CodeMovingFlagged, SqSeverity.Warn, string.Format(
                    "{0} renderer(s) carry occlusion flags but can move at runtime. Baked occlusion describes where they were, not where they are.",
                    movingFlagged))
                    .WithCount(movingFlagged)
                    .WithAction("Apply the plan to clear their flags, or remove the Rigidbody/Animator if they do not actually move.");
            }

            if (demotedTransparent > 0)
            {
                problems.Add(CodeTransparentOccluder, SqSeverity.Warn, string.Format(
                    "{0} renderer(s) are marked as occluders but are transparent, cut out or double-sided. They would hide geometry that should stay visible through them.",
                    demotedTransparent))
                    .WithCount(demotedTransparent)
                    .WithAction("Apply the plan to demote them to occludee only.");
            }

            if (tooThin > 0)
            {
                problems.Add(CodeThinOccluder, SqSeverity.Info, string.Format(
                    "{0} large but very flat renderer(s) were excluded from occluding. Umbra voxelises the scene, and a surface thinner than a voxel gets fattened to fill one - which invents occlusion and makes nearby geometry vanish.",
                    tooThin))
                    .WithCount(tooThin)
                    .WithAction("Give the geometry real thickness if it is meant to block sight lines, such as a wall built from a box rather than a plane.");
            }

            if (nonStatic.Count > 0)
            {
                problems.Add(CodeNonStatic, SqSeverity.Info,
                    "Some renderers are not marked static and are therefore excluded from occlusion entirely.")
                    .WithCount(nonStatic.Count)
                    .WithSamples(nonStatic)
                    .WithAction("Mark genuinely fixed geometry static so it can participate.");
            }
        }

        /// <summary>
        /// Applies the flag changes. Caller owns the undo group.
        ///
        /// Flags are written as a masked update, never a whole-enum assignment: the same
        /// field carries Contribute GI, batching, navigation and reflection settings that
        /// belong to other tools and to the user.
        /// </summary>
        public static int Apply(List<OcclusionDecision> decisions)
        {
            int changed = 0;

            for (int i = 0; i < decisions.Count; i++)
            {
                OcclusionDecision decision = decisions[i];
                if (!decision.Changed) continue;
                if (decision.Renderer == null || decision.Renderer.GameObject == null) continue;

                GameObject go = decision.Renderer.GameObject;

                // Static flags live on the GameObject, and RecordObject does not capture
                // them - only RegisterCompleteObjectUndo does.
                SqUndo.ModifyGameObject(go, "Assign Occlusion Flags");

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);

                if (decision.WantOccluder) flags |= StaticEditorFlags.OccluderStatic;
                else flags &= ~StaticEditorFlags.OccluderStatic;

                if (decision.WantOccludee) flags |= StaticEditorFlags.OccludeeStatic;
                else flags &= ~StaticEditorFlags.OccludeeStatic;

                GameObjectUtility.SetStaticEditorFlags(go, flags);
                changed++;
            }

            return changed;
        }
    }
}
