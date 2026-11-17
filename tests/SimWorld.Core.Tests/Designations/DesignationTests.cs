using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Designations
{
    /// <summary>The designation layer (RimWorld: <c>Verse.DesignationManager</c>, <c>Verse.Designation</c>).</summary>
    public class DesignationTests : ContentTestBase
    {
        public DesignationTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(20, 20, TerrainDefOf.Soil);

        private static Thing Rock(CoreMap map, IntVec3 c, string def = "Limestone") =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def)), c, map);

        private static Designation Mine(IntVec3 c) => new Designation(new LocalTargetInfo(c), DesignationDefOf.Mine);

        [Fact]
        public void Mining_a_designated_rock_takes_its_designation_with_it()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(5, 0, 5);
            var rock = (Mineable)Rock(map, cell);
            map.designationManager.AddDesignation(Mine(cell));
            Assert.True(map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Mine));

            rock.DestroyMined(null);

            Assert.Null(map.designationManager.DesignationAt(cell, DesignationDefOf.Mine));
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_cell_cannot_be_designated_twice_for_the_same_work()
        {
            CoreMap map = NewMap();
            map.designationManager.AddDesignation(Mine(new IntVec3(3, 0, 3)));
            Assert.Throws<InvalidOperationException>(() => map.designationManager.AddDesignation(Mine(new IntVec3(3, 0, 3))));
        }

        [Fact]
        public void Mine_designations_survive_a_scribe_round_trip()
        {
            CoreMap map = NewMap();
            var a = new IntVec3(4, 0, 4);
            var b = new IntVec3(9, 0, 2);
            Rock(map, a);
            Rock(map, b);
            map.designationManager.AddDesignation(Mine(a));
            map.designationManager.AddDesignation(Mine(b));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            IReadOnlyList<Designation> mine = loaded.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine);
            Assert.Equal(new[] { a, b }, new[] { mine[0].target.Cell, mine[1].target.Cell });
            Assert.Equal(2, mine.Count);
            Assert.Same(loaded.designationManager, mine[0].designationManager);
        }
    }
}
