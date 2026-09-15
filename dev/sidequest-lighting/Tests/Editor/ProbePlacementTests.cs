// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using SideQuest.LightingTools.LightProbes;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Tests
{
    /// <summary>
    /// Verifies placement results against the T2 fixture.
    ///
    /// Run as menu items rather than NUnit tests so they can be driven over MCP exactly
    /// the way the tools themselves are, and so a failure reports through the same console
    /// protocol. Each writes one [SQLT-TEST] line that says PASS or FAIL and why.
    /// </summary>
    public static class ProbePlacementTests
    {
        const string Prefix = "[SQLT-TEST]";

        /// <summary>
        /// The regression that matters most.
        ///
        /// LightProbeGroup.probePositions is in the group's LOCAL space. The prior-art
        /// placer wrote world positions into it directly and looked correct in every scene
        /// where the group sat at the origin with no rotation. T2 puts the group at an
        /// arbitrary position and rotation, so that bug scatters every probe outside the
        /// building - which is precisely what this measures.
        /// </summary>
        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify T2 Probe Placement", false, 40)]
        public static void VerifyT2ProbePlacement()
        {
            LightProbeGroup group = Object.FindFirstObjectByType<LightProbeGroup>();
            if (group == null) { Fail("no LightProbeGroup in the scene"); return; }

            Vector3[] local = group.probePositions;
            if (local == null || local.Length == 0) { Fail("the probe group is empty - run Apply Plan first"); return; }

            // If the group were at the origin unrotated, this test could not tell a correct
            // implementation from the bug it exists to catch.
            bool transformIsTrap =
                group.transform.position.sqrMagnitude > 1f ||
                Quaternion.Angle(group.transform.rotation, Quaternion.identity) > 1f;

            if (!transformIsTrap)
            {
                Fail("the probe group is at the origin with no rotation, so this test proves nothing - rebuild T2");
                return;
            }

            List<Bounds> rooms = FindRoomBounds();
            if (rooms.Count == 0) { Fail("no rooms found - is T2_MultiRoom open?"); return; }

            int inside = 0;
            int above = 0;
            int lateral = 0;
            Vector3 worstPoint = Vector3.zero;
            float worstDistance = 0f;

            Transform transform = group.transform;

            for (int i = 0; i < local.Length; i++)
            {
                Vector3 world = transform.TransformPoint(local[i]);

                float nearest = float.MaxValue;
                Bounds nearestRoom = rooms[0];

                for (int r = 0; r < rooms.Count; r++)
                {
                    float distance = Mathf.Sqrt(rooms[r].SqrDistance(world));
                    if (distance < nearest) { nearest = distance; nearestRoom = rooms[r]; }
                }

                if (nearest <= 0.75f) { inside++; continue; }

                // Where a stray probe went says what went wrong. Probes above the rooms are
                // a sampling mistake - the roof read as a floor. Probes scattered laterally
                // are the world-vs-local signature. Reporting the split saves guessing.
                if (world.y > nearestRoom.max.y) above++;
                else lateral++;

                if (nearest > worstDistance) { worstDistance = nearest; worstPoint = world; }
            }

            int outside = above + lateral;

            if (outside == 0)
            {
                Pass(string.Format(
                    "all {0} probes are inside the rooms in world space, with the group at {1} rotated {2:0}deg",
                    local.Length, transform.position.ToString("0.#"),
                    Quaternion.Angle(transform.rotation, Quaternion.identity)));
                return;
            }

            string diagnosis = lateral > above
                ? "mostly scattered sideways, which is the world-vs-local signature: positions written without InverseTransformPoint"
                : "mostly above the rooms, which means exterior surfaces were sampled as floors";

            Fail(string.Format(
                "{0} of {1} probes ({2:P0}) fall outside every room - {3} above, {4} sideways, worst {5:0.#}m at {6}. {7}",
                outside, local.Length, (float)outside / local.Length,
                above, lateral, worstDistance, worstPoint.ToString("0.#"), diagnosis));
        }

        /// <summary>
        /// Probes must be layered, not laid out in one flat sheet.
        ///
        /// A single horizontal set lights the floor and not the viewer's head, and
        /// tetrahedralises into slivers that interpolate badly in every direction.
        /// </summary>
        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify Probe Layering", false, 41)]
        public static void VerifyProbeLayering()
        {
            LightProbeGroup group = Object.FindFirstObjectByType<LightProbeGroup>();
            if (group == null || group.probePositions == null || group.probePositions.Length == 0)
            {
                Fail("no probes to check - run Apply Plan first");
                return;
            }

            List<Vector3> world = ProbeGroupWriter.ReadWorldPositions(group);

            float min = float.MaxValue, max = float.MinValue;
            var heights = new HashSet<int>();

            for (int i = 0; i < world.Count; i++)
            {
                float y = world[i].y;
                if (y < min) min = y;
                if (y > max) max = y;
                heights.Add(Mathf.RoundToInt(y * 4f)); // quarter-metre buckets
            }

            if (ProbeGroupWriter.IsCoplanar(world))
            {
                Fail(string.Format("probes span only {0:0.##}m vertically - the set is effectively flat", max - min));
                return;
            }

            Pass(string.Format("probes span {0:0.##}m vertically across {1} distinct heights", max - min, heights.Count));
        }

        /// <summary>Contribute GI must be off for anything that can move, whatever its size.</summary>
        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify Moving Objects Excluded", false, 42)]
        public static void VerifyMovingObjectsExcluded()
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            var offenders = new List<string>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                bool canMove = renderer.GetComponentInParent<Rigidbody>() != null ||
                               renderer.GetComponentInParent<Animator>() != null;
                if (!canMove) continue;

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) != 0) offenders.Add(renderer.gameObject.name);
            }

            if (offenders.Count > 0)
            {
                Fail("these can move but still contribute GI: " + string.Join(", ", offenders.ToArray()));
                return;
            }

            Pass("no movable renderer contributes GI");
        }

        [MenuItem("Tools/SideQuest/Lighting/Tests/Verify All", false, 60)]
        public static void VerifyAll()
        {
            VerifyT2ProbePlacement();
            VerifyProbeLayering();
            VerifyMovingObjectsExcluded();
        }

        /// <summary>
        /// Room interiors, derived from the fixture's own wall objects rather than
        /// hard-coded, so changing the room size in the builder cannot silently invalidate
        /// the assertion.
        /// </summary>
        static List<Bounds> FindRoomBounds()
        {
            var rooms = new List<Bounds>();

            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (!roots[i].name.StartsWith("Room")) continue;

                Renderer[] parts = roots[i].GetComponentsInChildren<Renderer>();
                if (parts.Length == 0) continue;

                Bounds bounds = parts[0].bounds;
                for (int p = 1; p < parts.Length; p++) bounds.Encapsulate(parts[p].bounds);

                rooms.Add(bounds);
            }

            return rooms;
        }

        static void Pass(string message) { Debug.Log(Prefix + " PASS " + message); }
        static void Fail(string message) { Debug.LogError(Prefix + " FAIL " + message); }
    }
}
