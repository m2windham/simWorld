using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// The medicine work givers and job drivers that close this lane's part of the work-economy gap
    /// (`docs/WORK-REGISTER.md`'s "Eighteen work types have no worker"): a colonist finding an injured or
    /// downed pawn through the ordinary work-giver scan and actually walking over to do something about it,
    /// rather than the tending/rescue/feeding mechanics only ever being reachable from a unit test calling
    /// <see cref="TendUtility"/> directly. See <see cref="AI.WorkGiver_Tend"/>, <see cref="AI.WorkGiver_RescueDowned"/>
    /// and <see cref="AI.WorkGiver_FeedPatient"/>'s own docs for the shape and every judgment call; this class
    /// proves it end to end.
    /// </summary>
    public class DoctorAITests : ContentTestBase
    {
        public DoctorAITests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager/Storyteller but ContentTestBase (Sim/**, out of
            // this lane's reach) does not reset it — see WardenAITests' own constructor comment for the same.
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static (Faction captor, Faction hostileOther) HostileFactions()
        {
            Faction captor = NewFaction("Captors");
            Faction other = NewFaction("Raiders");
            captor.SetRelationDirect(other, FactionRelationKind.Hostile, -100);
            return (captor, other);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, string defName = "RawPotatoes")
        {
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        private static Thing SpawnBed(CoreMap map, IntVec3 cell)
        {
            Thing bed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed"));
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        /// <summary>A bleeding, tendable wound (RimWorld's own "Cut" injury bleeds) — the emergency case.</summary>
        private static void MakeBleedingWound(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), 6f, hitPart: Part(p, "left arm")), p);

        /// <summary>An ordinary, non-bleeding tendable wound (a bruise never bleeds — see
        /// Hediffs_Local_Injuries.xml's own comment) — the routine case.</summary>
        private static void MakeOrdinaryWound(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Blunt").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Blunt"), 3f, hitPart: Part(p, "left foot")), p);

        // ---- content ----

        [Fact]
        public void Doctor_JobDefs_load_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(JobDefOf.TendPatient);
            Assert.NotNull(JobDefOf.Rescue);
            Assert.NotNull(JobDefOf.FeedPatient);
        }

        [Fact]
        public void The_five_medicine_WorkGiverDefs_are_wired_as_this_lane_decided()
        {
            Assert.IsType<WorkGiver_Tend>(DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency").Worker);
            Assert.IsType<WorkGiver_Tend>(DefDatabase<WorkGiverDef>.GetNamed("DoctorTend").Worker);
            Assert.IsType<WorkGiver_FeedPatient>(DefDatabase<WorkGiverDef>.GetNamed("DoctorFeedHumanlikes").Worker);
            Assert.IsType<WorkGiver_RescueDowned>(DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue").Worker);

            // WardenDeliverFood is wired too now (Building.RoomTracker gave it the room screen it was
            // waiting on), and it still does not overlap this lane's feeding giver by one patient: this one
            // excludes prisoners outright. See WorkGiver_Warden_DeliverFood's own doc and WardenDeliverFoodTests.
            Assert.IsType<WorkGiver_Warden_DeliverFood>(DefDatabase<WorkGiverDef>.GetNamed("WardenDeliverFood").Worker);
        }

        // ---- WorkGiver_Tend ----

        [Fact]
        public void Tend_giver_has_no_job_when_the_patient_has_nothing_tendable()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn healthy = SpawnHuman(map, new IntVec3(4, 0, 4), "Healthy");
            healthy.faction = f;

            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            var ordinary = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Assert.False(emergency.HasJobOnThing(doctor, healthy));
            Assert.False(ordinary.HasJobOnThing(doctor, healthy));
        }

        [Fact]
        public void Tend_giver_never_targets_the_doctor_themselves()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            MakeBleedingWound(doctor);

            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            Assert.True(TendUtility.HasAnythingToTend(doctor), "Sanity: the doctor really is hurt.");
            Assert.False(emergency.HasJobOnThing(doctor, doctor), "A pawn is never its own patient through this giver — see its own doc on self-tend.");
        }

        [Fact]
        public void Tend_giver_has_no_job_on_an_uncared_for_pawn()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = captor;
            Pawn stranger = SpawnHuman(map, new IntVec3(4, 0, 4), "Stranger");
            stranger.faction = other; // hostile, never captured — not this doctor's to treat
            MakeBleedingWound(stranger);

            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            Assert.False(emergency.HasJobOnThing(doctor, stranger));
        }

        [Fact]
        public void Emergency_giver_matches_bleeding_and_ordinary_giver_matches_a_non_bleeding_wound()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn bleeder = SpawnHuman(map, new IntVec3(4, 0, 4), "Bleeder");
            bleeder.faction = f;
            MakeBleedingWound(bleeder);
            Pawn bruised = SpawnHuman(map, new IntVec3(2, 0, 2), "Bruised");
            bruised.faction = f;
            MakeOrdinaryWound(bruised);

            Assert.True(TendUtility.NeedsEmergencyTend(bleeder));
            Assert.False(TendUtility.NeedsEmergencyTend(bruised));

            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            var ordinary = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };

            Assert.True(emergency.HasJobOnThing(doctor, bleeder));
            Assert.False(emergency.HasJobOnThing(doctor, bruised));
            Assert.True(ordinary.HasJobOnThing(doctor, bruised));
            Assert.False(ordinary.HasJobOnThing(doctor, bleeder));

            Job? job = emergency.JobOnThing(doctor, bleeder);
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.TendPatient, job!.def);
            Assert.Same(bleeder, job.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Tend_giver_treats_a_prisoner_of_its_own_faction_exactly_like_a_colonist()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = captor;
            Pawn prisoner = SpawnHuman(map, new IntVec3(4, 0, 4), "Prisoner");
            prisoner.faction = other;
            prisoner.health.ForceDowned = true;
            CaptureUtility.Capture(doctor, prisoner);
            prisoner.health.ForceDowned = false;
            MakeBleedingWound(prisoner);

            var emergency = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };
            Assert.True(emergency.HasJobOnThing(doctor, prisoner));
        }

        // ---- JobDriver_TendPatient ----

        [Fact]
        public void A_completed_tend_job_stops_the_bleeding()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn patient = SpawnHuman(map, new IntVec3(2, 0, 2), "Patient");
            patient.faction = f;
            MakeBleedingWound(patient);
            Assert.True(patient.health.hediffSet.BleedRateTotal > 0f);

            doctor.jobs.StartJob(new Job(JobDefOf.TendPatient, patient));
            RunTicks(1000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
        }

        // ---- WorkGiver_RescueDowned / JobDriver_TakeToBed ----

        [Fact]
        public void Rescue_giver_has_no_job_for_a_pawn_that_is_not_downed()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn rescuer = SpawnHuman(map, new IntVec3(0, 0, 0), "Rescuer");
            rescuer.faction = f;
            Pawn standing = SpawnHuman(map, new IntVec3(4, 0, 4), "Standing");
            standing.faction = f;
            SpawnBed(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_RescueDowned();
            Assert.False(giver.HasJobOnThing(rescuer, standing));
        }

        [Fact]
        public void Rescue_giver_has_no_job_when_no_bed_exists_on_the_map()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn rescuer = SpawnHuman(map, new IntVec3(0, 0, 0), "Rescuer");
            rescuer.faction = f;
            Pawn downed = SpawnHuman(map, new IntVec3(4, 0, 4), "Downed");
            downed.faction = f;
            downed.health.ForceDowned = true;

            var giver = new WorkGiver_RescueDowned();
            Assert.False(giver.HasJobOnThing(rescuer, downed), "Honest answer: no bed anywhere means no rescue job at all.");
        }

        [Fact]
        public void Rescue_giver_finds_a_downed_colonist_and_targets_the_nearest_bed()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn rescuer = SpawnHuman(map, new IntVec3(0, 0, 0), "Rescuer");
            rescuer.faction = f;
            Pawn downed = SpawnHuman(map, new IntVec3(4, 0, 4), "Downed");
            downed.faction = f;
            downed.health.ForceDowned = true;
            Thing bed = SpawnBed(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_RescueDowned();
            Assert.Contains(downed, giver.PotentialWorkThingsGlobal(rescuer));
            Assert.True(giver.HasJobOnThing(rescuer, downed));

            Job? job = giver.JobOnThing(rescuer, downed);
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.Rescue, job!.def);
            Assert.Same(downed, job.GetTarget(TargetIndex.A).Thing);
            Assert.Same(bed, job.GetTarget(TargetIndex.B).Thing);
        }

        [Fact]
        public void A_completed_rescue_job_carries_the_downed_patient_to_the_bed()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn rescuer = SpawnHuman(map, new IntVec3(0, 0, 0), "Rescuer");
            rescuer.faction = f;
            Pawn downed = SpawnHuman(map, new IntVec3(5, 0, 5), "Downed");
            downed.faction = f;
            downed.health.ForceDowned = true;
            var bedCell = new IntVec3(1, 0, 1);
            Thing bed = SpawnBed(map, bedCell);

            rescuer.jobs.StartJob(new Job(JobDefOf.Rescue, downed, bed));
            RunTicks(2000, rescuer);

            Assert.Equal(bedCell, downed.Position);
        }

        // ---- WorkGiver_FeedPatient ----

        [Fact]
        public void Feed_giver_has_no_job_for_a_downed_prisoner_thats_WardenFeeds_case()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = captor;
            Pawn prisoner = SpawnHuman(map, new IntVec3(4, 0, 4), "Prisoner");
            prisoner.faction = other;
            prisoner.health.ForceDowned = true;
            CaptureUtility.Capture(doctor, prisoner);
            prisoner.needs.food!.CurLevelPercentage = 0.1f;
            SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_FeedPatient();
            Assert.False(giver.HasJobOnThing(doctor, prisoner), "A downed, hungry prisoner is WardenFeed's patient, not this giver's.");
        }

        [Fact]
        public void Feed_giver_has_no_job_for_a_colonist_that_is_not_downed_or_not_hungry()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn patient = SpawnHuman(map, new IntVec3(4, 0, 4), "Patient");
            patient.faction = f;
            SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_FeedPatient();
            Assert.False(giver.HasJobOnThing(doctor, patient), "Not downed yet.");

            patient.health.ForceDowned = true;
            Assert.Equal(HungerCategory.Fed, patient.needs.food!.CurCategory);
            Assert.False(giver.HasJobOnThing(doctor, patient), "Downed but not hungry.");
        }

        [Fact]
        public void Feed_giver_finds_a_downed_hungry_colonist_with_food_reachable()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn patient = SpawnHuman(map, new IntVec3(4, 0, 4), "Patient");
            patient.faction = f;
            patient.health.ForceDowned = true;
            patient.needs.food!.CurLevelPercentage = 0.1f;
            Thing food = SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_FeedPatient();
            Assert.True(giver.HasJobOnThing(doctor, patient));

            Job? job = giver.JobOnThing(doctor, patient);
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.FeedPatient, job!.def);
            Assert.Same(food, job.GetTarget(TargetIndex.A).Thing);
            Assert.Same(patient, job.GetTarget(TargetIndex.B).Thing);
        }

        // ---- priority ordering ----

        [Fact]
        public void An_available_emergency_patient_is_treated_before_an_available_routine_one()
        {
            CoreMap map = NewMap(10, 10);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;

            // The routine patient sits right next to the doctor; the emergency one is far away — if distance
            // beat emergency-first ordering, the routine one would win. It should not.
            Pawn bruised = SpawnHuman(map, new IntVec3(1, 0, 1), "Bruised");
            bruised.faction = f;
            MakeOrdinaryWound(bruised);
            Pawn bleeder = SpawnHuman(map, new IntVec3(9, 0, 9), "Bleeder");
            bleeder.faction = f;
            MakeBleedingWound(bleeder);

            doctor.jobs.TryFindAndStartJob();

            Assert.Equal(JobDefOf.TendPatient, doctor.jobs.curJob?.def);
            Assert.Same(bleeder, doctor.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void A_doctor_with_nobody_to_care_for_gets_no_doctor_job()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(2, 0, 2), "Doctor");
            doctor.faction = f;

            doctor.jobs.TryFindAndStartJob();

            JobDef? gotJob = doctor.jobs.curJob?.def;
            Assert.NotEqual(JobDefOf.TendPatient, gotJob);
            Assert.NotEqual(JobDefOf.Rescue, gotJob);
            Assert.NotEqual(JobDefOf.FeedPatient, gotJob);
        }

        // ---- Scribe round trip, mid-tend ----

        [Fact]
        public void A_tend_job_round_trips_through_Scribe_and_finishes_after_loading()
        {
            CoreMap map = NewMap(6, 6);
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(2, 0, 2), "Patient");
            MakeBleedingWound(patient);

            doctor.jobs.StartJob(new Job(JobDefOf.TendPatient, patient));

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedDoctor = (Pawn)loaded.mapPawns.AllPawns[0];
            var loadedPatient = (Pawn)loaded.mapPawns.AllPawns[1];

            Assert.NotNull(loadedDoctor.jobs.curJob);
            Assert.Equal(JobDefOf.TendPatient, loadedDoctor.jobs.curJob!.def);
            Assert.Same(loadedPatient, loadedDoctor.jobs.curJob.GetTarget(TargetIndex.A).Thing);

            RunTicks(1000, loadedDoctor, loadedPatient);
            Assert.Equal(0f, loadedPatient.health.hediffSet.BleedRateTotal);
        }

        // ---- end to end: the point of the whole module ----

        [Fact]
        public void A_bleeding_colonist_is_found_and_tended_by_another_entirely_through_the_job_system()
        {
            CoreMap map = NewMap(10, 10);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn patient = SpawnHuman(map, new IntVec3(8, 0, 8), "Patient");
            patient.faction = f;
            MakeBleedingWound(patient);
            float bleedBefore = patient.health.hediffSet.BleedRateTotal;
            Assert.True(bleedBefore > 0f);

            // Nothing tells the doctor to do this — TryFindAndStartJob runs the ordinary think tree, which
            // reaches JobGiver_Work, which scans DoctorTendEmergency like any other WorkGiverDef.
            RunTicks(3000, doctor, patient);

            Assert.True(patient.health.hediffSet.BleedRateTotal == 0f,
                "The bleeding colonist should have been found and tended by the other colonist, unprompted.");
        }

        [Fact]
        public void A_downed_colonist_is_carried_to_a_bed_entirely_through_the_job_system()
        {
            CoreMap map = NewMap(10, 10);
            Faction f = NewFaction("Colony");
            Pawn rescuer = SpawnHuman(map, new IntVec3(0, 0, 0), "Rescuer");
            rescuer.faction = f;
            Pawn downed = SpawnHuman(map, new IntVec3(8, 0, 8), "Downed");
            downed.faction = f;
            downed.health.ForceDowned = true;
            var bedCell = new IntVec3(1, 0, 1);
            SpawnBed(map, bedCell);

            RunTicks(3000, rescuer);

            Assert.Equal(bedCell, downed.Position);
        }

        [Fact]
        public void A_starving_downed_colonist_is_fed_entirely_through_the_job_system()
        {
            CoreMap map = NewMap(10, 10);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), "Doctor");
            doctor.faction = f;
            Pawn patient = SpawnHuman(map, new IntVec3(8, 0, 8), "Patient");
            patient.faction = f;
            patient.health.ForceDowned = true;
            patient.needs.food!.CurLevelPercentage = 0f;
            float hungerBefore = patient.needs.food.CurLevel;
            SpawnFood(map, new IntVec3(1, 0, 1));

            RunTicks(3000, doctor, patient);

            Assert.True(patient.needs.food.CurLevel > hungerBefore,
                "The starving, downed colonist should have been found and fed by the other colonist, unprompted.");
        }
    }
}
