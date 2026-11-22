using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Building;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// REQ 007 from the host: since houses became rooms of real walls, the map view says which house a cell
    /// belongs to and whose it is (<see cref="DwellingView"/>), how warm it is inside against the outdoor air
    /// (<see cref="CellTemperatureView"/>), which roofs are earthen (<see cref="RoofView.IsEarthen"/>), and whether
    /// a cooler or hearth is switched on and fuelled (<see cref="ThingView.SwitchedOn"/>,
    /// <see cref="ThingView.FuelFraction"/>). Snapshot and delta must say the same thing, and the structural seam
    /// test in <see cref="MapViewTests"/> already covers the new types.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewDwellingTests : ContentTestBase
    {
        public MapViewDwellingTests(CoreContentFixture content) : base(content)
        {
            MapViewTracker.ResetSessionIdCounter();
        }

        private static CoreMap NewMap(float outdoor, int size = 30) =>
            new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };

        /// <summary>Lets the room tracker see the walls and give the house its own air.</summary>
        private static void Settle(CoreMap map, int ticks = 2)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }
        }

        private static IntVec3 FirstBed(Dwelling d) => d.Parts.First(p => p.Kind == DwellingPartKind.Bed).Cell;

        [Fact]
        public void A_finished_house_reports_its_rung_plot_beds_and_household_on_snapshot_and_delta()
        {
            CoreMap map = NewMap(20f);
            Pawn a = NewHuman("Ada"), b = NewHuman("Bo");
            Family family = Find.FamilyManager.FoundHousehold(a, b, 0);
            DwellingDef rung = ShelterKit.Rung("House_Neo");
            Dwelling d = ShelterKit.Raise(map, rung, new IntVec3(5, 0, 5), householdId: family.id, rot: Rot4.South);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);
            DwellingView view = Assert.Single(snapshot.Dwellings);
            Assert.Equal("House_Neo", view.DefName);
            Assert.Equal(rung.label, view.Label);
            Assert.Equal(rung.rung, view.Rung);
            Assert.Equal(new IntVec3(5, 0, 5), view.OccupiedMin);
            Assert.Equal(rung.Size.x, view.OccupiedSize.x);
            Assert.Equal(rung.Size.z, view.OccupiedSize.z);
            Assert.Equal(Rot4.South, view.Rotation);
            Assert.True(view.IsComplete);
            Assert.Equal(rung.BedCount, view.BedsPlanned);
            Assert.Equal(rung.BedCount, view.BedsStanding);
            Assert.Equal(family.id, view.HouseholdId);
            Assert.Equal(CitizenNameOf(a), view.HouseholdName);
            Assert.False(string.IsNullOrEmpty(view.HouseholdName));

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, snapshot.Versions);
            AssertSame(snapshot.Dwellings, delta.Dwellings);
        }

        /// <summary>The roster's own wording, so the inspector and the citizen line never disagree.</summary>
        private static string? CitizenNameOf(Pawn p)
        {
            Family? f = Find.FamilyManager.GetFamily(p.relations.familyId);
            return f == null ? null : (f.surname.Length > 0 ? f.surname : "Family " + f.id);
        }

        [Fact]
        public void A_house_losing_a_bed_reads_as_going_up_with_its_beds_counted_and_a_vacant_one_has_no_household()
        {
            CoreMap map = NewMap(20f);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(5, 0, 5));
            MapViewSnapshot before = MapViewSnapshot.Capture(map);
            Assert.True(before.Dwellings[0].IsComplete);
            Assert.Null(before.Dwellings[0].HouseholdId);
            Assert.Null(before.Dwellings[0].HouseholdName);

            Dwelling.BedAt(map, FirstBed(d))!.Destroy(DestroyMode.Vanish);

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, before.Versions);
            DwellingView after = Assert.Single(delta.Dwellings);
            Assert.False(after.IsComplete);
            Assert.Equal(after.BedsPlanned - 1, after.BedsStanding);
            AssertSame(delta.Dwellings, MapViewSnapshot.Capture(map).Dwellings);
        }

        [Fact]
        public void A_closed_house_has_its_own_air_and_the_ground_outside_has_the_outdoors()
        {
            CoreMap map = NewMap(34f);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(10, 0, 10));
            ShelterKit.Roof(map, d);
            Settle(map);
            RoomGroup group = map.roomTracker.RoomAt(FirstBed(d))!.Group;
            Assert.False(group.UsesOutdoorTemperature);
            group.temperature = 18f;

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);
            Assert.Equal(34f, snapshot.OutdoorTemperature);
            Assert.Equal(18f, snapshot.Dwellings[0].IndoorTemperature!.Value, 3);
            Assert.Equal(snapshot.OutdoorTemperature, MapViewSnapshot.CaptureChanges(map, snapshot.Versions).OutdoorTemperature);

            CellTemperatureView inside = MapViewSnapshot.TemperatureAt(map, FirstBed(d));
            Assert.True(inside.HasMap);
            Assert.True(inside.Indoors);
            Assert.Equal(18f, inside.RoomTemperature!.Value, 3);
            Assert.Equal(18f, inside.Temperature, 3);
            Assert.Equal(34f, inside.OutdoorTemperature);
            Assert.Equal(group.CellCount, inside.RoomCellCount);

            CellTemperatureView outside = MapViewSnapshot.TemperatureAt(map, new IntVec3(1, 0, 1));
            Assert.False(outside.Indoors);
            Assert.Null(outside.RoomTemperature);
            Assert.Equal(0, outside.RoomCellCount);
            Assert.Equal(34f, outside.Temperature);
        }

        [Fact]
        public void An_unroofed_house_has_no_air_of_its_own_and_a_cell_off_the_map_is_outdoors()
        {
            CoreMap map = NewMap(5f);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(10, 0, 10));
            Settle(map);

            Assert.Null(MapViewSnapshot.Capture(map).Dwellings[0].IndoorTemperature);
            Assert.False(MapViewSnapshot.TemperatureAt(map, FirstBed(d)).Indoors);
            CellTemperatureView off = MapViewSnapshot.TemperatureAt(map, new IntVec3(-3, 0, 99));
            Assert.False(off.Indoors);
            Assert.Equal(5f, off.Temperature);
        }

        [Fact]
        public void With_no_game_the_temperature_read_explains_itself()
        {
            CellTemperatureView none = MapViewSnapshot.TemperatureAt(0, new IntVec3(1, 0, 1));
            Assert.False(none.HasMap);
            Assert.False(string.IsNullOrWhiteSpace(none.AbsenceReason));
            Assert.False(MapViewSnapshot.TemperatureAt(new IntVec3(1, 0, 1)).HasMap);
        }

        [Fact]
        public void An_earthen_roof_is_known_by_its_flag_not_its_name()
        {
            CoreMap map = NewMap(20f);
            map.roofGrid.SetRoof(new IntVec3(2, 0, 2), ShelterRoofDefOf.RoofEarth);
            map.roofGrid.SetRoof(new IntVec3(3, 0, 2), RoofDefOf.RoofConstructed);

            RoofLayer roofs = MapViewSnapshot.Capture(map).Roofs;
            RoofView earth = roofs.Palette[roofs.Cells[2 * roofs.SizeX + 2]];
            RoofView built = roofs.Palette[roofs.Cells[2 * roofs.SizeX + 3]];
            Assert.True(earth.IsEarthen);
            Assert.False(built.IsEarthen);
        }

        [Fact]
        public void A_hearth_reports_its_switch_and_fuel_and_the_delta_carries_a_flip_and_the_fire_going_out()
        {
            CoreMap map = NewMap(-5f);
            var fire = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.Campfire), new IntVec3(4, 0, 4), map);
            CompRefuelable fuel = fire.GetComp<CompRefuelable>()!;
            fuel.Refuel(fuel.Props.fuelCapacity);
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.WallEarth), new IntVec3(8, 0, 8), map);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);
            ThingView hearth = snapshot.AllThings().Single(t => t.ThingId == fire.thingIDNumber);
            Assert.True(hearth.SwitchedOn);
            Assert.True(hearth.BurnsFuel);
            Assert.Equal(1f, hearth.FuelFraction, 3);
            ThingView plain = snapshot.AllThings().Single(t => t.ThingId == wall.thingIDNumber);
            Assert.Null(plain.SwitchedOn);
            Assert.False(plain.BurnsFuel);

            MapViewVersions held = snapshot.Versions;
            fire.GetComp<CompFlickable>()!.DoFlick();
            MapViewDelta flipped = MapViewSnapshot.CaptureChanges(map, held);
            ThingView off = flipped.ChangedChunks.SelectMany(c => c.Things).Single(t => t.ThingId == fire.thingIDNumber);
            Assert.False(off.SwitchedOn);
            held = flipped.Versions;

            // Into the middle of a 5% step; a sliver burned there crosses none, and nothing is re-sent.
            fuel.ConsumeFuel(fuel.Props.fuelCapacity * 0.025f);
            held = MapViewSnapshot.CaptureChanges(map, held).Versions;
            fuel.ConsumeFuel(fuel.Props.fuelCapacity * 0.001f);
            MapViewDelta quiet = MapViewSnapshot.CaptureChanges(map, held);
            Assert.Empty(quiet.ChangedChunks);
            held = quiet.Versions;

            fuel.ConsumeFuel(fuel.Props.fuelCapacity);
            MapViewDelta cold = MapViewSnapshot.CaptureChanges(map, held);
            ThingView dead = cold.ChangedChunks.SelectMany(c => c.Things).Single(t => t.ThingId == fire.thingIDNumber);
            Assert.Equal(0f, dead.FuelFraction);
            Assert.Equal(dead.FuelFraction, MapViewSnapshot.Capture(map).AllThings().Single(t => t.ThingId == fire.thingIDNumber).FuelFraction);
        }

        [Fact]
        public void Burning_a_full_load_re_sends_the_hearths_chunk_about_twenty_times_not_every_tick()
        {
            CoreMap map = NewMap(-5f);
            var fire = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.Campfire), new IntVec3(4, 0, 4), map);
            CompRefuelable fuel = fire.GetComp<CompRefuelable>()!;
            fuel.Refuel(fuel.Props.fuelCapacity);
            int chunk = map.mapView.ChunkIndexAt(fire.Position);
            int start = map.mapView.VersionOfChunk(chunk);

            float bite = fuel.Props.fuelCapacity / 1000f;
            for (int i = 0; i < 1100 && fuel.HasFuel; i++) fuel.ConsumeFuel(bite);

            Assert.False(fuel.HasFuel);
            Assert.InRange(map.mapView.VersionOfChunk(chunk) - start, 15, 22);
        }

        [Fact]
        public void A_house_its_household_and_its_switches_read_the_same_after_a_scribe_round_trip()
        {
            CoreMap map = NewMap(20f);
            Pawn a = NewHuman("Ada");
            Family family = Find.FamilyManager.FoundHousehold(a, null, 0);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(10, 0, 10), householdId: family.id);
            ShelterKit.Roof(map, d);
            var fire = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.Campfire), d.Spots(DwellingPartKind.HearthSpot).Single(), map);
            fire.GetComp<CompRefuelable>()!.Refuel(3f);
            fire.GetComp<CompFlickable>()!.DoFlick();
            MapViewSnapshot before = MapViewSnapshot.Capture(map);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            MapViewSnapshot after = MapViewSnapshot.Capture(loaded);

            Assert.Equal(before.Dwellings.Count, after.Dwellings.Count);
            DwellingView x = before.Dwellings[0], y = after.Dwellings[0];
            Assert.Equal(x.DefName, y.DefName);
            Assert.Equal(x.OccupiedMin, y.OccupiedMin);
            Assert.Equal(x.Rotation, y.Rotation);
            Assert.Equal(x.IsComplete, y.IsComplete);
            Assert.Equal(x.BedsStanding, y.BedsStanding);
            Assert.Equal(x.HouseholdId, y.HouseholdId);
            Assert.Equal(x.HouseholdName, y.HouseholdName);
            ThingView h0 = before.AllThings().Single(t => t.DefName == ShelterThingDefOf.Campfire.defName);
            ThingView h1 = after.AllThings().Single(t => t.DefName == ShelterThingDefOf.Campfire.defName);
            Assert.Equal(h0.SwitchedOn, h1.SwitchedOn);
            Assert.Equal(h0.FuelFraction, h1.FuelFraction, 4);
            Assert.True(after.Roofs.Palette.Any(r => r.IsEarthen) == before.Roofs.Palette.Any(r => r.IsEarthen));
        }

        private static void AssertSame(IReadOnlyList<DwellingView> a, IReadOnlyList<DwellingView> b)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].DefName, b[i].DefName);
                Assert.Equal(a[i].OccupiedMin, b[i].OccupiedMin);
                Assert.Equal(a[i].OccupiedSize.x, b[i].OccupiedSize.x);
                Assert.Equal(a[i].OccupiedSize.z, b[i].OccupiedSize.z);
                Assert.Equal(a[i].Rotation, b[i].Rotation);
                Assert.Equal(a[i].IsComplete, b[i].IsComplete);
                Assert.Equal(a[i].BedsStanding, b[i].BedsStanding);
                Assert.Equal(a[i].BedsPlanned, b[i].BedsPlanned);
                Assert.Equal(a[i].HouseholdId, b[i].HouseholdId);
                Assert.Equal(a[i].HouseholdName, b[i].HouseholdName);
                Assert.Equal(a[i].IndoorTemperature, b[i].IndoorTemperature);
            }
        }
    }
}
