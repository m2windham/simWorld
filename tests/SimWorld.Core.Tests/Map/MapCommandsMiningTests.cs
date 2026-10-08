using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The player's door into mining: <see cref="MapCommands.DesignateMine"/> and
    /// <see cref="MapCommands.CancelMineDesignation"/> (RimWorld: <c>Designator_Mine</c>).
    ///
    /// <para/>Until these existed the player could not mark a cell at all — there was no mark to make. The
    /// headline here is the same one <c>MapCommandsTests.A_citizen_builds_what_the_player_designated</c> makes
    /// for blueprints: nothing in <see cref="A_citizen_digs_what_the_player_marked_and_nothing_else"/> calls a
    /// worker or a job driver. It marks a rock through the command surface a host has, switches every other
    /// kind of work off for everybody so only the mark can explain what happens, and drives the real tick
    /// loop until a citizen has walked up and dug it out.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsMiningTests : ContentTestBase
    {
        public MapCommandsMiningTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        /// <summary>A seed whose tile has a mountain on it. The tile a seed lands on decides whether the map has
        /// any rock at all (a Flat tile has none), and these tests are about rock; this is one of the three
        /// <c>SettlementRoofCollapseTests</c> was measured on.</summary>
        private const string RockySeed = "roof-a";

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static List<Pawn> AllCitizensOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();

        private static bool RunUntil(int maxTicks, Func<bool> condition, params Pawn[] pawns)
        {
            RunTicks(0, pawns);
            TickManager tm = Find.TickManager;
            for (int i = 0; i < maxTicks; i++)
            {
                if (condition()) return true;
                tm.DoSingleTick();
            }
            return condition();
        }

        private static bool IsMarked(CoreMap map, IntVec3 cell) =>
            map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) != null;

        /// <summary>Rock the way a miner could actually be sent to it: standing, not holding a roof up, and
        /// reachable by <paramref name="miner"/> — nearest first, so the test gets one predictable answer.
        /// Cells nearest the miner can be a mountain's open edge, which is exactly where the guard says no.</summary>
        private static List<Mineable> DiggableRockNear(CoreMap map, Pawn miner, int count)
        {
            return map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .OfType<Mineable>()
                .Where(r => r.Spawned && r.def.mineable)
                .OrderBy(r => (r.Position - miner.Position).LengthHorizontalSquared)
                .ThenBy(r => r.Position.z).ThenBy(r => r.Position.x)
                .Where(r => !RoofCollapseUtility.WouldCollapseRoofIfRemoved(r) && Reachability.CanReach(miner, r, PathEndMode.Touch))
                .Take(count)
                .ToList();
        }

        /// <summary>Everybody's work switched off, so that only an explicit act can explain what they do.</summary>
        private static void SilenceEveryonesStandingWork(Settlement settlement, CoreMap map)
        {
            foreach (Pawn p in AllCitizensOn(settlement, map)) p.workSettings!.DisableAll();
        }

        // ---- refusals ----

        [Fact]
        public void With_no_settlement_open_both_commands_say_so()
        {
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.DesignateMine(new[] { new IntVec3(1, 0, 1) }).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.CancelMineDesignation(new[] { new IntVec3(1, 0, 1) }).Outcome);
        }

        [Fact]
        public void Asking_for_no_cells_or_cells_off_the_map_is_refused_and_marks_nothing()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Pawn worker = AllCitizensOn(settlement, map).First();
            Mineable rock = DiggableRockNear(map, worker, 1).Single();

            Assert.Equal(MapCommandOutcome.Refused, MapCommands.DesignateMine(Array.Empty<IntVec3>()).Outcome);
            MapCommandResult offMap = MapCommands.DesignateMine(new[] { rock.Position, new IntVec3(map.Size.x + 5, 0, 2) });

            Assert.Equal(MapCommandOutcome.OffMap, offMap.Outcome);
            Assert.False(IsMarked(map, rock.Position), "A request with a cell off the map is malformed and marks none of it.");
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.CancelMineDesignation(new[] { new IntVec3(-1, 0, 0) }).Outcome);
        }

        [Fact]
        public void Cells_with_no_rock_in_them_are_refused_when_that_is_all_there_is()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            IntVec3 open = map.AllCells.First(c => map.edificeGrid[c] == null && GenGrid.Walkable(c, map));

            MapCommandResult result = MapCommands.DesignateMine(new[] { open });

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        // ---- marking ----

        [Fact]
        public void A_rectangle_over_the_edge_of_a_mountain_marks_the_rock_and_skips_the_open_ground()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Pawn worker = AllCitizensOn(settlement, map).First();
            Mineable rock = DiggableRockNear(map, worker, 1).Single();

            // Every cell within two of the rock: some rock, some open ground, as a drag across a mountain edge is.
            var area = new CellRect(rock.Position.x - 2, rock.Position.z - 2, 5, 5).ClipInsideMap(map).Cells.ToList();
            int rockCells = area.Count(c => MineableUtility.GetFirstMineable(c, map) != null);
            Assert.InRange(rockCells, 1, area.Count - 1);

            MapCommandResult result = MapCommands.DesignateMine(area);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Equal(rockCells, map.designationManager.AllDesignations.Count);
            foreach (IntVec3 c in area)
            {
                Assert.Equal(MineableUtility.GetFirstMineable(c, map) != null, IsMarked(map, c));
            }
            Assert.Contains("skipped", result.Reason);
        }

        [Fact]
        public void Marking_what_is_already_marked_is_no_change_and_marks_nothing_twice()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Mineable rock = DiggableRockNear(map, AllCitizensOn(settlement, map).First(), 1).Single();

            Assert.Equal(MapCommandOutcome.Done, MapCommands.DesignateMine(new[] { rock.Position }).Outcome);
            MapCommandResult again = MapCommands.DesignateMine(new[] { rock.Position });

            Assert.Equal(MapCommandOutcome.NoChange, again.Outcome);
            Assert.Single(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_cell_holding_a_roof_up_is_marked_all_the_same_because_that_is_the_players_call()
        {
            // The command refuses only the physically impossible. WorkGiver_Miner keeps its own judgement
            // about digging a support (see MiningDesignationTests), so the mark is recorded and stays.
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Mineable support = map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .OfType<Mineable>()
                .OrderBy(r => r.Position.z).ThenBy(r => r.Position.x)
                .First(r => RoofCollapseUtility.WouldCollapseRoofIfRemoved(r));

            MapCommandResult result = MapCommands.DesignateMine(new[] { support.Position });

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(IsMarked(map, support.Position));
        }

        // ---- cancelling ----

        [Fact]
        public void Cancelling_takes_marks_off_the_marked_cells_and_ignores_the_rest()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            List<Mineable> rocks = DiggableRockNear(map, AllCitizensOn(settlement, map).First(), 3);
            MapCommands.DesignateMine(rocks.Select(r => r.Position).ToList());
            Assert.Equal(3, map.designationManager.AllDesignations.Count);

            MapCommandResult one = MapCommands.CancelMineDesignation(new[] { rocks[0].Position, new IntVec3(0, 0, 0) });

            Assert.Equal(MapCommandOutcome.Done, one.Outcome);
            Assert.False(IsMarked(map, rocks[0].Position));
            Assert.True(IsMarked(map, rocks[1].Position));
            Assert.True(IsMarked(map, rocks[2].Position));

            MapCommandResult none = MapCommands.CancelMineDesignation(new[] { rocks[0].Position });
            Assert.Equal(MapCommandOutcome.NoChange, none.Outcome);
        }

        [Fact]
        public void The_player_can_cancel_a_mark_the_settlement_made_because_they_are_one_ledger()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Mineable rock = DiggableRockNear(map, AllCitizensOn(settlement, map).First(), 1).Single();
            // Whoever made it: the manager does not record authorship, and the command does not ask.
            map.designationManager.AddDesignation(new Designation(rock.Position, DesignationDefOf.Mine));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.CancelMineDesignation(new[] { rock.Position }).Outcome);
            Assert.False(IsMarked(map, rock.Position));
        }

        // ---- the headline ----

        [Fact]
        public void A_citizen_digs_what_the_player_marked_and_nothing_else()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();

            // Nobody chooses any work at all, except one who mines. If anything gets dug, it is the mark.
            SilenceEveryonesStandingWork(settlement, map);
            Pawn miner = everyone[0];
            miner.workSettings!.SetPriority(WorkTypeDefOf.Mining, 1);
            miner.skills!.GetSkill(SkillDefOf.Mining)!.Level = 15;

            List<Mineable> candidates = DiggableRockNear(map, miner, 2);
            Mineable marked = candidates[0];
            Mineable bystander = candidates[1];
            int rockBefore = map.listerThings.ThingsInGroup(ThingRequestGroup.Building).OfType<Mineable>().Count();

            MapCommandResult result = MapCommands.DesignateMine(new[] { marked.Position });
            Assert.Equal(MapCommandOutcome.Done, result.Outcome);

            Assert.True(RunUntil(20000, () => marked.Destroyed, everyone), "The marked rock was never dug.");

            Assert.False(IsMarked(map, marked.Position), "The mark should have gone with the rock.");
            Assert.False(bystander.Destroyed);
            Assert.Equal(bystander.MaxHitPoints, bystander.HitPoints);
            int rockAfter = map.listerThings.ThingsInGroup(ThingRequestGroup.Building).OfType<Mineable>().Count();
            Assert.Equal(rockBefore - 1, rockAfter);
        }

        [Fact]
        public void A_mark_withdrawn_before_anyone_gets_to_it_is_never_dug()
        {
            Settlement settlement = OpenedSettlement(RockySeed);
            CoreMap map = settlement.InteriorMap!;
            Pawn[] everyone = AllCitizensOn(settlement, map).ToArray();
            SilenceEveryonesStandingWork(settlement, map);
            Pawn miner = everyone[0];
            miner.workSettings!.SetPriority(WorkTypeDefOf.Mining, 1);
            Mineable rock = DiggableRockNear(map, miner, 1).Single();

            MapCommands.DesignateMine(new[] { rock.Position });
            MapCommands.CancelMineDesignation(new[] { rock.Position });
            RunTicks(6000, everyone);

            Assert.False(rock.Destroyed);
            Assert.Equal(rock.MaxHitPoints, rock.HitPoints);
        }
    }
}
