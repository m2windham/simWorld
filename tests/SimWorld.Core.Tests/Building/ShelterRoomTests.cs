using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using SimWorld.Work;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The shelter lane: each rung of the dwelling ladder is a real room (<see cref="DwellingDef"/>), roofed in its
    /// own roof, and what it does for the people inside in a heat wave or a cold snap is RimWorld's room model's
    /// answer (<see cref="RoomTracker"/>). Pinned as comparisons between rungs and against the harm threshold, not
    /// as temperatures: the earth-sheltered rungs hold under RimWorld's safe maximum where an above-ground hut does
    /// not, a cooler or hearth moves a room the way it should, and the settlement switches them by the season
    /// (<see cref="SettlementClimate"/>). The planner's village is <see cref="DwellingTests"/>.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ShelterRoomTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public ShelterRoomTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static readonly string[] Ladder =
        {
            "House_Paleo", "House_Meso", "House_Neo", "House_Chalco", "House_Classical", "House_Medieval",
            "House_Industrial", "House_Information", "House_Exotic",
        };

        /// <summary>RimWorld's safe maximum for a human: comfortable maximum plus ten (TemperatureInjuryUtility).</summary>
        private static float SafeMax(Pawn p) => TemperatureInjuryUtility.SafeTemperatureRange(p).max;

        private static CoreMap NewMap(float outdoor, int size = 30) =>
            new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), name, "F_" + name);

        private static void Finish(params string[] projects)
        {
            foreach (string p in projects) Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(p));
        }

        private static void Run(CoreMap map, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }
        }

        private static Room BedRoom(CoreMap map, Dwelling d) => map.roomTracker.RoomAt(d.Interior.First())!;

        // ---- the ladder ----

        [Fact]
        public void The_ladder_runs_through_every_era_in_order_and_every_plan_loads_clean()
        {
            IReadOnlyList<DwellingDef> ladder = DwellingUtility.Ladder;
            Assert.Equal(Ladder, ladder.Select(d => d.defName));
            for (int i = 0; i < ladder.Count; i++)
            {
                DwellingDef d = ladder[i];
                Assert.Empty(d.ConfigErrors());
                Assert.Equal(i, d.rung);
                Assert.NotEmpty(d.researchPrerequisites);
                Assert.NotNull(d.era);
                // The rung belongs to the era of the newest research it needs.
                Assert.Same(d.era, d.researchPrerequisites.OrderByDescending(r => r.era!.order).First().era);
                Assert.InRange(d.BedCount, 2, 6);
                Assert.True(d.Size.x <= DwellingUtility.PlotSize.x && d.Size.z <= DwellingUtility.PlotSize.z);
            }
            // Every era from the first to the last has a rung.
            Assert.Equal(DefDatabase<EraDef>.AllDefsListForReading.Count, ladder.Select(d => d.era).Distinct().Count());
        }

        [Fact]
        public void The_earth_sheltered_rungs_keep_no_cooler_spot_and_every_above_ground_rung_one()
        {
            foreach (DwellingDef d in DwellingUtility.Ladder)
            {
                bool earth = d.wallDef != null && d.wallDef.earthBanked;
                Assert.Equal(earth, d.roofDef != null && d.roofDef.isEarthen);
                int coolers = 0, hearths = 0;
                for (int x = 0; x < d.Size.x; x++)
                {
                    for (int z = 0; z < d.Size.z; z++)
                    {
                        if (d.PartAt(x, z) == DwellingPartKind.CoolerSpot) coolers++;
                        if (d.PartAt(x, z) == DwellingPartKind.HearthSpot) hearths++;
                    }
                }
                Assert.Equal(1, hearths);
                // The first rung is a windbreak, older than any cooler the tree knows.
                Assert.Equal(earth || d.rung == 0 ? 0 : 1, coolers);
            }
        }

        // ---- rooms ----

        [Theory]
        [MemberData(nameof(Rungs))]
        public void Every_rung_built_is_one_closed_room_round_its_beds_and_is_marked_for_its_roof(string defName)
        {
            CoreMap map = NewMap(20f);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung(defName), new IntVec3(10, 0, 10), NewFaction("Settlers"));
            map.MapTick();

            Assert.True(d.IsComplete(map));
            Assert.Equal(d.def.BedCount, d.Beds(map).Count);
            Room room = BedRoom(map, d);
            Assert.False(room.TouchesOutside);
            Assert.Equal(d.Interior.OrderBy(c => c.x).ThenBy(c => c.z), room.Cells.OrderBy(c => c.x).ThenBy(c => c.z));
            Assert.All(d.Beds(map), b => Assert.Same(room, map.roomTracker.RoomAt(b.Position)));
            Assert.All(d.Interior, c => Assert.True(map.areaManager.BuildRoof[c], $"{c} should be marked"));
        }

        [Fact]
        public void A_house_turned_South_is_the_same_room_mirrored_with_its_door_on_the_north()
        {
            CoreMap map = NewMap(20f);
            DwellingDef hut = ShelterKit.Rung("House_Chalco");
            Dwelling north = ShelterKit.Raise(map, hut, new IntVec3(2, 0, 2), NewFaction("A"));
            Dwelling south = ShelterKit.Raise(map, hut, new IntVec3(12, 0, 2), NewFaction("B"), rot: Rot4.South);
            map.MapTick();

            Assert.Equal(north.Interior.Count, south.Interior.Count);
            Assert.Equal(north.footprint.minZ, north.Parts.Single(p => p.Kind == DwellingPartKind.Door).Cell.z);
            Assert.Equal(south.footprint.maxZ, south.Parts.Single(p => p.Kind == DwellingPartKind.Door).Cell.z);
            Assert.False(BedRoom(map, south).TouchesOutside);
            Assert.All(south.Beds(map), b => Assert.Equal(Rot4.South, b.Rotation));
        }

        [Fact]
        public void A_builder_lays_a_pit_house_an_earthen_roof_and_a_hut_a_built_one()
        {
            CoreMap map = NewMap(20f, 40);
            Faction settlers = NewFaction("Settlers");
            Dwelling pit = ShelterKit.Raise(map, ShelterKit.Rung("House_Meso"), new IntVec3(4, 0, 4), settlers);
            Dwelling hut = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(20, 0, 4), settlers);
            Pawn builder = NewHuman("Builder");
            builder.faction = settlers;
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 10;
            GenSpawn.Spawn(builder, new IntVec3(15, 0, 20), map);

            for (int i = 0; i < 20000; i++)
            {
                RunTicks(1, builder);
                map.MapTick();
                if (pit.Interior.Concat(hut.Interior).All(c => map.roofGrid.Roofed(c))) break;
            }

            Assert.All(pit.Interior, c => Assert.Same(ShelterRoofDefOf.RoofEarth, map.roofGrid.RoofAt(c)));
            Assert.All(hut.Interior, c => Assert.Same(RoofDefOf.RoofConstructed, map.roofGrid.RoofAt(c)));
            map.MapTick();
            Assert.False(BedRoom(map, pit).Group.UsesOutdoorTemperature);
        }

        // ---- air ----

        /// <summary>Where a rung's bedroom settles after a day of <paramref name="outdoor"/>, from 21 C, roofed in
        /// its own roof, with whatever devices are named spawned on their spots and switched on.</summary>
        private float AfterADay(string rung, float outdoor, params ThingDef[] devices)
        {
            CoreMap map = NewMap(outdoor);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung(rung), new IntVec3(10, 0, 10));
            ShelterKit.Roof(map, d);
            foreach (ThingDef device in devices)
            {
                DwellingPartKind spot = ReferenceEquals(device, ShelterThingDefOf.PassiveCooler) ? DwellingPartKind.CoolerSpot : DwellingPartKind.HearthSpot;
                GenSpawn.Spawn(ThingMaker.MakeThing(device), d.Spots(spot).Single(), map);
            }
            map.MapTick();
            BedRoom(map, d).Group.temperature = 21f;
            Run(map, GenDate.TicksPerDay);
            float air = BedRoom(map, d).Temperature;
            output.WriteLine($"{rung} at {outdoor} C with [{string.Join(",", devices.Select(x => x.defName))}]: {air:0.0} C");
            return air;
        }

        [Fact]
        public void In_a_heat_wave_the_pit_house_stays_under_the_harm_line_and_the_hut_does_not()
        {
            Pawn someone = NewHuman("Someone");
            float safeMax = SafeMax(someone);
            const float heatWave = 47f; // a hot climate's summer mean plus RimWorld's +17 heat wave, as the bench sees

            float hut = AfterADay("House_Paleo", heatWave);
            float pit = AfterADay("House_Meso", heatWave);
            float longhouse = AfterADay("House_Neo", heatWave);

            Assert.True(hut > safeMax, $"a windbreak hut follows the heat: {hut}");
            Assert.True(pit < safeMax, $"the pit house holds: {pit} under {safeMax}");
            Assert.True(longhouse < safeMax, $"the longhouse holds: {longhouse} under {safeMax}");
        }

        [Fact]
        public void An_above_ground_house_needs_its_cooler_and_its_cooler_brings_it_down()
        {
            const float heatWave = 47f;
            float bare = AfterADay("House_Chalco", heatWave);
            float cooled = AfterADay("House_Chalco", heatWave, ShelterThingDefOf.PassiveCooler);
            Assert.True(cooled < bare - 5f, $"cooled {cooled} vs bare {bare}");
        }

        [Fact]
        public void In_the_cold_a_hearth_warms_a_hut()
        {
            float bare = AfterADay("House_Paleo", -10f);
            float hearth = AfterADay("House_Paleo", -10f, ShelterThingDefOf.Campfire);
            Assert.True(hearth > bare + 5f, $"hearth {hearth} vs bare {bare}");
        }

        [Fact]
        public void Earth_overhead_and_banked_round_pulls_a_warm_room_down_toward_fifteen_and_never_warms_a_cold_one()
        {
            // RimWorld's deep equalization (RoomGroupTempTracker): only rooms warmer than 15 C are pulled. A room
            // closed in earth banks under an earthen roof trades nothing with the outdoors, so the pull is all it has.
            float After(float outdoor, float start)
            {
                CoreMap map = NewMap(outdoor);
                var shell = new CellRect(5, 5, 6, 6);
                foreach (IntVec3 c in shell.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.WallEarth), c, map);
                foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, ShelterRoofDefOf.RoofEarth);
                map.MapTick();
                Room room = map.roomTracker.RoomAt(new IntVec3(7, 0, 7))!;
                Assert.False(room.Group.UsesOutdoorTemperature);
                room.Group.temperature = start;
                Run(map, GenDate.TicksPerDay / 2);
                return room.Temperature;
            }

            float hot = After(45f, 30f);
            Assert.True(hot < 30f && hot >= 15f, $"a warm room under earth cools toward 15: {hot}");
            Assert.Equal(5f, After(-20f, 5f), 3);
        }

        [Fact]
        public void An_earth_bank_keeps_out_the_heat_a_wooden_wall_lets_in()
        {
            float After(ThingDef wall)
            {
                CoreMap map = NewMap(45f);
                var shell = new CellRect(5, 5, 6, 6);
                foreach (IntVec3 c in shell.EdgeCells)
                {
                    ThingDef? stuff = wall.MadeFromStuff ? SimWorld.Crafting.GenStuff.DefaultStuffFor(wall) : null;
                    GenSpawn.Spawn(ThingMaker.MakeThing(wall, stuff), c, map);
                }
                foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, ShelterRoofDefOf.RoofEarth);
                map.MapTick();
                Room room = map.roomTracker.RoomAt(new IntVec3(7, 0, 7))!;
                room.Group.temperature = 20f;
                Run(map, GenDate.TicksPerDay / 4);
                return room.Temperature;
            }

            float earth = After(ShelterThingDefOf.WallEarth);
            float wood = After(ConstructionThingDefOf.Wall);
            Assert.True(earth < wood - 2f, $"earth {earth} vs wood {wood}");
        }

        [Fact]
        public void A_powered_cooler_in_a_wall_cools_the_room_behind_it_and_heats_the_one_it_faces()
        {
            // RimWorld's Building_Cooler: cold side south of it (turned North), hot side north, a quarter more heat
            // out than it takes in.
            (float Cold, float Hot) After(bool switchedOn)
            {
                CoreMap map = NewMap(30f);
                var shell = new CellRect(5, 5, 7, 9);
                foreach (IntVec3 c in shell.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.WallEarth), c, map);
                var coolerCell = new IntVec3(8, 0, 9);
                for (int x = shell.minX + 1; x < shell.maxX; x++)
                {
                    var c = new IntVec3(x, 0, 9);
                    if (c != coolerCell) GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.WallEarth), c, map);
                }
                var cooler = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Cooler")), coolerCell, map, Rot4.North);
                if (!switchedOn) cooler.GetComp<CompFlickable>()!.SwitchIsOn = false;
                foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                map.MapTick();
                Room cold = map.roomTracker.RoomAt(new IntVec3(8, 0, 7))!, hot = map.roomTracker.RoomAt(new IntVec3(8, 0, 11))!;
                Assert.NotSame(cold.Group, hot.Group);
                cold.Group.temperature = 30f;
                hot.Group.temperature = 30f;
                Run(map, 2500);
                return (cold.Temperature, hot.Temperature);
            }

            (float cold, float hot) = After(switchedOn: true);
            Assert.True(cold < 25f, $"cold side {cold}");
            Assert.True(hot > 35f, $"hot side {hot}");
            (float offCold, float offHot) = After(switchedOn: false);
            Assert.InRange(offCold, 29f, 31f);
            Assert.InRange(offHot, 29f, 31f);
        }

        // ---- the switch ----

        [Fact]
        public void A_hearth_switched_off_burns_no_wood_warms_nothing_and_is_not_refuelled_and_a_pawn_flips_it()
        {
            CoreMap map = NewMap(-10f);
            Faction settlers = NewFaction("Settlers");
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Paleo"), new IntVec3(10, 0, 10), settlers);
            ShelterKit.Roof(map, d);
            var fire = (ThingWithComps)ThingMaker.MakeThing(ShelterThingDefOf.Campfire);
            fire.SetFaction(settlers);
            GenSpawn.Spawn(fire, d.Spots(DwellingPartKind.HearthSpot).Single(), map);
            CompFlickable flick = fire.GetComp<CompFlickable>()!;
            CompRefuelable fuel = fire.GetComp<CompRefuelable>()!;

            flick.SetWantSwitchOn(false);
            Assert.NotNull(map.designationManager.DesignationOn(fire, FlickDesignationDefOf.Flick));
            Assert.True(flick.SwitchIsOn, "asking is not flipping: a pawn has to walk over");

            Pawn hand = NewHuman("Hand");
            hand.faction = settlers;
            GenSpawn.Spawn(hand, new IntVec3(3, 0, 3), map);
            Assert.True(new WorkGiver_Flick().HasJobOnThing(hand, fire));
            for (int i = 0; i < 3000 && flick.SwitchIsOn; i++)
            {
                RunTicks(1, hand);
                map.MapTick();
            }
            Assert.False(flick.SwitchIsOn);
            Assert.Null(map.designationManager.DesignationOn(fire, FlickDesignationDefOf.Flick));
            hand.DeSpawn();

            float before = fuel.Fuel;
            BedRoom(map, d).Group.temperature = -10f;
            Run(map, 2500);
            Assert.Equal(before, fuel.Fuel, 4);
            Assert.InRange(BedRoom(map, d).Temperature, -10.5f, -9.5f);
            fuel.ConsumeFuel(15f);
            Assert.False(fuel.ShouldAutoRefuelNowIgnoringFuelPct);

            flick.SetWantSwitchOn(true);
            flick.DoFlick();
            Assert.True(fuel.ShouldAutoRefuelNowIgnoringFuelPct);
            Run(map, 2500);
            Assert.True(fuel.Fuel < before - 15f + 0.01f);
            Assert.True(BedRoom(map, d).Temperature > -5f);
        }

        [Fact]
        public void The_switch_and_what_was_asked_of_it_round_trip_through_Scribe()
        {
            CoreMap map = NewMap(20f);
            var cooler = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(ShelterThingDefOf.PassiveCooler), new IntVec3(5, 0, 5), map);
            cooler.GetComp<CompFlickable>()!.SetWantSwitchOn(false);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            var again = (ThingWithComps)loaded.listerThings.ThingsOfDef(ShelterThingDefOf.PassiveCooler).Single();
            CompFlickable flick = again.GetComp<CompFlickable>()!;
            Assert.True(flick.SwitchIsOn);
            Assert.False(flick.WantSwitchOn);
            Assert.True(flick.WantsFlick());
            Assert.NotNull(loaded.designationManager.DesignationOn(again, FlickDesignationDefOf.Flick));
        }

        // ---- the settlement's judgement ----

        [Fact]
        public void The_outlook_reads_the_season_and_a_heat_wave_and_not_the_hour()
        {
            CoreMap map = NewMap(20f);
            map.weatherManager.Climate = new MapClimate { averageTemperature = 20f, latitude = 0f, longitude = 0f };
            float calm = SettlementClimate.Outlook(map).DailyMean;
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerDay / 2);
            Assert.Equal(calm, SettlementClimate.Outlook(map).DailyMean, 2);

            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 2f));
            float wave = SettlementClimate.Outlook(map).DailyMean;
            Assert.Equal(map.gameConditionManager.AggregateTemperatureOffset(), wave - calm, 2);
            Assert.True(wave > calm + 10f);
        }

        /// <summary>A settlement of two households housed in finished town houses, everything known that the
        /// climate pass might build.</summary>
        private (Settlement, CoreMap, List<Dwelling>) TownOfTwo(float outdoor)
        {
            Finish("Fire", "WindbreakShelters", "Copper", "Windcatchers");
            CoreMap map = NewMap(outdoor, 40);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Town", 0);
            var houses = new List<Dwelling>();
            for (int i = 0; i < 2; i++)
            {
                Pawn a = NewHuman("A" + i), b = NewHuman("B" + i);
                settlement.AddCitizen(a);
                settlement.AddCitizen(b);
                Find.FamilyManager.FoundHousehold(a, b, 0);
                houses.Add(ShelterKit.Raise(map, ShelterKit.Rung("House_Chalco"), new IntVec3(5 + 10 * i, 0, 10), householdId: a.relations.familyId));
            }
            return (settlement, map, houses);
        }

        private static Thing? At(CoreMap map, IntVec3 c, System.Func<Thing, bool> what) => map.thingGrid.ThingsListAt(c).FirstOrDefault(what);

        [Fact]
        public void A_mild_day_builds_nothing()
        {
            (Settlement settlement, CoreMap map, _) = TownOfTwo(21f);
            Assert.Equal(0, SettlementClimate.Tick(settlement, map));
        }

        [Fact]
        public void A_heat_wave_has_a_cooler_built_in_every_house_and_the_autumn_switches_it_off()
        {
            (Settlement settlement, CoreMap map, List<Dwelling> houses) = TownOfTwo(40f);
            Assert.Equal(2, SettlementClimate.Tick(settlement, map));
            foreach (Dwelling d in houses)
            {
                IntVec3 spot = d.Spots(DwellingPartKind.CoolerSpot).Single();
                var bp = (Blueprint)At(map, spot, t => t is Blueprint)!;
                Assert.Same(ShelterThingDefOf.PassiveCooler, bp.EntityToBuild);
                Assert.Null(At(map, d.Spots(DwellingPartKind.HearthSpot).Single(), t => t is Blueprint));
                bp.ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));
            }
            Assert.Equal(0, SettlementClimate.Tick(settlement, map));

            map.outdoorTemperature = 21f;
            SettlementClimate.Tick(settlement, map);
            foreach (Dwelling d in houses)
            {
                var cooler = (ThingWithComps)At(map, d.Spots(DwellingPartKind.CoolerSpot).Single(), t => ReferenceEquals(t.def, ShelterThingDefOf.PassiveCooler))!;
                Assert.False(cooler.GetComp<CompFlickable>()!.WantSwitchOn);
                Assert.NotNull(map.designationManager.DesignationOn(cooler, FlickDesignationDefOf.Flick));
            }

            map.outdoorTemperature = 0f;
            Assert.Equal(2, SettlementClimate.Tick(settlement, map));
            Assert.All(houses, d => Assert.Same(ShelterThingDefOf.Campfire,
                ((Blueprint)At(map, d.Spots(DwellingPartKind.HearthSpot).Single(), t => t is Blueprint)!).EntityToBuild));
        }

        [Fact]
        public void Nothing_is_built_that_the_civilization_has_not_learned()
        {
            CoreMap map = NewMap(40f, 40);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Town", 0);
            Pawn a = NewHuman("A"), b = NewHuman("B");
            settlement.AddCitizen(a);
            settlement.AddCitizen(b);
            Find.FamilyManager.FoundHousehold(a, b, 0);
            ShelterKit.Raise(map, ShelterKit.Rung("House_Chalco"), new IntVec3(5, 0, 10), householdId: a.relations.familyId);
            Assert.Equal(0, SettlementClimate.Tick(settlement, map));
        }

        [Fact]
        public void A_pit_house_is_planned_dug_and_lived_in()
        {
            Finish("WindbreakShelters", "PitDwellings");
            CoreMap map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Pits", 0);
            Pawn a = NewHuman("A"), b = NewHuman("B");
            settlement.AddCitizen(a);
            settlement.AddCitizen(b);
            Find.FamilyManager.FoundHousehold(a, b, 0);
            Pawn builder = NewHuman("Builder");

            for (int pass = 0; pass < 60; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
                foreach (Blueprint bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
                {
                    bp.ReplaceWithFrame().CompleteConstruction(builder);
                }
            }

            Dwelling pit = Assert.Single(DwellingUtility.DwellingsOn(map));
            Assert.Same(ShelterKit.Rung("House_Meso"), pit.def);
            Assert.True(pit.IsComplete(map));
            Assert.Equal(a.relations.familyId, pit.householdId);
            Assert.All(pit.Parts.Where(p => p.Kind == DwellingPartKind.Wall), p => Assert.Same(ShelterThingDefOf.WallEarth, map.edificeGrid[p.Cell]!.def));
            Assert.Equal(2, DwellingUtility.HousedCitizens(settlement, map));
        }

        public static IEnumerable<object[]> Rungs() => Ladder.Select(n => new object[] { n });
    }
}
