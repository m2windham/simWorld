using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Roof support flood-fill and collapse (system 16: Building — roof collapse).</summary>
    public class RoofCollapseTests : ContentTestBase
    {
        public RoofCollapseTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static global::SimWorld.Building.Building SpawnWall(CoreMap map, IntVec3 cell)
        {
            var wall = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            return wall;
        }

        [Fact]
        public void A_roofed_cell_far_from_any_remaining_support_collapses_when_its_only_support_is_removed()
        {
            CoreMap map = NewMap(20, 3);
            var z = new IntVec3(0, 0, 1);
            SpawnWall(map, new IntVec3(0, 0, 1)); // P1: permanent support
            SpawnWall(map, new IntVec3(3, 0, 1)); // P3: permanent support, covers a nearby band
            global::SimWorld.Building.Building p2 = SpawnWall(map, new IntVec3(10, 0, 1)); // removed below

            for (int x = 1; x <= 13; x++)
            {
                if (x == 3) continue; // P3's own cell
                map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            }

            var far = new IntVec3(13, 0, 1);
            Thing sitTest = ThingMaker.MakeThing(Def("WoodLog"));
            sitTest.HitPoints = 1000;
            GenSpawn.Spawn(sitTest, far, map);

            Assert.True(map.roofGrid.Roofed(far));

            p2.Destroy();

            Assert.False(map.roofGrid.Roofed(far), "A roofed cell 3 tiles from the removed pillar and 10 from any remaining wall should have collapsed.");
            Assert.True(sitTest.HitPoints < 1000, "Whatever was under the collapsing cell should have taken damage.");
        }

        [Fact]
        public void A_roofed_cell_still_close_to_a_remaining_wall_keeps_its_roof()
        {
            CoreMap map = NewMap(20, 3);
            SpawnWall(map, new IntVec3(0, 0, 1)); // P1
            SpawnWall(map, new IntVec3(3, 0, 1)); // P3: stays — supports the near band
            global::SimWorld.Building.Building p2 = SpawnWall(map, new IntVec3(10, 0, 1)); // removed below

            for (int x = 1; x <= 13; x++)
            {
                if (x == 3) continue;
                map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            }

            var near = new IntVec3(6, 0, 1); // 3 from P3 (stays), 4 from the despawning P2 — a real candidate
            Assert.True(map.roofGrid.Roofed(near));

            p2.Destroy();

            Assert.True(map.roofGrid.Roofed(near), "A cell still within radius of a wall that was never touched should not collapse.");
        }

        [Fact]
        public void Removing_a_wall_with_no_roof_anywhere_does_nothing()
        {
            CoreMap map = NewMap(5, 5);
            global::SimWorld.Building.Building wall = SpawnWall(map, new IntVec3(2, 0, 2));
            wall.Destroy(); // must not throw even though nothing is roofed
            Assert.False(map.roofGrid.Roofed(new IntVec3(2, 0, 2)));
        }

        [Fact]
        public void A_thick_roof_collapses_for_more_damage_than_a_constructed_roof()
        {
            CoreMap map = NewMap(10, 10);
            var thinCell = new IntVec3(2, 0, 2);
            var thickCell = new IntVec3(7, 0, 7);
            map.roofGrid.SetRoof(thinCell, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(thickCell, SimWorld.Map.RoofDefOf.RoofRockThick);

            Thing underThin = ThingMaker.MakeThing(Def("WoodLog"));
            underThin.HitPoints = 1000;
            GenSpawn.Spawn(underThin, thinCell, map);
            Thing underThick = ThingMaker.MakeThing(Def("WoodLog"));
            underThick.HitPoints = 1000;
            GenSpawn.Spawn(underThick, thickCell, map);

            // Neither cell has any nearby support at all (no walls anywhere on this map), so calling the
            // collapse check directly with each as its own "vacated footprint" collapses it deterministically
            // — the point of this test is the damage multiplier, not the despawn-triggered wiring (covered above).
            RoofCollapseUtility.Notify_RoofHolderDespawned(CellRect.SingleCell(thinCell), map);
            RoofCollapseUtility.Notify_RoofHolderDespawned(CellRect.SingleCell(thickCell), map);

            Assert.False(map.roofGrid.Roofed(thinCell));
            Assert.False(map.roofGrid.Roofed(thickCell));
            int thinDamage = 1000 - underThin.HitPoints;
            int thickDamage = 1000 - underThick.HitPoints;
            Assert.True(thinDamage > 0);
            Assert.True(thickDamage > thinDamage, "A thick rock roof should collapse for more damage than an ordinary one.");
        }

        [Fact]
        public void A_frame_completing_construction_does_not_falsely_collapse_a_roof_it_alone_supports()
        {
            // Regression test for the WillReplace fix: Frame.CompleteConstruction destroys the Frame and
            // spawns the real Building at the same cell in the same call. Between those two lines the cell
            // genuinely holds no edifice for a moment — without DestroyMode.WillReplace telling the roof
            // check to ignore that moment, a roof only this Frame supported would be destroyed for good, even
            // though the finished wall reappears microseconds later.
            CoreMap map = NewMap(10, 10);
            var frameCell = new IntVec3(5, 0, 5);
            var nearbyCell = new IntVec3(6, 0, 5); // adjacent: the frame is its only possible support

            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Wall"));
            frame.AddMaterial(Def("WoodLog"), 5);
            GenSpawn.Spawn(frame, frameCell, map);
            map.roofGrid.SetRoof(nearbyCell, SimWorld.Map.RoofDefOf.RoofConstructed);
            Assert.True(map.roofGrid.Roofed(nearbyCell));

            Thing built = frame.CompleteConstruction(NewHuman());

            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.True(map.roofGrid.Roofed(nearbyCell), "Completing a Frame must not trigger a false collapse via its own momentary despawn.");
        }
    }
}
