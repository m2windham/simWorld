using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// What a settlement builds for itself never walls off any of the ground it stands on. Random placement
    /// was harmless while nothing it placed could be built; once the map had wood, storage huts went up by the
    /// hundred around the road hub and closed into mazes, and on seed 777 of the storyteller bench a citizen
    /// was shut in among them by day 12. Every walkable cell has to stay reachable from every other, however
    /// much the settlement builds.
    /// </summary>
    public class PlacementConnectivityTests : ContentTestBase
    {
        public PlacementConnectivityTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A settlement with no citizens and enough in store to want <paramref name="huts"/> storage
        /// huts, so every blueprint it places is an impassable one.</summary>
        private static Settlement HoardingSettlement(int huts)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Hoard", 0);
            settlement.AddStore(Def("WoodLog"), huts * ConstructionInitiativeTuning.GoodsPerStorageHut);
            return settlement;
        }

        private static void RunPasses(Settlement settlement, CoreMap map, int passes)
        {
            for (int i = 0; i < passes; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
        }

        /// <summary>Turns every blueprint into the finished building at once, as if the builders had caught up.</summary>
        private static int BuildEverything(CoreMap map)
        {
            List<Blueprint> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList();
            foreach (Blueprint bp in blueprints)
            {
                ThingDef built = bp.EntityToBuild;
                IntVec3 at = bp.Position;
                bp.Destroy();
                GenSpawn.Spawn(ThingMaker.MakeThing(built), at, map);
            }
            return blueprints.Count;
        }

        /// <summary>How many separate pieces the walkable ground is in, moving the way a pawn does: to any
        /// cardinal neighbour, and diagonally only when neither cardinal cell beside the step is blocked.</summary>
        private static int WalkableComponents(CoreMap map)
        {
            var seen = new HashSet<IntVec3>();
            int components = 0;
            foreach (IntVec3 start in map.AllCells)
            {
                if (seen.Contains(start) || !map.pathGrid.Walkable(start)) continue;
                components++;
                var queue = new Queue<IntVec3>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    IntVec3 c = queue.Dequeue();
                    foreach (IntVec3 offset in GenAdj.AdjacentCells)
                    {
                        IntVec3 n = c + offset;
                        if (!GenGrid.InBounds(n, map) || seen.Contains(n) || !map.pathGrid.Walkable(n)) continue;
                        if (offset.x != 0 && offset.z != 0
                            && (!map.pathGrid.Walkable(new IntVec3(n.x, 0, c.z)) || !map.pathGrid.Walkable(new IntVec3(c.x, 0, n.z))))
                        {
                            continue;
                        }
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            return components;
        }

        [Fact]
        public void Hundreds_of_huts_around_the_hub_leave_every_walkable_cell_reachable()
        {
            var map = new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);
            Settlement settlement = HoardingSettlement(huts: 400);

            RunPasses(settlement, map, passes: 140);
            int built = BuildEverything(map);

            Assert.True(built >= 300, "The settlement should have planned hundreds of huts to test against; it planned " + built + ".");
            Assert.Equal(1, WalkableComponents(map));
        }

        [Fact]
        public void A_hut_that_would_be_the_only_way_through_is_refused_and_open_ground_is_not()
        {
            // Two walls across the map, each with a gap at x = 4: the band of ground between them connects
            // the gaps, the band's two halves and the open ground above and below only through (4, 4).
            var map = new CoreMap(9, 9, SimWorld.Map.TerrainDefOf.Soil);
            for (int x = 0; x < 9; x++)
            {
                if (x == 4) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(x, 0, 3), map);
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(x, 0, 5), map);
            }
            Settlement settlement = HoardingSettlement(huts: 1);

            // The home area holds only the crossing: the hut waits rather than cut the map in four.
            map.areaManager.Home[new IntVec3(4, 0, 4)] = true;
            RunPasses(settlement, map, passes: 1);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.NotNull(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map));

            // Open ground beside it: the hut goes there.
            map.areaManager.Home[new IntVec3(1, 0, 7)] = true;
            RunPasses(settlement, map, passes: 1);
            Blueprint placed = Assert.Single(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>());
            Assert.Equal(new IntVec3(1, 0, 7), placed.Position);
            Assert.Equal(1, WalkableComponents(map));
        }
    }
}
