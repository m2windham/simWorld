using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Conditions;
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
using SimWorld.Work;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Babies and the cold, on a real generated map. A baby cannot walk, wears nothing and is comfortable only
    /// from 16 C; it moves only when somebody rescues it (<see cref="WorkGiver_RescueDowned"/>), and it is warm only
    /// where its household's hearth burns (<see cref="SettlementClimate"/>). Every exposure death of a ten-year
    /// arc was a baby: carried to a neighbour's bed found for the rescuer, in a room nobody heated for it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BabyShelterTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BabyShelterTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private sealed class Village
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map = null!;
            public int FamilyA, FamilyB;
            public Dwelling HouseA = null!, HouseB = null!;
        }

        /// <summary>A generated tribal start, its compound laid out, and two of its households housed in finished
        /// House_Meso on the planner's own plots.</summary>
        private static Village NewVillage(string seed)
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
            v.FamilyA = families[0];
            v.FamilyB = families[1];
            DwellingDef meso = ShelterKit.Rung("House_Meso");
            v.HouseA = ShelterKit.RaiseOnPlot(v.Map, meso, v.FamilyA, v.Settlement.faction)!;
            v.HouseB = ShelterKit.RaiseOnPlot(v.Map, meso, v.FamilyB, v.Settlement.faction)!;
            Assert.NotNull(v.HouseA);
            Assert.NotNull(v.HouseB);
            return v;
        }

        /// <summary>A newborn of <paramref name="family"/>, born as <see cref="FamilyManager"/> births one, and
        /// laid on open ground the way a birth leaves it.</summary>
        private static Pawn Newborn(Village v, int family)
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: v.Settlement.faction);
            baby.relations.familyId = family;
            v.Settlement.AddCitizen(baby);
            IntVec3 near = new IntVec3(v.HouseA.footprint.minX, 0, v.HouseA.footprint.minZ);
            IntVec3 cell = v.Map.AllCells.OrderBy(c => (c - near).LengthHorizontalSquared).First(c => GenGrid.Standable(c, v.Map) && v.Map.roomTracker.RoomAt(c)?.Group.UsesOutdoorTemperature != false
                && DwellingUtility.DwellingAt(v.Map, c) == null && v.Map.thingGrid.ThingsListAt(c).Count == 0
                && Reachability.CanReach(Member(v, family), new LocalTargetInfo(c), PathEndMode.Touch));
            GenSpawn.Spawn(baby, cell, v.Map);
            Run(v.Game, 1); // the health tick reads the life stage and lays it down
            Assert.True(baby.Downed, "a newborn is downed by its life stage");
            return baby;
        }

        private static Pawn Member(Village v, int family) =>
            v.Settlement.Citizens.First(p => !p.Dead && p.Spawned && p.relations.familyId == family
                && p.ageTracker.CurLifeStage?.developmentalStage != DevelopmentalStage.Baby);

        private static void Run(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.TickManager.DoSingleTick();
        }

        private static string HearthState(CoreMap map, Dwelling house)
        {
            string state = "";
            foreach (IntVec3 spot in house.Spots(DwellingPartKind.HearthSpot))
            {
                foreach (Thing t in map.thingGrid.ThingsListAt(spot))
                {
                    var tw = t as ThingWithComps;
                    CompFlickable? flick = tw?.GetComp<CompFlickable>();
                    CompRefuelable? fuel = tw?.GetComp<CompRefuelable>();
                    state += "[" + t.def.defName + (flick == null ? "" : " want " + flick.WantSwitchOn + " on " + flick.SwitchIsOn)
                        + (fuel == null ? "" : " fuel " + fuel.Fuel.ToString("0.0")) + "]";
                }
            }
            int wood = 0;
            foreach (Thing w in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("WoodLog"))) wood += w.stackCount;
            return state + " wood on map " + wood;
        }

        private static bool InOwnHomeBed(Pawn baby) =>
            baby.CurrentBed() is Thing bed && DwellingUtility.IsOwnHomeBed(baby, bed);

        // ---- the rescue bed is the patient's ----

        [Fact]
        public void A_neighbour_rescuing_a_baby_carries_it_to_its_own_households_bed()
        {
            Village v = NewVillage("baby-rescue-home");
            Pawn baby = Newborn(v, v.FamilyA);
            Pawn neighbour = Member(v, v.FamilyB);

            var giver = new WorkGiver_RescueDowned();
            Assert.True(giver.HasJobOnThing(neighbour, baby));
            Job job = giver.JobOnThing(neighbour, baby)!;
            Assert.NotNull(job);
            Thing bed = job.GetTarget(TargetIndex.B).Thing!;
            // Found for the baby: its own household's house, not the neighbour's, though the neighbour's own home
            // has free beds and would be the rescuer's answer.
            Assert.Same(v.HouseA, DwellingUtility.DwellingOfBed(bed));
            Assert.NotNull(DwellingUtility.FindDwellingBedFor(neighbour));

            neighbour.jobs.StartJob(job);
            for (int i = 0; i < 20 && !baby.InBed(); i++) Run(v.Game, 500);
            Assert.True(InOwnHomeBed(baby), "the baby lies at " + baby.Position + ", not in a bed of its own household's house");

            // In its own bed, nobody moves it again.
            Assert.False(giver.HasJobOnThing(neighbour, baby));
        }

        [Fact]
        public void A_baby_laid_in_a_neighbours_bed_is_carried_home()
        {
            Village v = NewVillage("baby-rescue-home");
            Pawn baby = Newborn(v, v.FamilyA);
            Pawn neighbour = Member(v, v.FamilyB);
            Thing strangersBed = v.HouseB.Beds(v.Map).First();
            baby.Position = strangersBed.GetSleepingSlotPos();
            Assert.True(baby.InBed());

            var giver = new WorkGiver_RescueDowned();
            Assert.True(giver.HasJobOnThing(neighbour, baby));
            Assert.Same(v.HouseA, DwellingUtility.DwellingOfBed(giver.JobOnThing(neighbour, baby)!.GetTarget(TargetIndex.B).Thing!));
        }

        [Fact]
        public void Two_babies_of_one_household_are_not_laid_in_the_same_bed()
        {
            Village v = NewVillage("baby-rescue-home");
            Pawn first = Newborn(v, v.FamilyA);
            Pawn second = Newborn(v, v.FamilyA);
            Pawn rescuer = Member(v, v.FamilyA);
            var giver = new WorkGiver_RescueDowned();
            Thing firstBed = giver.JobOnThing(rescuer, first)!.GetTarget(TargetIndex.B).Thing!;
            first.Position = firstBed.GetSleepingSlotPos();

            Thing? secondBed = giver.JobOnThing(rescuer, second)?.GetTarget(TargetIndex.B).Thing;
            Assert.NotNull(secondBed);
            Assert.NotSame(firstBed, secondBed);
        }

        // ---- the winter ----

        /// <summary>
        /// A newborn left on the ground in a hard frost, in a band nobody tells what to do: it is carried to its
        /// own household's bed, the household lights its hearth for it, and it comes through without the
        /// hypothermia that killed every baby of the arcs. The cold is a cold snap held for the length of a winter
        /// quadrum (RimWorld's -20 C offset on today's season), pinned below the baby's safe minimum so the test
        /// cannot pass in mild weather.
        /// </summary>
        [Fact]
        public void A_newborn_in_a_hard_winter_is_carried_home_kept_warm_and_lives()
        {
            Village v = NewVillage("baby-winter");
            int winterTicks = 15 * GenDate.TicksPerDay;
            int lerp = GameCondition_TemperatureOffset.LerpTicks;
            v.Map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("ColdSnap"), winterTicks + 2 * lerp));
            Run(v.Game, lerp); // the snap comes in over RimWorld's lerp; the baby is born into the full cold
            Pawn baby = Newborn(v, v.FamilyA);
            float safeMin = TemperatureInjuryUtility.SafeTemperatureRange(baby).min;
            Assert.True(SettlementClimate.Outlook(v.Map).DailyMean < safeMin,
                "the winter is not below the baby's safe minimum: mean " + SettlementClimate.Outlook(v.Map).DailyMean);

            float worst = 0f;
            for (int hour = 0; hour < winterTicks / GenDate.TicksPerHour; hour++)
            {
                Run(v.Game, GenDate.TicksPerHour);
                Assert.False(baby.Dead, "the baby died on hour " + hour);
                worst = System.Math.Max(worst, baby.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f);
                if (hour % 12 == 0)
                {
                    output.WriteLine("h" + hour + " out " + v.Map.outdoorTemperature.ToString("0.0") + " baby air "
                        + TemperatureInjuryUtility.AmbientTemperature(baby).ToString("0.0") + " home " + InOwnHomeBed(baby)
                        + " hypothermia " + (baby.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f).ToString("0.00")
                        + " hearth " + HearthState(v.Map, v.HouseA));
                }
            }

            Assert.True(InOwnHomeBed(baby), "the baby ended the winter at " + baby.Position + ", not in its household's bed");
            Assert.True(worst < 0.35f, "hypothermia reached " + worst + " (serious from 0.35)");
            float air = TemperatureInjuryUtility.AmbientTemperature(baby);
            Assert.True(air >= TemperatureInjuryUtility.ComfortableTemperatureRange(baby).min,
                "the baby's room is at " + air + " at the end of the winter, below its comfortable minimum");
        }
    }
}
