using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
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
    /// Decided: an explicit 0 the player set for one citizen through <see cref="GodCommands.SetWorkPriority"/>
    /// beats an edict for that citizen; a 0 that is only the default state still yields to the edict. And an
    /// edict nobody may carry out says so in its own reason rather than stalling silently.
    /// </summary>
    public class EdictVersusPriorityTests : ContentTestBase
    {
        public EdictVersusPriorityTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static EdictDef MiningEdict() => new EdictDef
        {
            defName = "TestMiningOnlyEdict",
            prioritizedWork = new List<WorkTypeDef> { WorkTypeDefOf.Mining },
        };

        /// <summary>A citizen with a wall to build (routine work's first choice) and a rock to mine (the
        /// edict's work) — the fixture <c>GodTests</c> uses for edicts.</summary>
        private static Pawn CitizenWithWallAndRock()
        {
            var world = new CoreWorld();
            Find.World = world;
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 1, null, "EdictTown", 0);
            world.worldObjects.Add(settlement);

            var map = new CoreMap(10, 10, SimWorld.Map.TerrainDefOf.Soil);
            Pawn pawn = NewHuman("Worker");
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 0), map);
            settlement.AddCitizen(pawn);

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), new IntVec3(5, 0, 5), map);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 5;
            GenSpawn.Spawn(wood, new IntVec3(1, 0, 1), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), new IntVec3(2, 0, 0), map);
            map.designationManager.AddDesignation(new global::SimWorld.Designations.Designation(new global::SimWorld.AI.LocalTargetInfo(new IntVec3(2, 0, 0)), global::SimWorld.Designations.DesignationDefOf.Mine));
            return pawn;
        }

        private static JobDef? NextJob(Pawn pawn) => ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).Job?.def;

        [Fact]
        public void A_player_set_never_beats_the_edict_for_that_citizen()
        {
            Pawn pawn = CitizenWithWallAndRock();
            Find.God.Activate(MiningEdict());
            Assert.Equal(JobDefOf.Mine, NextJob(pawn));

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetWorkPriority(pawn.thingIDNumber, "Mining", 0).Outcome);

            Assert.NotEqual(JobDefOf.Mine, NextJob(pawn));
        }

        [Fact]
        public void A_default_zero_still_yields_to_the_edict()
        {
            Pawn pawn = CitizenWithWallAndRock();
            pawn.workSettings.Disable(WorkTypeDefOf.Mining);
            Assert.Equal(0, pawn.workSettings.GetPriority(WorkTypeDefOf.Mining));
            Assert.False(pawn.workSettings.IsExplicitlyNever(WorkTypeDefOf.Mining));

            Find.God.Activate(MiningEdict());

            Assert.Equal(JobDefOf.Mine, NextJob(pawn));
        }

        [Fact]
        public void An_edict_nobody_may_carry_out_says_so_in_its_reason_and_on_issue()
        {
            Pawn pawn = CitizenWithWallAndRock();
            EdictDef edict = DefDatabase<EdictDef>.AllDefsListForReading
                .First(e => e.prioritizedWork.Count == 1 && GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == e.defName).Availability == EdictAvailability.Available);
            WorkTypeDef work = edict.prioritizedWork[0];

            GodCommands.SetWorkPriority(pawn.thingIDNumber, work.defName, 0);
            GodCommandResult issued = GodCommands.IssueEdict(edict.defName);

            Assert.Equal(GodCommandOutcome.Done, issued.Outcome);
            string stalled = "nobody is allowed to do " + (work.gerundLabel ?? work.label);
            Assert.Contains(stalled, issued.Reason);
            Assert.Contains(stalled, GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == edict.defName).Reason);

            GodCommands.SetWorkPriority(pawn.thingIDNumber, work.defName, 3);
            Assert.Equal("In force.", GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == edict.defName).Reason);
        }
    }
}
