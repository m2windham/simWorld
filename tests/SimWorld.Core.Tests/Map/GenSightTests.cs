using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.Tests.Content;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>Line of sight over a map's own grids (system 12: Combat — cover), a fresh Bresenham walk, not a region-graph query (see <see cref="GenSight"/>'s own remarks for why).</summary>
    public class GenSightTests : ContentTestBase
    {
        public GenSightTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Thing SpawnWall(CoreMap map, IntVec3 cell)
        {
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        [Fact]
        public void Open_ground_has_line_of_sight()
        {
            CoreMap map = NewMap(10, 10);
            Assert.True(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(9, 0, 0), map));
            Assert.True(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(9, 0, 9), map)); // diagonal
        }

        [Fact]
        public void A_wall_directly_between_blocks_sight()
        {
            CoreMap map = NewMap(10, 10);
            SpawnWall(map, new IntVec3(5, 0, 0));
            Assert.False(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(9, 0, 0), map));
        }

        [Fact]
        public void A_wall_off_the_line_does_not_block_it()
        {
            CoreMap map = NewMap(10, 10);
            SpawnWall(map, new IntVec3(5, 0, 3)); // three rows off the shooter's own row
            Assert.True(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(9, 0, 0), map));
        }

        [Fact]
        public void The_destination_cells_own_occupant_never_blocks_the_shot()
        {
            CoreMap map = NewMap(10, 10);
            SpawnWall(map, new IntVec3(9, 0, 0)); // a wall standing right on the target cell itself
            Assert.True(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(9, 0, 0), map));
        }

        [Fact]
        public void SkipFirstCell_ignores_a_wall_on_the_shooters_own_cell()
        {
            CoreMap map = NewMap(10, 10);
            var shooterCell = new IntVec3(0, 0, 0);
            SpawnWall(map, shooterCell);
            Assert.False(GenSight.LineOfSight(shooterCell, new IntVec3(9, 0, 0), map, skipFirstCell: false));
            Assert.True(GenSight.LineOfSight(shooterCell, new IntVec3(9, 0, 0), map, skipFirstCell: true));
        }

        [Fact]
        public void Out_of_bounds_cells_never_have_line_of_sight()
        {
            CoreMap map = NewMap(5, 5);
            Assert.False(GenSight.LineOfSight(new IntVec3(0, 0, 0), new IntVec3(20, 0, 20), map));
            Assert.False(GenSight.LineOfSight(new IntVec3(-1, 0, 0), new IntVec3(2, 0, 2), map));
        }
    }
}
