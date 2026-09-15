// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Takes positions chosen by someone else - an agent writing a plan, or a person
    /// painting in the Scene view.
    ///
    /// This is the strategy for judgement the other two cannot encode. A heuristic knows
    /// that lighting varies near a lamp; it does not know that the alcove behind the desk
    /// is where players will stand, or that the dark corridor is supposed to stay dark. An
    /// agent reading the zone report, or a creator who knows their own scene, does.
    ///
    /// Positions still pass through the same decimation, budget and coplanarity checks as
    /// every other strategy. Being chosen deliberately does not exempt a position from
    /// being a duplicate, and it does not raise the probe budget.
    /// </summary>
    public sealed class AgentSampler : IProbeSampler
    {
        public const string CodeNoPositions = "LP040_NO_POSITIONS";
        public const string CodeOutOfBounds = "LP041_POSITION_OUT_OF_BOUNDS";

        /// <summary>World positions, supplied by a plan or the paint tool.</summary>
        public List<Vector3> Positions = new List<Vector3>();

        /// <summary>
        /// When true, each supplied position is treated as a floor point and a full column
        /// is emitted above it. A plan that specifies exact 3D positions wants them used
        /// verbatim; a plan that lists floor locations wants the layering.
        /// </summary>
        public bool EmitColumns;

        public ProbeStrategy Strategy { get { return ProbeStrategy.Agent; } }
        public string Id { get { return "agent"; } }
        public string DisplayName { get { return "Agent / manual"; } }

        public bool IsAvailable(SceneScan scan, ProbeSamplerSettings settings, out string reason)
        {
            if (Positions == null || Positions.Count == 0)
            {
                reason = "No positions supplied. Paint probes in the Scene view, or provide them in a decision plan.";
                return false;
            }

            reason = null;
            return true;
        }

        public List<Vector3> Sample(SceneScan scan, ProbeSamplerSettings settings, SqProblemList problems, SamplerStats stats)
        {
            var world = new List<Vector3>();

            if (Positions == null || Positions.Count == 0)
            {
                if (problems != null)
                {
                    problems.Add(CodeNoPositions, SqSeverity.Error, "No probe positions were supplied.")
                        .WithAction("Paint positions in the Scene view, or list them in the plan's positions array.");
                }
                return world;
            }

            int rejected = 0;

            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 position = Positions[i];

                // Supplied positions are the one input not derived from the scene, so they
                // are the one input that can be arbitrarily wrong. A coordinate far outside
                // the scene is a mistake, not a decision.
                if (scan.Scale.HasBounds && !PlanValidator.IsPositionPlausible(position, scan.Scale.WorldBounds))
                {
                    rejected++;
                    continue;
                }

                if (EmitColumns)
                    ProbeSampling.EmitColumn(position, settings.LayerHeights, scan.Scale.CeilingHeightEstimate, world);
                else
                    world.Add(position);
            }

            if (rejected > 0 && problems != null)
            {
                problems.Add(CodeOutOfBounds, SqSeverity.Warn, string.Format(
                    "{0} supplied position(s) were far outside the scene bounds and were dropped.", rejected))
                    .WithCount(rejected)
                    .WithAction("Check the coordinates against the scale.worldBounds in the analysis report.");
            }

            stats.Layers = EmitColumns && settings.LayerHeights != null ? settings.LayerHeights.Length : 1;
            stats.Notes = string.Format("{0} supplied, {1} dropped", Positions.Count, rejected);

            return ProbeSampling.Finalise(world, settings, problems, stats);
        }
    }
}
