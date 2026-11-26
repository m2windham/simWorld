using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.MapGen
{
    /// <summary>
    /// The age a town is founded in changes what its map's ruins are made of and hold, never where they stand. The
    /// first cut of era-limited ruins dropped steel from the pools, which moved every ruin after the first; on the
    /// era arc's seed 201 that left the house street short of plots for good and the town stood in the Classical age
    /// from day 121 to day 600, where the same seed had reached the Exotic.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RuinsOfTheAgeLayoutTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public RuinsOfTheAgeLayoutTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        /// <summary>TribalStart with its starting era moved to <paramref name="era"/>.</summary>
        private static global::SimWorld.Scenario.Scenario StartingIn(string era)
        {
            global::SimWorld.Scenario.Scenario tribal = ScenarioDefOf.TribalStart.scenario;
            var scenario = new global::SimWorld.Scenario.Scenario { name = era + " start", summary = era, description = era };
            foreach (ScenPart part in tribal.parts)
            {
                scenario.parts.Add(part is ScenPart_StartingEra starting
                    ? new ScenPart_StartingEra { def = starting.def, era = DefDatabase<EraDef>.GetNamed(era) }
                    : part);
            }
            return scenario;
        }

        /// <summary>Every ruin wall on the opened map (a wall no faction owns), by cell, with what it is made of.</summary>
        private static Dictionary<IntVec3, string> RuinWalls(global::SimWorld.Scenario.Scenario scenario, string seed)
        {
            NameUseChecker.Clear();
            Game game = Game.NewGame(scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            global::SimWorld.World.Settlement seat = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(seat.tile).Outcome);
            CoreMap map = seat.InteriorMap!;
            return map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => t.Faction == null && t.def.defName == "Wall")
                .ToDictionary(t => t.Position, t => t.Stuff?.defName ?? "-");
        }

        [Fact]
        public void A_ruin_stands_where_it_would_in_any_age()
        {
            Dictionary<IntVec3, string> stone = RuinWalls(ScenarioDefOf.TribalStart.scenario, "ruins-layout");
            Dictionary<IntVec3, string> medieval = RuinWalls(StartingIn("Medieval"), "ruins-layout");
            output.WriteLine($"stone age: {stone.Count} ruin walls [{string.Join(", ", stone.Values.GroupBy(v => v).Select(g => g.Key + " " + g.Count()))}]; "
                + $"medieval: {medieval.Count} [{string.Join(", ", medieval.Values.GroupBy(v => v).Select(g => g.Key + " " + g.Count()))}]");

            Assert.NotEmpty(stone);
            Assert.Contains("Steel", medieval.Values); // the seed has a steel ruin for the stone age to stand in for
            Assert.DoesNotContain("Steel", stone.Values);
            Assert.Equal(medieval.Keys.OrderBy(c => c.x).ThenBy(c => c.z), stone.Keys.OrderBy(c => c.x).ThenBy(c => c.z));
        }
    }
}
