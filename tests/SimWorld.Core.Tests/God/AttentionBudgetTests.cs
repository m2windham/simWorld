using System.Collections.Generic;
using System.Linq;

using SimWorld.God;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The Full-tier cap inside the focused settlement (<see cref="AttentionBudget"/>,
    /// <c>docs/spec/simworld-spec.md</c> §11.3/§11.5). <see cref="AttentionTests"/> proves attention bounds
    /// Full tier to one settlement's roster; this file is about the half that bound left open — that roster
    /// grows, so the bound held in space and not in time.
    /// <para/>
    /// Every assertion here is written against <see cref="TieringTuning.FullTierBudget"/> rather than the
    /// number it currently holds, so retuning the budget retunes the tests with it and nothing here pins a
    /// literal anyone would have to remember to change.
    /// <para/>
    /// The load-bearing ones are
    /// <see cref="No_citizen_changes_tier_on_two_consecutive_sweeps_of_a_stable_roster"/> — a cap that
    /// promoted and demoted the same citizen on alternating sweeps would be worse than no cap, since every
    /// promotion pays catch-up — and
    /// <see cref="A_century_of_demography_holds_the_Full_tier_flat_instead_of_doubling_it"/>, the measurement
    /// that motivated the lane.
    /// </summary>
    [Collection("GlobalDefs")]
    public class AttentionBudgetTests : ContentTestBase
    {
        public AttentionBudgetTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static CoreWorld NewWorld(string seed) =>
            WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 3, soloStart: true);

        /// <summary>Founds a real band on the first free land tile — the same shape
        /// <c>AttentionTests.FoundLiveBands</c> uses, and the only one that puts live <c>Pawn</c>s on a
        /// roster at all.</summary>
        private static Settlement Found(CoreWorld world, int seed, string name, int bandSize = 20)
        {
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            for (int tile = 0; tile < world.grid.TilesCount; tile++)
            {
                if (taken.Contains(tile) || world.grid.Tiles[tile].WaterCovered) continue;
                return SettlementFounder.Found(world, tile, world.factions[0], bandSize, new RandomStream(seed), name);
            }

            Assert.Fail("no free land tile for a settlement");
            return null!;
        }

        /// <summary>
        /// A settlement whose live roster overruns the budget by <paramref name="overBy"/>. The overflow is
        /// plain <see cref="ContentTestBase.NewHuman"/> citizens rather than a century of demography: what the
        /// cap reads is the roster and the four significance flags, and growing the roster for real is the
        /// separate, slower thing
        /// <see cref="A_century_of_demography_holds_the_Full_tier_flat_instead_of_doubling_it"/> does.
        /// Ids land in creation order, so the founding band is senior to every filler.
        /// </summary>
        private static Settlement OverBudget(CoreWorld world, int overBy, int seed, string name)
        {
            Settlement settlement = Found(world, seed, name);
            while (settlement.Citizens.Count < TieringTuning.FullTierBudget + overBy)
            {
                settlement.AddCitizen(NewHuman("Citizen"));
            }
            return settlement;
        }

        private static List<int> SeatedIds(Settlement settlement) =>
            settlement.Citizens.Where(c => c.tier.Tier == PawnTier.Full)
                               .Select(c => c.thingIDNumber)
                               .OrderBy(id => id)
                               .ToList();

        private static List<Pawn> HeldBack(Settlement settlement) =>
            settlement.Citizens.Where(c => c.tier.AttentionWithheld).ToList();

        // ---- the cap holds ----

        [Fact]
        public void A_settlement_larger_than_the_budget_holds_exactly_the_budget_at_Full()
        {
            CoreWorld world = NewWorld("budget-holds");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: 60, seed: 900, name: "Capitol");

            // "Before": founding leaves the whole roster Full, which is the unbounded state this lane exists
            // to close — attention alone would have kept every one of them there.
            Assert.True(home.Citizens.Count > TieringTuning.FullTierBudget);
            Assert.Equal(home.Citizens.Count, home.PopulationOf(PawnTier.Full));

            Assert.True(Find.God.Attention.Focus(home));

            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));
            Assert.Equal(home.Citizens.Count - TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Interval));

            // Held back, not thrown away. Every citizen is still a real Pawn on the roster, and not one of
            // them was spilled into StatisticalPopulation — the bare cohort count that has no Pawn behind a
            // person at all, and from which nobody comes back as themselves.
            Assert.Equal(0, home.StatisticalPopulation);
            Assert.Equal(0, home.PopulationOf(PawnTier.Statistical));
            List<Pawn> held = HeldBack(home);
            Assert.Equal(home.Citizens.Count - TieringTuning.FullTierBudget, held.Count);
            Assert.All(held, p => Assert.Equal(PawnTier.Interval, p.tier.Tier));

            // Attention is still attention: the held-back citizens' settlement is open and they know it. It
            // is only the budget that is refusing them a seat.
            Assert.All(home.Citizens, p => Assert.True(p.tier.Attending));

            // The periodic sweep reaches the same seating the immediate focus path did.
            Find.God.Attention.Reconcile();
            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));
        }

        [Fact]
        public void A_settlement_inside_the_budget_is_untouched_by_it()
        {
            CoreWorld world = NewWorld("budget-inside");
            Find.World = world;
            Settlement home = Found(world, 901, "Smallhome");

            Assert.True(Find.God.Attention.Focus(home));

            Assert.Equal(home.Citizens.Count, home.PopulationOf(PawnTier.Full));
            Assert.All(home.Citizens, p => Assert.False(p.tier.AttentionWithheld));
            Assert.All(home.Citizens, p => Assert.True(p.tier.Significant));
        }

        // ---- who is held back, and who never is ----

        [Fact]
        public void The_citizens_held_back_are_the_least_significant_by_the_ordering_not_an_arbitrary_slice()
        {
            CoreWorld world = NewWorld("budget-ordering");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: 60, seed: 902, name: "Ranked");

            // The three newest citizens in the settlement — dead last on the tie-break, so nothing but their
            // rank can seat them. One of each of the three reasons that outrank attention.
            List<Pawn> byId = home.Citizens.OrderBy(p => p.thingIDNumber).ToList();
            Pawn leader = byId[byId.Count - 1];
            Pawn remembered = byId[byId.Count - 2];
            Pawn kin = byId[byId.Count - 3];
            leader.tier.Notify_RoleChanged(true);
            remembered.tier.Notify_ChronicleNamed();
            kin.tier.Notify_RelatedToPromoted(true);

            Find.God.Attention.Focus(home);

            Assert.Equal(PawnTier.Full, leader.tier.Tier);
            Assert.Equal(PawnTier.Full, remembered.tier.Tier);
            Assert.Equal(PawnTier.Full, kin.tier.Tier);
            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));

            // Nobody significant for a reason of their own was held back: every held-back citizen is at the
            // bottom rank, significant only because someone is looking at their settlement.
            List<Pawn> held = HeldBack(home);
            Assert.NotEmpty(held);
            Assert.All(held, p => Assert.Equal(3, AttentionBudget.RankOf(p.tier)));

            // And within that bottom rank the cut is the ordering, not a slice of the list: every held-back
            // citizen is junior to every seated one.
            int mostJuniorSeated = home.Citizens
                .Where(p => p.tier.Tier == PawnTier.Full && AttentionBudget.RankOf(p.tier) == 3)
                .Max(p => p.thingIDNumber);
            Assert.All(held, p => Assert.True(
                p.thingIDNumber > mostJuniorSeated,
                "a held-back citizen was senior to a seated one — the cut is not the ordering"));
        }

        [Fact]
        public void A_leader_is_never_displaced_by_sheer_numbers()
        {
            CoreWorld world = NewWorld("budget-leader");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: TieringTuning.FullTierBudget * 2, seed: 903, name: "Throne");

            // Three times the budget, and the leader is the very last person to arrive: on the tie-break
            // alone they would be the last citizen in the settlement to get a seat.
            Pawn leader = home.Citizens.OrderBy(p => p.thingIDNumber).Last();
            leader.tier.Notify_RoleChanged(true);

            Find.God.Attention.Focus(home);
            Assert.Equal(PawnTier.Full, leader.tier.Tier);
            Assert.False(leader.tier.AttentionWithheld);

            // Still true after the roster grows again by another whole budget's worth of newcomers, and after
            // the focus has come and gone.
            for (int i = 0; i < TieringTuning.FullTierBudget; i++) home.AddCitizen(NewHuman("Latecomer"));
            Find.God.Attention.Reconcile();
            Assert.Equal(PawnTier.Full, leader.tier.Tier);

            Find.God.Attention.ClearFocus();
            Assert.Equal(PawnTier.Full, leader.tier.Tier); // Full for a reason the camera has no say in
            Find.God.Attention.Focus(home);
            Assert.Equal(PawnTier.Full, leader.tier.Tier);
        }

        // ---- the cap is a demotion constraint, never a promotion clock ----

        [Fact]
        public void A_held_back_citizen_who_had_already_settled_stays_Statistical_rather_than_being_lifted()
        {
            CoreWorld world = NewWorld("budget-no-promotion-clock");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: 60, seed: 904, name: "Deeptown");

            // The state an unattended settlement reaches as a matter of course: everyone settled to
            // Statistical while the god was looking elsewhere.
            foreach (Pawn p in home.Citizens)
            {
                p.tier.Notify_AttentionChanged(false);
                p.tier.DemoteToStatistical();
            }
            Assert.Equal(home.Citizens.Count, home.PopulationOf(PawnTier.Statistical));

            Find.God.Attention.Focus(home);

            // The seated go straight to Full, skipping Interval, exactly as §11.3's flowchart has it. The
            // held-back are not lifted anywhere: the cap decides whose attention counts, it never promotes,
            // so a citizen it cannot seat stays at precisely the tier they were already at.
            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));
            Assert.All(HeldBack(home), p => Assert.Equal(PawnTier.Statistical, p.tier.Tier));
            Assert.Equal(0, home.PopulationOf(PawnTier.Interval));
        }

        // ---- the anti-thrash test ----

        [Fact]
        public void No_citizen_changes_tier_on_two_consecutive_sweeps_of_a_stable_roster()
        {
            CoreWorld world = NewWorld("budget-no-thrash");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: 120, seed: 905, name: "Steady");
            Find.God.Attention.Focus(home);

            Dictionary<int, PawnTier> after = home.Citizens.ToDictionary(p => p.thingIDNumber, p => p.tier.Tier);

            // Sweeps spanning well past the settle threshold, so this also pins that the settle policy never
            // reaches into an *open* settlement: a citizen the budget held back is insignificant by the
            // ordering, and a year of that would sink them into the cohort if the policy did not key off the
            // settlement being unattended.
            int ticks = Find.TickManager.TicksGame;
            for (int sweep = 0; sweep < 6; sweep++)
            {
                ticks += TieringTuning.IntervalSettleTicks / 2;
                Find.TickManager.DebugSetTicksGame(ticks);
                Find.God.Attention.Reconcile();

                foreach (Pawn p in home.Citizens)
                {
                    Assert.Equal(after[p.thingIDNumber], p.tier.Tier);
                }
            }

            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));
            Assert.Equal(0, home.PopulationOf(PawnTier.Statistical));
        }

        [Fact]
        public void Growth_adds_newcomers_below_the_cut_rather_than_reshuffling_the_seated()
        {
            CoreWorld world = NewWorld("budget-growth-stable");
            Find.World = world;
            Settlement home = OverBudget(world, overBy: 20, seed: 906, name: "Growtown");
            Find.God.Attention.Focus(home);

            List<int> seatedBefore = SeatedIds(home);

            // Four waves of newcomers, each a fifth of the budget — the shape demography produces, if faster.
            // Because the tie-break is creation order, every newcomer sorts below every incumbent, so the
            // seated set cannot be reshuffled by growth alone. A tie-break that moved with the population
            // (anything hashed, anything re-drawn) would fail here.
            for (int wave = 0; wave < 5; wave++)
            {
                for (int i = 0; i < TieringTuning.FullTierBudget / 5; i++) home.AddCitizen(NewHuman("Newborn"));
                Find.God.Attention.Reconcile();

                Assert.Equal(seatedBefore, SeatedIds(home));
                Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));
            }

            Assert.True(home.Citizens.Count > TieringTuning.FullTierBudget * 2);
        }

        // ---- determinism ----

        [Fact]
        public void The_same_roster_seats_the_same_citizens_across_two_runs()
        {
            CoreWorld world = NewWorld("budget-determinism");
            Find.World = world;

            Settlement first = OverBudget(world, overBy: 60, seed: 907, name: "Alpha");
            Find.God.Attention.Focus(first);
            List<int> firstSeats = SeatedIds(first);

            // Captured while Alpha still holds the focus: moving it to Beta below un-attends Alpha, and a
            // settlement nobody is looking at has no seating to compare.
            List<int> firstPositions = SeatPositions(first);

            // Run 2a: the same roster, re-decided from scratch. Clearing the focus drops everyone to
            // Interval, so the second application starts from a different tier distribution than the first
            // did and must still reach the same answer — which is the property that makes the ordering a
            // function of the citizens rather than of the sweep history.
            Find.God.Attention.ClearFocus();
            Assert.Equal(0, first.PopulationOf(PawnTier.Full));
            Find.God.Attention.Focus(first);
            Assert.Equal(firstSeats, SeatedIds(first));

            // Run 2b: an independently built settlement with the same founding seed and the same roster size,
            // in the same world. Its citizens carry different ids (the allocator kept counting), so the two
            // are compared by seat *position* on the roster — the decision itself, with the id values it was
            // made from factored out.
            Settlement second = OverBudget(world, overBy: 60, seed: 907, name: "Beta");
            Find.God.Attention.Focus(second);

            List<int> secondPositions = SeatPositions(second);
            Assert.Equal(firstPositions, secondPositions);
            Assert.Equal(TieringTuning.FullTierBudget, secondPositions.Count);
        }

        private static List<int> SeatPositions(Settlement settlement)
        {
            List<Pawn> byId = settlement.Citizens.OrderBy(p => p.thingIDNumber).ToList();
            var positions = new List<int>();
            for (int i = 0; i < byId.Count; i++)
            {
                if (byId[i].tier.Tier == PawnTier.Full) positions.Add(i);
            }
            return positions;
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trips_the_seating_and_a_sweep_on_the_loaded_game_changes_nothing()
        {
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, "budget-scribe", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement home = game.World!.worldObjects.OfType<Settlement>().First();
            while (home.Citizens.Count < TieringTuning.FullTierBudget + 30) home.AddCitizen(NewHuman("Citizen"));

            // A leader among the juniors, so the save has to carry more than "the first N ids".
            Pawn leader = home.Citizens.OrderBy(p => p.thingIDNumber).Last();
            leader.tier.Notify_RoleChanged(true);

            RunTicks(1); // the real loop: Game.WireTickHooks -> GodManager.GodTick -> AttentionManager.Reconcile
            Assert.Equal(TieringTuning.FullTierBudget, home.PopulationOf(PawnTier.Full));

            List<int> seatedBefore = SeatedIds(home);
            List<int> withheldBefore = home.Citizens.Where(p => p.tier.AttentionWithheld)
                                                   .Select(p => p.thingIDNumber).OrderBy(id => id).ToList();
            int tile = home.tile;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement loadedHome = loaded.World!.worldObjects.OfType<Settlement>().Single(s => s.tile == tile);
            Assert.Equal(seatedBefore, SeatedIds(loadedHome));
            Assert.Equal(withheldBefore,
                loadedHome.Citizens.Where(p => p.tier.AttentionWithheld).Select(p => p.thingIDNumber).OrderBy(id => id).ToList());
            Assert.Equal(TieringTuning.FullTierBudget, loadedHome.PopulationOf(PawnTier.Full));

            // The withholding came back as withholding, not as "nobody is looking": a loaded game that had
            // forgotten the difference would re-promote the overflow on its next sweep.
            Assert.All(loadedHome.Citizens, p => Assert.True(p.tier.Attending));

            // And the load is a fixed point of the sweep — the strongest form of "it does not thrash", since
            // a save boundary is exactly where a cap rebuilt from partial state would re-cut the roster.
            loaded.God.Attention.Reconcile();
            Assert.Equal(seatedBefore, SeatedIds(loadedHome));
        }

        // ---- the measurement that motivated the lane ----

        /// <summary>
        /// A century of real demography on the settlement the god is watching. Before the cap the Full-tier
        /// roster was the settlement's whole live population, which doubles roughly every 18 years and crossed
        /// §11.3's measured Full ceiling well inside a century. The population still grows here; what stops
        /// growing is the part that costs a full tick.
        /// <para/>
        /// Run over several founding seeds, and that is not thoroughness for its own sake. A century of
        /// compounding births is chaotic in the founding band's random stream: measured across eight founding
        /// seeds the final roster ranges from about 100 to about 1,500, and adding a single new TraitDef —
        /// content that cannot affect demography at all, and only moves where each founder's rolls land in
        /// that stream — moved one seed's century from 1,132 people to 624 and another's from 1,404 to 1,575.
        /// This test used to assert its premise ("a century really does outgrow the budget") against one seed,
        /// and so was one unrelated content addition away from failing for a reason that had nothing to do
        /// with the cap it exists to prove. The premise is now asked of the set: some century outgrows the
        /// budget several times over. The conclusion — the Full tier never exceeds the budget, and goes flat
        /// once it reaches it — is asked of every one of them, which is strictly more than before.
        /// <para/>
        /// Years are advanced with the same <c>AgeTickMothballed</c> shortcut <c>SettlementTests</c> and
        /// <c>DemographyTests</c> use rather than by single-stepping 6,000,000 ticks, and
        /// <c>SyncCitizenSpawns</c> is called each year for the pruning half of what <c>Settlement.Tick</c>
        /// would do (a settlement with no interior map has nothing else to sync) — without it the roster keeps
        /// counting the dead, who are no longer anybody's simulation cost.
        /// </summary>
        [Fact]
        public void A_century_of_demography_holds_the_Full_tier_flat_instead_of_doubling_it()
        {
            List<CenturyRun> runs = new[] { 905, 906, 907, 908, 909 }.Select(RunOneCentury).ToList();

            // The conclusion, asked of every century: the Full tier never overran the budget, nobody was
            // spilled into the bare Statistical cohort, and the roster only ever grew.
            foreach (CenturyRun run in runs)
            {
                Assert.True(run.PeakFull <= TieringTuning.FullTierBudget,
                    $"seed {run.Seed}: the Full tier overran the budget at some point in the century (peak {run.PeakFull})");
                Assert.Equal(0, run.StatisticalPopulation);
                Assert.Equal(run.FinalRoster, run.TotalPopulation);
                Assert.True(run.FinalRoster > run.StartingRoster,
                    $"seed {run.Seed}: expected a century to leave more people than it started with; {run.StartingRoster} -> {run.FinalRoster}");
            }

            // The premise of the lane, asked of the set rather than of one seed: a century of demography really
            // does outgrow the budget several times over. If this ever fails for every seed at once, demography
            // changed and the rest of this test proves nothing.
            CenturyRun biggest = runs.OrderByDescending(r => r.FinalRoster).First();
            Assert.True(biggest.FinalRoster > TieringTuning.FullTierBudget * 2,
                "expected at least one century to grow the roster well past the budget; got "
                + string.Join(", ", runs.Select(r => r.Seed + ":" + r.FinalRoster)));
            Assert.True(biggest.FinalRoster > biggest.StartingRoster * 8,
                $"expected demographic growth over a century; {biggest.StartingRoster} -> {biggest.FinalRoster}");

            // Flat, not merely capped: in every century that reached the budget, the Full count stops moving
            // there while the roster behind it does not.
            int reachedBudget = 0;
            foreach (CenturyRun run in runs)
            {
                int firstAtBudget = run.FullByDecade.FindIndex(n => n == TieringTuning.FullTierBudget);
                if (firstAtBudget < 0) continue;
                reachedBudget++;

                for (int i = firstAtBudget; i < run.FullByDecade.Count; i++)
                {
                    Assert.Equal(TieringTuning.FullTierBudget, run.FullByDecade[i]);
                }
                Assert.True(run.RosterByDecade[run.RosterByDecade.Count - 1] > run.RosterByDecade[firstAtBudget],
                    $"seed {run.Seed}: expected the roster to outgrow the budget it was pinned to");
            }
            Assert.True(reachedBudget > 0, "no century reached the budget — see the premise above");
        }

        /// <summary>What one century leaves behind, so the assertions can be made over the set of them.</summary>
        private sealed class CenturyRun
        {
            public int Seed;
            public int StartingRoster;
            public int FinalRoster;
            public int TotalPopulation;
            public int StatisticalPopulation;
            public int PeakFull;
            public List<int> FullByDecade = new List<int>();
            public List<int> RosterByDecade = new List<int>();
        }

        /// <summary>
        /// One settlement, founded and then aged a hundred years. Resets the thread-static services first, the
        /// same way <see cref="ContentTestBase"/>'s constructor does, so each century is genuinely independent
        /// of the one before it rather than inheriting a focused settlement and a spent clock.
        /// </summary>
        private static CenturyRun RunOneCentury(int seed)
        {
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();

            CoreWorld world = NewWorld("budget-century-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Find.World = world;
            Settlement home = Found(world, seed, "Century", bandSize: 24);
            Find.God.Attention.Focus(home);

            var run = new CenturyRun
            {
                Seed = seed,
                StartingRoster = home.Citizens.Count,
                PeakFull = home.PopulationOf(PawnTier.Full),
            };
            Assert.Equal(run.StartingRoster, home.PopulationOf(PawnTier.Full));

            int tick = 0;
            for (int year = 1; year <= 100; year++)
            {
                foreach (Pawn p in home.Citizens.ToList())
                {
                    if (!p.Dead) p.ageTracker.AgeTickMothballed(DemographyTuning.DemographyIntervalTicks);
                }

                tick += DemographyTuning.DemographyIntervalTicks;
                Find.TickManager.DebugSetTicksGame(tick);
                home.GrowthTick();
                home.SyncCitizenSpawns();
                Find.God.Attention.Reconcile();

                run.PeakFull = System.Math.Max(run.PeakFull, home.PopulationOf(PawnTier.Full));
                if (year % 10 == 0)
                {
                    run.FullByDecade.Add(home.PopulationOf(PawnTier.Full));
                    run.RosterByDecade.Add(home.Citizens.Count);
                }
            }

            run.FinalRoster = home.Citizens.Count;
            run.TotalPopulation = home.TotalPopulation;
            run.StatisticalPopulation = home.StatisticalPopulation;
            return run;
        }
    }
}
