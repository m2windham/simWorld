using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A settlement's field reaches fertile ground beyond <see cref="GenRadial.MaxRadius"/> of where it started
    /// (<see cref="FarmingInitiative"/>). On the threat-free bench (band 20, seed 110, a desert) the field
    /// stopped at 211 of the 642 cells its mouths wanted because the rest of the map's 2,502 sowable cells lay
    /// outside the radial pattern, and all 22 starved.
    /// </summary>
    public class FarmingFieldReachTests : ContentTestBase
    {
        public FarmingFieldReachTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_field_whose_only_fertile_ground_lies_beyond_the_radial_reach_still_gets_planted()
        {
            const int size = 160;
            var map = new CoreMap(size, size, TerrainDefOf.Sand);
            // The only ground that takes a crop: a block in the far corner, well past GenRadial.MaxRadius from
            // the band standing at the origin.
            var patch = new CellRect(120, 120, 30, 30);
            foreach (IntVec3 c in patch.Cells) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            Assert.True((new IntVec3(120, 0, 120) - new IntVec3(2, 0, 2)).LengthHorizontal > GenRadial.MaxRadius);

            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Sandhold", 0);
            for (int i = 0; i < 4; i++)
            {
                var p = new Pawn(Human, "Farmer" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(2 + i, 0, 2), map);
            }
            int wanted = FarmingInitiative.FieldCellsWantedFor(settlement, map);
            Assert.True(wanted > 0 && wanted <= patch.Area, "the test needs a target the patch can meet");

            for (int i = 0; i < 40; i++) FarmingInitiative.Run(map);

            Zone_Growing? field = FarmingInitiative.FieldOf(map);
            Assert.NotNull(field);
            Assert.Equal(wanted, field!.CellCount);
            foreach (IntVec3 c in field.Cells) Assert.True(patch.Contains(c), $"field cell {c} is not on the fertile patch");
        }
    }
}
