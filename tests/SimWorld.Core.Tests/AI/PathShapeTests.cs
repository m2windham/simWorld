using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// ai.pathingshape: regions grow by geometry alone, so on open terrain one region holds nearly every
    /// walkable cell (measured at 99-100% of walkable on six of seven generated 250x250 maps). A corridor that
    /// is just the region the pawn already stands in narrows nothing, and was costing a corridor lookup per
    /// expanded neighbour for the same path. It is not built any more; a corridor across regions still is.
    /// </summary>
    public class PathShapeTests : ContentTestBase
    {
        public PathShapeTests(CoreContentFixture content) : base(content)
        {
        }

        private static void Spawn(CoreMap map, string def, int x, int z) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def)), new IntVec3(x, 0, z), map);

        /// <summary>A 21x11 field with a walled house in the middle (x 5..15, z 0..9) and a door in each end
        /// wall. Outside, the left field, the free top row and the right field are all one region.</summary>
        private static CoreMap HouseInAField()
        {
            var map = new CoreMap(21, 11, TerrainDefOf.Soil);
            for (int x = 5; x <= 15; x++)
            {
                Spawn(map, "Wall", x, 0);
                Spawn(map, "Wall", x, 9);
            }
            for (int z = 1; z <= 8; z++)
            {
                Spawn(map, z == 5 ? "Door" : "Wall", 5, z);
                Spawn(map, z == 5 ? "Door" : "Wall", 15, z);
            }
            map.regionAndRoomUpdater.RebuildIfNeeded();
            return map;
        }

        [Fact]
        public void No_corridor_is_built_inside_the_region_the_pawn_already_stands_in()
        {
            CoreMap map = HouseInAField();
            var cache = new RegionPathCorridorCache(map);
            var corridor = new System.Collections.Generic.HashSet<Region>();
            IntVec3 left = new IntVec3(2, 0, 5), right = new IntVec3(18, 0, 5), inside = new IntVec3(10, 0, 5);
            Assert.Same(map.regionGrid.RegionAt(left), map.regionGrid.RegionAt(right));

            Assert.False(cache.TryBuildCorridor(left, new[] { right }, 1, corridor));
            Assert.Empty(corridor);

            // Across regions the corridor still narrows: outside, through a door, into the house.
            Assert.True(cache.TryBuildCorridor(left, new[] { inside }, 1, corridor));
            Assert.Equal(3, corridor.Count);
        }

        [Fact]
        public void Two_points_in_one_region_take_the_short_way_through_another()
        {
            CoreMap map = HouseInAField();
            Pawn pawn = NewHuman("walker");
            IntVec3 start = new IntVec3(2, 0, 5);
            GenSpawn.Spawn(pawn, start, map);

            PawnPath path = map.pathFinder.FindPath(pawn, start, new IntVec3(18, 0, 5), PathEndMode.OnCell);

            // Straight through both doors is 16 steps; round the house over the top row is at least 26. A
            // corridor of the one outdoor region forbade the house and walked round it.
            Assert.True(path.Found);
            Assert.True(path.NodesLeftCount <= 18, $"took {path.NodesLeftCount} steps; the way through the house is 16");
            path.ReleaseToPool();
        }
    }
}
