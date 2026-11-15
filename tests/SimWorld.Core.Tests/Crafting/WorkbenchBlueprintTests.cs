using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// crafting.stonework.gaps: every workbench a recipe names can be raised. Smithy, TableTailor and
    /// TableButcher shipped with recipes naming them and no Blueprint/Frame pair, so no settlement could build
    /// one (Buildings_Workshops.xml).
    /// </summary>
    [Collection("GlobalDefs")]
    public class WorkbenchBlueprintTests : ContentTestBase
    {
        public WorkbenchBlueprintTests(CoreContentFixture content) : base(content)
        {
        }

        private static IEnumerable<ThingDef> BenchesRecipesName() =>
            DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .SelectMany(r => r.recipeUsers ?? new List<ThingDef>())
                .Where(d => d.category == ThingCategory.Building)
                .Distinct();

        [Fact]
        public void Every_bench_a_recipe_names_has_a_blueprint_and_a_frame()
        {
            List<ThingDef> benches = BenchesRecipesName().ToList();
            Assert.Contains(benches, b => b.defName == "Smithy");
            Assert.Contains(benches, b => b.defName == "TableTailor");
            Assert.Contains(benches, b => b.defName == "TableButcher");

            foreach (ThingDef bench in benches)
            {
                Assert.True(GenConstruct.BlueprintDefFor(bench) != null, bench.defName + " has no Blueprint.");
                Assert.True(GenConstruct.FrameDefFor(bench) != null, bench.defName + " has no Frame.");
            }
        }

        [Theory]
        [InlineData("Smithy")]
        [InlineData("TableTailor")]
        [InlineData("TableButcher")]
        public void A_placed_blueprint_becomes_the_bench(string benchName)
        {
            ThingDef bench = DefDatabase<ThingDef>.GetNamed(benchName);
            var map = new CoreMap(9, 9, TerrainDefOf.Soil);
            var cell = new IntVec3(4, 0, 4);
            Assert.True(GenConstruct.CanPlaceBlueprintAt(bench, cell, map, out string? reason), reason);

            var blueprint = (Blueprint)GenSpawn.Spawn(ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(bench)!), cell, map);
            Frame frame = blueprint.ReplaceWithFrame();
            Assert.True(frame.MaterialsFullySatisfied(), "a bench with no costList needs nothing delivered");

            Pawn builder = NewHuman("builder");
            GenSpawn.Spawn(builder, new IntVec3(1, 0, 1), map);
            Thing built = frame.CompleteConstruction(builder);

            Assert.Same(bench, built.def);
            Assert.True(built.Spawned);
            Assert.Equal(cell, built.Position);
        }
    }
}
