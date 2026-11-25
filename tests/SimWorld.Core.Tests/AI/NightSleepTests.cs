using System.Collections.Generic;
using System.Linq;

using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// "Healthy citizens lie down in daytime", from the fourth playtest. The cause: nobody's sleep was tied to
    /// the clock. With only the tired rule a citizen slept when the need ran low, at any hour, and the band
    /// drifted until a dozen were hauling at midnight and five lay down at noon. RimWorld's default timetable
    /// sleeps 22h to 5h; this checks a generated band follows it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class NightSleepTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public NightSleepTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        [Fact]
        public void A_band_sleeps_at_night_and_is_up_by_day()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "night-sleep", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement home = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(home.tile).Outcome);
            List<Pawn> band = home.Citizens.Where(p => p.Spawned && p.RaceProps.Humanlike).ToList();

            // A day to settle, then a day counted: asleep in the night block (0h-4h) against the working day (10h-16h).
            int night = 0, nightSamples = 0, day = 0, daySamples = 0;
            for (int i = 0; i < GenDate.TicksPerDay * 2; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i < GenDate.TicksPerDay || i % 500 != 0) continue;
                int hour = GenDate.HourOfDay(Find.TickManager.TicksAbs, SimWorld.Building.PlantUtility.AssumedLongitudeDegrees);
                int asleep = band.Count(p => !p.Dead && p.Spawned && p.Asleep);
                if (hour <= 4) { night += asleep; nightSamples++; }
                else if (hour >= 10 && hour <= 16) { day += asleep; daySamples++; }
            }
            float atNight = night / (float)nightSamples, byDay = day / (float)daySamples;
            output.WriteLine($"asleep on average: {atNight:0.0} of {band.Count} at night, {byDay:0.0} by day");
            Assert.True(atNight >= band.Count / 2f, $"only {atNight:0.0} of {band.Count} asleep in the night");
            Assert.True(byDay * 4f <= atNight, $"{byDay:0.0} asleep by day against {atNight:0.0} at night");
        }
    }
}
