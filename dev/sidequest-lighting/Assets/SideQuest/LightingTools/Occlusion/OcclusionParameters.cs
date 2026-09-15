// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.Occlusion
{
    /// <summary>
    /// The three numbers Unity's occlusion bake actually takes, solved from the scene.
    ///
    /// These are the settings creators most often leave at defaults and most often get
    /// wrong, because nothing in the Editor relates them to the scene in front of you.
    /// Unity's default Smallest Occluder of 5 is sized for a city block; in a room-scale
    /// VR world nothing is 5m, so the bake finds no occluders and culls nothing. Smallest
    /// Hole is worse in the other direction: too large and geometry pops in through
    /// doorways, too small and the data explodes into a multi-hour bake.
    ///
    /// Both are derivable. Occluder size comes from the sizes of the actual occluders, and
    /// hole size comes from the narrowest passage between rooms - which zone segmentation
    /// has already measured.
    /// </summary>
    public sealed class OcclusionParameters
    {
        public const string CodeCellBudget = "OC050_CELL_BUDGET";
        public const string CodeNoZoneGaps = "OC051_NO_ZONE_GAPS";
        public const string CodeDataBudget = "OC060_DATA_BUDGET";

        /// <summary>Estimated voxels above which the bake is refused outright.</summary>
        public const double RefuseCellCount = 60000000.0;

        /// <summary>Estimated voxels above which the bake is warned about but allowed.</summary>
        public const double WarnCellCount = 6000000.0;

        public float SmallestOccluder = 1.5f;
        public float SmallestHole = 0.25f;
        public float BackfaceThreshold = 100f;

        public double EstimatedCells;
        public string Rationale;

        /// <summary>
        /// Derives all three from the scan.
        /// </summary>
        public static OcclusionParameters Solve(
            SceneScan scan, List<OcclusionDecision> decisions, SqProblemList problems)
        {
            var parameters = new OcclusionParameters();

            parameters.SmallestOccluder = SolveSmallestOccluder(decisions);
            parameters.SmallestHole = SolveSmallestHole(scan, problems);
            parameters.BackfaceThreshold = SolveBackfaceThreshold(scan);

            parameters.EstimatedCells = EstimateCells(scan, parameters.SmallestHole);
            parameters.Rationale = string.Format(
                "occluder just under the smallest of {0} flagged occluder faces, hole at half the narrowest zone connection",
                CountOccluders(decisions));

            CheckCellBudget(parameters, problems);
            return parameters;
        }

        /// <summary>
        /// Just under the smallest face the classifier actually flagged.
        ///
        /// Umbra ignores any occluder smaller than this, so the two thresholds have to
        /// agree. An earlier version took the 25th percentile, which flagged a quarter of
        /// its own occluders below the size Umbra would consider - marking geometry as an
        /// occluder that then does nothing, while still costing data.
        ///
        /// Taking the minimum keeps the flagged set and Umbra's threshold consistent, and
        /// it no longer drags the bake down, because the classifier's own bar is now high
        /// enough that nothing small reaches here.
        /// </summary>
        static float SolveSmallestOccluder(List<OcclusionDecision> decisions)
        {
            float smallest = float.MaxValue;

            for (int i = 0; i < decisions.Count; i++)
            {
                if (!decisions[i].WantOccluder) continue;

                float face = decisions[i].Renderer.OccluderFaceSize;
                if (face < smallest) smallest = face;
            }

            if (smallest == float.MaxValue) return 2f;

            // A little under, so floating-point comparison inside Umbra cannot exclude the
            // very object the threshold was derived from.
            return Mathf.Clamp(smallest * 0.95f, 0.5f, 10f);
        }

        /// <summary>
        /// Unity's own default, and the ceiling this solver will not exceed.
        ///
        /// Smallest Hole seals every gap narrower than itself. Raising it is therefore not
        /// a mild tuning knob - it makes occluders progressively more solid, and the band
        /// of space within roughly half this distance of any surface stops being described
        /// reliably. The visible result is geometry vanishing when the camera is close to
        /// it, which reads as a rendering bug rather than a bake setting.
        ///
        /// An earlier version of this solver clamped to 0.5 and, on a scene whose narrowest
        /// doorway measured 0.75m, shipped exactly that - the most aggressive value it
        /// could produce. Hence a ceiling at Unity's default rather than above it.
        /// </summary>
        public const float MaxSmallestHole = 0.25f;

        /// <summary>
        /// The narrowest passage between two zones, biased well below it.
        ///
        /// This decides whether a player standing in a doorway sees the next room or
        /// watches it pop in. Zone segmentation already measured every connection while
        /// finding the rooms, so the real value is available instead of a guess.
        ///
        /// Half the measured width, not most of it. The error is asymmetric: too small
        /// costs bake time and data, while too large makes geometry disappear in front of
        /// the player. Only one of those is worth risking.
        /// </summary>
        static float SolveSmallestHole(SceneScan scan, SqProblemList problems)
        {
            float narrowest = float.MaxValue;

            for (int i = 0; i < scan.Zones.Count; i++)
            {
                Zone zone = scan.Zones[i];
                for (int c = 0; c < zone.Connections.Count; c++)
                {
                    float gap = zone.Connections[c].GapWidth;
                    if (gap > 0f && gap < narrowest) narrowest = gap;
                }
            }

            float cap = Mathf.Min(SqSettings.instance.occlusionMaxSmallestHole, MaxSmallestHole);

            if (narrowest == float.MaxValue)
            {
                if (problems != null)
                {
                    problems.Add(CodeNoZoneGaps, SqSeverity.Info, string.Format(
                        "No connections between zones were found, so Smallest Hole falls back to {0}m.", SqFormat.Num(cap)))
                        .WithAction("If the scene has narrow doorways or windows, check that zone segmentation resolved them.");
                }
                return cap;
            }

            return Mathf.Clamp(narrowest * 0.5f, 0.05f, cap);
        }

        /// <summary>
        /// How aggressively Umbra may cull using backfaces.
        ///
        /// 100 means no backface culling assumptions, which is correct for closed interiors
        /// where the player never sees the outside of a wall. Lowering it shrinks the data
        /// but lets the camera see through geometry from the wrong side, so it is reported
        /// rather than lowered automatically - the failure looks like a rendering bug and
        /// is very hard to trace back to a bake setting.
        /// </summary>
        static float SolveBackfaceThreshold(SceneScan scan)
        {
            return 100f;
        }

        static int CountOccluders(List<OcclusionDecision> decisions)
        {
            int n = 0;
            for (int i = 0; i < decisions.Count; i++) if (decisions[i].WantOccluder) n++;
            return n;
        }

        /// <summary>
        /// Umbra's cost scales with scene volume over hole size cubed, so halving the hole
        /// size multiplies the work by eight.
        /// </summary>
        static double EstimateCells(SceneScan scan, float smallestHole)
        {
            if (!scan.Scale.HasBounds) return 0.0;

            Vector3 size = scan.Scale.WorldBounds.size;
            double volume = (double)Mathf.Max(size.x, 1f) * Mathf.Max(size.y, 1f) * Mathf.Max(size.z, 1f);
            double cell = Mathf.Max(smallestHole, 0.01f);

            return volume / (cell * cell * cell);
        }

        /// <summary>
        /// Refuses a bake that would take hours.
        ///
        /// The check exists because the failure mode is uniquely bad: StaticOcclusionCulling
        /// gives no progress estimate worth the name, cannot be cancelled cleanly, and a
        /// hole size that is too small by a factor of four turns a two-minute bake into an
        /// overnight one with no warning.
        /// </summary>
        static void CheckCellBudget(OcclusionParameters parameters, SqProblemList problems)
        {
            if (problems == null || parameters.EstimatedCells <= WarnCellCount) return;

            bool refuse = parameters.EstimatedCells > RefuseCellCount;

            problems.Add(CodeCellBudget, refuse ? SqSeverity.Error : SqSeverity.Warn, string.Format(
                "At {0}m smallest hole this scene works out to roughly {1:N0} occlusion cells, which will make the bake {2}.",
                SqFormat.Num(parameters.SmallestHole), parameters.EstimatedCells,
                refuse ? "impractically long" : "slow"))
                .WithAction("Raise Smallest Hole. Doubling it cuts the work by eight.");
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject("parameters");
            w.Prop("smallestOccluder", SmallestOccluder);
            w.Prop("smallestHole", SmallestHole);
            w.Prop("backfaceThreshold", BackfaceThreshold);
            w.Prop("estimatedCells", EstimatedCells);
            if (Rationale != null) w.Prop("rationale", Rationale);
            w.EndObject();
        }

        public static OcclusionParameters Read(SqJsonValue source, PlanValidation validation)
        {
            var parameters = new OcclusionParameters();
            if (!source.IsObject) return parameters;

            parameters.SmallestOccluder = validation.Clamp("parameters.smallestOccluder",
                source["smallestOccluder"].AsFloat(1.5f), 0.1f, 50f);

            // Capped at the settings ceiling rather than at Unity's upper limit. A plan is
            // allowed to make occlusion more conservative but not more aggressive than the
            // user's own setting, because the aggressive failure - geometry vanishing at
            // close range - is the one nobody traces back to a bake parameter.
            float cap = Mathf.Min(SqSettings.instance.occlusionMaxSmallestHole, MaxSmallestHole);
            parameters.SmallestHole = validation.Clamp("parameters.smallestHole",
                source["smallestHole"].AsFloat(cap), 0.01f, cap);

            parameters.BackfaceThreshold = validation.Clamp("parameters.backfaceThreshold",
                source["backfaceThreshold"].AsFloat(100f), 5f, 100f);

            return parameters;
        }
    }
}
