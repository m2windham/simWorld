using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Emergency work outranks sleep, hunger and recreation, and a sleeping doctor gets up for it — RimWorld's
    /// shape (the colonist block's "starving? eat" then <c>JobGiver_Work</c> with <c>emergency=true</c>, above
    /// the needs sorter; <c>Toils_LayDown</c> re-asking every 211 ticks), measured here because the order had
    /// to be read from a structural dump of RimWorld's tree rather than the shipped XML.
    /// <para/>
    /// Why it matters: on the populated storyteller world a raid's survivors went to bed beside the people
    /// bleeding out next to them, because every tier that sent them there sat above the one work node, and a
    /// sleeping pawn was never asked again until rested. "Urgent" is RimWorld's eighteen-hour rule
    /// (<see cref="HealthAIUtility.ShouldBeTendedNowUrgent"/>), and the tests below hold it to that: a slow
    /// bleed gets nobody out of bed.
    /// </summary>
    public class EmergencyWorkTests : ContentTestBase
    {
        public EmergencyWorkTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction faction, string name)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void Cut(Pawn p, string part, float amount)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, amount, hitPart: Part(p, part)), p);
        }

        /// <summary>A raid casualty: down, and bleeding fast enough to die inside RimWorld's eighteen hours.
        /// Cuts are spread over the limbs until that holds, so no one part is destroyed on the way.</summary>
        private static void BleedingToDeath(Pawn p)
        {
            p.health.ForceDowned = true;
            string[] parts = { "left arm", "right arm", "left leg", "right leg", "torso" };
            for (int i = 0; i < 20 && HealthAIUtility.TicksUntilDeathDueToBloodLoss(p) >= HealthAIUtility.UrgentTicksUntilDeathDueToBloodLoss; i++)
            {
                Cut(p, parts[i % parts.Length], 6f);
            }
            Assert.True(HealthAIUtility.TicksUntilDeathDueToBloodLoss(p) < HealthAIUtility.UrgentTicksUntilDeathDueToBloodLoss,
                "The casualty was supposed to be bleeding to death inside eighteen hours.");
            Assert.False(p.Dead);
        }

        /// <summary>One cut on an arm: bleeding, tendable, and days from killing anybody.</summary>
        private static void SlowBleed(Pawn p)
        {
            p.health.ForceDowned = true;
            Cut(p, "left arm", 6f);
            Assert.True(p.health.hediffSet.BleedRateTotal > 0f);
            Assert.True(HealthAIUtility.TicksUntilDeathDueToBloodLoss(p) >= HealthAIUtility.UrgentTicksUntilDeathDueToBloodLoss);
        }

        private static void Tire(Pawn p) => p.needs.rest!.CurLevel = 0.1f;

        /// <summary>Asleep the way the game puts a pawn to sleep: tired enough that the tree issues LayDown.</summary>
        private static void PutToSleep(Pawn p)
        {
            Tire(p);
            RunTicks(5, p);
            Assert.Equal(JobDefOf.LayDown, p.jobs.curJob?.def);
            Assert.True(p.Asleep, "The doctor was supposed to be asleep before the test starts.");
        }

        private static bool Tending(Pawn doctor, Pawn patient) =>
            doctor.jobs.curJob?.def == JobDefOf.TendPatient && ReferenceEquals(doctor.jobs.curJob.GetTarget(TargetIndex.A).Thing, patient);

        // ---- where the tier sits ----

        [Fact]
        public void Emergency_work_sits_above_every_need_but_starvation_and_routine_work_below_them()
        {
            var root = (ThinkNode_Priority)ThinkTreeDefOf.Humanlike.thinkRoot;
            List<ThinkNode> nodes = root.subNodes;
            int fight = nodes.FindIndex(n => n is JobGiver_AIFightEnemies);
            int duty = nodes.FindIndex(n => n is ThinkNode_Duty);
            int starving = nodes.FindIndex(n => n is ThinkNode_ConditionalStarving);
            int emergency = nodes.FindIndex(n => n is JobGiver_Work w && w.emergency);
            int hungry = nodes.FindIndex(n => n is ThinkNode_ConditionalHungry);
            int tired = nodes.FindIndex(n => n is ThinkNode_ConditionalTired);
            int joy = nodes.FindIndex(n => n is ThinkNode_ConditionalLowJoy);
            int routine = nodes.FindIndex(n => n is JobGiver_Work w && !w.emergency);

            Assert.True(new[] { fight, duty, starving, emergency, hungry, tired, joy, routine }.All(i => i >= 0));
            Assert.Equal(1, nodes.Count(n => n is JobGiver_Work w && w.emergency));
            Assert.Equal(1, nodes.Count(n => n is JobGiver_Work w && !w.emergency));
            Assert.True(fight < emergency && duty < emergency, "this port's own fight and duty tiers stay above everything they were above");
            Assert.True(starving < emergency, "a starving doctor eats first — the one need RimWorld lets outrank emergency work");
            Assert.True(emergency < hungry && emergency < tired && emergency < joy, "emergency work outranks hunger, sleep and recreation");
            Assert.True(joy < routine && tired < routine && hungry < routine, "routine work still waits for every need");
            Assert.IsType<JobGiver_GetFood>(((ThinkNode_ConditionalStarving)nodes[starving]).subNodes.Single());
        }

        // ---- what counts as urgent ----

        [Fact]
        public void Urgent_is_bleeding_to_death_inside_eighteen_hours_and_a_slow_bleed_turns_urgent_as_blood_runs_out()
        {
            CoreMap map = NewMap(8);
            Faction f = NewFaction("Colony");
            Pawn fast = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Fast");
            Pawn slow = SpawnHuman(map, new IntVec3(3, 0, 3), f, "Slow");
            Pawn bruised = SpawnHuman(map, new IntVec3(5, 0, 5), f, "Bruised");
            BleedingToDeath(fast);
            SlowBleed(slow);
            DamageDef blunt = DefDatabase<DamageDef>.GetNamed("Blunt");
            blunt.Worker.Apply(new DamageInfo(blunt, 3f, hitPart: Part(bruised, "left foot")), bruised);

            Assert.True(TendUtility.NeedsEmergencyTend(fast));
            Assert.False(TendUtility.NeedsEmergencyTend(slow));
            Assert.False(TendUtility.NeedsEmergencyTend(bruised));
            Assert.Equal(int.MaxValue, HealthAIUtility.TicksUntilDeathDueToBloodLoss(bruised));

            // Same wound, less blood left to lose: time to death shrinks and the patient crosses the line.
            int before = HealthAIUtility.TicksUntilDeathDueToBloodLoss(slow);
            HealthUtility.AdjustSeverity(slow, HediffDefOf.BloodLoss, 0.9f);
            int after = HealthAIUtility.TicksUntilDeathDueToBloodLoss(slow);
            Assert.True(after < before);
            Assert.True(TendUtility.NeedsEmergencyTend(slow), "a slow bleed with almost no blood left is urgent");
        }

        // ---- awake: emergency work before needs ----

        [Fact]
        public void A_tired_doctor_tends_a_colonist_bleeding_to_death_before_going_to_bed_but_not_a_slow_bleed()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(9, 0, 9), f, "Casualty");
            Tire(doctor);

            SlowBleed(casualty);
            doctor.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.LayDown, doctor.jobs.curJob?.def);

            doctor.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            BleedingToDeath(casualty);
            doctor.jobs.TryFindAndStartJob();
            Assert.True(Tending(doctor, casualty), "a tired doctor went to bed beside a colonist bleeding to death");
        }

        [Fact]
        public void A_hungry_doctor_tends_first_and_a_starving_one_eats_first()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(9, 0, 9), f, "Casualty");
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawPotatoes"));
            food.stackCount = 20;
            GenSpawn.Spawn(food, new IntVec3(2, 0, 1), map);
            BleedingToDeath(casualty);

            var need = doctor.needs.food!;
            need.CurLevelPercentage = need.PercentageThreshHungry * 0.75f;
            Assert.Equal(SimWorld.Needs.HungerCategory.Hungry, need.CurCategory);
            doctor.jobs.TryFindAndStartJob();
            Assert.True(Tending(doctor, casualty), "a hungry doctor ate while a colonist bled to death");

            doctor.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            need.CurLevel = 0f;
            Assert.True(need.Starving);
            doctor.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.Ingest, doctor.jobs.curJob?.def);
        }

        // ---- asleep: the doctor gets up ----

        [Fact]
        public void A_sleeping_doctor_gets_up_and_tends_a_colonist_bleeding_to_death()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(6, 0, 6), f, "Casualty");
            PutToSleep(doctor);

            BleedingToDeath(casualty);
            int woke = -1;
            for (int t = 0; t < 2 * JobDriver_LayDown.LookForOtherJobsIntervalTicks && woke < 0; t++)
            {
                RunTicks(1, doctor, casualty);
                if (!doctor.Asleep) woke = t;
            }

            Assert.True(woke >= 0, "the doctor slept on beside a colonist bleeding to death");
            Assert.True(woke <= JobDriver_LayDown.LookForOtherJobsIntervalTicks, "woken within one look-up interval, not by chance later");
            Assert.True(Tending(doctor, casualty));
            Assert.True(doctor.needs.rest!.CurLevel < 0.5f, "got up tired: this was the emergency, not the end of a night's sleep");

            RunTicks(2000, doctor, casualty);
            Assert.Equal(0f, casualty.health.hediffSet.BleedRateTotal);
        }

        [Fact]
        public void A_sleeping_doctor_sleeps_through_a_slow_bleed()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(6, 0, 6), f, "Casualty");
            PutToSleep(doctor);

            SlowBleed(casualty);
            RunTicks(4 * JobDriver_LayDown.LookForOtherJobsIntervalTicks, doctor, casualty);

            Assert.True(doctor.Asleep, "a scratch got the doctor out of bed");
            Assert.Equal(JobDefOf.LayDown, doctor.jobs.curJob?.def);
            Assert.True(casualty.health.hediffSet.BleedRateTotal > 0f, "routine tending waits for the doctor to wake");
        }

        [Fact]
        public void Once_the_emergency_is_tended_the_doctor_goes_back_to_bed()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(6, 0, 6), f, "Casualty");
            PutToSleep(doctor);
            BleedingToDeath(casualty);

            RunTicks(3000, doctor, casualty);

            Assert.Equal(0f, casualty.health.hediffSet.BleedRateTotal);
            Assert.Equal(JobDefOf.LayDown, doctor.jobs.curJob?.def);
            Assert.True(doctor.Asleep);
        }

        // ---- asleep: firefighting is bounded by the home area ----

        /// <summary>
        /// The concern this test exists to close off: <c>FightFires</c> carries <c>emergency=true</c>, so
        /// once it can target a fire at all, a sleeping citizen wakes for it via the same
        /// <c>JobGiver_Work.TryGiveEmergencyJob</c> path <see cref="A_sleeping_doctor_gets_up_and_tends_a_colonist_bleeding_to_death"/>
        /// exercises for tending. Unbounded, that is a grass fire on the far side of the map getting the whole
        /// settlement out of bed. <see cref="AI.WorkGiver_FightFires"/>'s own home-area gate is what keeps this
        /// one asleep and the other one not.
        /// </summary>
        [Fact]
        public void A_sleeping_citizen_does_not_wake_for_a_fire_outside_the_home_area_but_wakes_for_one_inside()
        {
            CoreMap map = NewMap(20);
            Faction f = NewFaction("Colony");
            Pawn sleeper = SpawnHuman(map, new IntVec3(2, 0, 2), f, "Sleeper");
            foreach (IntVec3 c in new CellRect(0, 0, 5, 5).ClipInsideMap(map).Cells) map.areaManager.Home[c] = true;
            PutToSleep(sleeper);

            var farCell = new IntVec3(18, 0, 18);
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), farCell, map);
            Assert.True(SimWorld.Things.FireUtility.TryStartFireIn(farCell, map, 1f));

            RunTicks(2 * JobDriver_LayDown.LookForOtherJobsIntervalTicks, sleeper);
            Assert.True(sleeper.Asleep, "a fire outside the painted home area should not have woken the sleeper");
            Assert.Equal(JobDefOf.LayDown, sleeper.jobs.curJob?.def);

            var nearCell = new IntVec3(3, 0, 3);
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), nearCell, map);
            Assert.True(SimWorld.Things.FireUtility.TryStartFireIn(nearCell, map, 1f));

            int woke = -1;
            for (int t = 0; t < 2 * JobDriver_LayDown.LookForOtherJobsIntervalTicks && woke < 0; t++)
            {
                RunTicks(1, sleeper);
                if (!sleeper.Asleep) woke = t;
            }
            Assert.True(woke >= 0, "a fire inside the painted home area should have woken the sleeper");
            Assert.Equal(FireJobDefOf.BeatFire, sleeper.jobs.curJob?.def);
        }

        // ---- saved and loaded ----

        [Fact]
        public void A_doctor_saved_asleep_still_gets_up_for_an_emergency_after_loading()
        {
            CoreMap map = NewMap(12);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(1, 0, 1), f, "Doctor");
            Pawn casualty = SpawnHuman(map, new IntVec3(6, 0, 6), f, "Casualty");
            PutToSleep(doctor);

            // A map saved on its own cannot resolve a reference to a faction that lives in the FactionManager,
            // so the two go into the save factionless and are given the faction back after loading — what is
            // under test is the sleeper, not the faction reference.
            doctor.faction = null;
            casualty.faction = null;
            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedDoctor = loaded.mapPawns.AllPawns.Single(p => p.Label == "Doctor");
            Pawn loadedCasualty = loaded.mapPawns.AllPawns.Single(p => p.Label == "Casualty");
            loadedDoctor.faction = f;
            loadedCasualty.faction = f;
            Assert.Equal(JobDefOf.LayDown, loadedDoctor.jobs.curJob?.def);

            RunTicks(5, loadedDoctor, loadedCasualty);
            Assert.True(loadedDoctor.Asleep, "the loaded doctor was supposed to still be asleep");

            BleedingToDeath(loadedCasualty);
            RunTicks(3000, loadedDoctor, loadedCasualty);
            Assert.Equal(0f, loadedCasualty.health.hediffSet.BleedRateTotal);
        }
    }
}
