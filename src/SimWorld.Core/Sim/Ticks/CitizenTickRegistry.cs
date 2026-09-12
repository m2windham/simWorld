using System.Collections.Generic;

using SimWorld.Pawns;

namespace SimWorld.Sim
{
    /// <summary>
    /// Puts every citizen on the tick list their tier names, whether or not they are standing on a map
    /// (<c>docs/spec/simworld-spec.md</c> §11.3). RimWorld's nearest relative is <c>Verse.WorldPawns</c>, the
    /// half of its tick loop that drives the people who are not on any active map; this is that half, expressed
    /// through the tick lists this port already has rather than as a second loop beside them.
    ///
    /// <para/><b>The gap this closes.</b> Tick-list membership was granted in exactly one place —
    /// <see cref="Things.Thing.SpawnSetup"/> — and revoked in exactly one — <see cref="Things.Thing.DeSpawn"/>.
    /// That is right for a <see cref="Things.Thing"/>, which is on a map or nowhere at all. It is wrong for a
    /// <see cref="Pawn"/>, whose tiers are designed to exist off every map: §11.3 has Interval and Statistical
    /// "sit on the Long bucket ... because without <i>some</i> periodic driver a population that is never
    /// promoted would never age and never die, which would break demography participation". The tiering module
    /// built the dispatch (<see cref="Pawn.TickerType"/> reads the tier) and the work
    /// (<see cref="Pawn_TierTracker.CoarseTick"/>, <see cref="Needs.Need.NeedIntervalBulk"/>,
    /// <see cref="Pawn_AgeTracker.AgeTickMothballed"/>) and nothing ever built the membership, so none of it was
    /// reachable for a citizen who had never stood on a map. A settlement the player had never opened has no
    /// interior, therefore nobody spawned, therefore nobody on any list: its citizens' needs, mood and age were
    /// identical after six in-game days to what they were at founding, at every tier including
    /// <see cref="PawnTier.Full"/>. That is the whole of the defect — the civilization was a photograph, not a
    /// simulation running cheaply.
    ///
    /// <para/><b>What this deliberately does not decide.</b> Nothing here promotes, demotes or reads
    /// significance; it reads <see cref="Pawn.TickerType"/>, which is the tier's own answer, and obeys it.
    /// Which tier a citizen holds stays entirely <see cref="God.AttentionManager"/>'s and
    /// <see cref="God.AttentionBudget"/>'s business. So a settlement in focus keeps its roster at Full and pays
    /// Full's per-tick cost (bounded by <see cref="TieringTuning.FullTierBudget"/>, and only ever one settlement
    /// at a time), and every other settlement in the world rests at Interval and pays the coarse cost — which
    /// is what §11.1 means by a civilization running abstracted by default.
    ///
    /// <para/><b>Why a periodic sweep rather than a hook on every path that creates or moves a citizen.</b>
    /// Citizens arrive off-map from several directions — <see cref="World.SettlementFounder.Found"/>'s band
    /// (which §5b.4 also uses for every rival civilization that emerges), <see cref="FamilyManager"/>'s
    /// newborns, a citizen demoted out of Full and then taken off the interior by
    /// <see cref="World.Settlement.SyncCitizenSpawns"/>, and a Scribe load, which rebuilds the tick lists from
    /// the maps alone and so restores nobody who was not standing on one. Hooking each would leave the next one
    /// to be written silently frozen, which is exactly how this bug happened: the one path that did maintain
    /// membership was mistaken for all of them. One idempotent sweep asserting the invariant cannot be
    /// forgotten by a path that does not exist yet.
    ///
    /// <para/><b>Cost.</b> One walk of the live roster per settlement every <see cref="GenTicks.TickLongInterval"/>
    /// ticks — 30 walks per in-game day, each citizen costing a <see cref="TickList.Contains"/> probe (a modulo
    /// and a scan of one bucket, which holds <c>population / interval</c> entries) per list. It never touches
    /// <see cref="World.Settlement.StatisticalPopulation"/>, the bare cohort count with no <c>Pawn</c> behind
    /// each person, so it is O(citizens above Statistical) and never O(population) — the same guarantee
    /// <see cref="God.AttentionManager.Reconcile"/> and <see cref="God.GodRollup"/> already make. Deliberately
    /// a second pass rather than folded into that sweep: the two answer different questions (who is
    /// significant, versus who is actually being ticked), and the walk itself is a rounding error next to the
    /// coarse ticks it enables.
    /// </summary>
    public static class CitizenTickRegistry
    {
        /// <summary>The three lists a citizen could be on. <see cref="TickerType.Never"/> has no list, and is
        /// used here only as the "belongs on none of them" answer for a citizen who has died.</summary>
        private static readonly TickerType[] AllLists = { TickerType.Normal, TickerType.Rare, TickerType.Long };

        /// <summary>
        /// Wired into <see cref="Game"/>'s post-tickers and self-gated to <see cref="GenTicks.TickLongInterval"/>,
        /// the same cadence the coarse tick runs on — a citizen registered here waits at most one coarse
        /// interval for their first tick, which is exactly the wait every already-registered coarse citizen
        /// serves anyway. A tick it is not due on costs a modulo.
        /// </summary>
        public static void Tick()
        {
            TickManager? tm = Find.TickManager;
            if (tm == null) return;
            if (tm.TicksGame % GenTicks.TickLongInterval != 0) return;
            Reconcile();
        }

        /// <summary>
        /// Brings every settlement's live roster into agreement with the tick lists, and returns how many
        /// citizens had to be moved — zero on a settled world, which is what makes this safe to run for ever.
        /// Idempotent: it reads <see cref="Pawn.TickerType"/> and list membership and writes nothing else, so
        /// running it twice is the second run agreeing with the first.
        /// </summary>
        public static int Reconcile()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return 0;

            TickManager tm = Find.TickManager;
            int moved = 0;

            IReadOnlyList<SimWorld.World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is SimWorld.World.Settlement settlement)) continue;

                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int c = 0; c < citizens.Count; c++)
                {
                    if (ReconcileCitizen(tm, citizens[c])) moved++;
                }
            }

            return moved;
        }

        /// <summary>
        /// One citizen: on the list <see cref="Pawn.TickerType"/> names and on no other.
        ///
        /// <para/>The dead are taken off every list instead. A citizen who dies off-map leaves no body —
        /// <see cref="Things.CorpseMaker.MakeAndSpawnCorpseFor"/> returns null when there is no map to lay one
        /// on, which its own doc calls the ordinary case for this port's off-map citizens — so they are never
        /// <see cref="Things.Thing.DeSpawn"/>ed and never <see cref="Things.Thing.Destroyed"/>, and neither
        /// <see cref="TickList"/>'s own destroyed-entry sweep nor anything else would ever drop them. Their
        /// settlement removes them from the roster on its next
        /// <see cref="World.Settlement.SyncCitizenSpawns"/>, after which they are unreachable from here, so the
        /// removal has to happen while they are still on it.
        /// </summary>
        private static bool ReconcileCitizen(TickManager tm, Pawn citizen)
        {
            TickerType want = citizen.Dead ? TickerType.Never : citizen.TickerType;
            bool changed = false;

            for (int i = 0; i < AllLists.Length; i++)
            {
                TickList? list = tm.TickListFor(AllLists[i]);
                if (list == null) continue;

                bool listed = list.Contains(citizen);
                if (AllLists[i] == want)
                {
                    if (!listed)
                    {
                        list.RegisterThing(citizen);
                        changed = true;
                    }
                }
                else if (listed)
                {
                    list.DeregisterThing(citizen);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
