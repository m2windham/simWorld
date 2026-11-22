using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A settlement places Mine designations by need (<see cref="MiningInitiative"/>) and
    /// <see cref="WorkGiver_Miner"/> mines designated rock only, as RimWorld's does.
    /// </summary>
    public class MiningInitiativeTests : ContentTestBase
    {
        public MiningInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Thing Rock(CoreMap map, int x, int z, string def = "Limestone") =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Def(def)), new IntVec3(x, 0, z), map);

        private static Settlement NewSettlement() => new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quarrytown", 0);

        private static List<IntVec3> Designated(CoreMap map) =>
            map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine).Select(d => d.target.Cell).ToList();

        private static int CellsFor(ThingDef yield, int need) =>
            (int)Math.Ceiling(need / MiningInitiative.ExpectedYieldPerCell(yield));

        [Fact]
        public void The_miner_offers_designated_rock_only()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);
            Thing undesignated = Rock(map, 5, 5);
            Thing designated = Rock(map, 9, 5);
            var miner = new WorkGiver_Miner();

            Assert.True(miner.ShouldSkip(pawn));
            Assert.Empty(miner.PotentialWorkThingsGlobal(pawn));

            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(designated.Position), DesignationDefOf.Mine));

            Assert.False(miner.ShouldSkip(pawn));
            Assert.Equal(new[] { designated }, miner.PotentialWorkThingsGlobal(pawn));
            Assert.True(miner.HasJobOnThing(pawn, designated));
            Assert.False(miner.HasJobOnThing(pawn, undesignated));

            // Designated rock deep in a face is not offered until a miner could stand next to it.
            for (int x = 19; x <= 21; x++) for (int z = 19; z <= 21; z++) Rock(map, x, z);
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(new IntVec3(20, 0, 20)), DesignationDefOf.Mine));
            Assert.Equal(new[] { designated }, miner.PotentialWorkThingsGlobal(pawn));
        }

        [Fact]
        public void A_settlement_that_needs_no_stone_designates_no_rock()
        {
            CoreMap map = NewMap();
            for (int x = 5; x < 30; x += 3) Rock(map, x, 10, "MineableSteel");
            for (int x = 5; x < 30; x += 3) Rock(map, x, 20);

            MiningInitiative.Run(NewSettlement(), map);

            Assert.Empty(Designated(map));
        }

        [Fact]
        public void Steel_a_blueprint_needs_is_designated_nearest_first_and_only_what_it_takes()
        {
            CoreMap map = NewMap();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Battery")), new IntVec3(30, 0, 30), map);
            // Steel ore along a row east of the map centre (20, 20), limestone nearer than any of it.
            for (int x = 37; x > 21; x -= 3) Rock(map, x, 20, "MineableSteel"); // listed far to near
            Rock(map, 20, 18);

            MiningInitiative.Run(NewSettlement(), map);

            // Steel is smelted from iron ore since crafting.materialladder, so the need is read back through the
            // first recipe (by defName) that makes steel straight from a mined yield.
            int need = Def("Battery").costList!.Single(c => c.thingDef == Def("Steel")).count;
            var smelt = DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed("Smelt_Steel");
            int perIteration = smelt.products!.Single().count;
            int orePerIteration = (int)smelt.ingredients!.Single().count;
            int cells = CellsFor(Def("IronOre"), (need + perIteration - 1) / perIteration * orePerIteration);
            List<IntVec3> designated = Designated(map);
            Assert.Equal(cells, designated.Count);
            Assert.All(designated, c => Assert.Equal("MineableSteel", map.edificeGrid[c]!.def.defName));
            Assert.Equal(Enumerable.Range(0, cells).Select(i => new IntVec3(22 + 3 * i, 0, 20)), designated);
        }

        [Fact]
        public void Steel_already_in_the_stores_is_not_mined_again()
        {
            CoreMap map = NewMap();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Battery")), new IntVec3(30, 0, 30), map);
            Rock(map, 22, 20, "MineableSteel");
            Settlement settlement = NewSettlement();
            settlement.AddStore(Def("Steel"), Def("Battery").costList!.Single(c => c.thingDef == Def("Steel")).count);

            MiningInitiative.Run(settlement, map);

            Assert.Empty(Designated(map));
        }

        [Fact]
        public void Blocks_a_blueprint_needs_are_mined_as_the_chunks_they_are_cut_from()
        {
            CoreMap map = NewMap();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_WallLimestone")), new IntVec3(30, 0, 30), map);
            for (int x = 2; x < 38; x += 2) Rock(map, x, 5);
            for (int x = 2; x < 38; x += 2) Rock(map, x, 35, "Granite");

            MiningInitiative.Run(NewSettlement(), map);

            // One iteration of the blocks recipe covers the wall, and it takes one chunk.
            int cells = Math.Min(MiningTuning.MaxOutstandingDesignations, CellsFor(Def("ChunkLimestone"), 1));
            List<IntVec3> designated = Designated(map);
            Assert.Equal(cells, designated.Count);
            Assert.All(designated, c => Assert.Equal("Limestone", map.edificeGrid[c]!.def.defName));
        }

        [Fact]
        public void A_bill_on_a_bench_asks_for_the_chunks_it_will_cut()
        {
            CoreMap map = NewMap();
            Thing bench = GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableStonecutter")), new IntVec3(20, 0, 20), map);
            var recipe = DefDatabase<RecipeDef>.GetNamed("Make_Blocks_Limestone");
            ((ThingWithComps)bench).GetComp<CompBillGiver>()!.BillStack.AddBill(
                new Bill_Production(recipe) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 1 });
            Rock(map, 5, 5);

            Assert.Equal(1, MiningInitiative.YieldShortfall(null, map).Single(kv => kv.Key == Def("ChunkLimestone")).Value);
            MiningInitiative.Run(NewSettlement(), map);
            Assert.Single(Designated(map));
        }

        [Fact]
        public void Never_more_than_the_cap_are_open_at_once()
        {
            CoreMap map = NewMap();
            for (int i = 0; i < 5; i++) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_WallLimestone")), new IntVec3(30 + i, 0, 30), map);
            for (int x = 2; x < 38; x += 2) Rock(map, x, 5);

            MiningInitiative.Run(NewSettlement(), map);
            MiningInitiative.Run(NewSettlement(), map);

            Assert.Equal(MiningTuning.MaxOutstandingDesignations, Designated(map).Count);
        }

        [Fact]
        public void Rock_holding_a_roof_up_is_not_designated_and_a_designation_that_comes_to_is_withdrawn()
        {
            CoreMap map = NewMap();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Battery")), new IntVec3(30, 0, 30), map);
            Thing holder = Rock(map, 22, 20, "MineableSteel");
            Rock(map, 34, 20, "MineableSteel");
            map.roofGrid.SetRoof(new IntVec3(23, 0, 20), RoofDefOf.RoofRockThin);
            Assert.True(RoofCollapseUtility.WouldCollapseRoofIfRemoved(holder));

            MiningInitiative.Run(NewSettlement(), map);
            Assert.Equal(new[] { new IntVec3(34, 0, 20) }, Designated(map));

            // A designation whose rock has come to hold a roof up is withdrawn.
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(holder.Position), DesignationDefOf.Mine));
            MiningInitiative.Run(NewSettlement(), map);
            Assert.DoesNotContain(holder.Position, Designated(map));
        }

        [Fact]
        public void Rock_standing_on_the_compound_ground_is_cleared_and_rock_beside_it_is_not()
        {
            CoreMap map = NewMap(120);
            SettlementCompound compound = Assert.IsType<SettlementCompound>(SettlementCompound.TryPlan(map, 20, 50));
            map.settlementCompound = compound;
            CellRect yard = compound.WorkYard;
            Thing inYard = Rock(map, yard.minX, yard.minZ);
            IntVec3 clear = new[] { new IntVec3(0, 0, 0), new IntVec3(119, 0, 0), new IntVec3(0, 0, 119), new IntVec3(119, 0, 119) }
                .First(c => !compound.Village.ExpandedBy(1).Contains(c));
            Thing outside = Rock(map, clear.x, clear.z);

            MiningInitiative.Run(NewSettlement(), map);

            List<IntVec3> designated = Designated(map);
            Assert.Contains(inYard.Position, designated);
            Assert.DoesNotContain(outside.Position, designated);
        }
    }
}
