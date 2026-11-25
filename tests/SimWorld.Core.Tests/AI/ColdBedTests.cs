using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Conditions;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Building;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Where a sleeper lies down in a frost, and whether the dormitory is somewhere warm (exposure4 lane), on a real
    /// generated map. In the 600-day arc on 40/201 a quake opened the houses at -11 C; children slept in the unheated
    /// dormitory at -3 C, and children downed by the cold were carried home to the opened houses at -8 C, where they
    /// died. RimWorld's <c>RestUtility.FindBedFor</c> looks for a bed whose room is no danger to the sleeper before it
    /// takes any (B18 decompile, read), and a RimWorld player puts a campfire in the barracks.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ColdBedTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public ColdBedTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private sealed class Village
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map = null!;
            public SettlementCompound Compound = null!;
            public int Housed, Houseless;
            public Dwelling House = null!;
        }

        private static void Run(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.TickManager.DoSingleTick();
        }

        /// <summary>A generated tribal start in a cold snap held for <paramref name="days"/>: one household in a finished
        /// House_Meso with a fuelled hearth, another with no house, and the compound's dormitory standing whole, roofed
        /// and furnished, with a fuelled hearth in its place when <paramref name="dormitoryHearth"/>.</summary>
        private static Village NewWinterVillage(string seed, int days, bool dormitoryHearth)
        {
            var v = new Village
            {
                Game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20),
            };
            v.Settlement = v.Game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(v.Settlement.tile);
            v.Map = v.Settlement.InteriorMap!;
            Run(v.Game, 2 * ConstructionInitiativeTuning.IntervalTicks);
            v.Compound = v.Map.settlementCompound!;
            Assert.NotNull(v.Compound);
            // A hearth is built once Fire is known; a band that has wintered once has it.
            Find.ResearchManager.SetProjectFinishedForSetup(DefDatabase<ResearchProjectDef>.GetNamed("Fire"));

            List<int> families = DwellingUtility.Households(v.Settlement).Keys.ToList();
            Assert.True(families.Count >= 2, "the band has fewer than two households");
            v.Housed = families[0];
            v.Houseless = families[1];
            v.House = ShelterKit.RaiseOnPlot(v.Map, ShelterKit.Rung("House_Meso"), v.Housed, v.Settlement.faction)!;
            Assert.NotNull(v.House);
            foreach (IntVec3 spot in v.House.Spots(DwellingPartKind.HearthSpot)) Hearth(v, spot);

            BuildDormitory(v);
            if (dormitoryHearth) Hearth(v, v.Compound.HearthCell);

            int lerp = GameCondition_TemperatureOffset.LerpTicks;
            v.Map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("ColdSnap"), days * GenDate.TicksPerDay + 2 * lerp));
            Run(v.Game, lerp);
            // A day in the frost, so the rooms have settled to it.
            Run(v.Game, GenDate.TicksPerDay);
            return v;
        }

        private static Thing Hearth(Village v, IntVec3 spot)
        {
            foreach (Thing t in v.Map.thingGrid.ThingsListAt(spot).ToList()) if (t is Blueprint || t is Frame) t.Destroy();
            Thing hearth = ThingMaker.MakeThing(ShelterThingDefOf.Campfire);
            hearth.SetFaction(v.Settlement.faction);
            GenSpawn.Spawn(hearth, spot, v.Map, Rot4.North);
            ((ThingWithComps)hearth).GetComp<CompRefuelable>()!.Refuel(1000f);
            return hearth;
        }

        /// <summary>The compound's ring, door and beds spawned finished and its interior roofed, as builders leave them.</summary>
        private static void BuildDormitory(Village v)
        {
            void Put(ThingDef def, IntVec3 cell, Rot4 rot)
            {
                foreach (Thing t in v.Map.thingGrid.ThingsListAt(cell).ToList()) if (t is Blueprint || t is Frame) t.Destroy();
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.SetFaction(v.Settlement.faction);
                GenSpawn.Spawn(thing, cell, v.Map, rot);
            }
            foreach (IntVec3 c in v.Compound.WallCells) Put(ConstructionThingDefOf.Wall, c, Rot4.North);
            Put(CompoundThingDefOf.Door, v.Compound.DoorCell, v.Compound.RotationAt(CompoundThingDefOf.Door, v.Compound.DoorCell));
            foreach (IntVec3 c in v.Compound.BedCells) Put(ConstructionThingDefOf.Bed, c, Rot4.North);
            foreach (IntVec3 c in v.Compound.Ring.Cells) v.Map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            Run(v.Game, 1);
            Room? room = v.Map.roomTracker.RoomAt(v.Compound.BedCells.First());
            Assert.True(room != null && !room.Group.UsesOutdoorTemperature, "the dormitory is not a closed room: room "
                + (room == null ? "none" : room.Cells.Count + " cells") + ", ring walls standing "
                + v.Compound.WallCells.Count(c => v.Map.thingGrid.ThingsListAt(c).Any(t => t.def == ConstructionThingDefOf.Wall))
                + "/" + v.Compound.WallCells.Count());
        }

        /// <summary>A child born in the settlement and grown to five, in the one shirt the tailor made.</summary>
        private static Pawn Child(Village v, int family, IntVec3 cell)
        {
            Pawn child = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: v.Settlement.faction);
            child.ageTracker.DebugSetAge(5f);
            child.relations.familyId = family;
            child.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Shirt"), DefDatabase<ThingDef>.GetNamed("Cloth")));
            v.Settlement.AddCitizen(child);
            GenSpawn.Spawn(child, cell, v.Map);
            return child;
        }

        /// <summary>Open ground outside every house and the compound, reachable, with nothing on it.</summary>
        private static IEnumerable<IntVec3> OpenGround(Village v, IntVec3 near)
        {
            Pawn walker = v.Settlement.Citizens.First(p => p.Spawned && !p.Downed);
            CellRect footprint = v.Compound.Footprint;
            return v.Map.AllCells.OrderBy(c => (c - near).LengthHorizontalSquared).Where(c => GenGrid.Standable(c, v.Map)
                && v.Map.roomTracker.RoomAt(c)?.Group.UsesOutdoorTemperature != false && !footprint.Contains(c)
                && DwellingUtility.DwellingAt(v.Map, c) == null && v.Map.thingGrid.ThingsListAt(c).Count == 0
                && Reachability.CanReach(walker, new LocalTargetInfo(c), PathEndMode.Touch));
        }

        /// <summary>A bed out in the frost, in nobody's house, beside <paramref name="near"/>.</summary>
        private static Thing ColdBed(Village v, IntVec3 near)
        {
            foreach (IntVec3 c in OpenGround(v, near))
            {
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, v.Map, out _)) continue;
                Thing bed = ThingMaker.MakeThing(ConstructionThingDefOf.Bed,
                    ConstructionThingDefOf.Bed.MadeFromStuff ? GenStuff.DefaultStuffFor(ConstructionThingDefOf.Bed) : null);
                bed.SetFaction(v.Settlement.faction);
                GenSpawn.Spawn(bed, c, v.Map, Rot4.North);
                if (v.Map.roomTracker.RoomAt(bed.GetSleepingSlotPos())?.Group.UsesOutdoorTemperature == true) return bed;
                bed.Destroy();
            }
            throw new Xunit.Sdk.XunitException("no open ground for a bed");
        }

        private static float Air(Village v, Thing bed) => RotUtility.AmbientTemperatureAt(v.Map, bed.GetSleepingSlotPos());

        private static bool InDormitory(Village v, Thing? bed) => bed != null && v.Compound.Interior.Contains(bed.Position);

        /// <summary>Opens <paramref name="house"/> to the frost as a quake does: one of its walls comes down.</summary>
        private static void OpenToTheFrost(Village v, Dwelling house)
        {
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in house.Parts)
            {
                if (kind != DwellingPartKind.Wall) continue;
                Thing? wall = v.Map.thingGrid.ThingsListAt(cell).FirstOrDefault(t => t.def.passability == Traversability.Impassable);
                if (wall == null) continue;
                wall.Destroy();
                break;
            }
            Assert.False(house.IsComplete(v.Map));
        }

        [Fact]
        public void A_child_with_no_house_lies_down_in_the_heated_dormitory_not_the_frozen_bed_beside_it()
        {
            Village v = NewWinterVillage("cold-bed-nearest", 2, dormitoryHearth: true);
            IntVec3 start = OpenGround(v, v.Compound.DoorCell + new IntVec3(6, 0, 0)).First();
            Thing cold = ColdBed(v, start);
            Pawn child = Child(v, v.Houseless, OpenGround(v, cold.Position).First());
            float safeMin = TemperatureInjuryUtility.SafeTemperatureRange(child).min;
            output.WriteLine("cold bed air " + Air(v, cold).ToString("0.0") + ", child's safe min " + safeMin.ToString("0.0"));
            Assert.True(Air(v, cold) < safeMin, "the bed in the open is not dangerous to the child");

            Thing? bed = RestUtility.FindRestBedFor(child, child);

            Assert.NotNull(bed);
            Assert.NotSame(cold, bed);
            Assert.True(InDormitory(v, bed), "the child was not given a bed in the heated dormitory: " + bed!.Position);
            Assert.True(RestUtility.IsSafeTemperatureBed(child, bed));
        }

        [Fact]
        public void With_nowhere_safe_a_child_still_takes_the_nearest_bed_and_the_dormitory_once_its_hearth_has_fuel()
        {
            Village v = NewWinterVillage("cold-bed-none", 2, dormitoryHearth: false);
            IntVec3 start = OpenGround(v, v.Compound.DoorCell + new IntVec3(6, 0, 0)).First();
            Thing cold = ColdBed(v, start);
            Pawn child = Child(v, v.Houseless, OpenGround(v, cold.Position).First());
            Thing first = v.Map.thingGrid.ThingsListAt(v.Compound.BedCells.First()).First(t => t.def == ConstructionThingDefOf.Bed);
            output.WriteLine("out " + v.Map.outdoorTemperature.ToString("0.0") + " dormitory " + Air(v, first).ToString("0.0")
                + " safe min " + TemperatureInjuryUtility.SafeTemperatureRange(child).min.ToString("0.0"));
            Assert.All(v.Compound.BedCells, c => Assert.False(RestUtility.IsSafeTemperatureBed(child,
                v.Map.thingGrid.ThingsListAt(c).First(t => t.def == ConstructionThingDefOf.Bed))));

            Assert.Same(cold, RestUtility.FindRestBedFor(child, child));

            // A fuelled hearth set in the dormitory: before it has warmed the air at all, the dormitory is where the
            // child sleeps, since the thermostat lights it for whoever lies there (SettlementClimate.WillWarm).
            Hearth(v, v.Compound.HearthCell);
            Assert.True(Air(v, first) < TemperatureInjuryUtility.SafeTemperatureRange(child).min, "the dormitory air is already safe");
            Assert.True(RestUtility.IsSafeTemperatureBed(child, first), "a bed in the closed dormitory by a fuelled hearth is not safe");
            Assert.True(InDormitory(v, RestUtility.FindRestBedFor(child, child)), "the child was not sent to the dormitory with its fuelled hearth");
        }

        [Fact]
        public void A_child_sleeps_at_home_while_its_house_is_whole_and_in_the_heated_dormitory_once_the_frost_gets_in()
        {
            Village v = NewWinterVillage("cold-bed-open-house", 2, dormitoryHearth: true);
            Thing homeBed = v.House.Beds(v.Map).First();
            Pawn child = Child(v, v.Housed, homeBed.GetSleepingSlotPos());
            // Its house whole and its hearth fuelled, home is safe however cold the room is this hour.
            Assert.Same(homeBed.def, RestUtility.FindRestBedFor(child, child)?.def);
            Assert.True(DwellingUtility.IsOwnHomeBed(child, RestUtility.FindRestBedFor(child, child)!));

            OpenToTheFrost(v, v.House);
            output.WriteLine("open house air " + Air(v, homeBed).ToString("0.0") + ", safe min "
                + TemperatureInjuryUtility.SafeTemperatureRange(child).min.ToString("0.0"));
            Assert.False(RestUtility.IsSafeTemperatureBed(child, homeBed), "the opened house is still safe for the child");

            Thing? bed = RestUtility.FindRestBedFor(child, child);
            Assert.True(InDormitory(v, bed), "the child was sent to sleep in its opened house");
        }

        [Fact]
        public void A_child_downed_in_the_heated_dormitory_is_not_carried_home_to_a_house_open_to_the_frost()
        {
            Village v = NewWinterVillage("cold-bed-rescue", 2, dormitoryHearth: true);
            OpenToTheFrost(v, v.House);
            Run(v.Game, GenDate.TicksPerHour * 2);
            Pawn rescuer = v.Settlement.Citizens.First(p => p.Spawned && !p.Downed && p.relations.familyId != v.Housed);

            // Downed on open ground: carried to the dormitory, not home.
            IntVec3 ground = OpenGround(v, v.Compound.DoorCell + new IntVec3(6, 0, 0)).First();
            Pawn child = Child(v, v.Housed, ground);
            Hediff cold = HediffMaker.MakeHediff(TemperatureDefOf.Hypothermia, child);
            cold.Severity = 0.7f; // extreme (from 0.62, Hediffs_Temperature.xml): down
            child.health.AddHediff(cold);
            Run(v.Game, 1);
            Assert.True(child.Downed, "the child is not downed");
            Thing? to = WorkGiver_RescueDowned.BedFor(child, rescuer);
            Assert.True(InDormitory(v, to), "the downed child would be carried to " + (to == null ? "nowhere" : to.Position.ToString()));

            // Already lying in a dormitory bed: left there, not carried home.
            Thing dormBed = to!;
            child.Position = dormBed.GetSleepingSlotPos();
            Assert.True(child.InBed());
            Assert.Null(WorkGiver_RescueDowned.BedFor(child, rescuer));

            // Lying in its own bed in the opened house: carried out of it, to the dormitory.
            Thing homeBed = v.House.Beds(v.Map).First();
            child.Position = homeBed.GetSleepingSlotPos();
            Assert.Same(homeBed, child.CurrentBed());
            Assert.True(new WorkGiver_RescueDowned().HasJobOnThing(rescuer, child), "nobody would carry the child out of its opened house");
            Assert.True(InDormitory(v, WorkGiver_RescueDowned.BedFor(child, rescuer)));
        }

        [Fact]
        public void A_baby_left_in_a_house_open_to_the_frost_is_carried_to_the_heated_dormitory()
        {
            Village v = NewWinterVillage("cold-bed-baby", 2, dormitoryHearth: true);
            Thing homeBed = v.House.Beds(v.Map).Last();
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: v.Settlement.faction);
            baby.relations.familyId = v.Housed;
            v.Settlement.AddCitizen(baby);
            GenSpawn.Spawn(baby, homeBed.GetSleepingSlotPos(), v.Map);
            OpenToTheFrost(v, v.House);
            Pawn carer = v.Settlement.Citizens.First(p => p.Spawned && !p.Downed && p.ageTracker.CurLifeStage?.developmentalStage == DevelopmentalStage.Adult);
            output.WriteLine("open house air " + Air(v, homeBed).ToString("0.0") + ", baby comfy min "
                + TemperatureInjuryUtility.ComfortableTemperatureRange(baby).min.ToString("0.0"));

            Assert.True(BabySafetyUtility.NeedsMovingForTemperature(baby), "the baby is not in danger in the opened house");
            Thing? to = BabySafetyUtility.SafeBedFor(baby, carer);
            Assert.True(InDormitory(v, to), "the baby would be carried to " + (to == null ? "nowhere" : to.Position.ToString()));
        }

        [Fact]
        public void A_dormitory_raised_with_a_bed_on_the_hearth_spot_takes_that_bed_down_for_the_hearth()
        {
            Village v = NewWinterVillage("dorm-old-bed", 3, dormitoryHearth: false);
            // A dormitory raised before it kept a place for its hearth: a bed on the spot, as on the arc's day 519.
            Thing old = ThingMaker.MakeThing(ConstructionThingDefOf.Bed,
                ConstructionThingDefOf.Bed.MadeFromStuff ? GenStuff.DefaultStuffFor(ConstructionThingDefOf.Bed) : null);
            old.SetFaction(v.Settlement.faction);
            IntVec3 slot = v.Compound.HearthCell;
            IntVec2 size = ConstructionThingDefOf.Bed.size;
            IntVec3 foot = new IntVec3(v.Compound.Interior.minX + (slot.x - v.Compound.Interior.minX) / size.x * size.x, 0,
                v.Compound.Interior.minZ + (slot.z - v.Compound.Interior.minZ) / size.z * size.z);
            GenSpawn.Spawn(old, foot, v.Map, Rot4.North);
            Assert.Contains(old, v.Map.thingGrid.ThingsListAt(slot));
            Pawn child = Child(v, v.Houseless, v.Compound.BedCells.First());

            SettlementClimate.Tick(v.Settlement, v.Map);
            Assert.NotNull(v.Map.designationManager.DesignationOn(old, global::SimWorld.Designations.PlayerDesignationDefOf.Deconstruct));

            int builtAt = -1;
            for (int hour = 1; hour <= 24 && builtAt < 0; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                if (v.Map.thingGrid.ThingsListAt(slot).Any(t => t.def == ShelterThingDefOf.Campfire || (t is Blueprint b && b.EntityToBuild == ShelterThingDefOf.Campfire)
                    || (t is Frame f && f.EntityToBuild == ShelterThingDefOf.Campfire))) builtAt = hour;
            }
            output.WriteLine("old bed down and the hearth placed at h" + builtAt + "; child at " + child.Position);
            Assert.True(builtAt > 0, "the hearth was not placed within a day: " + string.Join(",", v.Map.thingGrid.ThingsListAt(slot).Select(t => t.def.defName)));
        }

        [Fact]
        public void The_dormitory_builds_its_hearth_for_the_winter_and_lights_it_for_a_cold_child_lying_there()
        {
            Village v = NewWinterVillage("dorm-hearth", 2, dormitoryHearth: false);
            Thing dormBed = v.Map.thingGrid.ThingsListAt(v.Compound.BedCells.First()).First(t => t.def == ConstructionThingDefOf.Bed);
            Pawn child = Child(v, v.Houseless, dormBed.GetSleepingSlotPos());
            float comfyMin = TemperatureInjuryUtility.ComfortableTemperatureRange(child).min;
            Assert.True(SettlementClimate.Outlook(v.Map).DailyMean < comfyMin, "the winter is not cold for the child");

            SettlementClimate.Tick(v.Settlement, v.Map);
            Assert.Contains(v.Map.thingGrid.ThingsListAt(v.Compound.HearthCell), t => t is Blueprint b && b.EntityToBuild == ShelterThingDefOf.Campfire);

            Thing hearth = Hearth(v, v.Compound.HearthCell);
            Assert.True(Air(v, dormBed) < comfyMin, "the dormitory is already warm: " + Air(v, dormBed));
            foreach (Pawn p in v.Settlement.Citizens.Where(p => p != child && p.Spawned && v.Compound.Interior.Contains(p.Position)).ToList())
            {
                p.Position = OpenGround(v, v.Compound.DoorCell).First();
            }
            Assert.True(RestUtility.IsSafeTemperatureBed(child, dormBed), "a bed in the closed dormitory by a fuelled hearth is not safe");

            SettlementClimate.Tick(v.Settlement, v.Map);
            Assert.True(((ThingWithComps)hearth).GetComp<CompFlickable>()!.WantSwitchOn, "the dormitory's hearth was not asked to burn for the cold child");

            // Then the night: the child, worn out, goes to bed in the dormitory, and the hearth is lit and warms it.
            child.needs.rest!.CurLevel = 0.05f;
            child.jobs!.EndCurrentJob(JobCondition.InterruptForced);
            int warmAt = -1;
            for (int hour = 0; hour < 12 && warmAt < 0; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                var tw = (ThingWithComps)hearth;
                Room? room = v.Map.roomTracker.RoomAt(dormBed.GetSleepingSlotPos());
                output.WriteLine("h" + hour + " air " + Air(v, dormBed).ToString("0.0") + " out " + v.Map.outdoorTemperature.ToString("0.0")
                    + " spawned " + hearth.Spawned + " want " + tw.GetComp<CompFlickable>()!.WantSwitchOn + " on " + tw.GetComp<CompFlickable>()!.SwitchIsOn
                    + " child in " + v.Compound.Interior.Contains(child.Position) + " " + (child.jobs?.curJob?.def.defName ?? "-") + " bed " + child.CurrentBed()?.Position + " fuel " + tw.GetComp<CompRefuelable>()!.Fuel.ToString("0.0")
                    + " room cells " + (room?.Cells.Count ?? -1) + " same room " + ReferenceEquals(room, v.Map.roomTracker.RoomAt(hearth.Position)));
                if (Air(v, dormBed) >= comfyMin) warmAt = hour;
            }
            Assert.True(InDormitory(v, child.CurrentBed()), "the tired child did not lie down in the dormitory");
            output.WriteLine("dormitory air " + Air(v, dormBed).ToString("0.0") + " comfy min " + comfyMin.ToString("0.0") + " warm at h" + warmAt);
            Assert.True(warmAt >= 0, "the dormitory was not warm enough for the child within 12 h: " + Air(v, dormBed));
        }
    }
}
