using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A room a settlement walls in gets a roof, and with it air of its own (AutoBuildRoofAreaSetter,
    /// WorkGiver_BuildRoof, JobDriver_BuildRoof). Before this nothing a settlement built was ever roofed, so a
    /// walled room was outdoors and the door model never took effect in play. Also holds the room temperature
    /// model to two terms no earlier test could tell apart: heat through a thin roof, and the scale-back that
    /// keeps a fast exchange through a building from overshooting the mean.
    /// </summary>
    public class AutoRoofTests : ContentTestBase
    {
        public AutoRoofTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(float outdoor) =>
            new CoreMap(16, 16, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), name, "F_" + name);

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName, Faction? faction = null)
        {
            Thing t = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            if (faction != null) t.SetFaction(faction);
            return GenSpawn.Spawn(t, cell, map);
        }

        /// <summary>A 7x7 shell (5x5 inside) whose east wall holds a door, every piece owned by
        /// <paramref name="owner"/> as a finished frame would be (<see cref="Frame.CompleteConstruction"/> gives a
        /// building its builder's faction).</summary>
        private static CellRect BuildShell(CoreMap map, int x, int z, Faction? owner, Faction? doorOwner = null)
        {
            var shell = new CellRect(x, z, 7, 7);
            var door = new IntVec3(x + 6, 0, z + 3);
            foreach (IntVec3 c in shell.EdgeCells)
            {
                if (c == door) Spawn(map, c, "Door", doorOwner ?? owner);
                else Spawn(map, c, "Wall", owner);
            }
            return shell;
        }

        private static IntVec3 Middle(CellRect shell) => new IntVec3(shell.minX + 3, 0, shell.minZ + 3);

        [Fact]
        public void A_settlement_built_house_is_marked_for_roof_walls_and_all()
        {
            CoreMap map = NewMap(20f);
            CellRect shell = BuildShell(map, 2, 2, NewFaction("Settlers"));
            map.MapTick();

            foreach (IntVec3 c in shell.Cells) Assert.True(map.areaManager.BuildRoof[c], $"{c} should be marked");
            Assert.Equal(shell.Area, map.areaManager.BuildRoof.TrueCount);
            // Marking is not building: the room is still open to the sky until someone roofs it.
            Assert.True(map.roomTracker.RoomAt(Middle(shell))!.Group.UsesOutdoorTemperature);
        }

        [Fact]
        public void A_dwelling_is_walls_round_its_beds_so_its_room_is_marked_for_roof()
        {
            // A dwelling is real walls, a door and beds (shelter lane, Building.DwellingDef), so the settlement
            // that built it has a room to roof, as RimWorld's auto-roof roofs any room a colony walls in.
            CoreMap map = NewMap(20f);
            Dwelling hut = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(6, 0, 6), NewFaction("Settlers"));
            map.MapTick();

            Assert.NotEmpty(hut.Interior);
            Room room = map.roomTracker.RoomAt(hut.Interior.First())!;
            Assert.False(room.TouchesOutside);
            Assert.Equal(hut.Interior.OrderBy(c => c.x).ThenBy(c => c.z), room.Cells.OrderBy(c => c.x).ThenBy(c => c.z));
            foreach (IntVec3 c in hut.footprint.Cells) Assert.True(map.areaManager.BuildRoof[c], $"{c} should be marked");
            Assert.Equal(hut.footprint.Area, map.areaManager.BuildRoof.TrueCount);
        }

        [Fact]
        public void A_ruin_with_no_owner_is_not_marked()
        {
            CoreMap map = NewMap(20f);
            BuildShell(map, 2, 2, owner: null);
            map.MapTick();
            Assert.Equal(0, map.areaManager.BuildRoof.TrueCount);
        }

        [Fact]
        public void A_room_two_factions_built_is_not_marked()
        {
            CoreMap map = NewMap(20f);
            BuildShell(map, 2, 2, NewFaction("Settlers"), doorOwner: NewFaction("Others"));
            map.MapTick();
            Assert.Equal(0, map.areaManager.BuildRoof.TrueCount);
        }

        [Fact]
        public void A_room_too_big_is_not_marked()
        {
            CoreMap map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            var shell = new CellRect(1, 1, 21, 21); // 19x19 inside: 361 cells, over the 320 limit
            Faction owner = NewFaction("Settlers");
            foreach (IntVec3 c in shell.EdgeCells) Spawn(map, c, "Wall", owner);
            map.MapTick();
            Assert.True(map.roomTracker.RoomAt(new IntVec3(10, 0, 10))!.Cells.Count > AutoBuildRoofAreaSetter.MaxRoomCells);
            Assert.Equal(0, map.areaManager.BuildRoof.TrueCount);
        }

        [Fact]
        public void A_builder_roofs_the_house_and_its_room_then_keeps_its_own_air()
        {
            CoreMap map = NewMap(20f);
            Faction settlers = NewFaction("Settlers");
            CellRect shell = BuildShell(map, 2, 2, settlers);
            Pawn builder = NewHuman("Builder");
            builder.faction = settlers;
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 10;
            GenSpawn.Spawn(builder, new IntVec3(12, 0, 5), map);

            for (int i = 0; i < 6000; i++)
            {
                RunTicks(1, builder);
                map.MapTick();
                bool allRoofed = true;
                foreach (IntVec3 c in shell.Cells) allRoofed &= map.roofGrid.Roofed(c);
                if (allRoofed) break;
            }

            foreach (IntVec3 c in shell.Cells)
            {
                Assert.True(map.roofGrid.Roofed(c), $"{c} should be roofed");
                Assert.Same(SimWorld.Map.RoofDefOf.RoofConstructed, map.roofGrid.RoofAt(c));
            }

            // Roofed, the room is no longer the outdoors: a heat wave reaches it late and not in full.
            map.MapTick();
            Room inside = map.roomTracker.RoomAt(Middle(shell))!;
            Assert.False(inside.Group.UsesOutdoorTemperature);
            map.outdoorTemperature = 45f;
            builder.DeSpawn();
            for (int i = 0; i < 2500; i++) map.MapTick();
            Assert.InRange(inside.Temperature, 20.5f, 40f);
        }

        [Fact]
        public void Building_elsewhere_on_the_map_does_not_reset_a_rooms_air()
        {
            CoreMap map = NewMap(45f);
            CellRect shell = BuildShell(map, 2, 2, owner: null);
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            map.roomTracker.RoomAt(Middle(shell))!.Group.temperature = 22f;

            // Any edifice anywhere re-floods every room; the new room must take the old one's air, as
            // RimWorld's TemperatureCache hands it on.
            Spawn(map, new IntVec3(13, 0, 13), "Wall");
            map.MapTick();

            Room rebuilt = map.roomTracker.RoomAt(Middle(shell))!;
            Assert.False(rebuilt.Group.UsesOutdoorTemperature);
            Assert.InRange(rebuilt.Temperature, 22f, 23f);
        }

        [Fact]
        public void Heat_comes_through_a_thin_roof_and_not_through_thick_rock()
        {
            // Same shell, same walls, same 45 C outside; only the roof differs. Thick rock passes no outdoor heat
            // and pulls a room warmer than 15 C back toward 15 (RimWorld's RoomGroupTempTracker), so whatever the
            // thin-roofed room gains beyond it came through the roof.
            float After(RoofDef roof)
            {
                CoreMap map = NewMap(45f);
                CellRect shell = BuildShell(map, 2, 2, owner: null);
                foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, roof);
                map.MapTick();
                Room inside = map.roomTracker.RoomAt(Middle(shell))!;
                inside.Group.temperature = 25f;
                for (int i = 0; i < 2500; i++) map.MapTick();
                return inside.Temperature;
            }

            float thin = After(SimWorld.Map.RoofDefOf.RoofConstructed);
            float thick = After(SimWorld.Map.RoofDefOf.RoofRockThick);
            Assert.True(thin > thick + 0.5f, $"thin {thin} vs thick {thick}");
        }

        [Fact]
        public void A_fast_exchange_through_a_building_stops_at_the_mean_rather_than_overshooting()
        {
            // Two one-cell rooms either side of a door. At a door's own rates the step is far smaller than the gap,
            // so the scale-back never binds; at RimWorld's vent rate (14) a one-cell room would swing far past the
            // mean without it.
            CoreMap map = NewMap(10f);
            var shell = new CellRect(2, 2, 5, 3);
            foreach (IntVec3 c in shell.EdgeCells) Spawn(map, c, "Wall");
            var doorCell = new IntVec3(4, 0, 3);
            var door = (Door)Spawn(map, doorCell, "Door");
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            Room left = map.roomTracker.RoomAt(new IntVec3(3, 0, 3))!;
            Room right = map.roomTracker.RoomAt(new IntVec3(5, 0, 3))!;
            Assert.NotSame(left.Group, right.Group);
            Assert.Single(left.Cells);
            Assert.Single(right.Cells);
            left.Group.temperature = 30f;
            right.Group.temperature = 0f;

            map.roomTracker.EqualizeThroughDoor(door, 14f);

            Assert.InRange(left.Temperature, 14.99f, 15.01f);
            Assert.InRange(right.Temperature, 14.99f, 15.01f);
        }

        [Fact]
        public void The_build_roof_area_survives_a_save_round_trip()
        {
            CoreMap map = NewMap(20f);
            map.areaManager.BuildRoof[new IntVec3(3, 0, 4)] = true;
            map.areaManager.BuildRoof[new IntVec3(9, 0, 1)] = true;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(2, loaded.areaManager.BuildRoof.TrueCount);
            Assert.True(loaded.areaManager.BuildRoof[new IntVec3(3, 0, 4)]);
            Assert.True(loaded.areaManager.BuildRoof[new IntVec3(9, 0, 1)]);
        }
    }
}
