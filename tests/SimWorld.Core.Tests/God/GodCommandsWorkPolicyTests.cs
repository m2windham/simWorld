using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.God;
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
    /// The population-wide half of the work lever: <see cref="GodCommands.ApplyRoleToSettlement"/> and
    /// <see cref="GodCommands.ApplyRoleToCivilization"/>, wiring up
    /// <see cref="WorkPolicyUtility.ApplyRoleToPopulation"/> — "built, tested, and never called from
    /// <c>src/</c>" per <c>docs/design/player-first.md</c> §10. See <c>GodCommands.WorkPolicy.cs</c> for what
    /// this refuses and why roles rather than a per-pawn grid.
    /// </summary>
    public class GodCommandsWorkPolicyTests : ContentTestBase
    {
        public GodCommandsWorkPolicyTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            Thing rock = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

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

        private static RoleDef Role(string defName) => DefDatabase<RoleDef>.GetNamed(defName);

        // ---- refusals, one per reason ----

        [Fact]
        public void Refuses_an_unknown_settlement_tile()
        {
            BareWorld();

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(999, "Miner");

            Assert.Equal(GodCommandOutcome.UnknownSettlement, result.Outcome);
        }

        [Fact]
        public void Refuses_an_unknown_role_name_on_a_settlement()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 1, "RefusalTown");

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "NoSuchRoleAtAll");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("NoSuchRoleAtAll", result.Reason);
        }

        [Fact]
        public void Refuses_an_unknown_role_name_across_the_civilization()
        {
            BareWorld();

            GodCommandResult result = GodCommands.ApplyRoleToCivilization("NoSuchRoleAtAll");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("NoSuchRoleAtAll", result.Reason);
        }

        [Fact]
        public void Refuses_a_civilization_wide_application_with_no_game_running()
        {
            Find.World = null;

            GodCommandResult result = GodCommands.ApplyRoleToCivilization("Miner");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("No game is running", result.Reason);
        }

        // ---- the happy path ----

        [Fact]
        public void Applies_a_role_to_every_working_citizen_of_a_settlement()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 2, "HappyTown");
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            settlement.AddCitizen(a);
            settlement.AddCitizen(b);

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Equal("Miner", a.workSettings.Role?.defName);
            Assert.Equal("Miner", b.workSettings.Role?.defName);
        }

        [Fact]
        public void Clearing_a_role_with_null_is_a_legitimate_standing_choice_not_a_refusal()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 3, "ClearTown");
            Pawn a = NewHuman("A");
            settlement.AddCitizen(a);
            a.workSettings.SetRole(Role("Miner"));
            Assert.NotNull(a.workSettings.Role);

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, null);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Null(a.workSettings.Role);
        }

        [Fact]
        public void Applies_a_role_across_every_settlement_in_the_civilization_at_once()
        {
            CoreWorld world = BareWorld();
            Settlement north = NewSettlement(world, 4, "North");
            Settlement south = NewSettlement(world, 5, "South");
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Pawn c = NewHuman("C");
            north.AddCitizen(a);
            north.AddCitizen(b);
            south.AddCitizen(c);

            GodCommandResult result = GodCommands.ApplyRoleToCivilization("Farmer");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal("Farmer", a.workSettings.Role?.defName);
            Assert.Equal("Farmer", b.workSettings.Role?.defName);
            Assert.Equal("Farmer", c.workSettings.Role?.defName);
        }

        // ---- what it never overwrites ----

        [Fact]
        public void Applying_a_role_never_overwrites_a_citizens_own_manually_set_priority()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 6, "ManualTown");
            Pawn a = NewHuman("A");
            settlement.AddCitizen(a);

            // The citizen's own explicit choice, made before any standing role touches them.
            a.workSettings.SetPriority(WorkTypeDefOf.Mining, 4);

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner"); // emphasizes Mining

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal("Miner", a.workSettings.Role?.defName);
            // Miner would otherwise push Mining to EmphasizedPriority (1); the citizen's own 4 stands.
            Assert.Equal(4, a.workSettings.GetPriority(WorkTypeDefOf.Mining));
        }

        // ---- the real tick loop: an applied role actually changes what a citizen does ----

        [Fact]
        public void Applying_a_role_makes_a_citizen_actually_do_that_work_ahead_of_its_own_default_top_priority()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 7, "WorkTown");
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            settlement.AddCitizen(pawn);

            // A Construction candidate (naturalPriority 800) and a Mining candidate (naturalPriority 700) are
            // both available; absent intervention routine work's own priority order always picks Construction
            // first (the same fixture GodTests.Idle_pawn_under_an_edict_does_the_edicts_work... uses).
            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, new IntVec3(5, 0, 5), map);
            SpawnStack(map, new IntVec3(1, 0, 1), "WoodLog", 5);
            Thing rock = SpawnRock(map, new IntVec3(2, 0, 0));

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner");
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);

            RunTicks(2500, pawn);

            Assert.True(rock.Destroyed,
                "A citizen under the Miner role should have mined the reachable rock ahead of construction.");
        }

        // ---- wasteful, but allowed (docs/design/player-first.md §5) ----

        [Fact]
        public void Applying_a_role_nobody_can_usefully_act_on_yet_is_allowed()
        {
            // Scholar emphasizes Research, and this settlement has no research bench at all — the role still
            // applies; whether it does the civilization any good right now is the player's call, not this
            // command's to second-guess.
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 8, "NoBenchTown");
            Pawn a = NewHuman("A");
            settlement.AddCitizen(a);

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "Scholar");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal("Scholar", a.workSettings.Role?.defName);
        }

        [Fact]
        public void Assigning_the_same_role_to_an_entire_settlement_leaving_no_one_for_default_work_is_allowed()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 9, "MonocultureTown");
            var citizens = new List<Pawn>();
            for (int i = 0; i < 5; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                settlement.AddCitizen(p);
                citizens.Add(p);
            }

            GodCommandResult result = GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.All(citizens, p => Assert.Equal("Miner", p.workSettings.Role?.defName));
        }

        // ---- Scribe round trip ----

        [Fact]
        public void A_role_applied_through_the_command_round_trips_through_Scribe()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 10, "SaveTown");
            Pawn a = NewHuman("A");
            settlement.AddCitizen(a);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToSettlement(settlement.tile, "Miner").Outcome);

            string xml = Scribe.SaveToString(a, "pawn");
            Pawn.ResetThingIdCounter();
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal("Miner", loaded.workSettings.Role?.defName);
        }
    }
}
