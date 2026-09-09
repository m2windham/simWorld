using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// Rival civilizations emerging from the running simulation instead of being placed at world generation
    /// (spec §5b.4/§5b.5, tracker items <c>settlements.emergence</c>/<c>worldgen.civscale</c>). "World" is
    /// aliased to <c>global::SimWorld.World.World</c> throughout — this test namespace's own last segment is
    /// also "World", which would otherwise shadow it.
    /// </summary>
    public class EmergenceTests : ContentTestBase
    {
        public EmergenceTests(CoreContentFixture content) : base(content)
        {
        }

        private static global::SimWorld.World.World SoloWorld(string seed, int subdivision = 3) =>
            WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision, soloStart: true);

        /// <summary>
        /// A solo world with the player's own settlement founded on it. World generation deliberately does not
        /// place that settlement — founding it is the opening move of the game (spec §5b.3: a region, a site,
        /// a band of 20-40 and a chronicle entry), which <c>Game.NewGame</c> performs — so a test about what
        /// happens to an existing civilization has to establish that civilization itself rather than lean on a
        /// world-generation side effect.
        /// </summary>
        private static global::SimWorld.World.World SoloWorldWithPlayerSettlement(string seed, int subdivision = 3)
        {
            global::SimWorld.World.World world = SoloWorld(seed, subdivision);
            Faction player = world.factions.First();
            var rand = new RandomStream(GenText.StableStringHash(seed + "-player-settlement"));
            int tile = world.grid.Tiles
                .Select((t, i) => (tile: t, index: i))
                .First(x => !x.tile.WaterCovered && x.tile.biome != null && x.tile.biome.canBuildBase).index;
            SettlementFounder.FoundColony(world, tile, player, SettlementTuning.EstablishedColonyPopulationRange.min, rand, recordChronicle: false);
            return world;
        }

        /// <summary>Jumps the clock a whole year at a time and ticks emergence directly — the same
        /// "advance the clock, then invoke the interval-gated method" idiom <c>SettlementTests.AdvanceYear</c>
        /// already uses, since single-stepping centuries of ticks would make these tests far too slow.</summary>
        private static void RunYears(global::SimWorld.World.World world, int years, ref int tick)
        {
            for (int year = 0; year < years; year++)
            {
                tick += GenDate.TicksPerYear;
                Find.TickManager.DebugSetTicksGame(tick);
                world.emergence.Tick(world);
            }
        }

        // ----- Every settlement is a real Settlement (spec §5b.5) -----

        [Fact]
        public void Every_settlement_world_generation_places_is_a_real_Settlement()
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "worldgen-settlements-are-real", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Test", subdivisionOverride: 3, soloStart: false);

            List<WorldObject> settlementObjects = world.worldObjects.Where(o => o.def == WorldObjectDefOf.Settlement).ToList();
            Assert.NotEmpty(settlementObjects);
            Assert.All(settlementObjects, o => Assert.IsType<Settlement>(o));

            // world.Settlements itself is typed Settlement now, not a mix — reading TotalPopulation directly
            // (no cast) is the point of the fix.
            Assert.All(world.Settlements, s => Assert.True(s.TotalPopulation > 0));
        }

        [Fact]
        public void A_solo_start_world_begins_with_nothing_founded_at_all()
        {
            // "Alone, others emerge later" means exactly that at tick zero: no rival has founded anything,
            // and neither has the player — founding the first settlement is the opening move of the game
            // (spec §5b.3), which Game.NewGame performs through SettlementFounder.Found. World generation
            // placing an already-established colony for the player would hand their civilization a settlement
            // nobody founded, alongside the one they then found.
            global::SimWorld.World.World world = SoloWorld("worldgen-solo-start-empty");

            Assert.Single(world.factions);
            Assert.True(world.factions[0].def.isPlayer);
            Assert.DoesNotContain(world.worldObjects, o => o.def == WorldObjectDefOf.Settlement);
        }

        [Fact]
        public void World_generations_own_settlements_are_not_chronicled_as_founding_events()
        {
            // Backstory the player never watched happen (spec: the same reasoning
            // ResearchManager.SetProjectFinishedForSetup already applies to a scenario's starting era) should
            // not fill the chronicle with founding lines for settlements nobody watched come into being.
            global::SimWorld.World.World world = SoloWorld("worldgen-no-chronicle-spam");
            Assert.DoesNotContain(Find.Storyteller.Chronicle, e => e.incidentDefName.Contains("Founding"));
            Assert.DoesNotContain(Find.Storyteller.Chronicle, e => e.incidentDefName.Contains("Expansion"));
        }

        // ----- Era gates which civilizations can emerge (worldgen.civscale's "era seeding") -----

        [Fact]
        public void A_fresh_civilization_only_makes_neolithic_rivals_eligible_to_emerge()
        {
            global::SimWorld.World.World world = SoloWorld("era-gate-neolithic");
            IReadOnlyList<FactionDef> eligible = EmergenceManager.EligibleFactionDefs(world);

            Assert.Contains(eligible, d => d.defName == "TribalCivilization");
            Assert.DoesNotContain(eligible, d => d.defName == "OutlanderCivilization"); // Industrial
            Assert.DoesNotContain(eligible, d => d.defName == "RoughOutlanders"); // Medieval
        }

        [Fact]
        public void Reaching_a_later_era_widens_which_civilizations_can_emerge()
        {
            global::SimWorld.World.World world = SoloWorld("era-gate-advanced");
            Find.ResearchManager.DebugSetAllProjectsFinished(); // reach the top of the ladder (Exotic/Archotech)

            IReadOnlyList<FactionDef> eligible = EmergenceManager.EligibleFactionDefs(world);

            Assert.Contains(eligible, d => d.defName == "TribalCivilization");
            Assert.Contains(eligible, d => d.defName == "OutlanderCivilization");
            Assert.Contains(eligible, d => d.defName == "RoughOutlanders");
        }

        [Fact]
        public void A_faction_def_already_at_its_max_count_is_not_eligible_to_emerge_again()
        {
            global::SimWorld.World.World world = SoloWorld("era-gate-cap");
            FactionDef tribal = DefDatabase<FactionDef>.GetNamed("TribalCivilization");

            for (int i = 0; i < tribal.maxCountAtGameStart; i++)
            {
                world.factions.Add(new Faction(tribal, "Filler" + i, world.NextLoadId("Faction")));
            }

            Assert.DoesNotContain(EmergenceManager.EligibleFactionDefs(world), d => d == tribal);
        }

        // ----- Pacing: the behaviour is pinned by a band, never a literal (per the module's own brief) -----

        [Fact]
        public void Emergence_produces_a_plausible_band_of_civilizations_over_a_long_run()
        {
            global::SimWorld.World.World world = SoloWorld("emergence-pacing");
            int startingCivilizations = world.factions.Count;
            Assert.Equal(1, startingCivilizations); // solo start: the player, alone.

            int tick = 0;
            RunYears(world, 400, ref tick);

            int finalCivilizations = world.factions.Count;

            // Never fires so often the planet is instantly crowded (an upper bound well under every eligible
            // def's own maxCountAtGameStart total), and — the point of a solo start actually going anywhere —
            // never stays alone forever either. The exact count is this seed's own roll, not a contract; the
            // band is the contract (EmergenceTuning.NewCivilizationMTBYears' own doc explains the choice).
            Assert.InRange(finalCivilizations, startingCivilizations + 1, startingCivilizations + 8);
        }

        [Fact]
        public void A_new_civilization_that_emerges_is_a_real_settlement_with_a_live_founding_band_and_a_chronicle_entry()
        {
            global::SimWorld.World.World world = SoloWorld("emergence-founding-shape");
            int tick = 0;
            RunYears(world, 400, ref tick);

            Assert.True(world.factions.Count > 1, "Expected at least one civilization to have emerged over 400 years for this seed.");

            Faction emerged = world.factions.First(f => !f.def.isPlayer);
            Settlement settlement = world.Settlements.Single(s => s.faction == emerged);

            // Full-tier founders, several households worth, exactly the shape SettlementFounder.Found gives
            // the player's own start (spec §5b.3) — an emerged civilization gets the same treatment.
            Assert.InRange(settlement.Citizens.Count, SettlementTuning.FoundingBandRange.min, SettlementTuning.FoundingBandRange.max);
            Assert.All(settlement.Citizens, p => Assert.Equal(PawnTier.Full, p.tier.Tier));

            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.Contains("Civilization emerged") && e.incidentDefName.Contains(emerged.name));
        }

        // ----- Determinism: same seed, same emergence history -----

        [Fact]
        public void Same_seed_produces_the_same_emergence_history()
        {
            // Reset every piece of ambient state a founding can touch before each run: the clock itself
            // (world generation reads Find.TickManager.TicksGame for a settlement's foundingTick — worldA's
            // 300-year run must not leak into worldB's world generation), and Rand.Current (a live founding
            // band's pawn generation draws from it, not from either world's own seeded stream).
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(4242);
            global::SimWorld.World.World worldA = SoloWorld("emergence-determinism");
            int tickA = 0;
            RunYears(worldA, 300, ref tickA);

            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(4242);
            global::SimWorld.World.World worldB = SoloWorld("emergence-determinism");
            int tickB = 0;
            RunYears(worldB, 300, ref tickB);

            var historyA = worldA.factions.Select(f => (f.def.defName, f.name)).ToList();
            var historyB = worldB.factions.Select(f => (f.def.defName, f.name)).ToList();
            Assert.Equal(historyA, historyB);

            var settlementsA = worldA.Settlements.Select(s => (s.name, s.tile, s.foundingTick, s.TotalPopulation)).OrderBy(t => t.foundingTick).ToList();
            var settlementsB = worldB.Settlements.Select(s => (s.name, s.tile, s.foundingTick, s.TotalPopulation)).OrderBy(t => t.foundingTick).ToList();
            Assert.Equal(settlementsA, settlementsB);
        }

        // ----- A civilization is more than one settlement (growth -> a second settlement) -----

        [Fact]
        public void A_settlement_below_the_expansion_threshold_never_founds_a_colony()
        {
            global::SimWorld.World.World world = SoloWorldWithPlayerSettlement("expansion-below-threshold");
            Faction player = world.factions.First();

            // Replace whatever world generation placed with exactly one settlement, held under threshold, so
            // the claim under test — "below threshold, no expansion" — is not muddied by any of world
            // generation's own (larger, population 80-400) settlements independently crossing it.
            int tile = world.Settlements.First().tile;
            world.worldObjects.RemoveAll(o => o is Settlement);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, player, "SmallHome", 0);
            settlement.AddStatisticalPeople(EmergenceTuning.ExpansionPopulationThreshold - 1);
            world.worldObjects.Add(settlement);

            int tick = 0;
            RunYears(world, 300, ref tick);

            Assert.Single(world.Settlements, s => s.faction == player);
        }

        [Fact]
        public void A_large_settlement_eventually_founds_a_second_settlement_for_the_same_civilization()
        {
            global::SimWorld.World.World world = SoloWorldWithPlayerSettlement("expansion-grows");
            Faction player = world.factions.First();
            int settlementsBefore = world.Settlements.Count(s => s.faction == player);

            foreach (Settlement s in world.Settlements.Where(s => s.faction == player).ToList())
            {
                s.AddStatisticalPeople(EmergenceTuning.ExpansionPopulationThreshold);
            }

            int tick = 0;
            RunYears(world, 400, ref tick);

            int settlementsAfter = world.Settlements.Count(s => s.faction == player);
            Assert.True(settlementsAfter > settlementsBefore,
                $"Expected the player's civilization to have founded at least one colony over 400 years ({settlementsBefore} -> {settlementsAfter}).");
            Assert.True(settlementsAfter <= EmergenceTuning.MaxSettlementsPerFaction,
                "Expansion must never exceed the per-faction settlement cap.");

            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Expansion:"));
        }

        [Fact]
        public void Expansion_never_exceeds_the_per_faction_settlement_cap()
        {
            global::SimWorld.World.World world = SoloWorldWithPlayerSettlement("expansion-cap", subdivision: 4);
            Faction player = world.factions.First();

            // Force every existing settlement well above threshold, and keep doing so for any colony that
            // gets founded along the way, so the cap — not a shortage of eligible parents — is what stops it.
            int tick = 0;
            for (int year = 0; year < 600; year++)
            {
                foreach (Settlement s in world.Settlements.Where(s => s.faction == player).ToList())
                {
                    if (s.TotalPopulation < EmergenceTuning.ExpansionPopulationThreshold)
                    {
                        s.AddStatisticalPeople(EmergenceTuning.ExpansionPopulationThreshold);
                    }
                }
                tick += GenDate.TicksPerYear;
                Find.TickManager.DebugSetTicksGame(tick);
                world.emergence.Tick(world);
            }

            // The claim this test's name makes is the cap, so that is what it asserts — plus that expansion
            // actually ran, so it cannot pass by never firing. It used to assert exact equality with the cap,
            // which only held while world generation handed the player several settlements to start with;
            // from a single founding settlement, whether 600 years is enough to reach the cap exactly is the
            // pacing roll's business, not the cap's.
            int playerSettlements = world.Settlements.Count(s => s.faction == player);
            Assert.InRange(playerSettlements, 2, EmergenceTuning.MaxSettlementsPerFaction);
        }

        // ----- Scribe round trip: the manager's own random stream resumes, not restarts -----

        [Fact]
        public void Scribe_round_trip_resumes_the_same_emergence_history_instead_of_restarting_it()
        {
            global::SimWorld.World.World original = SoloWorld("emergence-scribe");
            int tick = 0;
            RunYears(original, 150, ref tick);

            string xml = Scribe.SaveToString(original, "world");
            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            // Continue both over the identical tick sequence from here. If the manager's random-stream
            // position round-tripped (not reset to its starting seed), the two draw the exact same sequence
            // of emergence decisions going forward.
            int tickOriginal = tick;
            int tickLoaded = tick;
            RunYears(original, 150, ref tickOriginal);
            RunYears(loaded, 150, ref tickLoaded);

            var originalFactions = original.factions.Select(f => (f.def.defName, f.name)).ToList();
            var loadedFactions = loaded.factions.Select(f => (f.def.defName, f.name)).ToList();
            Assert.Equal(originalFactions, loadedFactions);

            // Make sure this actually exercised the manager's random stream — otherwise "nothing happened
            // either way" would pass even for a broken round trip that silently reset it.
            Assert.True(original.factions.Count > 1, "Expected at least one civilization to have emerged over 300 years for this seed.");

            var originalSettlements = original.Settlements.Select(s => (s.name, s.tile, s.foundingTick)).OrderBy(t => t.foundingTick).ToList();
            var loadedSettlements = loaded.Settlements.Select(s => (s.name, s.tile, s.foundingTick)).OrderBy(t => t.foundingTick).ToList();
            Assert.Equal(originalSettlements, loadedSettlements);
        }
    }
}
