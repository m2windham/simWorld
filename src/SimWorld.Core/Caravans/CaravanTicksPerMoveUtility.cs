using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Caravans
{
    /// <summary>
    /// How many ticks a caravan needs to cross one tile of unit difficulty (RimWorld:
    /// <c>RimWorld.CaravanTicksPerMoveUtility</c>). The slowest member sets the pace: ticks scale inversely
    /// with the lowest <c>Moving</c> capacity level among the caravan's pawns, floored so a barely-mobile
    /// pawn still eventually arrives instead of dividing by (near) zero.
    /// </summary>
    public static class CaravanTicksPerMoveUtility
    {
        /// <summary>RimWorld: <c>CaravanTicksPerMoveUtility.DefaultTicksPerMove</c> — the baseline at Moving = 1.0.</summary>
        public const int DefaultTicksPerMove = 3300;

        /// <summary>RimWorld clamps the moving level this low before dividing, so a pawn near-unable to move still moves, just very slowly.</summary>
        public const float MinMovingLevel = 0.05f;

        public static int GetTicksPerMove(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return DefaultTicksPerMove;

            float slowest = float.MaxValue;
            for (int i = 0; i < pawns.Count; i++)
            {
                float level = pawns[i].health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                if (level < slowest) slowest = level;
            }
            if (slowest == float.MaxValue) slowest = 1f;
            slowest = Math.Max(MinMovingLevel, slowest);

            return (int)Math.Round(DefaultTicksPerMove / slowest);
        }
    }
}
