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
    /// A baby whose household has no house, in a hard winter, on a real generated map. Every exposure death of
    /// the 600-day arc on 40/201 (exposure3 lane) was a baby of a household still waiting for a house, lying in a
    /// bed outside any house — the compound's dormitory, unheated — at a few degrees above freezing, for three
    /// days, until it died there. Nobody moved it: the rescue only ever carries a patient home, and it had no home.
    /// RimWorld (Biotech) has anyone bring a baby to safety when the room its crib stands in is too cold.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BabyWarmthTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BabyWarmthTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private sealed class Village
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map = null!;
            public int Housed, Houseless;
            public Dwelling House = null!;
        }

        /// <summary>A generated tribal start in a cold snap held for <paramref name="days"/>, one household housed in
        /// a finished House_Meso on the planner's plot, and another household with no house at all.</summary>
        private static Village NewWinterVillage(string seed, int days)
        {
            var v = new Village
            {
                Game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20),
            };
            v.Settlement = v.Game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(v.Settlement.tile);
            v.Map = v.Settlement.InteriorMap!;
            Run(v.Game, 2 * ConstructionInitiativeTuning.IntervalTicks);
            Assert.NotNull(v.Map.settlementCompound);

            List<int> families = DwellingUtility.Households(v.Settlement).Keys.ToList();
            Assert.True(families.Count >= 2, "the band has fewer than two households");
            v.Housed = families[0];
            v.Houseless = families[1];
            v.House = ShelterKit.RaiseOnPlot(v.Map, ShelterKit.Rung("House_Meso"), v.Housed, v.Settlement.faction)!;
            Assert.NotNull(v.House);

            // The household's hearth stands with a full tank, as a settlement that has wintered before keeps it; whether
            // it burns is the thermostat's.
            foreach (IntVec3 spot in v.House.Spots(DwellingPartKind.HearthSpot))
            {
                Thing hearth = ThingMaker.MakeThing(ShelterThingDefOf.Campfire);
                hearth.SetFaction(v.Settlement.faction);
                GenSpawn.Spawn(hearth, spot, v.Map, Rot4.North);
                ((ThingWithComps)hearth).GetComp<CompRefuelable>()!.Refuel(1000f);
            }
            Assert.True(SettlementClimate.HasFueledHearth(v.Map, v.House));

            int lerp = GameCondition_TemperatureOffset.LerpTicks;
            v.Map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("ColdSnap"), days * GenDate.TicksPerDay + 2 * lerp));
            Run(v.Game, lerp);
            return v;
        }

        private static Pawn Newborn(Village v, int family, IntVec3 cell)
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: v.Settlement.faction);
            baby.relations.familyId = family;
            v.Settlement.AddCitizen(baby);
            GenSpawn.Spawn(baby, cell, v.Map);
            Run(v.Game, 1); // the health tick reads the life stage and lays it down
            Assert.True(baby.Downed, "a newborn is downed by its life stage");
            return baby;
        }

        /// <summary>Tired out, so it falls asleep where it lies (playtest-bugs4's Need_Rest).</summary>
        private static void PutToSleep(Village v, Pawn baby)
        {
            baby.needs.rest!.CurLevel = 0.05f;
            for (int i = 0; i < 4 && !baby.Asleep; i++) Run(v.Game, 150);
            Assert.True(baby.Asleep, "the tired baby did not fall asleep");
        }

        /// <summary>Open ground near the house that no house stands on, reachable from the village, nothing on it.</summary>
        private static IEnumerable<IntVec3> OpenGround(Village v)
        {
            IntVec3 near = new IntVec3(v.House.footprint.minX, 0, v.House.footprint.minZ);
            Pawn walker = v.Settlement.Citizens.First(p => p.Spawned && !p.Downed);
            return v.Map.AllCells.OrderBy(c => (c - near).LengthHorizontalSquared).Where(c => GenGrid.Standable(c, v.Map)
                && v.Map.roomTracker.RoomAt(c)?.Group.UsesOutdoorTemperature != false
                && DwellingUtility.DwellingAt(v.Map, c) == null && v.Map.thingGrid.ThingsListAt(c).Count == 0
                && Reachability.CanReach(walker, new LocalTargetInfo(c), PathEndMode.Touch));
        }

        /// <summary>A bed out in the frost, in nobody's house: the dormitory at its coldest.</summary>
        private static Thing ColdBed(Village v)
        {
            foreach (IntVec3 c in OpenGround(v))
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

        private static void Run(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.TickManager.DoSingleTick();
        }

        private static float Hypothermia(Pawn p) => p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f;

        private static bool Comfortable(Pawn p) =>
            p.Spawned && TemperatureInjuryUtility.AmbientTemperature(p) >= TemperatureInjuryUtility.ComfortableTemperatureRange(p).min;

        private string Line(Village v, Pawn baby, int hour) =>
            "h" + hour + " out " + v.Map.outdoorTemperature.ToString("0.0")
            + (baby.Spawned ? " at " + baby.Position + " air " + TemperatureInjuryUtility.AmbientTemperature(baby).ToString("0.0") : " carried")
            + " inbed " + baby.InBed() + " house " + (baby.Spawned ? DwellingUtility.DwellingAt(v.Map, baby.Position)?.householdId.ToString() ?? "-" : "-")
            + " asleep " + baby.Asleep + " hypothermia " + Hypothermia(baby).ToString("0.00");

        /// <summary>
        /// Lives through a winter: within <paramref name="reachHours"/> it is lying somewhere comfortable for it,
        /// and from then on its hypothermia never reaches the extreme stage (0.62, Hediffs_Temperature.xml) and it
        /// ends the winter rid of it.
        /// </summary>
        private void AssertWarmedAndLives(Village v, Pawn baby, int days, int reachHours)
        {
            float safeMin = TemperatureInjuryUtility.SafeTemperatureRange(baby).min;
            Assert.True(SettlementClimate.Outlook(v.Map).DailyMean < safeMin,
                "the winter is not below the baby's safe minimum: mean " + SettlementClimate.Outlook(v.Map).DailyMean);
            int reachedAt = -1;
            float worst = 0f;
            for (int hour = 0; hour < days * 24; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                Assert.False(baby.Dead, "the baby died on hour " + hour + ": " + Line(v, baby, hour));
                if (reachedAt < 0 && Comfortable(baby) && baby.InBed()) reachedAt = hour;
                if (reachedAt >= 0) worst = System.Math.Max(worst, Hypothermia(baby));
                if (hour % 12 == 0) output.WriteLine(Line(v, baby, hour));
            }
            Assert.True(reachedAt >= 0 && reachedAt <= reachHours,
                "the baby was not lying anywhere warm within " + reachHours + " h (first at h" + reachedAt + ")");
            Assert.True(worst < 0.62f, "after it was warm, hypothermia reached " + worst + " (extreme from 0.62)");
            Assert.True(Hypothermia(baby) < 0.04f, "the baby ended the winter with hypothermia " + Hypothermia(baby));
        }

        [Fact]
        public void A_sleeping_baby_with_no_home_in_a_frozen_bed_is_carried_somewhere_warm()
        {
            Village v = NewWinterVillage("baby-warmth-bed", 4);
            Thing cold = ColdBed(v);
            Pawn baby = Newborn(v, v.Houseless, cold.GetSleepingSlotPos());
            Assert.True(baby.InBed(), "the baby is not lying in the cold bed");
            Assert.Null(DwellingUtility.FindDwellingBedFor(baby));
            PutToSleep(v, baby);

            AssertWarmedAndLives(v, baby, 4, 12);
            Assert.NotSame(cold, baby.CurrentBed());
        }

        [Fact]
        public void A_sleeping_baby_with_no_home_on_frozen_ground_is_carried_to_warmth_not_the_nearest_bed()
        {
            Village v = NewWinterVillage("baby-warmth-ground", 4);
            Thing cold = ColdBed(v);
            // On the ground right beside the cold bed, so the nearest bed that is nobody's is the frozen one.
            IntVec3 beside = OpenGround(v).OrderBy(c => (c - cold.Position).LengthHorizontalSquared).First();
            Pawn baby = Newborn(v, v.Houseless, beside);
            Assert.False(baby.InBed());
            PutToSleep(v, baby);
            Thing? nearest = RestUtility.FindBedFor(baby, v.Settlement.Citizens.First(p => p.Spawned && !p.Downed));
            output.WriteLine("nearest bed that is nobody's: " + (nearest == null ? "none" : nearest.Position + " air "
                + RotUtility.AmbientTemperatureAt(v.Map, nearest.GetSleepingSlotPos()).ToString("0.0")));

            AssertWarmedAndLives(v, baby, 4, 12);
            Assert.NotSame(cold, baby.CurrentBed());
        }

        /// <summary>
        /// A baby at home in a hard frost beside a hearth with nothing in its tank, as the arc's babies of day 474 lay:
        /// it is brought to safety in a house whose hearth can burn, rather than left in its own bed to freeze.
        /// </summary>
        [Fact]
        public void A_sleeping_baby_at_home_by_an_empty_hearth_is_carried_somewhere_warm()
        {
            Village v = NewWinterVillage("baby-warmth-home", 4);
            Dwelling mine = ShelterKit.RaiseOnPlot(v.Map, ShelterKit.Rung("House_Meso"), v.Houseless, v.Settlement.faction)!;
            Assert.NotNull(mine);
            foreach (IntVec3 spot in mine.Spots(DwellingPartKind.HearthSpot))
            {
                Thing hearth = ThingMaker.MakeThing(ShelterThingDefOf.Campfire);
                hearth.SetFaction(v.Settlement.faction);
                GenSpawn.Spawn(hearth, spot, v.Map, Rot4.North);
                CompRefuelable tank = ((ThingWithComps)hearth).GetComp<CompRefuelable>()!;
                tank.ConsumeFuel(tank.Fuel);
            }
            Assert.False(SettlementClimate.HasFueledHearth(v.Map, mine));
            Thing own = mine.Beds(v.Map).First();
            Pawn baby = Newborn(v, v.Houseless, own.GetSleepingSlotPos());
            Assert.True(baby.InBed() && DwellingUtility.IsOwnHomeBed(baby, own));
            PutToSleep(v, baby);

            AssertWarmedAndLives(v, baby, 4, 12);
        }

        /// <summary>A house of <paramref name="family"/> on the planner's plot with one of its walls knocked down, as a quake
        /// leaves it: open to the frost, not finished, so its hearth is not lit.</summary>
        private static Dwelling OpenedHouse(Village v, int family)
        {
            Dwelling house = ShelterKit.RaiseOnPlot(v.Map, ShelterKit.Rung("House_Meso"), family, v.Settlement.faction)!;
            Assert.NotNull(house);
            IntVec3 wall = house.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell;
            v.Map.edificeGrid[wall]!.Destroy();
            Assert.False(house.IsComplete(v.Map));
            return house;
        }

        /// <summary>
        /// A baby asleep in its own bed in a house a quake has opened, in a hard frost, with a warm house standing: it is
        /// carried to the warm house's bed (RimWorld, Biotech: a baby is brought to safety when its bed's room is too
        /// cold). The 600-day 20/101 arc on integ-r13 lost five babies in one night lying in their own beds in opened
        /// houses at -4 to -9 C.
        /// </summary>
        [Fact]
        public void A_sleeping_baby_at_home_in_an_opened_house_is_carried_to_a_warm_bed()
        {
            Village v = NewWinterVillage("baby-warmth-opened", 3);
            Dwelling opened = OpenedHouse(v, v.Houseless);
            Thing own = opened.Beds(v.Map).First();
            Pawn baby = Newborn(v, v.Houseless, own.GetSleepingSlotPos());
            Assert.True(baby.InBed() && DwellingUtility.IsOwnHomeBed(baby, own));
            PutToSleep(v, baby);
            Assert.True(TemperatureInjuryUtility.AmbientTemperature(baby) < TemperatureInjuryUtility.ComfortableTemperatureRange(baby).min,
                "the opened house is not cold");

            int carriedAt = -1;
            for (int hour = 0; hour < 12 && carriedAt < 0; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                output.WriteLine(Line(v, baby, hour));
                if (baby.InBed() && !ReferenceEquals(baby.CurrentBed(), own)) carriedAt = hour;
            }
            Assert.True(carriedAt >= 0, "the baby was left in its own bed in the opened house");
            Assert.True(v.House.InteriorContains(baby.Position), "the baby was not carried to the warm house");
        }

        /// <summary>
        /// One of the house's own out in the frost with hypothermia, nobody at home: the house lights its hearth for them
        /// to come home to, and within hours it is somewhere warm to go. The arc's children of day 520 froze out of
        /// doors and in the dormitory while their houses stood at -5 C, because nobody was home to want the fire.
        /// </summary>
        [Fact]
        public void A_house_lights_its_hearth_for_one_of_its_own_freezing_away_from_home()
        {
            Village v = NewWinterVillage("hearth-for-the-away", 2);
            List<Pawn> household = v.Settlement.Citizens.Where(p => p.relations.familyId == v.Housed && p.Spawned).ToList();
            Assert.NotEmpty(household);
            List<IntVec3> ground = OpenGround(v).Take(household.Count).ToList();
            for (int i = 0; i < household.Count; i++)
            {
                household[i].Position = ground[i];
                Hediff? had = household[i].health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia);
                if (had != null) had.Severity = 0.001f; // below the first stage (0.04, Hediffs_Temperature.xml)
            }
            Assert.False(SettlementClimate.SomeoneHomeIsCold(v.Settlement, v.Map, v.House, 0f),
                "the hearth wants to burn with nobody home and nobody hurt by the cold");

            Pawn cold = household[0];
            Hediff hurt = HediffMaker.MakeHediff(TemperatureDefOf.Hypothermia, cold);
            hurt.Severity = 0.25f; // the minor stage (0.2 to 0.35, Hediffs_Temperature.xml)
            cold.health.AddHediff(hurt);
            Assert.True(SettlementClimate.SomeoneHomeIsCold(v.Settlement, v.Map, v.House, 0f),
                "one of the house's own freezing away from home does not ask its hearth to burn");

            float comfyMin = TemperatureInjuryUtility.ComfortableTemperatureRange(cold).min;
            IntVec3 inside = v.House.Beds(v.Map).First().GetSleepingSlotPos();
            float air = float.MinValue;
            int warmAt = -1;
            for (int hour = 0; hour < 12 && warmAt < 0; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                air = RotUtility.AmbientTemperatureAt(v.Map, inside);
                if (air >= comfyMin) warmAt = hour;
            }
            output.WriteLine("house air " + air.ToString("0.0") + " comfyMin " + comfyMin.ToString("0.0") + " warm at h" + warmAt);
            Assert.True(warmAt >= 0, "the house was not warm enough for them within 12 h: " + air);
        }

        /// <summary>
        /// The hearth answers whoever lies in the house: a baby of another household laid in its bed for the warmth
        /// keeps the hearth burning for it, with none of the house's own people at home.
        /// </summary>
        [Fact]
        public void A_house_keeps_its_hearth_lit_for_a_guest_baby_in_its_bed()
        {
            Village v = NewWinterVillage("baby-warmth-guest", 2);
            Thing bed = v.House.Beds(v.Map).First();
            Pawn baby = Newborn(v, v.Houseless, bed.GetSleepingSlotPos());
            Assert.True(baby.InBed());
            // Nobody of the house is in it.
            foreach (Pawn p in v.Settlement.Citizens.Where(p => p.relations.familyId == v.Housed && p.Spawned && v.House.InteriorContains(p.Position)).ToList())
            {
                p.Position = OpenGround(v).First();
            }
            Assert.True(TemperatureInjuryUtility.AmbientTemperature(baby) < TemperatureInjuryUtility.ComfortableTemperatureRange(baby).min);
            Assert.True(SettlementClimate.SomeoneHomeIsCold(v.Settlement, v.Map, v.House, 0f),
                "a cold guest baby lying in the house does not ask its hearth to burn");
        }
    }
}
