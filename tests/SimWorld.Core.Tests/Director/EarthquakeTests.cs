using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The EARTHQUAKE defect (tracker item <c>director.earthquake</c>): one of the three defects
    /// <c>docs/design/phase-2-pressure.md</c>'s "Known problems" names as blocked on a settlement having
    /// anything to damage — <see cref="World.Settlement.Structures"/> unblocks it. The unwatched half is the
    /// point: before this, an earthquake against a settlement with no interior map had nothing to touch, so a
    /// settlement was free to ignore it by simply never being looked at.
    ///
    /// <para/><b>Watched-map damage is direct <see cref="Thing.TakeDamage"/>, not
    /// <c>Building.RoofCollapserImmediate.DropRoofInCells</c></b> — see <see cref="IncidentWorker_Earthquake"/>'s
    /// own class doc for why that path is a poor fit here: this port's roof grid only ever covers mountain and
    /// cave cells, never a settlement's own constructed rooms.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeTests : ContentTestBase
    {
        public EarthquakeTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static IncidentDef Earthquake => DefDatabase<IncidentDef>.GetNamed("Earthquake");

        private static ThingDef Wall => DefDatabase<ThingDef>.GetNamed("Wall");

        private static ThingDef Bed => DefDatabase<ThingDef>.GetNamed("Bed");

        private static CoreMap NewMap(int size = 24) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Settlement PlainSettlement(string name = "Quaketown", int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, name, 0);

        /// <summary>A settlement with no map at all — the unwatched case, the whole reason this incident was
        /// unblocked. <paramref name="registerWithStoryteller"/> controls whether the target's pawns are
        /// visible to <c>StorytellerPawnEvents.IsCivilizationMember</c> — false reproduces "a settlement on no
        /// civilization roster" for the over-claim guard.</summary>
        private static IncidentParms OntoSettlement(Settlement settlement, bool registerWithStoryteller = false)
        {
            CivilizationTarget target = registerWithStoryteller
                ? new CivilizationTarget(Find.Storyteller)
                : new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            return new IncidentParms { target = target };
        }

        /// <summary>A watched settlement, posed the way <c>ManhunterPackTests.OntoMap</c> is — the bare
        /// <see cref="CivilizationTarget.Map"/> hook stands in for a generated interior — but with a real
        /// <see cref="Settlement"/> attached too, since this incident's Structures ledger lives there and
        /// <c>ChooseTargetSettlement</c> declines an empty roster outright.</summary>
        private static IncidentParms OntoWatchedMap(Settlement settlement, CoreMap map, bool registerWithStoryteller = false)
        {
            CivilizationTarget target = registerWithStoryteller
                ? new CivilizationTarget(Find.Storyteller)
                : new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            return new IncidentParms { target = target };
        }

        /// <summary>The same site-scoring helper <c>SettlementStructuresTests</c> uses, so a real founded
        /// settlement lands somewhere it could plausibly stand.</summary>
        private static int BestScoredTile(WorldGrid grid)
        {
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                float score = SiteScorer.Score(grid, i, weights);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static int WallsOn(CoreMap map) => map.listerThings.ThingsOfDef(Wall).Count;

        /// <summary>Spawns <paramref name="count"/> walls on empty standable cells, mirroring
        /// <c>SettlementStructuresTests.SpawnThings</c>.</summary>
        private static void SpawnWalls(CoreMap map, int count)
        {
            int placed = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (placed >= count) break;
                if (!GenGrid.Standable(c, map) || GenGrid.GetThingList(c, map).Count != 0) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(Wall), c, map);
                placed++;
            }
            Assert.Equal(count, placed); // fixture sanity: the test map must have room for this many
        }

        /// <summary>One bed with one living citizen standing exactly on its cell — the only way this incident
        /// ever finds someone to crush, since a wall is never standable.</summary>
        private static Pawn SpawnBedWithSleeper(Settlement settlement, CoreMap map, IntVec3 cell)
        {
            GenSpawn.Spawn(ThingMaker.MakeThing(Bed), cell, map);
            Pawn sleeper = NewHuman("Sleeper");
            settlement.AddCitizen(sleeper);
            GenSpawn.Spawn(sleeper, cell, map);
            return sleeper;
        }

        // ---- content ----

        [Fact]
        public void Content_loads_and_earthquake_is_wired_up()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(EarthquakeIncidentDefOf.Earthquake);
            Assert.Same(Earthquake, EarthquakeIncidentDefOf.Earthquake);
            Assert.IsType<IncidentWorker_Earthquake>(Earthquake.Worker);
        }

        // ---- the headline claim: the unwatched half ----

        [Fact]
        public void An_unwatched_settlement_loses_structures_to_a_quake()
        {
            Settlement settlement = PlainSettlement();
            settlement.AddStructure(Wall, 40);
            Find.God.Attention.ClearFocus();

            bool fired = Earthquake.Worker.TryExecute(OntoSettlement(settlement));

            Assert.True(fired);
            // The assertion a no-op worker would fail: something the settlement actually built is actually gone.
            Assert.True(settlement.StructureCount(Wall) < 40,
                "an unwatched settlement's structures survived a quake untouched — the exact bug this incident exists to fix");
        }

        [Fact]
        public void An_unwatched_settlement_with_nothing_built_still_fires_and_destroys_nothing()
        {
            Settlement settlement = PlainSettlement();
            Find.God.Attention.ClearFocus();

            bool fired = Earthquake.Worker.TryExecute(OntoSettlement(settlement));

            Assert.True(fired);
            Assert.Equal(0, settlement.StructureCount(Wall));
            Assert.Equal(0, Find.Storyteller.resourceImpact.StructuresDestroyedBy("Earthquake"));
        }

        [Fact]
        public void Both_paths_send_the_player_a_letter()
        {
            Settlement settlement = PlainSettlement();
            settlement.AddStructure(Wall, 20);
            Find.God.Attention.ClearFocus();

            Earthquake.Worker.TryExecute(OntoSettlement(settlement));

            Assert.Contains(
                Find.LetterStack.LettersListForReading,
                l => l.label.Contains("Earthquake", System.StringComparison.Ordinal));
        }

        // ---- the watched half: real Things, on a real map ----

        [Fact]
        public void A_watched_settlements_buildings_are_actually_damaged()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            SpawnWalls(map, count: 30);

            int before = WallsOn(map);
            bool fired = Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map));

            Assert.True(fired);
            Assert.True(WallsOn(map) < before, "a watched settlement's own buildings survived a quake untouched");
        }

        /// <summary>
        /// The same claim end to end, through a settlement actually entered onto its own generated interior —
        /// the shape a real game uses (<c>Sim.Game.EnterSettlement</c> -&gt; <c>Settlement.EnterMap</c>), not
        /// the bare <see cref="CivilizationTarget.Map"/> test hook the lighter test above uses. Proves
        /// <see cref="World.Settlement.StructureCount"/> — the single accessor
        /// <see cref="Director.SettlementRaidResolver"/>'s own fortification term reads — actually reflects
        /// what this incident destroyed, because in a real game they are reading the exact same
        /// <see cref="CoreMap"/> object.
        /// </summary>
        [Fact]
        public void A_watched_settlements_own_StructureCount_reflects_the_quakes_damage()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "earthquake-structurecount", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "EarthquakeTest", 2, soloStart: true);
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(21), "EnteredTown");

            CoreMap map = settlement.EnterMap(world);
            int before = settlement.StructureCount(Wall);
            SpawnWalls(map, count: 30);
            Assert.Equal(before + 30, settlement.StructureCount(Wall));

            Find.God.Attention.Focus(settlement);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });

            bool fired = Earthquake.Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);
            Assert.True(settlement.StructureCount(Wall) < before + 30,
                "a watched settlement's own StructureCount did not reflect the quake's damage to its map");
        }

        // ---- deaths: credited from the ledger's own delta, never over-claimed ----

        [Fact]
        public void A_registered_citizen_crushed_by_the_quake_is_credited_to_it()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            Pawn sleeper = SpawnBedWithSleeper(settlement, map, new IntVec3(5, 0, 5));

            bool fired = Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: true));

            Assert.True(fired);
            Assert.True(sleeper.Dead, "the fixture itself must actually kill the sleeper, or this test proves nothing");
            Assert.Equal(1, Find.Storyteller.deaths.Total);
            Assert.Equal(1, Find.Storyteller.deaths.AttributedTo("Earthquake"));
        }

        /// <summary>
        /// The self-check the brief calls for. A settlement whose <see cref="CivilizationTarget"/> was never
        /// registered with the storyteller is exactly "a settlement on no civilization roster" —
        /// <c>StorytellerPawnEvents.IsCivilizationMember</c> cannot see its citizens, so
        /// <c>Pawn_HealthTracker.Kill</c> never counts this death in <see cref="DeathLedger.Total"/> at all.
        /// The sleeper still genuinely dies; the ledger's delta-based crediting must still read zero, because
        /// an instrument may under-claim but must never claim a death the total itself does not carry. This is
        /// the exact failure <see cref="Director.SettlementRaidResolver.KillCitizens"/>'s own doc records —
        /// four kills once reported against a total of zero deaths — reproduced here for EARTHQUAKE and proven
        /// not to recur.
        /// </summary>
        [Fact]
        public void A_death_on_a_settlement_off_the_civilization_roster_is_never_over_claimed()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            Pawn sleeper = SpawnBedWithSleeper(settlement, map, new IntVec3(5, 0, 5));

            bool fired = Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: false));

            Assert.True(fired);
            Assert.True(sleeper.Dead, "the sleeper must actually die, or this test never exercises the guard at all");
            Assert.True(Find.Storyteller.deaths.Total == 0,
                "a death nobody's civilization counted must not be counted here either");
            Assert.True(Find.Storyteller.deaths.AttributedTo("Earthquake") == 0,
                "attribution exceeded what the ledger actually counted — the exact bug this guard exists to prevent");
        }

        [Fact]
        public void No_one_is_crushed_by_a_wall_because_nobody_can_stand_on_one()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            SpawnWalls(map, count: 20);

            Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: true));

            Assert.Equal(0, Find.Storyteller.deaths.Total);
        }

        // ---- the instrument: structures destroyed, credited where it acts ----

        [Fact]
        public void The_structures_destroyed_counter_is_nonzero_live_and_exactly_zero_ablated()
        {
            int DestroyedAfter(bool ablated)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                Settlement settlement = PlainSettlement("Ledger" + ablated);
                settlement.AddStructure(Wall, 40);
                Find.God.Attention.ClearFocus();

                if (ablated) Ablation.Disable("Earthquake");
                try
                {
                    bool fired = Earthquake.Worker.TryExecute(OntoSettlement(settlement));
                    Assert.True(fired, "an ablated incident must still report success, or selection itself changes");
                }
                finally
                {
                    Ablation.Clear();
                }
                return Find.Storyteller.resourceImpact.StructuresDestroyedBy("Earthquake");
            }

            int ablatedResult = DestroyedAfter(true);
            int liveResult = DestroyedAfter(false);

            Assert.Equal(0, ablatedResult);
            Assert.True(liveResult > 0, "a live earthquake must destroy a measurable number of structures");
        }

        [Fact]
        public void The_ledger_reads_zero_when_ablated_including_structures_and_deaths()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            settlement.AddStructure(Wall, 30);
            Pawn sleeper = SpawnBedWithSleeper(settlement, map, new IntVec3(5, 0, 5));

            Ablation.Disable("Earthquake");
            try
            {
                bool fired = Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: true));

                Assert.True(fired, "an ablated incident must still report success, or selection itself changes");
                Assert.Equal(30, settlement.StructureCount(Wall));
                Assert.False(sleeper.Dead, "ablated, nothing should touch anyone at all");
                Assert.Equal(0, Find.Storyteller.resourceImpact.StructuresDestroyedBy("Earthquake"));
                Assert.Equal(0, Find.Storyteller.deaths.AttributedTo("Earthquake"));
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Switching_it_back_on_restores_the_quake()
        {
            Settlement settlement = PlainSettlement();
            settlement.AddStructure(Wall, 40);
            Find.God.Attention.ClearFocus();

            Ablation.Disable("Earthquake");
            Ablation.Clear();

            Earthquake.Worker.TryExecute(OntoSettlement(settlement));

            // The harness leaving an ablation set would quietly report a disabled world as the baseline, which
            // is the one failure that makes every number downstream wrong and none of them look it.
            Assert.True(settlement.StructureCount(Wall) < 40);
        }

        [Fact]
        public void Different_firings_can_destroy_different_shares_of_what_was_built()
        {
            var destroyed = new HashSet<int>();
            for (int i = 0; i < 25; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * 999);
                Settlement settlement = PlainSettlement("Sev" + i);
                settlement.AddStructure(Wall, 200);
                Find.God.Attention.ClearFocus();

                Assert.True(Earthquake.Worker.TryExecute(OntoSettlement(settlement)));
                destroyed.Add(200 - settlement.StructureCount(Wall));
            }

            // A band, not a literal: the claim is that severity varies at all, which is what an unsourced
            // FloatRange promises and a single fixed fraction never would.
            Assert.True(destroyed.Count > 1, "every quake destroyed exactly the same share of what was built");
        }

        // ---- the ablation discipline: dice are its own, ambient stream untouched ----

        [Fact]
        public void Firing_the_incident_does_not_disturb_the_ambient_random_stream()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            SpawnBedWithSleeper(settlement, map, new IntVec3(5, 0, 5));

            uint before = Rand.Current.Iterations;
            Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: true));

            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void Ablated_it_leaves_the_ambient_stream_exactly_where_the_live_one_does()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            SpawnBedWithSleeper(settlement, map, new IntVec3(5, 0, 5));

            Ablation.Disable("Earthquake");
            try
            {
                uint before = Rand.Current.Iterations;
                Earthquake.Worker.TryExecute(OntoWatchedMap(settlement, map, registerWithStoryteller: true));
                Assert.Equal(before, Rand.Current.Iterations);
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void The_same_tick_and_seed_destroy_the_same_share()
        {
            int DestroyedOnce()
            {
                Settlement settlement = PlainSettlement();
                settlement.AddStructure(Wall, 60);
                Find.God.Attention.ClearFocus();
                Earthquake.Worker.TryExecute(OntoSettlement(settlement));
                return 60 - settlement.StructureCount(Wall);
            }

            Find.TickManager.DebugSetTicksGame(7000);
            int a = DestroyedOnce();
            Find.TickManager.DebugSetTicksGame(7000);
            int b = DestroyedOnce();

            Assert.Equal(a, b);
        }

        // ---- the payoff: a weakened settlement is genuinely easier to raid ----

        private static Settlement Town(string name, int tile, int citizens, int walls)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman(name + i));
            if (walls > 0) settlement.AddStructure(Wall, walls);
            return settlement;
        }

        [Fact]
        public void An_earthquake_weakened_settlement_defends_measurably_worse_than_an_unshaken_one()
        {
            Settlement quaked = Town("Quaked", 10, citizens: 6, walls: 60);
            Settlement control = Town("Control", 11, citizens: 6, walls: 60);
            Find.God.Attention.ClearFocus();

            float baseline = SettlementRaidResolver.DefenceStrengthOf(quaked);
            Assert.Equal(baseline, SettlementRaidResolver.DefenceStrengthOf(control));

            bool fired = Earthquake.Worker.TryExecute(OntoSettlement(quaked));
            Assert.True(fired);

            float after = SettlementRaidResolver.DefenceStrengthOf(quaked);
            Assert.True(after < baseline, "an earthquake destroyed walls but defence strength did not drop");
            // The untouched settlement is exactly where it started — proof this is the quake's doing, not
            // some shared piece of state moving under both settlements at once.
            Assert.Equal(baseline, SettlementRaidResolver.DefenceStrengthOf(control));
        }

        private static Faction Raiders(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), name, "F_" + name);

        private static IncidentWorker_RaidEnemy NewRaidWorker()
        {
            var raidDef = new IncidentDef
            {
                defName = "TestEarthquakeRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            };
            return (IncidentWorker_RaidEnemy)raidDef.Worker;
        }

        private static int RepelledOutOf(bool shaken, int seed, int trials = 120)
        {
            Rand.Current = new RandomStream(seed);
            Faction raiders = Raiders("Quake" + shaken);
            int held = 0;
            for (int i = 0; i < trials; i++)
            {
                Settlement town = Town("Q" + shaken + "_" + i, 1000 + i, citizens: 5, walls: 40);
                Find.God.Attention.ClearFocus();
                if (shaken) Earthquake.Worker.TryExecute(OntoSettlement(town));

                IncidentWorker_RaidEnemy raidWorker = NewRaidWorker();
                var raidTarget = new CivilizationTarget();
                raidTarget.SetSettlements(new[] { town });
                Assert.True(raidWorker.TryExecute(new IncidentParms { target = raidTarget, points = 500f, faction = raiders }));
                if (raidWorker.LastRaidOutcome!.Value.Repelled) held++;
            }
            return held;
        }

        [Fact]
        public void An_earthquake_weakened_settlement_repels_fewer_of_the_same_raids_than_an_unshaken_one()
        {
            int shakenHeld = RepelledOutOf(shaken: true, seed: 4242);
            int unshakenHeld = RepelledOutOf(shaken: false, seed: 4242);

            Assert.True(unshakenHeld > shakenHeld,
                "an earthquake-weakened settlement repelled no fewer of the same raids than an identical unshaken one (shaken="
                + shakenHeld + ", unshaken=" + unshakenHeld + ")");
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trip_preserves_structures_destroyed()
        {
            var ledger = new ResourceImpactLedger();
            ledger.RecordStructuresDestroyed("Earthquake", 5);
            ledger.RecordStructuresDestroyed("Earthquake", 3);
            ledger.RecordStructuresDestroyed("SomethingElse", 2);

            string xml = Scribe.SaveToString(ledger, "resourceImpact");
            ResourceImpactLedger loaded = Scribe.Load<ResourceImpactLedger>(xml, "resourceImpact", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(8, loaded.StructuresDestroyedBy("Earthquake"));
            Assert.Equal(2, loaded.StructuresDestroyedBy("SomethingElse"));
            Assert.Equal(0, loaded.StructuresDestroyedBy("Nonexistent"));
            Assert.Equal(10, loaded.TotalStructuresDestroyed);
        }

        [Fact]
        public void The_structures_destroyed_counter_survives_a_save_of_the_whole_storyteller()
        {
            Find.Storyteller.def = DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic");
            Find.Storyteller.difficulty = DefDatabase<DifficultyDef>.GetNamed("Medium");
            Find.Storyteller.resourceImpact.RecordStructuresDestroyed("Earthquake", 17);

            string xml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            var loaded = Scribe.Load<global::SimWorld.Director.Storyteller>(
                xml, "storyteller", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(17, loaded.resourceImpact.StructuresDestroyedBy("Earthquake"));
        }
    }
}
