// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Places probes where lighting actually changes, not on a uniform grid.
    ///
    /// A fixed grid spends the same number of probes on a featureless floor as on the
    /// shadow edge by a doorway, which is backwards: the floor interpolates perfectly from
    /// four corners while the doorway needs every probe it can get. Probe count is a hard
    /// budget on Quest, so where those probes go decides how the scene looks.
    ///
    /// Density is driven by three signals. Proximity to a light, because falloff is
    /// steepest there. Proximity to a geometry edge, because that is where occlusion
    /// changes. And the variance of a cheap irradiance estimate, which catches the shadow
    /// boundaries the first two miss.
    ///
    /// Placement is dart-throwing rather than subdivision: candidates are visited densest
    /// first and accepted only if nothing already sits within their desired spacing. That
    /// produces a smooth transition between density tiers, where a quadtree-style
    /// subdivision leaves visible seams at tier boundaries.
    /// </summary>
    public sealed class AdaptiveSampler : IProbeSampler
    {
        public ProbeStrategy Strategy { get { return ProbeStrategy.Adaptive; } }
        public string Id { get { return "adaptive"; } }
        public string DisplayName { get { return "Adaptive density"; } }

        /// <summary>Ceiling on candidate floor points before dart-throwing, to bound the cost on a large scene.</summary>
        public const int MaxCandidates = 200000;

        public bool IsAvailable(SceneScan scan, ProbeSamplerSettings settings, out string reason)
        {
            if (scan.Grid == null)
            {
                reason = "The scene could not be voxelised, usually because it contains no renderers with bounds.";
                return false;
            }

            reason = null;
            return true;
        }

        struct Candidate
        {
            public Vector3 Floor;
            public float DesiredSpacing;
        }

        public List<Vector3> Sample(SceneScan scan, ProbeSamplerSettings settings, SqProblemList problems, SamplerStats stats)
        {
            var world = new List<Vector3>();
            OccupancyGrid grid = scan.Grid;
            if (grid == null) return world;

            var lights = new List<Light>(scan.Lights.Count);
            for (int i = 0; i < scan.Lights.Count; i++) lights.Add(scan.Lights[i].Light);
            var irradiance = new IrradianceProbe(lights, settings.UseShadowRays);

            List<Candidate> candidates = CollectCandidates(scan, grid, settings, irradiance);

            // Densest first. A sparse candidate accepted early would block the dense ones
            // that needed that space, so the ordering is what makes the budget land where
            // the detail is.
            candidates.Sort((a, b) => a.DesiredSpacing.CompareTo(b.DesiredSpacing));

            var accepted = new SpatialHash(Mathf.Max(settings.MinSpacing, 0.1f));
            int acceptedCount = 0;

            // Layers are validated against the same space the candidates came from, so a
            // column standing on a prop stops at the ceiling instead of passing through it.
            bool interiorOnly = grid.InteriorCount > 0;
            System.Func<Vector3, bool> isValid = position =>
            {
                Vector3Int cell = grid.WorldToCell(position);
                return interiorOnly
                    ? grid.IsInterior(cell.x, cell.y, cell.z)
                    : grid.IsFree(cell.x, cell.y, cell.z);
            };

            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if ((i & 1023) == 0)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                            "Placing Light Probes", "Choosing positions", (float)i / Mathf.Max(candidates.Count, 1)))
                            break;
                    }

                    Candidate c = candidates[i];
                    if (accepted.HasPointWithin(c.Floor, c.DesiredSpacing)) continue;

                    accepted.Insert(c.Floor);
                    acceptedCount++;

                    ProbeSampling.EmitColumn(c.Floor, settings.LayerHeights, scan.Scale.CeilingHeightEstimate, world, isValid);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            stats.Layers = settings.LayerHeights != null ? settings.LayerHeights.Length : 1;
            stats.Notes = string.Format("{0} candidates, {1} accepted columns", candidates.Count, acceptedCount);

            if (acceptedCount == 0 && problems != null)
            {
                problems.Add(ProbeSampling.CodeNoFloor, SqSeverity.Warn,
                    "No floor surfaces were found to place probes on.")
                    .WithAction("Check that floors have colliders, or use the Mesh Volume or Agent strategy instead.");
            }

            return ProbeSampling.Finalise(world, settings, problems, stats);
        }

        /// <summary>
        /// Finds standable surfaces and scores the spacing each one wants.
        ///
        /// A floor is a free cell directly above a solid one. Scanning upward rather than
        /// raycasting down once per column means upper storeys and mezzanines are found
        /// too, instead of only the first surface under the sky.
        /// </summary>
        static List<Candidate> CollectCandidates(SceneScan scan, OccupancyGrid grid, ProbeSamplerSettings settings, IrradianceProbe irradiance)
        {
            var candidates = new List<Candidate>();
            float gradientRadius = Mathf.Max(settings.Spacing * 0.5f, 0.5f);

            // "A free cell above a solid one" describes the roof and the ground outside
            // just as well as it describes a floor, so an enclosed scene would spend a
            // quarter of its probe budget lighting the outside of the building. Where the
            // scene has enclosed space, probes belong in it.
            //
            // A scene that is entirely open has no interior to restrict to, and there the
            // exterior surfaces are the floors - so fall back rather than place nothing.
            bool interiorOnly = grid.InteriorCount > 0;

            for (int x = 0; x < grid.SizeX; x++)
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    if (candidates.Count >= MaxCandidates) return candidates;

                    for (int y = 0; y < grid.SizeY; y++)
                    {
                        if (interiorOnly ? !grid.IsInterior(x, y, z) : !grid.IsFree(x, y, z)) continue;
                        if (!grid.IsSolid(x, y - 1, z)) continue; // not standing on anything

                        Vector3 floor = grid.CellCenter(x, y, z);
                        floor.y -= grid.CellSize * 0.5f; // the surface, not the cell centre

                        candidates.Add(new Candidate
                        {
                            Floor = floor,
                            DesiredSpacing = DesiredSpacing(scan, grid, settings, irradiance, floor, x, y, z, gradientRadius)
                        });
                    }
                }

            return candidates;
        }

        static float DesiredSpacing(
            SceneScan scan, OccupancyGrid grid, ProbeSamplerSettings settings, IrradianceProbe irradiance,
            Vector3 floor, int x, int y, int z, float gradientRadius)
        {
            float spacing = settings.Spacing;

            // Open, uniform space needs far fewer probes than an enclosed room: there is
            // nothing nearby to cast the variation probes exist to capture.
            Zone zone = FindZoneAt(scan, floor);
            if (zone != null && zone.Openness > 0.5f && zone.GradientScore < 0.2f) spacing *= 2f;

            // Near a light, falloff is steep and probes far apart interpolate through it.
            if (IsNearLight(scan, floor)) spacing *= 0.5f;

            // Geometry edges are where occlusion changes, so lighting changes with it.
            if (IsNearEdge(grid, x, y, z)) spacing *= 0.6f;

            // The catch-all: wherever the estimate varies sharply, whatever the cause.
            float gradient = irradiance.GradientScore(floor + Vector3.up * 1.0f, gradientRadius);
            if (gradient > 0.6f) spacing *= 0.5f;
            else if (gradient > 0.3f) spacing *= 0.75f;

            return Mathf.Max(spacing, settings.MinSpacing);
        }

        static bool IsNearLight(SceneScan scan, Vector3 point)
        {
            for (int i = 0; i < scan.Lights.Count; i++)
            {
                LightFacts light = scan.Lights[i];
                if (light.Type == LightType.Directional) continue;
                if (light.Light == null || !light.Light.enabled) continue;

                if ((light.Position - point).sqrMagnitude < light.Range * light.Range) return true;
            }
            return false;
        }

        /// <summary>True if any of the four horizontal neighbours is solid - a wall, step or pillar base.</summary>
        static bool IsNearEdge(OccupancyGrid grid, int x, int y, int z)
        {
            return grid.IsSolid(x + 1, y, z) || grid.IsSolid(x - 1, y, z)
                || grid.IsSolid(x, y, z + 1) || grid.IsSolid(x, y, z - 1);
        }

        static Zone FindZoneAt(SceneScan scan, Vector3 point)
        {
            for (int i = 0; i < scan.Zones.Count; i++)
                if (scan.Zones[i].Bounds.Contains(point)) return scan.Zones[i];
            return null;
        }
    }
}
