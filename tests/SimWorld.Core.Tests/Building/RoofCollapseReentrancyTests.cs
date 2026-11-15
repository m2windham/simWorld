using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A falling roof that destroys a roof holder must not start a second collapse inside the first.
    ///
    /// <para/><b>Both of these used to end the process, not fail.</b> <c>tools/bench --suite storyteller</c>
    /// died twice in twenty-seven runs with a stack eight frames wide and some 870 repeats deep —
    /// <c>CrushUnderRoof → TakeDamage → Destroy → DeSpawn → EdificeGrid.DeRegister →
    /// ProcessRoofHolderDespawned → DropRoofInCells → DropRoofInCellPhaseOne</c> and round again — started by
    /// a fire finishing off a wall (<c>docs/perf/storyteller-pressure/crashed/</c>). A test that regresses
    /// here will therefore take the test host down with it rather than report a red line; that is the
    /// defect's own shape and it is loud enough.
    ///
    /// <para/>There were two defects and each test below pins one (both mutations were run). Put the
    /// old despawn order back in <c>Thing.DeSpawn</c> and the first test fails: the holder is hit by the roof
    /// it has just let go of and leaves the map twice. Take the worklist out of
    /// <see cref="RoofCollapseCellsFinder"/> and the second test overflows the stack. Either fix alone
    /// stops the crash the bench found; the two together are RimWorld's shape.
    /// </summary>
    public class RoofCollapseReentrancyTests : ContentTestBase
    {
        public RoofCollapseReentrancyTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A wall that counts how many times it left the map.</summary>
        private sealed class CountingWall : Thing
        {
            public int Despawns;

            public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
            {
                if (Spawned) Despawns++;
                base.DeSpawn(mode);
            }
        }

        private static CountingWall SpawnWall(CoreMap map, IntVec3 cell)
        {
            var wall = new CountingWall { def = Def("Wall"), thingIDNumber = Thing.AllocateThingId() };
            wall.PostMake();
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        /// <summary>Something sturdy to stand under the roof and count how many times it landed.</summary>
        private static Thing SpawnBystander(CoreMap map, IntVec3 cell)
        {
            Thing log = ThingMaker.MakeThing(Def("WoodLog"));
            log.HitPoints = 1000;
            GenSpawn.Spawn(log, cell, map);
            return log;
        }

        private static void AssertHitExactlyOnce(Thing bystander)
        {
            int lost = 1000 - bystander.HitPoints;
            Assert.InRange(lost, RoofCollapserImmediate.ThinRoofCrushDamageRange.min, RoofCollapserImmediate.ThinRoofCrushDamageRange.max);
        }

        /// <summary>
        /// The way in the bench found. A wall that is the only thing holding its own roof up is destroyed by
        /// damage — so it reaches its despawn at zero hit points — and the roof it was holding comes down on
        /// the cell it is leaving. The wall was still listed in that cell, the roof landed on it, zero hit
        /// points meant "destroy", and destroying it despawned it again from inside its own despawn.
        /// </summary>
        [Fact]
        public void A_holder_destroyed_by_damage_under_the_roof_it_alone_holds_brings_that_roof_down_once()
        {
            CoreMap map = new CoreMap(12, 12, SimWorld.Map.TerrainDefOf.Soil);
            var wallCell = new IntVec3(5, 0, 5);
            var beside = new IntVec3(6, 0, 5);
            CountingWall wall = SpawnWall(map, wallCell);
            map.roofGrid.SetRoof(wallCell, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(beside, SimWorld.Map.RoofDefOf.RoofConstructed);
            Thing bystander = SpawnBystander(map, beside);

            wall.TakeDamage(new DamageInfo(RoofCollapseDefOf.Crush, wall.HitPoints));

            Assert.True(wall.Destroyed);
            Assert.False(wall.Spawned);
            Assert.Equal(1, wall.Despawns);
            Assert.False(map.roofGrid.Roofed(wallCell));
            Assert.False(map.roofGrid.Roofed(beside));
            Assert.Empty(map.thingGrid.ThingsListAt(wallCell));
            AssertHitExactlyOnce(bystander);
        }

        /// <summary>
        /// The general way in: a long chain of roof holders brought down by one event. Several edifices can
        /// share a cell and only the last one spawned is the cell's edifice, so when that one goes the roof
        /// falls on the rest — and every one of those it destroys is a roof holder leaving the map in the
        /// middle of a collapse. Each used to start a collapse of its own inside the one that killed it, one
        /// nesting per holder; three thousand of them is far past what a stack holds. Now each only adds to
        /// the list already being worked through, and the roof lands on everything under it exactly once.
        /// </summary>
        [Fact]
        public void A_long_chain_of_holders_destroyed_by_one_collapse_is_worked_through_without_nesting()
        {
            const int Chain = 3000;
            CoreMap map = new CoreMap(12, 12, SimWorld.Map.TerrainDefOf.Soil);
            var cell = new IntVec3(5, 0, 5);
            var beside = new IntVec3(6, 0, 5);

            var chain = new Thing[Chain];
            for (int i = 0; i < Chain; i++)
            {
                chain[i] = SpawnWall(map, cell);
                chain[i].HitPoints = 1;
            }
            Thing keystone = SpawnWall(map, cell);
            map.roofGrid.SetRoof(cell, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(beside, SimWorld.Map.RoofDefOf.RoofConstructed);
            Thing bystander = SpawnBystander(map, beside);

            keystone.Destroy();

            for (int i = 0; i < Chain; i++) Assert.True(chain[i].Destroyed, "holder " + i + " survived the roof that fell on it");
            Assert.False(map.roofGrid.Roofed(cell));
            Assert.False(map.roofGrid.Roofed(beside));
            Assert.Empty(map.thingGrid.ThingsListAt(cell));
            AssertHitExactlyOnce(bystander);
        }
    }
}
