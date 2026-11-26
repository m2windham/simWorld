using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Building;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// Host UX requests after playtest 4, on real generated settlements: the settlement's buildings and rooms with
    /// their roles, so a clicked roof resolves to a named building (<see cref="MapViewSnapshot.CaptureBuildings()"/>),
    /// and natural rock told apart from a built wall (<see cref="ThingView.IsNaturalRock"/>).
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewBuildingsTests : ContentTestBase
    {
        public MapViewBuildingsTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
            MapViewTracker.ResetSessionIdCounter();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement Opened(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        /// <summary>Ticks until the settlement has planned its compound.</summary>
        private static SettlementCompound PlannedCompound(CoreMap map)
        {
            for (int i = 0; i < GenDate.TicksPerDay && map.settlementCompound == null; i++) Find.TickManager.DoSingleTick();
            return Assert.IsType<SettlementCompound>(map.settlementCompound);
        }

        private static Thing Spawn(CoreMap map, string defName, IntVec3 cell, Faction? faction, Rot4 rot = default)
        {
            ThingDef def = Def(defName);
            Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            if (faction != null) t.SetFaction(faction);
            return GenSpawn.Spawn(t, cell, map, rot);
        }

        /// <summary>Clears whatever stands on <paramref name="cell"/> but pawns, so a test can build there.</summary>
        private static void Clear(CoreMap map, IntVec3 cell)
        {
            foreach (Thing t in map.thingGrid.ThingsListAt(cell).Where(t => !(t is Pawn)).ToList()) t.Destroy();
        }

        /// <summary>Walls <paramref name="room"/> in, its door on the east side, and roofs its floor.</summary>
        private static void WallIn(CoreMap map, CellRect room, Faction? faction)
        {
            CellRect ring = room.ExpandedBy(1);
            IntVec3 door = new IntVec3(ring.maxX, 0, room.minZ + room.height / 2);
            foreach (IntVec3 c in ring.Cells) Clear(map, c);
            foreach (IntVec3 c in ring.EdgeCells) Spawn(map, c == door ? "Door" : "Wall", c, faction);
            foreach (IntVec3 c in room.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
        }

        /// <summary>A rect of ordinary ground well away from the village, nobody standing on it.</summary>
        private static CellRect OpenGround(CoreMap map, int size)
        {
            CellRect village = map.settlementCompound!.Village.ExpandedBy(4);
            foreach (IntVec3 corner in map.AllCells)
            {
                var rect = new CellRect(corner.x, corner.z, size, size);
                if (rect.maxX >= map.Size.x - 2 || rect.maxZ >= map.Size.z - 2 || rect.minX < 2 || rect.minZ < 2) continue;
                if (!rect.ClipInsideRect(village).IsEmpty) continue;
                if (rect.Cells.All(c => map.terrainGrid.TerrainAt(c).passability == Traversability.Standable
                        && map.edificeGrid[c] == null && map.roofGrid.RoofAt(c) == null && map.zoneManager.ZoneAt(c) == null
                        && !map.thingGrid.ThingsListAt(c).Any(t => t is Pawn || t.def.category == ThingCategory.Blueprint)))
                {
                    return rect;
                }
            }
            throw new System.InvalidOperationException("no open ground");
        }

        private static void Settle(CoreMap map)
        {
            map.roomTracker.Notify_Dirty();
            map.roomTracker.RoomTrackerTick();
        }

        [Fact]
        public void A_roofed_cell_of_the_hall_resolves_to_the_communal_hall()
        {
            Settlement settlement = Opened("buildings-hall");
            CoreMap map = settlement.InteriorMap!;
            SettlementCompound compound = PlannedCompound(map);

            // Raise the hall as its builders would leave it: every ring cell walled but the door, the bed block
            // roofed.
            foreach (IntVec3 c in compound.Ring.EdgeCells)
            {
                Thing? standing = map.edificeGrid[c];
                if (standing != null && !standing.def.mineable) continue;
                Clear(map, c);
                Spawn(map, c == compound.DoorCell ? "Door" : "Wall", c, settlement.faction);
            }
            foreach (IntVec3 c in compound.Interior.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            Settle(map);

            SettlementBuildings buildings = MapViewSnapshot.CaptureBuildings();
            Assert.True(buildings.HasMap);
            BuildingView hall = Assert.Single(buildings.Buildings, b => b.Role == BuildingRole.Hall);
            Assert.Equal("communal hall", hall.Label);
            Assert.True(hall.Roofed);
            Assert.Equal(new IntVec3(compound.Ring.minX, 0, compound.Ring.minZ), hall.BoundsMin);
            Assert.Equal(compound.Interior.Area, hall.Cells.Count);

            foreach (IntVec3 c in compound.Interior.Cells) Assert.Same(hall, buildings.BuildingAt(c));
            Assert.Same(hall, buildings.BuildingAt(compound.DoorCell));

            // The rooms inside the hall are the hall's, not listed again as rooms of their own.
            var roomRoles = new[] { BuildingRole.Dormitory, BuildingRole.Bedroom, BuildingRole.Workshop, BuildingRole.Storeroom, BuildingRole.Room };
            Assert.DoesNotContain(buildings.Buildings, b => roomRoles.Contains(b.Role)
                && b.Cells.Any(c => compound.Interior.Contains(c)));
        }

        [Fact]
        public void Houses_rooms_stores_and_benches_carry_their_roles()
        {
            Settlement settlement = Opened("buildings-roles");
            CoreMap map = settlement.InteriorMap!;
            PlannedCompound(map);
            Faction faction = settlement.faction!;

            Dwelling house = ShelterKit.RaiseOnPlot(map, ShelterKit.Rung("House_Paleo"), Pawn_RelationsTracker.None, faction)!;
            Assert.NotNull(house);
            map.settlementCompound!.EnsureDwellingIds();

            // A room the player walled in, with a bench in it: a workshop.
            CellRect ground = OpenGround(map, 12);
            var workshop = new CellRect(ground.minX + 1, ground.minZ + 1, 3, 3);
            WallIn(map, workshop, faction);
            Thing bench = Spawn(map, "TableButcher", new IntVec3(workshop.minX + 1, 0, workshop.minZ + 1), faction);

            // A room walled by nobody (a ruin): not the settlement's building.
            var ruin = new CellRect(ground.minX + 7, ground.minZ + 1, 2, 2);
            WallIn(map, ruin, null);

            // A store standing on its own.
            IntVec3 hutCell = new IntVec3(ground.minX + 1, 0, ground.minZ + 8);
            Thing hut = Spawn(map, "StorageHut", hutCell, faction);
            Settle(map);

            SettlementBuildings buildings = MapViewSnapshot.CaptureBuildings();

            BuildingView home = buildings.Buildings.Single(b => b.DwellingId == house.id);
            Assert.Equal(BuildingRole.House, home.Role);
            Assert.Equal(house.def.defName, home.DefName);
            Assert.Same(home, buildings.BuildingAt(house.Parts.First(p => p.Kind == DwellingPartKind.Bed).Cell));

            BuildingView room = buildings.BuildingAt(new IntVec3(workshop.minX, 0, workshop.minZ))!;
            Assert.Equal(BuildingRole.Workshop, room.Role);
            Assert.Equal("workshop", room.RoleLabel);
            Assert.True(room.Roofed);
            // Its floor, less the bench's cell when the bench fills it.
            Assert.All(room.Cells, c => Assert.True(workshop.Contains(c)));
            Assert.True(room.Cells.Count >= workshop.Area - 1);

            BuildingView benchView = buildings.BuildingAt(bench.Position)!;
            Assert.Equal(BuildingRole.Bench, benchView.Role);
            Assert.Equal(bench.thingIDNumber, benchView.ThingId);

            BuildingView store = buildings.BuildingAt(hutCell)!;
            Assert.Equal(BuildingRole.Store, store.Role);
            Assert.Equal(hut.thingIDNumber, store.ThingId);
            Assert.Equal(Def("StorageHut").label, store.Label);

            Assert.Null(buildings.BuildingAt(new IntVec3(ruin.minX, 0, ruin.minZ)));

            // Beds make the same room a dormitory.
            bench.Destroy();
            Spawn(map, "Bed", new IntVec3(workshop.minX, 0, workshop.minZ), faction);
            Spawn(map, "Bed", new IntVec3(workshop.maxX, 0, workshop.minZ), faction);
            Settle(map);
            Assert.Equal(BuildingRole.Dormitory, MapViewSnapshot.CaptureBuildings().BuildingAt(new IntVec3(workshop.minX + 1, 0, workshop.minZ + 1))!.Role);
        }

        [Fact]
        public void Natural_rock_is_flagged_and_a_built_wall_is_not()
        {
            Settlement settlement = Opened("buildings-rock");
            CoreMap map = settlement.InteriorMap!;
            PlannedCompound(map);
            CellRect ground = OpenGround(map, 3);
            Thing wall = Spawn(map, "Wall", new IntVec3(ground.minX + 1, 0, ground.minZ + 1), settlement.faction);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture();
            List<ThingView> things = snapshot.AllThings().ToList();
            Assert.Contains(things, t => t.IsNaturalRock);
            foreach (ThingView t in things) Assert.Equal(Def(t.DefName).mineable, t.IsNaturalRock);
            Assert.False(things.Single(t => t.ThingId == wall.thingIDNumber).IsNaturalRock);

            // The delta says the same.
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(null);
            Assert.Equal(things.Count(t => t.IsNaturalRock), delta.ChangedChunks.SelectMany(c => c.Things).Count(t => t.IsNaturalRock));
        }

        [Fact]
        public void With_no_settlement_open_the_buildings_say_why()
        {
            Opened("buildings-absent");
            GodCommands.ClearSettlementFocus();
            SettlementBuildings buildings = MapViewSnapshot.CaptureBuildings();
            Assert.False(buildings.HasMap);
            Assert.False(string.IsNullOrEmpty(buildings.AbsenceReason));
            Assert.Empty(buildings.Buildings);
        }
    }
}
