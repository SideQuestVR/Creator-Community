// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Fills a chosen collider volume with a 3D probe grid.
    ///
    /// Useful when a creator knows exactly which space matters - one room, a stairwell, the
    /// volume a player can reach - and wants even coverage through it rather than a
    /// heuristic's opinion.
    ///
    /// Three defects from the prior-art volume placer are fixed here:
    ///
    /// Its bounds transform scaled the size but not the centre, so a mesh whose pivot was
    /// not centred filled the wrong region. Transforming all eight corners handles pivot,
    /// rotation and non-uniform scale together.
    ///
    /// Its containment test was Collider.bounds.Contains - the world-space AABB - so a
    /// rotated or L-shaped volume filled its bounding box, pushing probes through walls.
    /// Collider.ClosestPoint is exact for primitives and convex meshes.
    ///
    /// Its density knob computed spacing as pow(volume / (density * volume), 1/3), in which
    /// volume cancels entirely. The knob therefore meant the opposite of what it meant in
    /// the NavMesh mode, which is why that tool's own tooltips had to explain that "higher"
    /// means more probes in one mode and fewer in the other. Spacing here is metres, and
    /// smaller always means more probes.
    /// </summary>
    public sealed class MeshVolumeSampler : IProbeSampler
    {
        public const string CodeNonConvex = "LP030_NONCONVEX_VOLUME";
        public const string CodeNoVolume = "LP031_NO_VOLUME";

        public ProbeStrategy Strategy { get { return ProbeStrategy.MeshVolume; } }
        public string Id { get { return "volume"; } }
        public string DisplayName { get { return "Mesh volume fill"; } }

        public bool IsAvailable(SceneScan scan, ProbeSamplerSettings settings, out string reason)
        {
            if (settings.VolumeObject == null)
            {
                reason = "No volume object selected. Choose a GameObject with a collider that bounds the space to fill.";
                return false;
            }

            if (settings.VolumeObject.GetComponent<Collider>() == null)
            {
                reason = "The volume object has no Collider. Add one - a Box Collider is usually enough.";
                return false;
            }

            reason = null;
            return true;
        }

        public List<Vector3> Sample(SceneScan scan, ProbeSamplerSettings settings, SqProblemList problems, SamplerStats stats)
        {
            var world = new List<Vector3>();

            GameObject volumeObject = settings.VolumeObject;
            if (volumeObject == null)
            {
                if (problems != null)
                {
                    problems.Add(CodeNoVolume, SqSeverity.Error, "No volume object was selected.")
                        .WithAction("Select a GameObject with a collider in the Light Probes window.");
                }
                return world;
            }

            Collider collider = volumeObject.GetComponent<Collider>();
            if (collider == null) return world;

            bool exactContainment = SupportsClosestPoint(collider);
            if (!exactContainment && problems != null)
            {
                problems.Add(CodeNonConvex, SqSeverity.Warn,
                    "The volume is a non-convex Mesh Collider, which cannot be tested exactly. A ray-parity test was used instead; concave detail may be approximate.")
                    .WithAction("Tick Convex on the Mesh Collider, or use a Box Collider that bounds the space, for an exact fill.");
            }

            Bounds bounds = WorldBounds(volumeObject, collider);
            float spacing = Mathf.Max(settings.Spacing, 0.05f);

            int stepsX = Mathf.CeilToInt(bounds.size.x / spacing);
            int stepsY = Mathf.CeilToInt(bounds.size.y / spacing);
            int stepsZ = Mathf.CeilToInt(bounds.size.z / spacing);

            long total = (long)(stepsX + 1) * (stepsY + 1) * (stepsZ + 1);
            if (total > 4_000_000)
            {
                if (problems != null)
                {
                    problems.Add("LP032_VOLUME_TOO_DENSE", SqSeverity.Error, string.Format(
                        "This volume at {0:0.##}m spacing would test {1:N0} points.", spacing, total))
                        .WithAction("Increase Spacing, or use a smaller volume.");
                }
                return world;
            }

            try
            {
                for (int ix = 0; ix <= stepsX; ix++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Placing Light Probes", "Filling the volume", (float)ix / Mathf.Max(stepsX, 1)))
                        break;

                    float x = bounds.min.x + ix * spacing;

                    for (int iy = 0; iy <= stepsY; iy++)
                    {
                        float y = bounds.min.y + iy * spacing;

                        for (int iz = 0; iz <= stepsZ; iz++)
                        {
                            var point = new Vector3(x, y, bounds.min.z + iz * spacing);

                            bool inside = exactContainment
                                ? IsInsideConvex(collider, point)
                                : IsInsideByParity(collider, point, bounds);

                            if (inside) world.Add(point);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            stats.Layers = stepsY + 1;
            stats.Notes = string.Format("volume '{0}', {1:0.##}m spacing", volumeObject.name, spacing);
            return ProbeSampling.Finalise(world, settings, problems, stats);
        }

        /// <summary>
        /// World bounds of the volume, from all eight transformed corners.
        ///
        /// Using the collider's own world bounds would be simpler, but a rotated box
        /// collider reports an axis-aligned box larger than itself; taking the mesh corners
        /// through the transform keeps the fill region tight to what was actually drawn.
        /// </summary>
        static Bounds WorldBounds(GameObject volumeObject, Collider collider)
        {
            var filter = volumeObject.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return collider.bounds;

            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 matrix = volumeObject.transform.localToWorldMatrix;

            Vector3 min = local.min;
            Vector3 max = local.max;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);

            for (int corner = 1; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);

                bounds.Encapsulate(matrix.MultiplyPoint3x4(point));
            }

            return bounds;
        }

        static bool SupportsClosestPoint(Collider collider)
        {
            var mesh = collider as MeshCollider;
            return mesh == null || mesh.convex;
        }

        /// <summary>
        /// Exact for primitives and convex meshes: ClosestPoint returns the point itself
        /// only when it is inside.
        /// </summary>
        static bool IsInsideConvex(Collider collider, Vector3 point)
        {
            return (collider.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
        }

        /// <summary>
        /// Ray-parity fallback for non-convex mesh colliders: an odd number of surface
        /// crossings on the way out means the point started inside.
        ///
        /// Rays are cast from outside inward rather than outward, because a ray starting
        /// inside a mesh collider does not reliably register the face it exits through.
        /// </summary>
        static bool IsInsideByParity(Collider collider, Vector3 point, Bounds bounds)
        {
            int inside = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 direction = Vector3.zero;
                direction[axis] = 1f;

                float distance = bounds.size[axis] + 2f;
                Vector3 origin = point - direction * distance;

                int hits = 0;
                float travelled = 0f;

                // Step along, restarting just past each hit: a single RaycastAll would be
                // simpler but allocates per test, and this runs once per grid point.
                for (int guard = 0; guard < 32; guard++)
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(origin + direction * travelled, direction,
                            out hit, distance - travelled, ~0, QueryTriggerInteraction.Ignore))
                        break;

                    if (hit.collider == collider) hits++;
                    travelled += hit.distance + 0.001f;
                    if (travelled >= distance) break;
                }

                if ((hits & 1) == 1) inside++;
            }

            // Best of three axes: a single axis can be fooled by a coplanar face.
            return inside >= 2;
        }
    }
}
