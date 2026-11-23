using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A refused placement says what is actually in the way. The playtest clicked a cliff and was told "a
    /// building already occupies this cell": natural rock is an edifice, so the one generic reason covered it.
    /// </summary>
    public class PlacementRefusalReasonTests : ContentTestBase
    {
        public PlacementRefusalReasonTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        [Fact]
        public void A_cliff_refusal_names_the_rock_not_a_building()
        {
            var map = new CoreMap(9, 9, SimWorld.Map.TerrainDefOf.Soil);
            var cell = new IntVec3(4, 0, 4);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Granite")), cell, map);

            Assert.False(GenConstruct.CanPlaceBlueprintAt(Def("Wall"), cell, map, out string? reason));
            Assert.Contains("Granite", reason);
            Assert.Contains("mined", reason);
            Assert.DoesNotContain("building", reason);
        }

        [Fact]
        public void A_building_refusal_names_the_building()
        {
            var map = new CoreMap(9, 9, SimWorld.Map.TerrainDefOf.Soil);
            var cell = new IntVec3(4, 0, 4);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall"), GenStuffDefault(Def("Wall"))), cell, map);

            Assert.False(GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.StorageHut, cell, map, out string? reason));
            Assert.Contains(Def("Wall").LabelCap, reason);
        }

        private static ThingDef? GenStuffDefault(ThingDef def) => def.MadeFromStuff ? global::SimWorld.Crafting.GenStuff.DefaultStuffFor(def) : null;
    }
}
