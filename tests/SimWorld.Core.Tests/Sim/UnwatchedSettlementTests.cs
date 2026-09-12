using System.Collections.Generic;
using System.Linq;

using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// A settlement nobody is standing in still lives (<c>docs/spec/simworld-spec.md</c> §11.1/§11.3).
    ///
    /// <para/><b>The measurement these tests exist to hold.</b> <c>Game.NewGame(TribalStart, 25 founders)</c>,
    /// never entered, ticked six in-game days: mean food 0.80 and mean mood 0.50 on day zero, and <i>the same
    /// two numbers to two decimal places</i> on day six. Nothing moved at all — and not because the Statistical
    /// tier was doing its job: those 25 citizens were <see cref="PawnTier.Full"/>, and all three tick lists were
    /// empty.
    ///
    /// <para/><b>Where the tick did not reach.</b> Tick-list membership was granted in exactly one place,
    /// <c>Things.Thing.SpawnSetup</c>, and revoked in exactly one, <c>Things.Thing.DeSpawn</c>. That is right
    /// for a <c>Thing</c> — on a map or nowhere — and wrong for a <c>Pawn</c>, whose tiers are designed to exist
    /// off every map: §11.3 has Interval and Statistical sit on the Long bucket precisely so that "a population
    /// that is never promoted" still ages and still dies. A settlement nobody has opened has no interior map, so
    /// none of its citizens is spawned, so none of them is on any list, <i>at any tier</i> — and so
    /// <c>Pawn.Tick</c>, <c>Pawn.TickLong</c>, and through the latter <c>Pawn_TierTracker.CoarseTick</c>,
    /// <c>Need.NeedIntervalBulk</c> and <c>Pawn_AgeTracker.AgeTickMothballed</c>, were all unreachable. The
    /// tiering module built the dispatch and the work; nothing ever built the membership.
    /// <c>Sim.CitizenTickRegistry</c> is that membership.
    ///
    /// <para/><b>What these tests do not assert.</b> Not that a settlement <i>thrives</i>. Nothing here fixes
    /// the food economy — an unopened settlement has no map, so no crop, no hunt and no meal, and its citizens'
    /// food falls and does not come back. That is the other half of the register's §9 and somebody else's lane.
    /// Every assertion below is a direction, a list, or a round-trip, never a level, so none of them goes red
    /// when food starts working underneath them.
    /// </summary>
    public class UnwatchedSettlementTests : ContentTestBase
    {
        public UnwatchedSettlementTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Game NewSoloGame(string seed, int bandSize = 25) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: bandSize);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        private static void TickDays(Game game, int days) => TickFor(game, days * GenDate.TicksPerDay);

        private static void TickFor(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.TickManager.DoSingleTick();
        }

        private static float MeanNeed(IEnumerable<Pawn> citizens, NeedDef def)
        {
            var live = citizens.Where(p => !p.Dead).ToList();
            return live.Count == 0 ? 0f : live.Average(p => p.needs.AllNeeds.First(n => n.def == def).CurLevelPercentage);
        }

        // ---- the measurement ----

        /// <summary>
        /// The register's own probe, turned around: six in-game days of a settlement founded and never entered,
        /// asserting that something actually changed. Written against <i>movement</i> rather than any particular
        /// level — the direction of travel is what the tick path owns, and a level would be pinning the food
        /// economy, which is not this lane's.
        /// </summary>
        [Fact]
        public void An_unentered_settlement_is_not_a_photograph()
        {
            Game game = NewSoloGame("unwatched-lives");
            Settlement settlement = PlayerSettlement(game);
            Assert.Null(settlement.InteriorMap); // never entered: there is no map, and nobody is standing on one
            var band = settlement.Citizens.ToList();
            Assert.All(band, p => Assert.False(p.Spawned));

            float foodBefore = MeanNeed(band, NeedDefOf.Food);
            float moodBefore = MeanNeed(band, NeedDefOf.Mood);
            long ageBefore = band[0].ageTracker.ageBiologicalTicks;

            TickDays(game, 6);

            Assert.Contains(band, p => !p.Dead);
            // Changed, not fallen. This read "< foodBefore" until Economy.SettlementLarder gave an off-map
            // citizen something to eat (Settlement.Stores, the abstract consumption path this lane's own doc
            // called for): a fed town's mean nutrition now swings above its founding level as well as below
            // it, so a one-way assertion was pinning "nobody is feeding them" rather than "the clock runs".
            // Movement is what the tick path owns, which is what this asserts.
            Assert.NotEqual(foodBefore, MeanNeed(band, NeedDefOf.Food));
            Assert.NotEqual(moodBefore, MeanNeed(band, NeedDefOf.Mood));
            Assert.True(band[0].ageTracker.ageBiologicalTicks > ageBefore, "nobody aged a tick");
        }

        /// <summary>
        /// The same for a settlement that is not even in focus — the resting state of every rival civilization,
        /// whose bands <c>SettlementFounder.Found</c> creates Full and insignificant and the attention sweep
        /// demotes to Interval (§5b.4, §11.3). Before this lane they demoted correctly and then never ticked
        /// again: the demotion moved them off a list they had never been on, so <c>NeedIntervalBulk</c> — a
        /// closed form written for exactly this — had no caller in a running game.
        /// </summary>
        [Fact]
        public void A_settlement_out_of_focus_advances_at_Interval_tier()
        {
            Game game = NewSoloGame("unwatched-nofocus");
            Settlement settlement = PlayerSettlement(game);
            game.God.Attention.ClearFocus();
            var band = settlement.Citizens.ToList();

            float foodBefore = MeanNeed(band, NeedDefOf.Food);
            long ageBefore = band[0].ageTracker.ageBiologicalTicks;

            TickDays(game, 1);

            Assert.Contains(band, c => c.tier.Tier == PawnTier.Interval);
            Assert.True(MeanNeed(band, NeedDefOf.Food) < foodBefore);
            Assert.True(band[0].ageTracker.ageBiologicalTicks > ageBefore);
            Assert.All(band.Where(c => c.tier.Tier == PawnTier.Interval && !c.Dead),
                c => Assert.True(game.TickManager.TickListFor(TickerType.Long)!.Contains(c)));
        }

        /// <summary>
        /// Age is the one process the coarse path reproduces <i>exactly</i> at any gap size
        /// (<c>Pawn_AgeTracker.AgeTickMothballed</c>), so it is the sharpest available statement of §11.1's rule
        /// that "the abstract clock may only do what the real clock would have done": six days at Interval must
        /// age a citizen by six days' worth of ticks, not approximately and not a bucket short. A part-interval
        /// at each end is the granularity the Long bucket works in, not an error term, hence the one-interval
        /// band rather than an equality.
        /// </summary>
        [Fact]
        public void Ageing_at_Interval_lands_where_ticking_would_have()
        {
            Game game = NewSoloGame("unwatched-ageing");
            Settlement settlement = PlayerSettlement(game);
            game.God.Attention.ClearFocus();

            TickDays(game, 1); // settle the tiers and the registration before measuring
            Pawn citizen = settlement.Citizens.First(c => c.tier.Tier == PawnTier.Interval && !c.Dead);

            long ageAt = citizen.ageTracker.ageBiologicalTicks;
            int tickAt = game.TickManager.TicksGame;

            TickDays(game, 6);
            Assert.False(citizen.Dead, "this test needs a citizen who lives through the span it measures");

            long aged = citizen.ageTracker.ageBiologicalTicks - ageAt;
            int elapsed = game.TickManager.TicksGame - tickAt;
            Assert.InRange(aged, elapsed - GenTicks.TickLongInterval, elapsed);
        }

        // ---- which list a citizen is actually on ----

        /// <summary>
        /// §11.3's "dispatch by tick list, not a skip inside one", asserted from the list side. The tier decides
        /// the list and nothing else does: the focused settlement's roster is Full and therefore on the per-tick
        /// list even though it has no interior to stand on, and an unfocused settlement's roster is Interval and
        /// therefore on the coarse one. The registry obeys the tier; it never sets one.
        /// </summary>
        [Fact]
        public void The_tier_decides_the_list_whether_or_not_there_is_a_map()
        {
            Game game = NewSoloGame("unwatched-lists");
            Settlement settlement = PlayerSettlement(game);
            TickManager tm = game.TickManager;

            TickDays(game, 1);

            var live = settlement.Citizens.Where(c => !c.Dead).ToList();
            Assert.NotEmpty(live);
            Assert.All(live, c => Assert.False(c.Spawned));
            Assert.All(live.Where(c => c.tier.Tier == PawnTier.Full),
                c => Assert.True(tm.TickListFor(TickerType.Normal)!.Contains(c)));

            // Step back to civilization scope: the same citizens, the same absence of a map, the other list.
            game.God.Attention.ClearFocus();
            TickFor(game, GenTicks.TickLongInterval);

            var stillLive = settlement.Citizens.Where(c => !c.Dead).ToList();
            Assert.All(stillLive.Where(c => c.tier.Tier != PawnTier.Full),
                c => Assert.True(tm.TickListFor(TickerType.Long)!.Contains(c)));
            Assert.All(stillLive.Where(c => c.tier.Tier != PawnTier.Full),
                c => Assert.False(tm.TickListFor(TickerType.Normal)!.Contains(c)));
        }

        /// <summary>
        /// The path the register's <i>other</i> column takes: a settlement that was watched, entered, and then
        /// stepped away from. The demotion takes the citizen off the per-tick list and
        /// <c>Settlement.SyncCitizenSpawns</c> then takes them off the map, which — before this lane — took them
        /// off the coarse list the demotion had just put them on, leaving them ticking on nothing at all. This is
        /// the one case where the old incremental path actively undid itself.
        /// </summary>
        [Fact]
        public void A_settlement_the_player_walks_away_from_keeps_ticking_coarsely()
        {
            Game game = NewSoloGame("unwatched-walk-away", bandSize: 20);
            Settlement settlement = PlayerSettlement(game);
            game.EnterSettlement(settlement);
            TickFor(game, GenTicks.TickLongInterval);
            Assert.Contains(settlement.Citizens, c => c.Spawned);

            game.God.Attention.ClearFocus();
            // Long enough for the demotion, the map sync that despawns the demoted, and a registry sweep.
            TickDays(game, 1);

            TickManager tm = game.TickManager;
            var demoted = settlement.Citizens.Where(c => !c.Dead && c.tier.Tier != PawnTier.Full).ToList();
            Assert.NotEmpty(demoted);
            Assert.All(demoted, c => Assert.False(c.Spawned));
            Assert.All(demoted, c => Assert.True(tm.TickListFor(TickerType.Long)!.Contains(c)));

            float foodBefore = MeanNeed(demoted, NeedDefOf.Food);
            long ageBefore = demoted[0].ageTracker.ageBiologicalTicks;
            TickDays(game, 1);
            Assert.True(demoted[0].ageTracker.ageBiologicalTicks > ageBefore);
            Assert.True(MeanNeed(demoted, NeedDefOf.Food) <= foodBefore);
        }

        // ---- the watched settlement is untouched ----

        /// <summary>
        /// The thing most at risk from giving a second caller a say over tick-list membership: a citizen the
        /// registry has already seated being spawned onto a freshly generated interior, which is the one moment
        /// the two callers overlap. A duplicate entry there would tick that pawn twice for the rest of the game.
        /// Measured on the age clock, which advances exactly one per tick, so two entries would read as two.
        /// </summary>
        [Fact]
        public void Opening_the_settlement_does_not_tick_its_citizens_twice()
        {
            Game game = NewSoloGame("unwatched-then-opened", bandSize: 20);
            Settlement settlement = PlayerSettlement(game);
            TickFor(game, GenTicks.TickLongInterval); // registered off-map by the sweep

            CoreMap map = game.EnterSettlement(settlement);
            Pawn citizen = settlement.Citizens.First(c => c.Spawned && !c.Dead);
            Assert.Same(map, citizen.Map);

            long ageBefore = citizen.ageTracker.ageBiologicalTicks;
            const int span = 500;
            TickFor(game, span);

            Assert.False(citizen.Dead);
            Assert.Equal(span, citizen.ageTracker.ageBiologicalTicks - ageBefore);
        }

        /// <summary>Registration is a set, not a list: asking twice is asking once. The property
        /// <see cref="Opening_the_settlement_does_not_tick_its_citizens_twice"/> depends on, stated directly
        /// against <see cref="TickList"/> so a regression names the mechanism rather than the symptom.</summary>
        [Fact]
        public void Registering_a_tickable_twice_registers_it_once()
        {
            var list = new TickList(TickerType.Normal);
            Pawn p = NewHuman();

            list.RegisterThing(p);
            list.RegisterThing(p);
            Assert.Equal(1, list.Count);

            list.DeregisterThing(p);
            Assert.Equal(0, list.Count);
            Assert.False(list.Contains(p));
        }

        // ---- lifecycle ----

        /// <summary>
        /// A citizen who dies off the map leaves no body — <c>CorpseMaker</c> returns null with no map to lay
        /// one on — so nothing ever <c>DeSpawn</c>s or destroys them, and nothing but this sweep would take them
        /// off a tick list. Their settlement drops them from the roster shortly afterwards, at which point they
        /// are unreachable, so the removal has to happen while they are still on it.
        /// </summary>
        [Fact]
        public void A_citizen_who_dies_off_the_map_leaves_the_tick_lists()
        {
            Game game = NewSoloGame("unwatched-death", bandSize: 20);
            Settlement settlement = PlayerSettlement(game);
            TickFor(game, GenTicks.TickLongInterval);

            Pawn citizen = settlement.Citizens.First(c => !c.Dead);
            TickManager tm = game.TickManager;
            Assert.True(tm.TickListFor(citizen.TickerType)!.Contains(citizen));
            Assert.False(citizen.Spawned);

            citizen.health.Kill(null, null);
            CitizenTickRegistry.Reconcile();

            Assert.False(tm.TickListFor(TickerType.Normal)!.Contains(citizen));
            Assert.False(tm.TickListFor(TickerType.Rare)!.Contains(citizen));
            Assert.False(tm.TickListFor(TickerType.Long)!.Contains(citizen));
        }

        /// <summary>
        /// The module's Scribe round trip. Tick lists are transient and a load rebuilds them from the maps
        /// alone, so nobody who was not standing on one comes back registered — which means that before this
        /// lane, saving and reloading froze a civilization even where the original had been running. The sweep is
        /// what makes a load indistinguishable from a game that never stopped.
        /// </summary>
        [Fact]
        public void A_loaded_game_starts_ticking_its_off_map_citizens_again()
        {
            Game game = NewSoloGame("unwatched-scribe", bandSize: 20);
            Settlement settlement = PlayerSettlement(game);
            TickFor(game, GenTicks.TickLongInterval);
            int tile = settlement.tile;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement loadedSettlement = loaded.World!.worldObjects.OfType<Settlement>().Single(s => s.tile == tile);
            var band = loadedSettlement.Citizens.Where(c => !c.Dead).ToList();
            Assert.NotEmpty(band);

            // A fresh load starts with nobody on any list: they are on no map, and the maps are all the rebuild
            // has to go on.
            Assert.All(band, c => Assert.False(loaded.TickManager.TickListFor(c.TickerType)!.Contains(c)));

            long ageBefore = band[0].ageTracker.ageBiologicalTicks;
            TickDays(loaded, 1);

            Assert.True(band[0].ageTracker.ageBiologicalTicks > ageBefore,
                "a loaded game left its off-map citizens frozen");
            Assert.All(band.Where(c => !c.Dead),
                c => Assert.True(loaded.TickManager.TickListFor(c.TickerType)!.Contains(c)));
        }

        /// <summary>
        /// The sweep asserts an invariant rather than performing an action, so a second run over an unchanged
        /// world has nothing to do. That is what makes it safe to wire into the tick loop for ever, and it is
        /// also the property that says membership is a function of the tier and the map alone.
        /// </summary>
        [Fact]
        public void Reconciling_a_settled_world_twice_moves_nobody_the_second_time()
        {
            Game game = NewSoloGame("unwatched-idempotent", bandSize: 20);
            TickFor(game, GenTicks.TickLongInterval);

            CitizenTickRegistry.Reconcile();
            Assert.Equal(0, CitizenTickRegistry.Reconcile());
        }
    }
}
