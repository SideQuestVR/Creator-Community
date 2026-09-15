// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Tests
{
    /// <summary>
    /// Verifies occlusion classification against the T2 fixture.
    ///
    /// T2 is built with the three cases a naive classifier gets wrong: a thin wall that
    /// must occlude, a transparent pane and an alpha-clipped card that must not, and a
    /// movable crate that must be excluded from both roles.
    /// </summary>
    public static class OcclusionTests
    {
        const string Prefix = "[SQLT-TEST]";

        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify Occlusion Flags", false, 43)]
        public static void VerifyOcclusionFlags()
        {
            var failures = new List<string>();
            int walls = 0, wallOccluders = 0;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                GameObject go = renderer.gameObject;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);

                bool isOccluder = (flags & StaticEditorFlags.OccluderStatic) != 0;
                bool isOccludee = (flags & StaticEditorFlags.OccludeeStatic) != 0;

                string name = go.name;

                // Thin walls are the single best occluder in any interior, and the case a
                // size-based test rejects.
                if (name.StartsWith("Wall_"))
                {
                    walls++;
                    if (isOccluder) wallOccluders++;
                    else failures.Add(name + " is a wall but not an occluder");
                    continue;
                }

                // Transparent: you can see through it, so it must never hide anything.
                if (name.Contains("GlassPane"))
                {
                    if (isOccluder) failures.Add(name + " is transparent but marked as an occluder");
                    if (!isOccludee) failures.Add(name + " should still be an occludee");
                    continue;
                }

                // Alpha-clipped: opaque by RenderType and full of holes.
                if (name.Contains("FoliageCard"))
                {
                    if (isOccluder) failures.Add(name + " is alpha-clipped but marked as an occluder");
                    if (!isOccludee) failures.Add(name + " should still be an occludee");
                    continue;
                }

                // Movable: baked occlusion records where it was, not where it is.
                if (name.Contains("MovingCrate"))
                {
                    if (isOccluder) failures.Add(name + " can move but is marked as an occluder");
                    if (isOccludee) failures.Add(name + " can move but is marked as an occludee");
                    continue;
                }
            }

            if (walls == 0)
            {
                Fail("no walls found - is T2_MultiRoom open and built?");
                return;
            }

            if (failures.Count > 0)
            {
                Fail(string.Join("; ", failures.ToArray()));
                return;
            }

            Pass(string.Format(
                "{0} of {1} walls occlude; the glass pane and foliage card are occludee-only; the movable crate is excluded from both",
                wallOccluders, walls));
        }

        /// <summary>
        /// A scene of occludees with no occluders bakes successfully and culls nothing,
        /// which is the prior-art tool's actual behaviour and looks like a working setup.
        /// </summary>
        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify Occlusion Is Useful", false, 44)]
        public static void VerifyOcclusionIsUseful()
        {
            int occluders = 0, occludees = 0;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < renderers.Length; i++)
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderers[i].gameObject);
                if ((flags & StaticEditorFlags.OccluderStatic) != 0) occluders++;
                if ((flags & StaticEditorFlags.OccludeeStatic) != 0) occludees++;
            }

            if (occluders == 0) { Fail("no occluders: a bake would produce data that culls nothing"); return; }
            if (occludees == 0) { Fail("no occludees: nothing can be culled, whatever the occluders hide"); return; }

            Pass(string.Format("{0} occluders and {1} occludees - the bake has something to do", occluders, occludees));
        }

        static void Pass(string message) { Debug.Log(Prefix + " PASS " + message); }
        static void Fail(string message) { Debug.LogError(Prefix + " FAIL " + message); }
    }
}
