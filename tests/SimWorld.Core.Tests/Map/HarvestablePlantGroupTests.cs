using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The harvest, forage and felling scans walk <see cref="ThingRequestGroup.HarvestablePlant"/> rather than every
    /// plant (tick-cost lane). They offer the same plants in the same order only if the group holds every plant that
    /// could ever be <see cref="Plant.HarvestableNow"/> and every tree, and keeps the plant list's order; this holds
    /// it to that for every plant the content ships, with some taken away again.
    /// </summary>
    public class HarvestablePlantGroupTests : ContentTestBase
    {
        public HarvestablePlantGroupTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Every_yielding_plant_and_tree_is_in_the_group_in_plant_order()
        {
            List<ThingDef> plantDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Plant && d.plant != null)
                .ToList();
            Assert.Contains(plantDefs, d => d.plant!.harvestedThingDef == null && !d.plant.IsTree);
            Assert.Contains(plantDefs, d => d.plant!.IsTree);

            var map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            int cell = 0;
            for (int round = 0; round < 3; round++)
            {
                foreach (ThingDef def in plantDefs)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(def), new IntVec3(cell % 30, 0, cell / 30), map);
                    cell++;
                }
            }
            List<Thing> spawned = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).ToList();
            for (int i = 0; i < spawned.Count; i += 4) spawned[i].Destroy();

            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            List<Thing> yielding = plants
                .Where(t => t.def.plant!.harvestedThingDef != null || t.def.plant.IsTree)
                .ToList();
            Assert.True(yielding.Count < plants.Count);
            Assert.Equal(yielding, map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant).ToList());
        }
    }
}
