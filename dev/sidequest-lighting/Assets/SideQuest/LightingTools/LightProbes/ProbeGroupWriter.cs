// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// The one place probe positions are written to a LightProbeGroup.
    ///
    /// LightProbeGroup.probePositions is in the group's LOCAL space. The prior-art tool
    /// got this right in its hand-painting path and wrong everywhere else, writing world
    /// positions straight into the array. That worked only because the group happened to
    /// sit at the origin with an identity transform - move or rotate it and every probe
    /// lands somewhere else entirely, silently, with the bake appearing to succeed.
    ///
    /// So every sampler in this package produces world positions and nothing else, and
    /// this is the only code that converts. There is no second path to get wrong.
    /// </summary>
    public static class ProbeGroupWriter
    {
        public const string DefaultGroupName = "SQ_LightProbes";

        /// <summary>
        /// Writes world-space positions into a group, converting to local space.
        ///
        /// Records undo, marks dirty and marks the scene dirty - the automatic paths in
        /// the prior art did none of these, so an automatic placement could not be undone
        /// and could be lost by closing the scene without saving.
        /// </summary>
        public static void CommitWorldPositions(LightProbeGroup group, IList<Vector3> worldPositions, string label)
        {
            if (group == null) return;

            SqUndo.Modify(group, label);

            var local = new Vector3[worldPositions.Count];
            Transform transform = group.transform;

            for (int i = 0; i < worldPositions.Count; i++)
                local[i] = transform.InverseTransformPoint(worldPositions[i]);

            group.probePositions = local;
        }

        /// <summary>Reads a group's positions back out in world space.</summary>
        public static List<Vector3> ReadWorldPositions(LightProbeGroup group)
        {
            var result = new List<Vector3>();
            if (group == null) return result;

            Vector3[] local = group.probePositions;
            if (local == null) return result;

            Transform transform = group.transform;
            for (int i = 0; i < local.Length; i++)
                result.Add(transform.TransformPoint(local[i]));

            return result;
        }

        /// <summary>
        /// Finds this tool's group, or creates one at the origin with an identity transform.
        ///
        /// A fresh group is created unrotated and unscaled deliberately: local space then
        /// equals world space, so the stored values stay readable in the Inspector and a
        /// hand edit means what it looks like it means. The conversion above still runs, so
        /// a group the user later moves keeps working.
        /// </summary>
        public static LightProbeGroup FindOrCreateGroup(SceneScan scan, out bool created)
        {
            created = false;

            for (int i = 0; i < scan.Existing.ProbeGroups.Count; i++)
            {
                LightProbeGroup group = scan.Existing.ProbeGroups[i];
                if (group != null && group.gameObject.name == DefaultGroupName) return group;
            }

            // Fall back to any existing group rather than adding a second one: two groups
            // in a scene is legal but almost always a mistake, and adopting the existing
            // one is far less surprising than quietly creating a rival.
            if (scan.Existing.ProbeGroups.Count > 0 && scan.Existing.ProbeGroups[0] != null)
                return scan.Existing.ProbeGroups[0];

            GameObject go = SqUndo.CreateGameObject(DefaultGroupName, null, "Create Light Probe Group");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            created = true;
            return SqUndo.AddComponent<LightProbeGroup>(go, "Create Light Probe Group");
        }

        /// <summary>
        /// Warns when a probe set is effectively flat.
        ///
        /// Unity tetrahedralises probes, and a single horizontal sheet degenerates that
        /// into slivers - lighting then interpolates badly or not at all above and below
        /// the plane, which in VR is exactly head height. The prior-art tool placed one
        /// sheet at a fixed offset and had no way to notice.
        /// </summary>
        public static bool IsCoplanar(IList<Vector3> worldPositions, float tolerance = 0.25f)
        {
            if (worldPositions == null || worldPositions.Count < 4) return true;

            float min = worldPositions[0].y;
            float max = min;

            for (int i = 1; i < worldPositions.Count; i++)
            {
                float y = worldPositions[i].y;
                if (y < min) min = y;
                if (y > max) max = y;
            }

            return (max - min) < tolerance;
        }
    }
}
