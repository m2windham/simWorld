using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// <c>MapCommands.Designations.cs</c> and <c>MapViewSnapshot.Designations.cs</c> as a host reaches them: a
    /// defName and cells or a thing id in, a <see cref="MapCommandResult"/> and a values-only
    /// <see cref="DesignationsView"/> out.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsDesignationsTests : ContentTestBase
    {
        public MapCommandsDesignationsTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static CoreMap OpenedMap(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement.InteriorMap!;
        }

        private static IntVec3 EmptyCell(CoreMap map)
        {
            foreach (IntVec3 c in map.AllCells)
            {
                if (GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0) return c;
            }
            throw new System.InvalidOperationException("No empty cell.");
        }

        [Fact]
        public void Designating_a_drag_rectangle_marks_what_it_can_and_says_why_the_rest_did_not()
        {
            CoreMap map = OpenedMap("designate-mine-rect");
            // Rock of the test's own: a generated interior need not have any mountain on it.
            Thing rock = GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), EmptyCell(map), map);
            IntVec3 empty = EmptyCell(map);

            MapCommandResult result = MapCommands.Designate("Mine", new List<IntVec3> { rock.Position, empty, new IntVec3(-5, 0, -5) });

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Contains("Marked 1", result.Reason);
            Assert.Contains("nothing here can be mined", result.Reason);
            Assert.Contains("off the map", result.Reason);

            DesignationsView view = MapViewSnapshot.DesignationsOf();
            Assert.True(view.HasMap);
            DesignationView mark = Assert.Single(view.Designations, d => d.ByPlayer);
            Assert.Equal("Mine", mark.DefName);
            Assert.False(mark.TargetsThing);
            Assert.Equal(rock.Position, mark.Cell);

            Assert.Equal(MapCommandOutcome.NoChange, MapCommands.Designate("Mine", new List<IntVec3> { rock.Position }).Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.Designate("Mine", new List<IntVec3> { empty }).Outcome);
            Assert.Equal(MapCommandOutcome.UnknownDef, MapCommands.Designate("Juggle", new List<IntVec3> { empty }).Outcome);

            int before = view.Version;
            Assert.Equal(MapCommandOutcome.Done, MapCommands.RemoveDesignations("Mine", new List<IntVec3> { rock.Position }).Outcome);
            DesignationsView after = MapViewSnapshot.DesignationsOf();
            Assert.DoesNotContain(after.Designations, d => d.ByPlayer);
            Assert.NotEqual(before, after.Version);
            Assert.Equal(MapCommandOutcome.NoChange, MapCommands.RemoveDesignations(null, new List<IntVec3> { empty }).Outcome);
        }

        [Fact]
        public void A_thing_is_designated_by_id_and_refused_in_the_rules_own_words()
        {
            CoreMap map = OpenedMap("designate-by-id");
            Pawn citizen = map.mapPawns.AllPawnsSpawned.First(p => p.RaceProps.Humanlike);
            var chicken = new Pawn(DefDatabase<ThingDef>.GetNamed("Chicken"), "Chicken");
            GenSpawn.Spawn(chicken, EmptyCell(map), map);

            MapCommandResult refused = MapCommands.Designate("Hunt", citizen.thingIDNumber);
            Assert.Equal(MapCommandOutcome.Refused, refused.Outcome);
            Assert.Contains("is not an animal", refused.Reason);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.Designate("Mine", chicken.thingIDNumber).Outcome);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.Designate("Hunt", chicken.thingIDNumber).Outcome);
            Assert.Equal(MapCommandOutcome.NoChange, MapCommands.Designate("Hunt", chicken.thingIDNumber).Outcome);
            DesignationView mark = Assert.Single(MapViewSnapshot.DesignationsOf().Designations, d => d.DefName == "Hunt");
            Assert.True(mark.TargetsThing);
            Assert.Equal(chicken.thingIDNumber, mark.ThingId);

            // Tame replaces Hunt on the same animal.
            Assert.Equal(MapCommandOutcome.Done, MapCommands.Designate("Tame", chicken.thingIDNumber).Outcome);
            Assert.DoesNotContain(MapViewSnapshot.DesignationsOf().Designations, d => d.DefName == "Hunt");

            Assert.Equal(MapCommandOutcome.Done, MapCommands.RemoveDesignations(null, chicken.thingIDNumber).Outcome);
            Assert.DoesNotContain(MapViewSnapshot.DesignationsOf().Designations, d => d.ByPlayer);
        }

        [Fact]
        public void The_initiative_switch_is_set_and_read_back_through_the_seam()
        {
            OpenedMap("designate-switch");

            Assert.All(MapViewSnapshot.DesignationsOf().Initiatives, s => Assert.True(s.On));
            Assert.Equal(new[] { "Mine", "CutPlant", "Hunt", "Tame" },
                MapViewSnapshot.DesignationsOf().Initiatives.Select(s => s.DesignationDefName));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetSettlementInitiative("CutPlant", false).Outcome);
            Assert.Equal(MapCommandOutcome.NoChange, MapCommands.SetSettlementInitiative("CutPlant", false).Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.SetSettlementInitiative("Slaughter", false).Outcome);
            Assert.Equal(MapCommandOutcome.UnknownDef, MapCommands.SetSettlementInitiative("Nope", false).Outcome);

            InitiativeSwitchView cut = MapViewSnapshot.DesignationsOf().Initiatives.Single(s => s.DesignationDefName == "CutPlant");
            Assert.False(cut.On);
        }
    }
}
