using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Puts on the best apparel lying on the map that is worth the change (RimWorld:
    /// <c>RimWorld.JobGiver_OptimizeApparel</c>). Same shape as RimWorld's: a per-pawn cooldown kept on
    /// <see cref="MindState.Pawn_MindState.nextApparelOptimizeTick"/>, every candidate scored by
    /// <see cref="ApparelScoreGain"/> against what it would replace, nothing below
    /// <see cref="MinScoreGainToCare"/>, and a <see cref="ApparelJobDefOf.Wear"/> job for the winner.
    ///
    /// <para/><b>Recalled, not checked against source:</b> the cooldown range, the gain threshold, the
    /// x10 bonus for apparel that replaces nothing, the 0.1 base score, the armor sum and the hit-points
    /// curve. The tests pin behaviour (a citizen with nothing on dresses; a citizen trades up and never down;
    /// the cooldown holds), not these numbers.
    ///
    /// <para/><b>Trimmed:</b> RimWorld only considers apparel in storage and allowed by the pawn's outfit
    /// policy. This port has neither outfits nor a player to haul for them, so any spawned, unreserved,
    /// reachable apparel counts. Insulation, gender and the tainted/corpse penalties are not modelled.
    /// </summary>
    public sealed class JobGiver_OptimizeApparel : ThinkNode_JobGiver
    {
        /// <summary>RimWorld's <c>ApparelOptimizeCheckIntervalMin</c>/<c>Max</c> (recalled).</summary>
        public const int ApparelOptimizeCheckIntervalMin = 6000;

        public const int ApparelOptimizeCheckIntervalMax = 9000;

        /// <summary>A change worth less than this is not worth walking to (recalled).</summary>
        public const float MinScoreGainToCare = 0.05f;

        /// <summary>RimWorld's <c>HitPointsPercentScoreFactorCurve</c> (recalled): worn-out apparel is worth
        /// little, and below a fifth of its hit points next to nothing.</summary>
        public static readonly SimpleCurve HitPointsPercentScoreFactorCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(0.2f, 0.2f),
            new CurvePoint(0.22f, 0.6f),
            new CurvePoint(0.5f, 0.6f),
            new CurvePoint(0.52f, 1f),
        });

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (pawn.apparel == null || pawn.Map == null || !pawn.RaceProps.Humanlike) return null;
            if (Find.TickManager.TicksGame < pawn.mindState.nextApparelOptimizeTick) return null;

            Thing? best = null;
            float bestGain = 0f;
            IReadOnlyList<Thing> items = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not ThingWithComps apparel || !apparel.def.IsApparel || !apparel.Spawned) continue;
                float gain = ApparelScoreGain(pawn, apparel);
                if (gain < MinScoreGainToCare || gain < bestGain) continue;
                if (!pawn.Map.reservationManager.CanReserve(pawn, apparel)) continue;
                if (!Reachability.CanReach(pawn, apparel, PathEndMode.OnCell)) continue;
                best = apparel;
                bestGain = gain;
            }

            if (best == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }
            return new Job(ApparelJobDefOf.Wear, best);
        }

        /// <summary>RimWorld's <c>SetNextOptimizeTick</c>: the next look is 6000-9000 ticks away, drawn from
        /// the seeded stream.</summary>
        public static void SetNextOptimizeTick(Pawn pawn)
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame
                + Rand.Current.Range(ApparelOptimizeCheckIntervalMin, ApparelOptimizeCheckIntervalMax);
        }

        /// <summary>
        /// What putting <paramref name="ap"/> on is worth to <paramref name="pawn"/> (RimWorld:
        /// <c>JobGiver_OptimizeApparel.ApparelScoreGain</c>): its own score, less the score of everything worn
        /// that it would have to replace, and ten times its score when it replaces nothing.
        /// </summary>
        public static float ApparelScoreGain(Pawn pawn, ThingWithComps ap)
        {
            float score = ApparelScoreRaw(ap);
            bool replaces = false;
            IReadOnlyList<ThingWithComps> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                ApparelProperties? wornProps = worn[i].def.apparel;
                if (wornProps == null || !wornProps.ConflictsWith(ap.def.apparel!)) continue;
                score -= ApparelScoreRaw(worn[i]);
                replaces = true;
            }
            if (!replaces) score *= 10f;
            return score;
        }

        /// <summary>
        /// What a piece of apparel is worth on its own (RimWorld: <c>JobGiver_OptimizeApparel.ApparelScoreRaw</c>,
        /// trimmed to what this port models): 0.1, plus its sharp, blunt and heat armor, scaled by how worn
        /// out it is.
        /// </summary>
        public static float ApparelScoreRaw(Thing ap)
        {
            float score = 0.1f;
            score += StatOf(ap, "ArmorRating_Sharp") + StatOf(ap, "ArmorRating_Blunt") + StatOf(ap, "ArmorRating_Heat");
            if (ap.def.useHitPoints && ap.MaxHitPoints > 0)
            {
                score *= HitPointsPercentScoreFactorCurve.Evaluate(ap.HitPoints / (float)ap.MaxHitPoints);
            }
            return score;
        }

        private static float StatOf(Thing thing, string statName)
        {
            StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail(statName);
            return stat == null ? 0f : thing.GetStatValue(stat);
        }
    }
}
