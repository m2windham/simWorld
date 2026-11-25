using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// <see cref="GodCommands.SetWorkPriority"/> and <see cref="GodViewSnapshot.WorkPrioritiesOf"/>: the work
    /// tab as a lever. The headline test runs the real think tree and shows the citizen taking different work
    /// because of the command, not merely a number changing.
    /// </summary>
    public class WorkPriorityCommandTests : ContentTestBase
    {
        public WorkPriorityCommandTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static WorkTypeDef Work(string name) => DefDatabase<WorkTypeDef>.GetNamed(name);

        private static CoreWorld BareWorld()
        {
            var world = new CoreWorld();
            Find.World = world;
            return world;
        }

        private static Settlement NewSettlement(CoreWorld world, int tile, string name)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            world.worldObjects.Add(settlement);
            return settlement;
        }

        private static Pawn Citizen(Settlement settlement, string name)
        {
            Pawn p = NewHuman(name);
            settlement.AddCitizen(p);
            return p;
        }

        /// <summary>Construction (naturalPriority 800) and Mining (700) both on offer; the same fixture the role
        /// test in <see cref="GodCommandsWorkPolicyTests"/> uses.</summary>
        private static (Pawn Pawn, Thing Rock) MinerOrBuilderFixture(int tile)
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, tile, "WorkTown");
            var map = new CoreMap(8, 8, TerrainDefOf.Soil);
            Pawn pawn = NewHuman("Worker");
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 0), map);
            settlement.AddCitizen(pawn);

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), new IntVec3(5, 0, 5), map);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 5;
            GenSpawn.Spawn(wood, new IntVec3(1, 0, 1), map);
            Thing rock = ThingMaker.MakeThing(Def("Sandstone"));
            GenSpawn.Spawn(rock, new IntVec3(2, 0, 0), map);
            map.designationManager.AddDesignation(new global::SimWorld.Designations.Designation(new global::SimWorld.AI.LocalTargetInfo(rock.Position), global::SimWorld.Designations.DesignationDefOf.Mine));
            return (pawn, rock);
        }

        // ---- the lever ----

        [Fact]
        public void Mining_first_reorders_what_the_work_scan_tries_first()
        {
            (Pawn pawn, _) = MinerOrBuilderFixture(1);
            Assert.NotEqual("Mining", pawn.workSettings.WorkGiversInOrderNormal[0].workType.defName);

            GodCommandResult result = GodCommands.SetWorkPriority(pawn.thingIDNumber, "Mining", 1);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal("Mining", pawn.workSettings.WorkGiversInOrderNormal[0].workType.defName);
        }

        [Fact]
        public void The_first_job_taken_follows_the_priority_and_without_it_is_construction()
        {
            (Pawn control, _) = MinerOrBuilderFixture(3);
            RunTicks(60, control);
            Assert.NotEqual(global::SimWorld.AI.JobDefOf.Mine, control.jobs.curJob?.def);

            (Pawn miner, _) = MinerOrBuilderFixture(30);
            GodCommands.SetWorkPriority(miner.thingIDNumber, "Mining", 1);
            RunTicks(60, miner);
            Assert.Equal(global::SimWorld.AI.JobDefOf.Mine, miner.jobs.curJob?.def);
        }

        [Fact]
        public void Zero_takes_the_work_out_of_the_scan_entirely()
        {
            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 4, "Town"), "A");

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 0).Outcome);

            Assert.DoesNotContain(p.workSettings.WorkGiversInOrderNormal, g => g.workType == Work("Mining"));
            Assert.Equal(0, p.workSettings.GetPriority(Work("Mining")));
        }

        [Fact]
        public void A_set_priority_survives_an_office_role_applied_afterwards()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 5, "Town");
            Pawn p = Citizen(settlement, "A");

            GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 4);
            GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner");

            Assert.Equal(4, p.workSettings.GetPriority(Work("Mining")));
        }

        [Fact]
        public void A_priority_is_answered_in_words_of_order_not_a_number()
        {
            Assert.Equal(WorkPriorityTable.LowestPriority, WorkPriorityTable.PriorityWords.Count);
            Assert.Equal(new[] { "first", "soon", "later", "last" }, WorkPriorityTable.PriorityWords);
            Assert.Equal("left alone", WorkPriorityTable.PriorityWord(0));

            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 21, "Town"), "Wordy");
            GodCommandResult set = GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 2);
            Assert.Equal(GodCommandOutcome.Done, set.Outcome);
            Assert.EndsWith(" soon.", set.Reason);
            Assert.DoesNotContain("priority", set.Reason);
            Assert.EndsWith(" soon.", GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 2).Reason);
        }

        // ---- refusals ----

        [Fact]
        public void Work_a_trait_forbids_is_refused_with_the_trait_named()
        {
            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 6, "Town"), "Pyro");
            p.story.traits.GainTrait(new Trait(Trait("Pyromaniac")));

            GodCommandResult result = GodCommands.SetWorkPriority(p.thingIDNumber, "Firefighter", 1);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("fire-touched", result.Reason);
            Assert.Equal(0, p.workSettings.GetPriority(Work("Firefighter")));
        }

        [Fact]
        public void The_table_shows_the_disabled_cell_with_the_same_reason_the_command_gives()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 7, "Town");
            Pawn p = Citizen(settlement, "Pyro");
            p.story.traits.GainTrait(new Trait(Trait("Pyromaniac")));

            WorkPriorityTable table = GodViewSnapshot.WorkPrioritiesOf(settlement.tile)!;
            WorkPriorityCell cell = table.Rows.Single(r => r.ThingId == p.thingIDNumber)
                .Cells.Single(c => c.WorkTypeDefName == "Firefighter");

            Assert.True(cell.Disabled);
            Assert.Equal(0, cell.Priority);
            Assert.Equal(GodCommands.SetWorkPriority(p.thingIDNumber, "Firefighter", 2).Reason, cell.DisabledReason);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        public void A_number_outside_zero_to_four_is_refused(int priority)
        {
            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 8, "Town"), "A");

            Assert.Equal(GodCommandOutcome.Refused, GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", priority).Outcome);
        }

        [Fact]
        public void An_unknown_work_type_or_citizen_is_refused_and_nothing_before_a_world()
        {
            Find.World = null;
            Assert.Equal(GodCommandOutcome.Refused, GodCommands.SetWorkPriority(1, "Mining", 1).Outcome);
            Assert.Null(GodViewSnapshot.WorkPrioritiesOf(1));

            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 9, "Town"), "A");
            GodCommandResult unknown = GodCommands.SetWorkPriority(p.thingIDNumber, "NoSuchWork", 1);
            Assert.Equal(GodCommandOutcome.Refused, unknown.Outcome);
            Assert.Contains("NoSuchWork", unknown.Reason);
            Assert.Equal(GodCommandOutcome.Refused, GodCommands.SetWorkPriority(p.thingIDNumber + 999, "Mining", 1).Outcome);
        }

        // ---- the read side ----

        [Fact]
        public void The_table_lists_every_work_type_in_natural_priority_order_and_reads_the_set_value()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 10, "Town");
            Pawn p = Citizen(settlement, "A");
            GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 2);

            WorkPriorityTable table = GodViewSnapshot.WorkPrioritiesOf(settlement.tile)!;

            Assert.Equal(DefDatabase<WorkTypeDef>.AllDefsListForReading.Count, table.WorkTypes.Count);
            Assert.Equal(table.WorkTypes.Select(w => w.NaturalPriority).OrderByDescending(n => n), table.WorkTypes.Select(w => w.NaturalPriority));
            WorkPriorityRow row = table.Rows.Single();
            Assert.True(row.ManualPriorities);
            Assert.Equal(table.WorkTypes.Select(w => w.DefName), row.Cells.Select(c => c.WorkTypeDefName));
            Assert.Equal(2, row.Cells.Single(c => c.WorkTypeDefName == "Mining").Priority);
        }

        // ---- save ----

        [Fact]
        public void A_priority_set_through_the_command_round_trips_through_Scribe()
        {
            CoreWorld world = BareWorld();
            Pawn p = Citizen(NewSettlement(world, 11, "Town"), "A");
            GodCommands.SetWorkPriority(p.thingIDNumber, "Mining", 1);

            string xml = Scribe.SaveToString(p, "pawn");
            Pawn.ResetThingIdCounter();
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(1, loaded.workSettings.GetPriority(Work("Mining")));
            Assert.True(loaded.workSettings.useWorkPriorities);
        }
    }
}
