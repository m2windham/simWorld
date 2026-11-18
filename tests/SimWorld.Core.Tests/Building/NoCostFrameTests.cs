using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Work;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A blueprint whose building has no cost list is built by the ordinary work loop
    /// (<see cref="JobDriver_PlaceNoCostFrame"/>): nothing is ever delivered to it, so before this it stayed a
    /// blueprint for good.
    /// </summary>
    [Collection("GlobalDefs")]
    public class NoCostFrameTests : ContentTestBase
    {
        public NoCostFrameTests(CoreContentFixture content) : base(content)
        {
        }

        [Theory]
        [InlineData("FueledStove")]
        public void A_builder_raises_a_bench_that_costs_nothing(string benchName)
        {
            ThingDef bench = DefDatabase<ThingDef>.GetNamed(benchName);
            Assert.True(bench.costList == null || bench.costList.Count == 0, "test setup: " + benchName + " has a cost list now");
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            var cell = new IntVec3(10, 0, 10);
            GenSpawn.Spawn(ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(bench)!), cell, map);
            Pawn builder = NewHuman("Builder");
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            GenSpawn.Spawn(builder, new IntVec3(3, 0, 3), map);

            RunTicks(4000, builder);

            Thing built = Assert.Single(map.listerThings.ThingsOfDef(bench));
            Assert.Equal(cell, built.Position);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
        }
    }
}
