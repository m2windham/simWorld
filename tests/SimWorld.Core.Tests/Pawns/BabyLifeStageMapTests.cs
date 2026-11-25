using System.Collections.Generic;
using System.Linq;

using SimWorld.Alerts;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// A baby on a real generated map is a baby, not a casualty. The third playtest's babies read "25%, down,
    /// cannot walk", Rest 0%, Recreation 0% and extreme break risk for the three years each was a baby: the health
    /// readout capped anyone downed at a quarter, no road to sleep existed without a job, the recreation need had
    /// no way to be met, and the mental breaker counted them like adults. In RimWorld (Biotech, recalled) a baby
    /// sleeps in a crib or on the ground, plays rather than takes recreation, and cries rather than breaks.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BabyLifeStageMapTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BabyLifeStageMapTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static (Settlement settlement, CoreMap map) Opened(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return (settlement, settlement.InteriorMap!);
        }

        /// <summary>A newborn of the band, born as FamilyManager births one, laid beside an adult of the band.</summary>
        private static Pawn Newborn(Settlement settlement, CoreMap map)
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: settlement.faction);
            Pawn adult = settlement.Citizens.First(p => p.Spawned && !p.Downed);
            baby.relations.familyId = adult.relations.familyId;
            settlement.AddCitizen(baby);
            IntVec3 cell = map.AllCells.OrderBy(c => (c - adult.Position).LengthHorizontalSquared)
                .First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0);
            GenSpawn.Spawn(baby, cell, map);
            Find.TickManager.DoSingleTick(); // the health tick reads the life stage and lays it down
            Assert.True(baby.Downed, "a newborn is down by its life stage");
            return baby;
        }

        [Fact]
        public void A_healthy_baby_reads_whole_and_not_down_and_a_hurt_one_still_does()
        {
            (Settlement settlement, CoreMap map) = Opened("baby-readout");
            Pawn baby = Newborn(settlement, map);

            Assert.True(HealthReadout.Of(baby) > 0.9f, "a healthy baby reads " + HealthReadout.Of(baby));
            CitizenLine line = Line(settlement, baby);
            Assert.False(line.Downed, "a baby lying down because it cannot walk yet is not shown as down");

            // Down for another reason as well: the readout and the roster say so again.
            baby.health.ForceDowned = true;
            Assert.True(HealthReadout.Of(baby) <= HealthReadout.DownedCeiling);
            Assert.True(Line(settlement, baby).Downed);
        }

        [Fact]
        public void A_baby_has_no_recreation_need_and_gains_one_when_it_grows_into_a_child()
        {
            (Settlement settlement, CoreMap map) = Opened("baby-joy");
            Pawn baby = Newborn(settlement, map);
            Assert.Null(baby.needs.joy);
            Assert.NotNull(baby.needs.rest);
            Assert.NotNull(baby.needs.food);
            Assert.All(settlement.Citizens.Where(p => p.ageTracker.CurLifeStage?.developmentalStage != DevelopmentalStage.Baby),
                p => Assert.NotNull(p.needs.joy));

            baby.ageTracker.DebugSetAge(4f);
            Assert.Equal(DevelopmentalStage.Child, baby.ageTracker.CurLifeStage!.developmentalStage);
            Assert.NotNull(baby.needs.joy);
        }

        [Fact]
        public void A_baby_sleeps_when_tired_and_its_rest_never_runs_out()
        {
            (Settlement settlement, CoreMap map) = Opened("baby-rest");
            Pawn baby = Newborn(settlement, map);
            baby.needs.rest!.CurLevel = Need_Rest.ThreshTired - 0.02f;

            int asleep = 0, samples = 0;
            float lowest = 1f;
            for (int i = 0; i < GenDate.TicksPerDay * 2; i++)
            {
                Find.TickManager.DoSingleTick();
                if (baby.Dead) break;
                if (i % 250 != 0) continue;
                samples++;
                if (baby.Asleep) asleep++;
                lowest = System.Math.Min(lowest, baby.needs.rest.CurLevel);
            }
            output.WriteLine($"asleep {asleep}/{samples}, lowest rest {lowest:0.00}, now {baby.needs.rest.CurLevel:0.00}, activity {Line(settlement, baby).ActivityLabel}");
            Assert.False(baby.Dead);
            Assert.True(asleep > 0 && asleep < samples, "a baby sleeps part of the day, not all of it and not never");
            Assert.True(lowest > 0.05f, "a baby's rest never runs out: lowest " + lowest);
            Assert.DoesNotContain(baby.needs.mood!.thoughts.MoodThoughtGroupsAsComputed(), g => g[0].def.defName == "Tired" && g[0].MoodOffset() <= -10f);
        }

        private static CitizenLine Line(Settlement settlement, Pawn pawn) =>
            GodViewSnapshot.CitizensOf(settlement.tile)!.Citizens.Single(c => c.ThingId == pawn.thingIDNumber);

        [Fact]
        public void A_baby_is_never_at_break_risk_however_low_its_mood_and_an_adult_is()
        {
            (Settlement settlement, CoreMap map) = Opened("baby-break");
            Pawn baby = Newborn(settlement, map);
            Pawn adult = settlement.Citizens.First(p => p.Spawned && !p.Downed && p != baby);

            baby.needs.mood!.CurLevel = 0.01f;
            adult.needs.mood!.CurLevel = 0.01f;
            Assert.Equal(MentalBreakIntensity.None, baby.mindState.mentalBreaker.CurMoodBreakIntensity);
            Assert.False(baby.mindState.mentalBreaker.CanDoRandomMentalBreaks);
            Assert.Equal(MentalBreakIntensity.Extreme, adult.mindState.mentalBreaker.CurMoodBreakIntensity);

            CitizenLine babyLine = Line(settlement, baby);
            Assert.True(babyLine.MoodBand is null or MentalBreakIntensity.None, "the roster shows no break risk for a baby");

            var context = new AlertContext(map, settlement.Citizens.Where(p => p.Spawned).ToList(), settlement.tile);
            AlertReport report = new Alert_MajorOrExtremeBreakRisk().GetReport(context);
            Assert.Contains(adult, report.Culprits);
            Assert.DoesNotContain(baby, report.Culprits);
        }
    }
}
