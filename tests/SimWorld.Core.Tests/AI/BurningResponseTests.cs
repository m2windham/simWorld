using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// What a pawn does about being on fire (task #111; RimWorld's BurningResponse think tree, ported from the
    /// 1.0 decompile in <c>AI/BurningResponse.cs</c>). On the populated storyteller world one Flashstorm killed
    /// a whole settlement: a citizen who caught fire had no response of their own and kept working until they
    /// burned to death. These pin the tier, each of its three givers, the two interrupt rules that keep a
    /// burning pawn's response from being thrown away mid-roll, and the firefighting giver's refusal of the
    /// pawn's own fire. Tuned numbers are asserted as bands or orderings, per CLAUDE.md.
    /// </summary>
    public class BurningResponseTests : ContentTestBase
    {
        public BurningResponseTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size = 24) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Citizen", Faction? faction = null, string? weapon = null)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon)));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn BurningHuman(CoreMap map, IntVec3 cell, string name = "Torch")
        {
            Pawn p = SpawnHuman(map, cell, name);
            Assert.True(p.TryAttachFire(Fire.MinFireSize));
            return p;
        }

        private static Faction NewFaction(string name, string defName = "TribalCivilization")
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static readonly HashSet<string> BurningTierJobs = new HashSet<string> { "ExtinguishSelf", "Wait_Wander", "GotoWander_Run", "Goto_OnCell" };

        private static bool IsBurningTierJob(Job? job) => job != null && BurningTierJobs.Contains(job.def.defName);

        private static bool IsBurningTierNode(ThinkNode? node) =>
            node is JobGiver_JumpInWater || node is JobGiver_ExtinguishSelf || node is JobGiver_RunRandom;

        // ---- the tier, and where it sits ----

        [Fact]
        public void The_burning_tier_opens_the_humanlike_tree_with_RimWorlds_three_givers_in_order()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(BurningJobDefOf.ExtinguishSelf);
            Assert.NotNull(BurningJobDefOf.Wait_Wander);
            Assert.NotNull(BurningJobDefOf.GotoWander_Run);
            Assert.NotNull(BurningJobDefOf.Goto_OnCell);

            var root = (ThinkNode_Priority)ThinkTreeDefOf.Humanlike.thinkRoot;
            var burning = Assert.IsType<ThinkNode_ConditionalBurning>(root.subNodes[0]);
            Assert.Equal(3, burning.subNodes.Count);
            Assert.IsType<JobGiver_JumpInWater>(burning.subNodes[0]);
            Assert.IsType<JobGiver_ExtinguishSelf>(burning.subNodes[1]);
            Assert.IsType<JobGiver_RunRandom>(burning.subNodes[2]);
        }

        [Fact]
        public void A_burning_citizen_answers_from_the_burning_tier_before_a_mental_state_starvation_or_a_fire_to_fight()
        {
            CoreMap map = NewMap();
            Pawn pawn = SpawnHuman(map, new IntVec3(6, 0, 6));

            // Everything that would otherwise win: a mental state (the old first tier), starvation with food in
            // reach, and a fire to fight two cells away (emergency work).
            pawn.needs.food!.CurLevel = 0f;
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("RawPotatoes")), new IntVec3(8, 0, 6), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(6, 0, 9), map);
            Assert.True(FireUtility.TryStartFireIn(new IntVec3(6, 0, 9), map, 1f));
            Assert.True(pawn.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("Wander_Sad"), forced: true));

            ThinkResult cold = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn);
            Assert.True(cold.IsValid);
            Assert.False(IsBurningTierNode(cold.SourceNode), "test setup: a pawn who is not alight must answer from some other tier");

            Assert.True(pawn.TryAttachFire(Fire.MinFireSize));
            for (int i = 0; i < 20; i++)
            {
                ThinkResult hot = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn);
                Assert.True(IsBurningTierNode(hot.SourceNode), "A burning pawn answered from " + hot.SourceNode?.GetType().Name + ".");
                Assert.True(IsBurningTierJob(hot.Job));
            }
        }

        [Fact]
        public void Catching_fire_mid_job_ends_the_job_and_the_next_think_is_the_burning_tier()
        {
            CoreMap map = NewMap();
            Pawn pawn = SpawnHuman(map, new IntVec3(6, 0, 6));
            RunTicks(3, pawn);
            Job? before = pawn.jobs.curJob;
            Assert.NotNull(before);
            Assert.False(IsBurningTierJob(before));

            pawn.TryAttachFire(Fire.MinFireSize);
            Assert.Null(pawn.jobs.curJob);

            RunTicks(1, pawn);
            Assert.True(IsBurningTierJob(pawn.jobs.curJob), "The pawn went back to " + pawn.jobs.curJob?.def.defName + " alight.");
        }

        // ---- ExtinguishSelf ----

        [Fact]
        public void The_extinguish_roll_comes_up_about_one_think_in_ten_and_never_for_a_pawn_not_alight()
        {
            CoreMap map = NewMap();
            Pawn pawn = BurningHuman(map, new IntVec3(6, 0, 6));
            Pawn cold = SpawnHuman(map, new IntVec3(12, 0, 12), "Cold");
            var giver = new JobGiver_ExtinguishSelf();

            const int Thinks = 4000;
            int rolled = 0;
            for (int i = 0; i < Thinks; i++)
            {
                ThinkResult result = giver.TryIssueJobPackage(pawn);
                if (!result.IsValid) continue;
                rolled++;
                Assert.Equal(BurningJobDefOf.ExtinguishSelf, result.Job!.def);
                Assert.Same(pawn.GetAttachedFire(), result.Job.targetA.Thing);
            }
            float rate = rolled / (float)Thinks;
            Assert.InRange(rate, 0.08f, 0.12f);

            for (int i = 0; i < 200; i++) Assert.False(giver.TryIssueJobPackage(cold).IsValid);
        }

        [Fact]
        public void ExtinguishSelf_stands_still_for_its_full_duration_through_the_fires_own_damage_and_then_puts_it_out()
        {
            CoreMap map = NewMap();
            Pawn pawn = BurningHuman(map, new IntVec3(6, 0, 6));
            Fire fire = pawn.GetAttachedFire()!;
            IntVec3 at = pawn.Position;
            float healthBefore = pawn.health.summaryHealth.SummaryHealthPercent;

            pawn.jobs.StartJob(new Job(BurningJobDefOf.ExtinguishSelf, fire));
            Job job = pawn.jobs.curJob!;

            for (int t = 0; t < JobDriver_ExtinguishSelf.ExtinguishTicks - 1; t++)
            {
                RunTicks(1, pawn);
                Assert.Same(job, pawn.jobs.curJob);
                Assert.Equal(at, pawn.Position);
                Assert.True(pawn.IsBurning());
            }

            RunTicks(3, pawn);
            Assert.True(fire.Destroyed, "ExtinguishSelf ran its course and the fire was still burning.");
            Assert.False(pawn.IsBurning());

            // A roll as long as a fire's whole pass interval cannot dodge it: the pawn was burned while rolling,
            // and the job above survived that hit unchanged.
            Assert.True(pawn.health.summaryHealth.SummaryHealthPercent < healthBefore, "test setup: the fire should have hurt the pawn mid-roll");
        }

        // ---- RunRandom ----

        [Fact]
        public void RunRandom_alternates_a_short_wait_and_a_run_leg_and_a_pawn_mid_leg_gets_a_fresh_leg()
        {
            CoreMap map = NewMap(30);
            Pawn pawn = BurningHuman(map, new IntVec3(15, 0, 15));
            var giver = new JobGiver_RunRandom();
            Assert.True(pawn.mindState.nextMoveOrderIsWait, "RimWorld's default: a pawn's first wander-style order is a pause");

            for (int round = 0; round < 6; round++)
            {
                Job wait = giver.TryIssueJobPackage(pawn).Job!;
                Assert.Equal(BurningJobDefOf.Wait_Wander, wait.def);
                Assert.InRange(wait.expiryInterval, JobGiver_RunRandom.TicksBetweenWandersRange.min, JobGiver_RunRandom.TicksBetweenWandersRange.max);

                Job leg = giver.TryIssueJobPackage(pawn).Job!;
                Assert.Equal(BurningJobDefOf.GotoWander_Run, leg.def);
                IntVec3 dest = leg.targetA.Cell;
                Assert.True((dest - pawn.Position).LengthHorizontalSquared <= JobGiver_RunRandom.WanderRadius * JobGiver_RunRandom.WanderRadius);
                Assert.NotEqual(pawn.Position, dest);
                Assert.True(GenGrid.Standable(dest, map));
            }

            // Asked again while already on a leg (a damage-driven re-think, say): a fresh leg, never a pause,
            // and the alternation is not advanced by it.
            pawn.jobs.StartJob(new Job(BurningJobDefOf.GotoWander_Run, new IntVec3(20, 0, 15)));
            bool flagBefore = pawn.mindState.nextMoveOrderIsWait;
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(BurningJobDefOf.GotoWander_Run, giver.TryIssueJobPackage(pawn).Job!.def);
                Assert.Equal(flagBefore, pawn.mindState.nextMoveOrderIsWait);
            }
        }

        [Fact]
        public void A_burning_pawn_with_room_to_run_pauses_runs_and_gets_somewhere_else()
        {
            CoreMap map = NewMap(30);
            var start = new IntVec3(15, 0, 15);
            Pawn pawn = BurningHuman(map, start);

            var seen = new HashSet<string>();
            int maxDistSq = 0;
            for (int t = 0; t < 300 && pawn.IsBurning(); t++)
            {
                RunTicks(1, pawn);
                if (pawn.jobs.curJob != null) seen.Add(pawn.jobs.curJob.def.defName);
                maxDistSq = System.Math.Max(maxDistSq, (pawn.Position - start).LengthHorizontalSquared);
            }

            Assert.Contains("Wait_Wander", seen);
            Assert.Contains("GotoWander_Run", seen);
            Assert.True(maxDistSq > 0, "A burning pawn with open ground around it never moved.");
            Assert.All(seen, d => Assert.Contains(d, BurningTierJobs));
        }

        // ---- interrupts ----

        [Fact]
        public void Flame_damage_does_not_make_a_burning_pawn_re_decide_its_run_but_a_cut_does()
        {
            Assert.False(FireDamageDefOf.Flame.canInterruptJobs);
            Assert.True(DamageDefOf.Cut.canInterruptJobs);

            CoreMap map = NewMap(30);
            Pawn pawn = BurningHuman(map, new IntVec3(10, 0, 10));
            Fire fire = pawn.GetAttachedFire()!;

            // A leg toward a cell further than RunRandom's radius, so any re-decided leg is a different one.
            var far = new IntVec3(19, 0, 10);
            pawn.jobs.StartJob(new Job(BurningJobDefOf.GotoWander_Run, far));
            Job leg = pawn.jobs.curJob!;

            FireDamageDefOf.Flame.Worker.Apply(new DamageInfo(FireDamageDefOf.Flame, 2f, instigator: fire), pawn);
            Assert.Same(leg, pawn.jobs.curJob);

            // The same pawn, the same leg, hit by something that can interrupt: it re-asks the tree, and the
            // tree's answer (still the burning tier) is a different job.
            DamageDefOf.Cut.Worker.Apply(new DamageInfo(DamageDefOf.Cut, 1f), pawn);
            Assert.NotSame(leg, pawn.jobs.curJob);
            Assert.True(IsBurningTierJob(pawn.jobs.curJob));
        }

        [Fact]
        public void The_constant_tree_leaves_a_burning_pawn_alone_and_ExtinguishSelf_refuses_a_casual_interrupt()
        {
            Assert.False(BurningJobDefOf.ExtinguishSelf.casualInterruptible);

            CoreMap map = NewMap(30);
            Faction ours = NewFaction("Ours", "PlayerCivilization");
            Faction theirs = NewFaction("Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);

            Pawn burning = SpawnHuman(map, new IntVec3(5, 0, 5), "Burning", ours, "Gun_Revolver");
            Pawn rolling = SpawnHuman(map, new IntVec3(5, 0, 9), "Rolling", ours, "Gun_Revolver");
            Pawn calm = SpawnHuman(map, new IntVec3(5, 0, 13), "Calm", ours, "Gun_Revolver");
            SpawnHuman(map, new IntVec3(10, 0, 9), "Raider", theirs, "Gun_Revolver");
            ThinkNode constant = ConstantThinkTreeDefOf.HumanlikeConstant.thinkRoot;

            // test setup: the same armed enemy is a constant-tree interrupt for a pawn who is not alight.
            Assert.True(constant.TryIssueJobPackage(calm).IsValid);

            burning.TryAttachFire(Fire.MinFireSize);
            Assert.False(constant.TryIssueJobPackage(burning).IsValid, "The constant tree offered a burning pawn a fight.");

            // Mid-roll, after the fire was put out by some other hand (rain, a neighbour): not burning any more,
            // and still not to be pulled off the job by something it has merely seen.
            rolling.TryAttachFire(Fire.MinFireSize);
            Fire onRolling = rolling.GetAttachedFire()!;
            rolling.jobs.StartJob(new Job(BurningJobDefOf.ExtinguishSelf, onRolling));
            onRolling.Destroy();
            Assert.False(rolling.IsBurning());
            Assert.False(constant.TryIssueJobPackage(rolling).IsValid, "ExtinguishSelf was casually interruptible.");
        }

        // ---- the firefighting work giver ----

        [Fact]
        public void FightFires_never_takes_the_pawns_own_fire_but_a_neighbour_takes_it_for_them()
        {
            CoreMap map = NewMap();
            Pawn burning = BurningHuman(map, new IntVec3(6, 0, 6));
            Pawn neighbour = SpawnHuman(map, new IntVec3(7, 0, 6), "Neighbour");
            Fire fire = burning.GetAttachedFire()!;

            // No home area at all (every fire is work), and then a home area painted right over them both.
            Assert.Equal(0, map.areaManager.Home.TrueCount);
            Assert.False(WorkGiver_FightFires.IsFireToFight(burning, fire));
            Assert.True(WorkGiver_FightFires.IsFireToFight(neighbour, fire));

            foreach (IntVec3 c in CellRect.CenteredOn(burning.Position, 3).Cells) map.areaManager.Home[c] = true;
            Assert.False(WorkGiver_FightFires.IsFireToFight(burning, fire));
            Assert.True(WorkGiver_FightFires.IsFireToFight(neighbour, fire));
        }

        // ---- water ----

        [Fact]
        public void A_burning_pawn_makes_for_wadeable_water_and_the_water_puts_the_fire_out()
        {
            CoreMap map = NewMap(30);
            foreach (IntVec3 c in CellRect.FromLimits(14, 8, 18, 14).Cells) map.terrainGrid.SetTerrain(c, SimWorld.Map.TerrainDefOf.WaterShallow);
            Pawn pawn = BurningHuman(map, new IntVec3(8, 0, 11));

            Job jump = new JobGiver_JumpInWater().TryIssueJobPackage(pawn).Job!;
            Assert.Equal(BurningJobDefOf.Goto_OnCell, jump.def);
            Assert.True(GenGrid.GetTerrain(jump.targetA.Cell, map).IsWater);

            IntVec3? putOutAt = null;
            for (int t = 0; t < 1200 && putOutAt == null; t++)
            {
                RunTicks(1, pawn);
                if (!pawn.IsBurning()) putOutAt = pawn.Position;
            }
            Assert.NotNull(putOutAt);
            Assert.False(pawn.Dead);
            Assert.True(GenGrid.GetTerrain(putOutAt!.Value, map).IsWater, "The fire went out somewhere other than in the water.");
        }

        [Fact]
        public void Water_nobody_can_stand_in_is_not_somewhere_to_jump()
        {
            CoreMap map = NewMap(30);
            foreach (IntVec3 c in CellRect.FromLimits(14, 8, 18, 14).Cells) map.terrainGrid.SetTerrain(c, SimWorld.Map.TerrainDefOf.WaterDeep);
            Pawn pawn = BurningHuman(map, new IntVec3(10, 0, 11));

            var giver = new JobGiver_JumpInWater();
            for (int i = 0; i < 20; i++) Assert.False(giver.TryIssueJobPackage(pawn).IsValid);
        }

        // ---- knowing you are alight ----

        [Fact]
        public void A_pawn_that_thinks_on_the_tick_it_steps_still_knows_it_is_alight()
        {
            CoreMap map = NewMap();
            Pawn pawn = BurningHuman(map, new IntVec3(6, 0, 6));
            Fire fire = pawn.GetAttachedFire()!;

            // The step, before the fire's own tick has caught it up: the fire is one cell behind.
            pawn.Position = new IntVec3(7, 0, 7);
            Assert.Equal(new IntVec3(6, 0, 6), fire.Position);

            Assert.Same(fire, pawn.GetAttachedFire());
            Assert.True(pawn.IsBurning());
            Assert.True(IsBurningTierNode(ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).SourceNode));
        }

        // ---- the case that killed the settlement ----

        [Fact]
        public void A_citizen_alight_outside_the_home_area_puts_themself_out_and_lives()
        {
            CoreMap map = NewMap(40);
            // Home is somewhere else entirely, so the firefighting giver would never take this fire for anyone.
            foreach (IntVec3 c in CellRect.CenteredOn(new IntVec3(32, 0, 32), 4).Cells) map.areaManager.Home[c] = true;
            Pawn pawn = BurningHuman(map, new IntVec3(10, 0, 10));
            Fire fire = pawn.GetAttachedFire()!;

            bool extinguished = false;
            for (int t = 0; t < 6000 && pawn.IsBurning(); t++)
            {
                RunTicks(1, pawn);
                if (pawn.jobs.curJob?.def == BurningJobDefOf.ExtinguishSelf) extinguished = true;
            }

            Assert.False(pawn.IsBurning(), "A citizen alight outside the home area burned on for 6000 ticks.");
            Assert.True(extinguished, "The fire went out without the citizen ever stopping to put it out.");
            Assert.True(fire.Destroyed);
            Assert.False(pawn.Dead);
        }

        // ---- scribe ----

        [Fact]
        public void A_pawn_mid_roll_round_trips_through_Scribe_with_its_job_its_fire_and_its_wait_or_move_flag()
        {
            CoreMap map = NewMap();
            Pawn pawn = BurningHuman(map, new IntVec3(6, 0, 6), "Smoulder");
            Fire fire = pawn.GetAttachedFire()!;
            pawn.jobs.StartJob(new Job(BurningJobDefOf.ExtinguishSelf, fire));
            pawn.mindState.nextMoveOrderIsWait = false;
            RunTicks(20, pawn);
            Assert.Equal(BurningJobDefOf.ExtinguishSelf, pawn.jobs.curJob?.def);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedPawn = loaded.mapPawns.AllPawns.Single(p => p.name == "Smoulder");
            Fire loadedFire = loadedPawn.GetAttachedFire()!;
            Assert.NotNull(loadedFire);
            Assert.False(loadedPawn.mindState.nextMoveOrderIsWait);
            Assert.Equal(BurningJobDefOf.ExtinguishSelf, loadedPawn.jobs.curJob?.def);
            Assert.Same(loadedFire, loadedPawn.jobs.curJob!.targetA.Thing);

            // And it is a live roll, not a frozen one: it finishes and the fire goes out.
            for (int t = 0; t < 2 * JobDriver_ExtinguishSelf.ExtinguishTicks && loadedPawn.IsBurning(); t++) RunTicks(1, loadedPawn);
            Assert.False(loadedPawn.IsBurning());
            Assert.True(loadedFire.Destroyed);

            // And a pawn that never had the flag written starts on RimWorld's default: a pause first.
            Assert.True(NewHuman("Fresh").mindState.nextMoveOrderIsWait);
        }
    }
}
