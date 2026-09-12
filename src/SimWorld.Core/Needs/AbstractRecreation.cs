using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Needs
{
    /// <summary>
    /// <b>Recreation for a citizen with no map to take it on</b> — the same seam
    /// <c>Economy.SettlementLarder</c> closes for food, one need over.
    ///
    /// <para/><b>The defect this closes, and why it was inevitable.</b> Every joy source this port has is a
    /// <see cref="Job"/>: <see cref="JobGiver_GetJoy"/> returns null the instant <c>pawn.Map</c> is null,
    /// and each <see cref="JoyGiver"/> returns null again for the same reason. But a citizen of a settlement
    /// nobody has opened <i>ticks</i> (<c>Sim.CitizenTickRegistry</c>) and its needs decay at full speed,
    /// with no world to satisfy them in. That is exactly the shape of the regression
    /// <c>Economy.SettlementLarder</c>'s own doc records for food — "an unwatched settlement starved where it
    /// used to be frozen, and in a world of many settlements that is all of them but one" — and building a
    /// recreation system without this would have reproduced it precisely: every unwatched settlement pinned
    /// at the recreation need's worst stage, −20 mood, for ever.
    ///
    /// <para/><b>The predicate is <c>!Spawned</c>, and deliberately not a tier</b>, for the reason the larder
    /// gives at length: what an abstract citizen lacks is not fidelity but <i>a world to act in</i>, and
    /// <c>Spawned</c> is exactly the question "is there a map under this pawn".
    ///
    /// <para/><b>Nothing here is invented recreation.</b> The activities are the shipped
    /// <see cref="JoyGiverDef"/>s that declare <see cref="JoyGiverDef.canDoWithoutMap"/> — the ones whose
    /// only requirement is the sky, the ground, or other people — and the joy is credited at that giver's
    /// own <see cref="JoyGiverDef.joyGainRate"/> through <see cref="Need_Joy.GainJoy"/>, so it builds the
    /// same per-kind tolerance, is damped by it the same way, and lands in the same need. A citizen bored of
    /// every placeless kind gains nothing and its recreation falls, exactly as it would on a map.
    ///
    /// <para/><b>No roll, ever, and no state.</b> This runs inside the tick loop, where one draw would shift
    /// every subsequent roll in the game (<c>Economy.SettlementLarder</c> and
    /// <c>Economy.SettlementStockInitiative</c> both say the same thing about themselves). Which activity is
    /// taken is a total order over the placeless givers — least-tolerated kind first, ties broken by defName
    /// — so it is decided, not drawn. Nothing is remembered between calls: the answer is re-derived from the
    /// pawn's own need and tolerances every time.
    ///
    /// <para/><b>What this deliberately does not model.</b> An off-map citizen always finds its break, where
    /// a citizen on a map may fail to (no open sky, rain, nobody free to sit with). That asymmetry is the
    /// larder's exactly — an off-map citizen always eats if the ledger has food — and it is the honest cost
    /// of not simulating a world for them. It also means recreation is slightly <i>easier</i> off-map than
    /// on; the integration test measures both columns rather than assuming they agree.
    /// </summary>
    public static class AbstractRecreation
    {
        /// <summary>
        /// One pass of off-map recreation, called from <see cref="Need_Joy.NeedInterval"/> and from its bulk
        /// twin. It takes no elapsed span and needs none: recreation arrives in whole sessions rather than
        /// as a per-tick rate (see below), and the threshold below is what decides whether another one is
        /// due — so the Full tier's 150-tick call and the Interval tier's 2,000-tick call reach the same
        /// answer from the same state, which is exactly what <see cref="Need.NeedIntervalBulk"/>'s contract
        /// asks of a rate that is constant across the span.
        /// </summary>
        public static void JoyInterval(Pawn pawn, Need_Joy joy)
        {
            if (pawn == null || joy == null) return;

            // A citizen standing on a live map walks to its recreation and takes it there; this never
            // touches them. Same predicate, same reason, as Economy.SettlementLarder's.
            if (pawn.Spawned) return;

            // Asleep, suspended, or otherwise held: the need is not falling either, so nothing is owed.
            if (joy.IsFrozen) return;

            // Above the line the think tree's own recreation tier turns on at, a citizen is doing something
            // else — so an off-map one is too. Without this an abstract citizen would sit permanently full
            // and a watched settlement would look worse than an unwatched one for no reason but the map.
            if (joy.CurLevel >= Need_Joy.ThreshLow) return;

            JoyGiverDef? giver = BestPlacelessGiver(joy);
            if (giver == null) return;

            // A whole session at once, not a trickle. The map path's recreation arrives in sessions — the
            // think tree's recreation tier fires below this same threshold and the driver then pays
            // joyGainRate for joyDuration ticks or until the need is full — so an abstract citizen's
            // recreation has to arrive the same way, or the two halves of the game would sit at different
            // levels of the same need for no reason but which one has a map. What it does not spend is the
            // <i>time</i>: an abstract citizen has no clock to spend it on, which is what spec 11.3 means by
            // a tier whose "full state exists" but only advances on the coarse buckets. Amplitude, period
            // and tolerance accrual all match the map path; only the walking is missing.
            joy.GainJoy(giver.joyGainRate * JoyUtility.JoyGainPerTickAtRate1 * giver.joyDuration, giver.joyKind);
        }

        /// <summary>
        /// The placeless activity this pawn is least tired of, or null when it is bored of all of them.
        /// Deterministic: lowest tolerance wins, then lowest <c>defName</c>.
        /// </summary>
        public static JoyGiverDef? BestPlacelessGiver(Need_Joy joy)
        {
            JoyGiverDef? best = null;
            float bestTolerance = 0f;
            IReadOnlyList<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            for (int i = 0; i < givers.Count; i++)
            {
                JoyGiverDef giver = givers[i];
                if (!giver.canDoWithoutMap) continue;
                if (joy.tolerances.BoredOf(giver.joyKind)) continue;

                float tolerance = joy.tolerances[giver.joyKind];
                if (best == null
                    || tolerance < bestTolerance
                    || (tolerance == bestTolerance && string.CompareOrdinal(giver.defName, best.defName) < 0))
                {
                    best = giver;
                    bestTolerance = tolerance;
                }
            }
            return best;
        }
    }
}
