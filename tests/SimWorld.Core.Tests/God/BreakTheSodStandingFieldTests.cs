using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Break the sod issued over a village whose field is already the size the edict asks for, as in the fourth
    /// playtest: the band sowed what there was in the first morning and then, with every hand held to field work,
    /// had nothing to do for the rest of the edict ("3 citizens idle"), drought or not.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BreakTheSodStandingFieldTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public BreakTheSodStandingFieldTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static (Settlement settlement, CoreMap map, List<Pawn> citizens) Opened(string seed)
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("AgrarianVillage").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            return (settlement, map, settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList());
        }

        private static int SowSamples(List<Pawn> citizens, int ticks)
        {
            int sow = 0;
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % 250 != 0) continue;
                sow += citizens.Count(p => !p.Dead && p.Spawned && p.jobs.curJob?.def.defName == "Sow");
            }
            return sow;
        }

        [Fact]
        public void Break_the_sod_keeps_people_sowing_after_the_standing_field_is_sown()
        {
            (Settlement _, CoreMap map, List<Pawn> citizens) = Opened("sod-standing-field");
            Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict("BreakTheSod").Outcome);

            var perDay = new List<int>();
            for (int day = 0; day < 4; day++)
            {
                perDay.Add(SowSamples(citizens, GenDate.TicksPerDay));
                Zone_Growing field = FarmingInitiative.FieldOf(map)!;
                output.WriteLine($"day {day}: sow samples {perDay[day]}, field {field.CellCount}, to sow {FarmingInitiative.CellsToSow(map, field, field.plantDefToGrow!)}");
            }

            // The first day sows the field the edict found; before the fix the next two had no sowing at all.
            Assert.True(perDay[0] > 0, "nobody sowed on the edict's first day");
            Assert.True(perDay.Skip(1).All(n => n > 0), "a day with no sowing under the edict: " + string.Join(", ", perDay));

            EdictOption option = GodViewSnapshot.Capture().Edicts.First(e => e.DefName == "BreakTheSod");
            output.WriteLine(option.Reason);
            Assert.StartsWith("In force.", option.Reason);
            Assert.True(option.Reason.Length > "In force.".Length, "an edict that breaks ground says what its fields need");
        }

        [Fact]
        public void Out_of_season_the_edict_says_nothing_sown_will_grow()
        {
            (Settlement _, CoreMap map, List<Pawn> _) = Opened("sod-season");
            var frozen = new SimWorld.Weather.MapClimate { averageTemperature = -40f, latitude = 70f };
            Assert.False(FarmingInitiative.GrowingSeasonNow(frozen));
            string report = FarmingInitiative.FieldWorkReport(map, frozen);
            Assert.Contains("out of the growing season", report);

            var mild = new SimWorld.Weather.MapClimate { averageTemperature = 20f, latitude = 10f };
            Assert.True(FarmingInitiative.GrowingSeasonNow(mild));
            Assert.DoesNotContain("growing season", FarmingInitiative.FieldWorkReport(map, mild));
        }
    }
}
