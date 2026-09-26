using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <b>Where</b> <see cref="SettlementConstructionInitiative"/> builds, as opposed to what
    /// (<c>ConstructionInitiativeTests</c> owns the what). <c>docs/design/the-loop.md</c> §5 item 2 recorded the
    /// defect: every blueprint went to a uniformly random cell on the whole map, so a settlement scattered lone
    /// walls and beds across tens of thousands of cells, and a home area the player painted was ignored.
    /// <para/>
    /// Everything here asserts structure — inside the painted area, nearer the hub than uniform placement
    /// would be, the same cells for the same seed — never a literal coordinate, because the cells themselves
    /// are a seeded draw and pinning them would pin the stream rather than the behaviour.
    /// </summary>
    public class ConstructionPlacementTests : ContentTestBase
    {
        public ConstructionPlacementTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement PlainSettlement(int citizens, int storedWood = 0)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Placement", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            if (storedWood > 0) settlement.AddStore(Def("WoodLog"), storedWood);
            return settlement;
        }

        /// <summary>The road hub exactly as <c>MapGen.GenStep_Roads</c> defines it — restated here rather than
        /// read off the class under test, so these tests pin "clusters at the hub the streets run to" and not
        /// "clusters wherever the initiative happens to think the middle is".</summary>
        private static IntVec3 RoadHub(CoreMap map) => new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

        private static double Distance(IntVec3 a, IntVec3 b)
        {
            double dx = a.x - b.x, dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static List<global::SimWorld.Building.Blueprint> Blueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<global::SimWorld.Building.Blueprint>()
                .ToList();

        /// <summary>Every blueprint as (what, where), in a stable order — the comparison the determinism and
        /// round-trip tests make.</summary>
        private static List<string> Placements(CoreMap map) =>
            Blueprints(map)
                .Select(bp => bp.EntityToBuild.defName + "@" + bp.Position)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        /// <summary>Runs <paramref name="passes"/> gated passes of the initiative — the tick moved onto each
        /// interval boundary in turn, exactly the clock <c>TickSettlement</c> gates itself on.</summary>
        private static void RunPasses(Settlement settlement, CoreMap map, int passes)
        {
            for (int i = 0; i < passes; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
        }

        private static List<IntVec3> Paint(CoreMap map, CellRect rect)
        {
            var cells = rect.ClipInsideMap(map).Cells.ToList();
            foreach (IntVec3 c in cells) map.areaManager.Home[c] = true;
            return cells;
        }

        /// <summary>Mean distance from the hub over every cell of the map: what uniform whole-map placement
        /// would give on average. Computed from the map rather than quoted, so the comparison below holds
        /// whatever size the map is.</summary>
        private static double UniformMeanDistance(CoreMap map)
        {
            IntVec3 hub = RoadHub(map);
            return map.AllCells.Average(c => Distance(c, hub));
        }

        // ---- the player's intent binds ----

        [Fact]
        public void With_a_painted_home_area_every_blueprint_the_settlement_places_lands_inside_it()
        {
            // A 10x10 home area in a corner of a 60x60 map, well away from the middle, so "inside it" cannot be
            // satisfied by accident: uniform placement would put ~97% of blueprints outside it.
            CoreMap map = NewMap(60, 60);
            var home = new CellRect(4, 44, 10, 10);
            Paint(map, home);

            // 5 citizens: 5 beds, 10 walls; 120 wood stored: 3 huts. 18 things, room for 100.
            Settlement settlement = PlainSettlement(citizens: 5, storedWood: 120);
            RunPasses(settlement, map, passes: 10);

            List<global::SimWorld.Building.Blueprint> placed = Blueprints(map);
            Assert.Equal(18, placed.Count); // the whole need, placed: a big enough area stalls nothing
            Assert.All(placed, bp => Assert.True(map.areaManager.Home[bp.Position],
                bp.EntityToBuild.defName + " blueprint at " + bp.Position + " is outside the painted home area " + home));
        }

        [Fact]
        public void The_players_home_area_command_steers_where_a_real_settlement_builds()
        {
            // The lever end to end: a real game, the real player command, the production Tick entry point.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "placement-follows-the-player",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;

            // Somewhere off to one side of the hub, and only ground a bed can actually go on — the claim is
            // "the settlement builds where the player painted", not "the player can paint a lake".
            IntVec3 hub = RoadHub(map);
            IntVec3 aside = new IntVec3(hub.x + map.Size.x / 4, 0, hub.z - map.Size.z / 4);
            List<IntVec3> painted = CellRect.CenteredOn(aside, 10).ClipInsideMap(map).Cells
                .Where(c => GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, map, out _))
                .ToList();
            Assert.True(painted.Count >= 60, "test setup: the painted patch should have room for the whole need");
            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetHomeArea(painted, true).Outcome);

            var before = new HashSet<Thing>(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            for (int i = 0; i < 8; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.Tick();
            }

            List<global::SimWorld.Building.Blueprint> placed = Blueprints(map).Where(bp => !before.Contains(bp)).ToList();
            Assert.NotEmpty(placed);
            Assert.All(placed, bp => Assert.True(map.areaManager.Home[bp.Position],
                bp.EntityToBuild.defName + " blueprint at " + bp.Position + " is outside the home area the player painted"));
        }

        // ---- a home area too small for the need ----

        [Fact]
        public void A_home_area_too_small_for_the_need_fills_then_waits_for_the_player_and_says_why()
        {
            // "Sensibly", decided: the home area is the player's standing rule, so a full one binds. Nothing
            // spills outside it, the shortfall waits (as it always did on a full map), the simulation says in
            // its own words what is waiting and why, and widening the area is the lever that releases it.
            CoreMap map = NewMap(40, 40);
            Paint(map, new CellRect(2, 2, 2, 2)); // 4 cells, in a corner far from the hub

            // 3 citizens: 3 beds, 6 walls. Nine things wanted; a bed is 1x2 (as in RimWorld — this used to
            // count four 1x1 places), so four cells hold two beds side by side and nothing else.
            Settlement settlement = PlainSettlement(citizens: 3);
            Assert.Null(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map)); // room, so nothing to say yet

            RunPasses(settlement, map, passes: 6); // far more passes than it takes to fill four cells

            List<global::SimWorld.Building.Blueprint> placed = Blueprints(map);
            Assert.Equal(2, placed.Count);
            Assert.All(placed, bp => Assert.All(bp.OccupiedRect().Cells, c => Assert.True(map.areaManager.Home[c])));
            Assert.Equal(2, placed.Count(bp => bp.EntityToBuild == ConstructionThingDefOf.Bed)); // beds first

            // Not silent: the simulation names what is waiting, and that it is the home area it is waiting on.
            string? why = SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map);
            Assert.NotNull(why);
            Assert.Contains("1 bed", why);
            Assert.Contains("6 walls", why);
            Assert.Contains("home area", why);

            // The player widens it: the very next pass uses the new room, inside the new paint and only there.
            List<IntVec3> widened = Paint(map, new CellRect(2, 4, 6, 4));
            Find.TickManager.DebugSetTicksGame(100 * ConstructionInitiativeTuning.IntervalTicks);
            SettlementConstructionInitiative.TickSettlement(settlement, map);

            List<global::SimWorld.Building.Blueprint> after = Blueprints(map);
            Assert.Equal(2 + ConstructionInitiativeTuning.MaxBlueprintsPerTick, after.Count);
            Assert.All(after, bp => Assert.All(bp.OccupiedRect().Cells, c => Assert.True(map.areaManager.Home[c])));
            Assert.Contains(after, bp => widened.Contains(bp.Position));
            Assert.Null(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map)); // room again
        }

        [Fact]
        public void A_home_area_with_no_buildable_ground_places_nothing_rather_than_building_elsewhere()
        {
            // Too small in the other sense: painted, but every painted cell is already taken.
            CoreMap map = NewMap(30, 30);
            List<IntVec3> painted = Paint(map, new CellRect(1, 1, 3, 1));
            foreach (IntVec3 c in painted) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);

            Settlement settlement = PlainSettlement(citizens: 2);
            RunPasses(settlement, map, passes: 3);

            Assert.Empty(Blueprints(map));
            string? why = SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map);
            Assert.NotNull(why);
            Assert.Contains("2 beds", why);

            // Unpainting the whole area hands the choice back to the settlement, which then builds around the hub.
            foreach (IntVec3 c in painted) map.areaManager.Home[c] = false;
            Assert.Null(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map));
            Find.TickManager.DebugSetTicksGame(100 * ConstructionInitiativeTuning.IntervalTicks);
            SettlementConstructionInitiative.TickSettlement(settlement, map);
            Assert.NotEmpty(Blueprints(map));
        }

        // ---- no home area: the road hub ----

        [Fact]
        public void Without_a_home_area_the_settlement_gathers_around_the_road_hub()
        {
            CoreMap map = NewMap(200, 200);
            Settlement settlement = PlainSettlement(citizens: 20); // 20 beds + 40 walls
            RunPasses(settlement, map, passes: 25);

            List<global::SimWorld.Building.Blueprint> placed = Blueprints(map);
            Assert.Equal(60, placed.Count);

            IntVec3 hub = RoadHub(map);
            double mean = placed.Average(bp => Distance(bp.Position, hub));
            double uniform = UniformMeanDistance(map);
            Assert.True(mean < uniform / 3,
                "Mean distance to the road hub was " + mean.ToString("F1") + " cells; uniform placement over the whole map gives "
                + uniform.ToString("F1") + ". A settlement should gather, not scatter.");
        }

        [Fact]
        public void A_founded_settlement_on_a_generated_map_gathers_around_its_road_hub()
        {
            // What the player actually looks at: a generated interior (terrain, rock, water, roads where the
            // tile has them), its own founding band, the production entry point.
            SimWorld.World.World world = WorldGenerator.GenerateWorld("placement-hub", 0.3f, OverallRainfall.Normal,
                OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(7));
            CoreMap map = settlement.EnterMap(world);
            Find.World = world;

            for (int i = 0; i < 25; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.Tick();
            }

            // At least a bed per founder. Not necessarily any walls: MapGen.GenStep_Ruins leaves ancient walls on
            // a generated interior, and the initiative counts every wall standing on the map toward its own wall
            // need, so a map with enough ruins asks for none. That is the need model's call, not placement's.
            List<global::SimWorld.Building.Blueprint> placed = Blueprints(map);
            Assert.True(placed.Count >= settlement.Citizens.Count, "test setup: expected at least a bed per founder, got "
                + string.Join(", ", placed.GroupBy(bp => bp.EntityToBuild.defName).Select(g => g.Count() + " " + g.Key))
                + " for " + settlement.Citizens.Count + " citizens");

            IntVec3 hub = RoadHub(map);
            double mean = placed.Average(bp => Distance(bp.Position, hub));
            double uniform = UniformMeanDistance(map);
            Assert.True(mean < uniform / 3,
                "Mean distance to the road hub was " + mean.ToString("F1") + " cells against " + uniform.ToString("F1")
                + " for uniform placement on this " + map.Size.x + "x" + map.Size.z + " map.");
        }

        // ---- determinism ----

        [Fact]
        public void The_same_seed_places_the_same_blueprints_in_the_same_cells()
        {
            List<string> Run(int seed, bool paintHome)
            {
                SimWorld.Map.Map.ResetMapIdCounter();
                Pawn.ResetThingIdCounter();
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap(80, 80);
                if (paintHome) Paint(map, new CellRect(50, 50, 20, 20));
                RunPasses(PlainSettlement(citizens: 4, storedWood: 60), map, passes: 4);
                return Placements(map);
            }

            foreach (bool paintHome in new[] { true, false })
            {
                List<string> first = Run(777, paintHome);
                List<string> again = Run(777, paintHome);
                List<string> otherSeed = Run(778, paintHome);

                Assert.Equal(12, first.Count); // 4 passes x 3 per pass
                Assert.Equal(first, again);
                // The seeded stream is what chooses the cell, not a fixed scan order: another seed, other cells.
                Assert.NotEqual(first, otherSeed);
            }
        }

        // ---- Scribe ----

        [Fact]
        public void A_saved_and_loaded_map_places_exactly_where_the_unsaved_one_does()
        {
            // Placement keeps no state of its own. It reads the map (the home area, what already stands) and the
            // seeded stream. So the round trip that matters is the input it reads: the player's paint, and a
            // blueprint already placed, must survive a save, and a loaded map must then build in the very same
            // cells as one that was never saved.
            CoreMap original = NewMap(50, 50);
            Paint(original, new CellRect(30, 5, 12, 12));
            Settlement settlement = PlainSettlement(citizens: 3, storedWood: 60);
            RunPasses(settlement, original, passes: 1); // one blueprint pass before saving

            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(original.areaManager.Home.TrueCount, loaded.areaManager.Home.TrueCount);
            Assert.All(original.AllCells, c => Assert.Equal(original.areaManager.Home[c], loaded.areaManager.Home[c]));
            Assert.Equal(Placements(original), Placements(loaded));

            List<string> Continue(CoreMap map)
            {
                Rand.Current = new RandomStream(2024);
                for (int i = 1; i <= 3; i++)
                {
                    Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                    SettlementConstructionInitiative.TickSettlement(settlement, map);
                }
                return Placements(map);
            }

            List<string> fromOriginal = Continue(original);
            List<string> fromLoaded = Continue(loaded);
            Assert.Equal(fromOriginal, fromLoaded);
            Assert.All(Blueprints(loaded), bp => Assert.True(loaded.areaManager.Home[bp.Position]));
        }
    }
}
