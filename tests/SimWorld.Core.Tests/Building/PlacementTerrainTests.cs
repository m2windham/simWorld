using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// What ground a building can stand on (RimWorld: <c>terrainAffordanceNeeded</c> against a terrain's
    /// <c>affordances</c>), and what a refused placement says. The playtest found a storage hut planned and
    /// built in a river: no buildable def asked for any affordance, so water's lack of one never mattered.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PlacementTerrainTests : ContentTestBase
    {
        public PlacementTerrainTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static TerrainDef Terrain(string name) => DefDatabase<TerrainDef>.GetNamed(name);

        /// <summary>Every def the content authors a Blueprint for — everything a player or a settlement can
        /// plan — so a new building is covered the day it lands.</summary>
        public static IEnumerable<object[]> Buildables() =>
            DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Blueprint && d.entityToBuild != null)
                .Select(d => d.entityToBuild!.defName)
                .Distinct()
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .Select(n => new object[] { n });

        public static IEnumerable<object[]> Waters() =>
            new[] { "WaterShallow", "WaterMovingShallow", "WaterDeep", "WaterMovingChestDeep" }.Select(n => new object[] { n });

        [Fact]
        public void The_content_authors_buildables_to_check()
        {
            Assert.True(Buildables().Count() >= 8, "the theory below would pass vacuously");
            Assert.Contains(Buildables(), row => (string)row[0] == "StorageHut");
        }

        /// <summary>A loop, not a theory: member data is read at discovery, before the content is loaded.</summary>
        [Fact]
        public void No_buildable_can_be_planned_on_water_and_every_one_can_on_soil()
        {
            foreach (object[] buildable in Buildables()) AssertWaterRefusesAndSoilTakes((string)buildable[0]);
        }

        private static void AssertWaterRefusesAndSoilTakes(string defName)
        {
            ThingDef def = Def(defName);
            var centre = new IntVec3(4, 0, 4);
            foreach (object[] row in Waters())
            {
                var river = new CoreMap(9, 9, Terrain((string)row[0]));
                Assert.False(GenConstruct.CanPlaceBlueprintAt(def, centre, river, out string? reason), defName + " on " + row[0]);
                Assert.Contains("cannot support", reason);
            }

            var field = new CoreMap(9, 9, SimWorld.Map.TerrainDefOf.Soil);
            Assert.True(GenConstruct.CanPlaceBlueprintAt(def, centre, field, out string? soilReason), defName + " on soil: " + soilReason);
        }

        [Fact]
        public void Ground_that_is_poor_but_dry_still_takes_a_hut()
        {
            // The default is RimWorld's Light, not Heavy: marsh and mud carry Light, so the fix refuses water
            // and nothing a settlement could build on before.
            foreach (string name in new[] { "Marsh", "Mud", "MarshyTerrain", "Ice", "Sand", "Gravel" })
            {
                var map = new CoreMap(9, 9, Terrain(name));
                Assert.True(GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.StorageHut, new IntVec3(4, 0, 4), map, out string? reason), name + ": " + reason);
            }
        }

        [Fact]
        public void A_def_that_names_its_own_need_keeps_it()
        {
            var needsHeavy = new ThingDef { defName = "TestNeedsHeavy", category = ThingCategory.Building, terrainAffordanceNeeded = SimWorld.Map.TerrainAffordanceDefOf.Heavy };
            Assert.Same(SimWorld.Map.TerrainAffordanceDefOf.Heavy, GenConstruct.TerrainAffordanceNeed(needsHeavy));
            Assert.Same(SimWorld.Map.TerrainAffordanceDefOf.Light, GenConstruct.TerrainAffordanceNeed(ConstructionThingDefOf.StorageHut));
            Assert.Null(GenConstruct.TerrainAffordanceNeed(Def("WoodLog")));
        }

        [Fact]
        public void The_players_hut_click_on_a_river_cell_is_refused_at_that_cell_and_nothing_is_planned()
        {
            Settlement settlement = OpenedSettlement("hut-in-the-river");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = map.AllCells.First(c => GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.StorageHut, c, map, out _)
                && !map.thingGrid.ThingsListAt(c).Any(t => t is Pawn));
            map.terrainGrid.SetTerrain(cell, Terrain("WaterMovingShallow"));
            int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;

            for (int click = 0; click < 2; click++)
            {
                MapCommandResult result = MapCommands.PlaceBlueprint("StorageHut", cell);
                Assert.Equal(MapCommandOutcome.Occupied, result.Outcome);
                Assert.Contains("moving water", result.Reason, System.StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(before, map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count);
        }

        // ---- zones on water ----

        [Fact]
        public void The_settlements_own_granary_is_never_painted_on_water()
        {
            // Shallow water is standable, so the granary's old test let it in — and RimWorld's shallow water
            // rots what lies in it (extraDeteriorationFactor). A settlement choosing its own store keeps it dry.
            var map = new CoreMap(20, 20, SimWorld.Map.TerrainDefOf.Soil);
            foreach (IntVec3 c in map.AllCells.Where(c => c.x <= 10)) map.terrainGrid.SetTerrain(c, Terrain("WaterShallow"));
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Riverside", 0);
            for (int i = 0; i < 3; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            for (int i = 0; i < 8; i++)
            {
                Thing chunk = ThingMaker.MakeThing(Def("ChunkSandstone"));
                GenSpawn.Spawn(chunk, new IntVec3(11, 0, 4 + i), map);
            }

            Assert.True(SettlementStockInitiative.EnsureGranary(settlement, map) > 0);

            var granary = SettlementStockInitiative.GranaryOf(map)!;
            Assert.All(granary.Cells, c => Assert.False(map.terrainGrid.TerrainAt(c).IsWater, c + " is water"));
        }

        [Fact]
        public void A_player_stockpile_drawn_across_a_river_leaves_out_the_deep_water_and_keeps_the_shallows()
        {
            // RimWorld: a stockpile may sit on shallow water (the player's call; it rots there), never on
            // terrain nothing can stand on. A rectangle dragged across a river takes what it can, as RimWorld's
            // zone designator does, rather than refusing the whole drag.
            Settlement settlement = OpenedSettlement("pile-across-the-river");
            CoreMap map = settlement.InteriorMap!;
            List<IntVec3> rect = FreeRect(map, 3, 3);
            map.terrainGrid.SetTerrain(rect[0], Terrain("WaterDeep"));
            map.terrainGrid.SetTerrain(rect[1], Terrain("WaterShallow"));

            MapCommandResult result = MapCommands.MarkStockpile(rect);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Zone? zone = map.zoneManager.ZoneAt(rect[1]);
            Assert.IsType<Zone_Stockpile>(zone);
            Assert.Null(map.zoneManager.ZoneAt(rect[0]));
            Assert.Equal(rect.Count - 1, zone!.CellCount);
            Assert.Contains("deep water", result.Reason);

            List<IntVec3> allDeep = FreeRect(map, 2, 2);
            foreach (IntVec3 c in allDeep) map.terrainGrid.SetTerrain(c, Terrain("WaterDeep"));
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.MarkStockpile(allDeep).Outcome);
            Assert.All(allDeep, c => Assert.Null(map.zoneManager.ZoneAt(c)));
        }

        private static List<IntVec3> FreeRect(CoreMap map, int w, int h)
        {
            foreach (IntVec3 corner in map.AllCells)
            {
                var rect = new CellRect(corner.x, corner.z, w, h);
                if (corner.x + w > map.Size.x || corner.z + h > map.Size.z) continue;
                List<IntVec3> cells = rect.Cells.ToList();
                if (cells.All(c => GenGrid.Standable(c, map) && map.zoneManager.ZoneAt(c) == null)) return cells;
            }
            throw new System.InvalidOperationException("no free rect");
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }
    }
}
