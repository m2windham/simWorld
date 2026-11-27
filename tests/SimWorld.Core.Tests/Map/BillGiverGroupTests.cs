using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The crafting and economy initiatives look for benches in <see cref="ThingRequestGroup.PotentialBillGiver"/>
    /// rather than in every building (tick-cost lane). That finds the same benches in the same order only if every
    /// building carrying a <see cref="CompBillGiver"/> is in the group and the group keeps the building list's order;
    /// this holds both for every bench the content ships, spawned among rock and walls and with some taken away.
    /// </summary>
    public class BillGiverGroupTests : ContentTestBase
    {
        public BillGiverGroupTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Every_bench_is_a_potential_bill_giver_in_the_building_order()
        {
            List<ThingDef> benches = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building && d.HasComp(typeof(CompBillGiver)))
                .ToList();
            Assert.NotEmpty(benches);

            var map = new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);
            int x = 1;
            foreach (ThingDef bench in benches)
            {
                ThingDef? stuff = bench.MadeFromStuff ? GenStuff.DefaultStuffFor(bench) : null;
                GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), new IntVec3(x, 0, 1), map);
                GenSpawn.Spawn(ThingMaker.MakeThing(bench, stuff), new IntVec3(x, 0, 10), map);
                GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall"), DefDatabase<ThingDef>.GetNamed("WoodLog")), new IntVec3(x, 0, 30), map);
                x += 4;
            }
            // Take a few away again, so the order is held through removals too.
            List<Thing> spawned = map.listerThings.ThingsInGroup(ThingRequestGroup.Building).ToList();
            for (int i = 0; i < spawned.Count; i += 5) spawned[i].Destroy();

            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            List<Thing> withComp = buildings.Where(t => (t as ThingWithComps)?.GetComp<CompBillGiver>() != null).ToList();
            Assert.NotEmpty(withComp);
            List<Thing> group = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).ToList();

            // The walk the initiatives make, through either list, finds the same benches in the same order.
            Assert.Equal(withComp, group.Where(t => (t as ThingWithComps)?.GetComp<CompBillGiver>() != null).ToList());
            Assert.Equal(buildings.Where(group.Contains).ToList(), group);
        }
    }
}
