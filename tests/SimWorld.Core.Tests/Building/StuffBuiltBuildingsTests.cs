using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The buildings RimWorld makes from stuff — door, bed and the work benches — are made from stuff here too:
    /// each costs its <c>costStuffCount</c> of a chosen stuff instead of a hand-written <c>costList</c> of wood,
    /// a settlement's planner picks a stuff it has on hand (wood first), and the stuff's MaxHitPoints factor
    /// reaches the finished thing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class StuffBuiltBuildingsTests : ContentTestBase
    {
        public StuffBuiltBuildingsTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wood => Def("WoodLog");
        private static ThingDef Steel => Def("Steel");
        private static ThingDef Granite => Def("BlocksGranite");

        public static IEnumerable<object[]> StuffBuilt() =>
            new[] { "Door", "Bed", "TableButcher", "TableStonecutter", "Smithy", "TableTailor", "ResearchBench" }
                .Select(n => new object[] { n });

        [Theory]
        [MemberData(nameof(StuffBuilt))]
        public void A_stuff_built_building_costs_its_stuff_and_no_hand_written_wood(string defName)
        {
            ThingDef def = Def(defName);
            Assert.True(def.MadeFromStuff, defName + " is made from stuff");
            Assert.True(def.costStuffCount > 0, defName + " has a stuff cost");
            foreach (ThingDef stuff in new[] { Wood, Steel, Granite }) Assert.True(GenStuff.CanMake(stuff, def));
            Assert.False(GenStuff.CanMake(Def("Cloth"), def));

            // Nothing but its stuff: the old costList wood is gone, so a granite one costs no wood at all.
            Assert.Equal(new[] { (Wood, def.costStuffCount) }, def.CostListAdjusted(null).Select(e => (e.thingDef, e.count)));
            Assert.Equal(new[] { (Granite, def.costStuffCount) }, def.CostListAdjusted(Granite).Select(e => (e.thingDef, e.count)));
        }

        [Fact]
        public void A_bed_takes_its_stuffs_hit_points_wood_below_the_def_and_granite_above()
        {
            ThingDef bed = Def("Bed");
            int wooden = ThingMaker.MakeThing(bed, Wood).MaxHitPoints;
            int steel = ThingMaker.MakeThing(bed, Steel).MaxHitPoints;
            int granite = ThingMaker.MakeThing(bed, Granite).MaxHitPoints;

            Assert.True(wooden < bed.BaseMaxHitPoints, "wood weakens: " + wooden);
            Assert.Equal(bed.BaseMaxHitPoints, steel); // steel's factor is 1
            Assert.True(granite > bed.BaseMaxHitPoints, "granite toughens: " + granite);
            // A fresh one starts whole at its stuff's maximum.
            Thing fresh = ThingMaker.MakeThing(bed, Wood);
            Assert.Equal(fresh.MaxHitPoints, fresh.HitPoints);
        }

        private static CoreMap NewMap() => new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);

        private static void SpawnStack(CoreMap map, ThingDef def, int count, IntVec3 at)
        {
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, at, map);
        }

        [Fact]
        public void A_planner_takes_wood_when_it_has_enough_else_a_stuff_it_has_else_wood()
        {
            ThingDef bed = Def("Bed");
            int need = bed.costStuffCount;

            CoreMap empty = NewMap();
            Assert.Same(Wood, GenStuff.AffordableStuffFor(empty, bed)); // nothing on hand: ask for wood

            CoreMap steelOnly = NewMap();
            SpawnStack(steelOnly, Wood, need - 1, new IntVec3(1, 0, 1));
            SpawnStack(steelOnly, Steel, need, new IntVec3(2, 0, 1));
            Assert.Same(Steel, GenStuff.AffordableStuffFor(steelOnly, bed));

            CoreMap both = NewMap();
            SpawnStack(both, Wood, need, new IntVec3(1, 0, 1));
            SpawnStack(both, Steel, need, new IntVec3(2, 0, 1));
            Assert.Same(Wood, GenStuff.AffordableStuffFor(both, bed));

            Assert.Null(GenStuff.AffordableStuffFor(both, Def("StorageHut"))); // not made from stuff
        }

        /// <summary>The works initiative raises its research bench out of what the settlement has: with no wood
        /// and a bench's worth of steel, a steel bench, not a wooden one nobody can supply.</summary>
        [Fact]
        public void The_works_initiative_lays_its_bench_in_a_stuff_the_map_holds()
        {
            CoreMap map = NewMap();
            ThingDef bench = Def("ResearchBench");
            SpawnStack(map, Steel, bench.costStuffCount, new IntVec3(1, 0, 1));

            Assert.True(SettlementWorksInitiative.TryPlaceBlueprint(map, bench, anchor: new IntVec3(15, 0, 15), radius: 4));

            Blueprint blueprint = Assert.Single(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>());
            Assert.Same(Steel, blueprint.stuffToUse);
            Assert.Equal(new[] { (Steel, bench.costStuffCount) }, blueprint.TotalMaterialCost().Select(e => (e.thingDef, e.count)));
        }
    }
}
