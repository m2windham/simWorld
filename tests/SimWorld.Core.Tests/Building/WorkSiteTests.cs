using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The compound's work yard (<see cref="SettlementCompound.WorkSiteCells"/>): research, art, the kitchen,
    /// the stonecutter and the workshops try it first, each keeps its old search as the fallback, and no
    /// fallback ever lands on the compound's own ground.
    /// </summary>
    [Collection("GlobalDefs")]
    public class WorkSiteTests : ContentTestBase
    {
        public WorkSiteTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A built compound for a band of twenty with no households, so no dwellings.</summary>
        private static (Settlement, CoreMap) BuiltCompound()
        {
            var map = new CoreMap(120, 120, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Yard", 0);
            for (int i = 0; i < 20; i++) settlement.AddCitizen(NewHuman("C" + i));
            Pawn builder = NewHuman("Builder");
            for (int pass = 0; pass < 60; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
                foreach (Blueprint bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
                {
                    bp.ReplaceWithFrame().CompleteConstruction(builder);
                }
            }
            Assert.NotNull(map.settlementCompound);
            return (settlement, map);
        }

        private static Thing TheDoor(CoreMap map) => Assert.Single(map.listerThings.ThingsOfDef(CompoundThingDefOf.Door));

        private static List<Blueprint> Planned(CoreMap map, ThingDef def) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => ReferenceEquals(bp.EntityToBuild, def)).ToList();

        /// <summary>Just outside the door, beside the street: within a few cells east of it and no more than a
        /// dwelling's depth either side of its row, and not on the street itself.</summary>
        private static bool InTheYard(Thing door, IntVec3 c) =>
            c.x > door.Position.x + 1 && c.x <= door.Position.x + 5
            && System.Math.Abs(c.z - door.Position.z) >= 2 && System.Math.Abs(c.z - door.Position.z) <= 6;

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        [Fact]
        public void Research_benches_and_art_go_up_in_the_yard_by_the_door()
        {
            (Settlement settlement, CoreMap map) = BuiltCompound();
            Thing door = TheDoor(map);

            for (int i = 0; i < 4; i++) SettlementWorksInitiative.Run(settlement, map);

            List<Blueprint> benches = Planned(map, ResearchWorkDefOf.ResearchBench);
            Assert.Equal(WorksInitiativeTuning.ResearchBenchesWanted(settlement), benches.Count);
            Assert.All(benches, b => Assert.True(InTheYard(door, b.Position), "research bench at " + b.Position + ", door at " + door.Position));
            List<Blueprint> art = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => SculptureMaterials.AllSculptureDefs.Contains(bp.EntityToBuild)).ToList();
            Assert.NotEmpty(art);
            Assert.True(InTheYard(door, art[0].Position), "first art at " + art[0].Position);

            // Every bench is worked from the street: a standable cell beside it lies on the street's rows.
            Assert.All(benches, b => Assert.Contains(GenAdj.CardinalDirections, o =>
                GenGrid.Standable(b.Position + o, map) && System.Math.Abs((b.Position + o).z - door.Position.z) <= 1));
        }

        [Fact]
        public void A_kitchen_goes_in_the_yard_when_the_food_is_in_reach_of_it_and_to_the_food_when_it_is_not()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Fire"));
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Cooking"));
            ThingDef stove = Def("FueledStove");

            (Settlement _, CoreMap near) = BuiltCompound();
            Thing door = TheDoor(near);
            SpawnStack(near, door.Position + new IntVec3(-2, 0, 0), "RawBerries", 60); // stored inside the ring
            CookingInitiative.Run(near);
            Blueprint inYard = Assert.Single(Planned(near, stove));
            Assert.True(InTheYard(door, inYard.Position), "kitchen at " + inYard.Position);

            (Settlement _, CoreMap far) = BuiltCompound();
            var corner = new IntVec3(5, 0, 5);
            SpawnStack(far, corner, "RawBerries", 60);
            CookingInitiative.Run(far);
            Blueprint byFood = Assert.Single(Planned(far, stove));
            Assert.True(byFood.Position.InHorDistOf(corner, CookingTuning.StovePlacementRadius * 1.5f), "kitchen at " + byFood.Position);
            Assert.False(far.settlementCompound!.Reserves(byFood.OccupiedRect()));
        }

        [Fact]
        public void A_stonecutter_goes_to_the_stone_unless_the_stone_is_already_home()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));
            ThingDef table = Def("TableStonecutter");

            (Settlement _, CoreMap home) = BuiltCompound();
            Thing door = TheDoor(home);
            SpawnStack(home, door.Position + new IntVec3(2, 0, 0), "ChunkGranite", 1); // hauled home, in the street
            StonecutterInitiative.Run(home);
            Blueprint inYard = Assert.Single(Planned(home, table));
            Assert.True(InTheYard(door, inYard.Position), "table at " + inYard.Position);

            (Settlement _, CoreMap quarry) = BuiltCompound();
            SpawnStack(quarry, new IntVec3(5, 0, 5), "ChunkGranite", 1);
            StonecutterInitiative.Run(quarry);
            Blueprint byStone = Assert.Single(Planned(quarry, table));
            Assert.False(quarry.settlementCompound!.Reserves(byStone.OccupiedRect()));
            Assert.True(byStone.Position.InHorDistOf(new IntVec3(5, 0, 5), WorkGiver_DoBill.IngredientSearchRadius));
        }

        [Fact]
        public void A_fallback_never_lands_on_the_compounds_ground_even_with_the_material_right_against_the_ring()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));
            (Settlement _, CoreMap map) = BuiltCompound();
            SettlementCompound compound = map.settlementCompound!;
            Thing door = TheDoor(map);

            // The yard's stonecutter site taken by something else, and the stone beside the door's threshold.
            IntVec3 site = compound.WorkSiteCells(SettlementCompound.WorkSite.Stonecutter).First();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), site, map);
            SpawnStack(map, door.Position + new IntVec3(1, 0, 0), "ChunkGranite", 1);

            StonecutterInitiative.Run(map);

            foreach (Blueprint bp in Planned(map, Def("TableStonecutter")))
            {
                Assert.False(compound.Reserves(bp.OccupiedRect()), "table planned on the compound at " + bp.Position);
                Assert.NotEqual(door.Position + new IntVec3(1, 0, 0), bp.Position);
            }
        }

        /// <summary>A citizen standing inside the ring, wearing and holding nothing, able to do violence: one
        /// who lacks both a shirt and a knife.</summary>
        private static Pawn SpawnBareCitizen(CoreMap map, Thing door)
        {
            for (int i = 0; ; i++)
            {
                Pawn p = NewHuman("Bare" + i);
                if (p.WorkTagIsDisabled(WorkTags.Violent)) continue;
                GenSpawn.Spawn(p, door.Position + new IntVec3(-2, 0, 0), map);
                Assert.Empty(p.apparel!.WornApparel);
                Assert.Null(p.equipment!.Primary);
                return p;
            }
        }

        [Theory]
        [InlineData(SettlementWorkshops.TailorDefName)]
        [InlineData(SettlementWorkshops.SmithyDefName)]
        public void An_outfitting_bench_goes_up_in_the_yard_once_a_citizen_on_the_map_lacks_what_it_makes(string benchName)
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));
            (Settlement settlement, CoreMap map) = BuiltCompound();
            Thing door = TheDoor(map);
            ThingDef bench = Def(benchName);

            // The band is on the roster but nobody stands on the map, so nobody there lacks anything.
            SettlementWorkshops.Run(settlement, map);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));

            SpawnBareCitizen(map, door);
            SettlementWorkshops.Run(settlement, map);
            Blueprint placed = Assert.Single(Planned(map, bench));
            Assert.True(InTheYard(door, placed.Position), benchName + " at " + placed.Position);

            // One of each: a second pass plans no second bench.
            SettlementWorkshops.Run(settlement, map);
            Assert.Single(Planned(map, bench));
        }

        [Fact]
        public void A_smithy_waits_for_a_weapon_recipe_to_be_researched()
        {
            (Settlement settlement, CoreMap map) = BuiltCompound();
            SpawnBareCitizen(map, TheDoor(map));

            SettlementWorkshops.Run(settlement, map);
            Assert.Empty(Planned(map, Def(SettlementWorkshops.SmithyDefName)));
            Assert.Single(Planned(map, Def(SettlementWorkshops.TailorDefName))); // a shirt needs no research

            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));
            SettlementWorkshops.Run(settlement, map);
            Assert.Single(Planned(map, Def(SettlementWorkshops.SmithyDefName)));
        }

        [Fact]
        public void A_tailors_table_goes_within_reach_of_a_cotton_field_the_yard_cannot_reach()
        {
            (Settlement settlement, CoreMap map) = BuiltCompound();
            SpawnBareCitizen(map, TheDoor(map));
            var field = new Zone_Growing { label = "Cotton field", plantDefToGrow = Def("Plant_Cotton") };
            map.zoneManager.RegisterZone(field);
            foreach (IntVec3 c in CellRect.CenteredOn(new IntVec3(8, 0, 8), 2).Cells) map.zoneManager.AddCell(field, c);

            SettlementWorkshops.Run(settlement, map);

            Blueprint placed = Assert.Single(Planned(map, Def(SettlementWorkshops.TailorDefName)));
            Assert.Contains(field.Cells, c => CellRect.CenteredOn(placed.Position, WorkGiver_DoBill.IngredientSearchRadius).Contains(c));
            Assert.False(map.settlementCompound!.Reserves(placed.OccupiedRect()));
        }

        [Fact]
        public void A_butchers_table_goes_up_in_the_yard_once_a_carcass_lies_in_reach()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));
            (Settlement settlement, CoreMap map) = BuiltCompound();
            Thing door = TheDoor(map);

            SettlementWorkshops.Run(settlement, map);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));

            Pawn deer = new Pawn(DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.race != null && d.race.Animal), "Deer");
            GenSpawn.Spawn(deer, door.Position + new IntVec3(-2, 0, 0), map);
            deer.health.Kill(null, null);
            SettlementWorkshops.Run(settlement, map);

            Blueprint placed = Assert.Single(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList());
            Assert.Same(Def(SettlementWorkshops.ButcherDefName), placed.EntityToBuild);
            Assert.True(InTheYard(door, placed.Position), "butcher's table at " + placed.Position);
        }

        [Fact]
        public void A_built_tailors_table_keeps_a_cloth_stockpile_beside_it_that_cloth_is_hauled_to_first()
        {
            (Settlement settlement, CoreMap map) = BuiltCompound();
            Thing door = TheDoor(map);
            SpawnBareCitizen(map, door);
            SettlementWorkshops.Run(settlement, map);
            Blueprint planned = Assert.Single(Planned(map, Def(SettlementWorkshops.TailorDefName)));
            Thing bench = planned.ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));

            // A granary far off, as a settlement paints one, that takes everything.
            var granary = new Zone_Stockpile();
            map.zoneManager.RegisterZone(granary);
            map.zoneManager.AddCell(granary, new IntVec3(5, 0, 5));
            granary.filter.SetAllowAll(null);

            SettlementWorkshops.Run(settlement, map);
            SettlementWorkshops.Run(settlement, map); // a second pass adds nothing

            Zone_Stockpile stock = Assert.Single(map.zoneManager.AllZones.OfType<Zone_Stockpile>(), z => !ReferenceEquals(z, granary));
            Assert.InRange(stock.CellCount, 1, SettlementWorkshops.BenchStockCells);
            Assert.All(stock.Cells, c => Assert.True(CellRect.CenteredOn(bench.Position, 2).Contains(c), "stock cell " + c));
            Assert.True(stock.filter.Allows(Def("Cloth")));
            Assert.False(stock.filter.Allows(Def("RawBerries")));

            // Cloth cut from a field near the bench goes to the bench's stock, not the granary.
            Thing cloth = SpawnStack(map, bench.Position + new IntVec3(0, 0, 5), "Cloth", 10);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, bench.Position + new IntVec3(0, 0, 6), map);
            Assert.True(global::SimWorld.AI.HaulAIUtility.TryFindBestStockpileCell(hauler, cloth, out IntVec3 to));
            Assert.Contains(to, stock.Cells);
        }

        [Fact]
        public void A_cotton_field_starts_within_reach_of_the_tailors_table_not_by_the_food_field()
        {
            (Settlement settlement, CoreMap map) = BuiltCompound();
            SpawnBareCitizen(map, TheDoor(map));
            SettlementWorkshops.Run(settlement, map);
            Thing bench = Assert.Single(Planned(map, Def(SettlementWorkshops.TailorDefName))).ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));

            // The food field far off in a corner, where cotton used to start.
            var food = new Zone_Growing { label = FarmingInitiative.FieldLabel, plantDefToGrow = Def("Plant_Potato") };
            map.zoneManager.RegisterZone(food);
            map.zoneManager.AddCell(food, new IntVec3(3, 0, 3));

            global::SimWorld.Farming.CropDemandFields.Run(map, settlement);

            Zone_Growing cotton = Assert.Single(map.zoneManager.AllZones.OfType<Zone_Growing>(), z => z.plantDefToGrow == Def("Plant_Cotton"));
            Assert.NotEqual(0, cotton.CellCount);
            CellRect reach = CellRect.CenteredOn(bench.Position, WorkGiver_DoBill.IngredientSearchRadius);
            Assert.True(reach.Contains(cotton.Cells[0]), "first cotton cell " + cotton.Cells[0] + ", bench at " + bench.Position);
            Assert.All(cotton.Cells, c => Assert.False(map.settlementCompound!.Reserves(CellRect.SingleCell(c)), "cotton on compound ground at " + c));
        }
    }
}
