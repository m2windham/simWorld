using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <c>WardenDeliverFood</c>, the last of the twenty-nine shipped <c>WorkGiverDef</c>s to be wired
    /// (<see cref="WorkGiver_Warden_DeliverFood"/>, <see cref="JobDriver_FoodDeliver"/>). Two earlier passes
    /// left it a <c>WorkGiver_Pending</c> placeholder because the screen that makes it a distinct giver
    /// upstream — "is there already food in the prisoner's room" — had no room system to ask; these tests
    /// pin the behaviour that <see cref="SimWorld.Building.RoomTracker"/> made expressible, and in particular
    /// that this giver never reaches a patient <c>WardenFeed</c> or <c>DoctorFeedHumanlikes</c> would.
    /// <para/>
    /// Its own file rather than more cases in <c>WardenAITests</c>: additive tests need no edit to a file
    /// other lanes may also be editing (CLAUDE.md).
    /// </summary>
    public class WardenDeliverFoodTests : ContentTestBase
    {
        public WardenDeliverFoodTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager but ContentTestBase does not reset it — see
            // WardenAITests' and CaptureTests' own identical constructor comments.
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

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

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, int count = 1, string defName = "RawPotatoes")
        {
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            food.stackCount = count;
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        private static void SpawnBuilding(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);

        /// <summary>
        /// A four-walled cell with one door at (5,3), enclosing (3..4, 2..4) — a prison cell in the ordinary
        /// sense, built out of the only two structural defs this port ships. The door keeps the room
        /// *reachable* (a Door's passability is Standable here, see <c>SimWorld.Building.Door</c>) while the
        /// walls keep it a separate <c>Room</c>, which is the distinction this giver reads.
        /// </summary>
        private static IntVec3 BuildPrisonCell(CoreMap map)
        {
            var doorCell = new IntVec3(5, 0, 2);
            var rect = new CellRect(2, 1, 4, 4); // edge cells are the walls; interior is (3..4, 2..3)
            foreach (IntVec3 c in rect.EdgeCells)
            {
                if (c != doorCell) SpawnBuilding(map, c, "Wall");
            }
            SpawnBuilding(map, doorCell, "Door");
            return new IntVec3(3, 0, 2);
        }

        /// <summary>
        /// A prisoner of <paramref name="captor"/>, held, ambulatory and hungry — this giver's own case, and
        /// the exact complement of <c>WardenFeed</c>'s downed one.
        /// <para/>
        /// Left out of the tick lists on the way out. A hungry, ambulatory prisoner is exactly the pawn whose
        /// own <c>JobGiver_GetFood</c> tier sends it across the map to eat, and a warden delivering to a
        /// moving target is a race, not a measurement — the race is real, is what
        /// <see cref="WorkGiver_Warden_DeliverFood"/>'s own doc is about, and is the wrong thing for a test
        /// of the delivery job to be resolving. <c>RunTicks</c> re-registers any pawn handed to it, so a test
        /// that wants the prisoner acting simply names it.
        /// </summary>
        private static Pawn CaptureAmbulatoryPrisoner(CoreMap map, Pawn warden, Faction other, IntVec3 at)
        {
            Pawn victim = SpawnHuman(map, at, "Prisoner");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.health.ForceDowned = false;
            victim.needs.food!.CurLevelPercentage = 0.1f;
            Find.TickManager.DeRegisterAllTickabilityFor(victim);
            return victim;
        }

        // ---- content ----

        [Fact]
        public void WardenDeliverFood_is_wired_to_a_real_scanner_and_its_JobDef_is_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.IsType<WorkGiver_Warden_DeliverFood>(
                DefDatabase<WorkGiverDef>.GetNamed("WardenDeliverFood").Worker);
            Assert.NotNull(WardenDeliverFoodDefOf.DeliverFood);
            Assert.Equal(typeof(JobDriver_FoodDeliver), WardenDeliverFoodDefOf.DeliverFood.driverClass);
        }

        // ---- the screens that keep this giver from being anybody else's ----

        [Fact]
        public void No_job_for_a_downed_prisoner_because_that_is_WardenFeeds_patient()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            prisoner.health.ForceDowned = true;
            SpawnFood(map, new IntVec3(9, 0, 8));
            map.MapTick();

            // The same prisoner, the same food, the same map: exactly one of the two givers takes it, and the
            // Downed flag is the whole of what tells them apart.
            Assert.False(new WorkGiver_Warden_DeliverFood().HasJobOnThing(warden, prisoner));
            Assert.True(new WorkGiver_Warden_Feed().HasJobOnThing(warden, prisoner));
        }

        [Fact]
        public void No_job_when_the_prisoners_own_room_already_holds_something_to_eat()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            SpawnFood(map, new IntVec3(9, 0, 8));
            map.MapTick();

            var giver = new WorkGiver_Warden_DeliverFood();
            Assert.True(giver.HasJobOnThing(warden, prisoner), "premise: with an empty cell there is a job");

            // One meal standing in the cell is the whole self-limiting mechanism: a restocked room asks for
            // nothing, which is what stops this giver firing once per hungry prisoner per scan.
            SpawnFood(map, new IntVec3(4, 0, 3));
            map.MapTick();
            Assert.False(giver.HasJobOnThing(warden, prisoner));
        }

        [Fact]
        public void No_job_on_a_prisoner_another_faction_holds()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Faction rival = NewFaction("Rivals");
            rival.SetRelationDirect(other, FactionRelationKind.Hostile, -100);

            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            SpawnFood(map, new IntVec3(9, 0, 8));
            map.MapTick();

            Pawn stranger = SpawnHuman(map, new IntVec3(8, 0, 9), "StrangerWarden");
            stranger.faction = rival;
            Assert.False(new WorkGiver_Warden_DeliverFood().HasJobOnThing(stranger, prisoner));
        }

        [Fact]
        public void No_job_when_the_only_food_is_already_in_the_prisoners_room()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            SpawnFood(map, new IntVec3(4, 0, 3));
            map.MapTick();

            // Carrying a meal out of the prisoner's room and back into it would be a job that changes nothing.
            Assert.False(new WorkGiver_Warden_DeliverFood().HasJobOnThing(warden, prisoner));
        }

        // ---- the job itself ----

        [Fact]
        public void A_warden_carries_one_meal_into_the_prisoners_room_and_leaves_it_there()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            Thing larder = SpawnFood(map, new IntVec3(9, 0, 8), count: 4);
            map.MapTick();

            var giver = new WorkGiver_Warden_DeliverFood();
            Job? job = giver.JobOnThing(warden, prisoner);
            Assert.NotNull(job);
            Assert.Equal(WardenDeliverFoodDefOf.DeliverFood, job!.def);
            Assert.Same(larder, job.GetTarget(TargetIndex.A).Thing);
            Assert.Same(prisoner, job.GetTarget(TargetIndex.B).Thing);

            float hungerBefore = prisoner.needs.food!.CurLevelPercentage;
            warden.jobs.StartJob(job);
            RunTicks(600, warden);

            // Delivered, not fed: the meal is standing in the prisoner's room and the prisoner's own need has
            // not moved — eating it is the prisoner's job, through the ordinary JobGiver_GetFood tier.
            map.MapTick();
            SimWorld.Building.Room cell = map.roomTracker.RoomAt(prisoner.Position)!;
            List<Thing> mealsInCell = map.listerThings.ThingsInGroup(ThingRequestGroup.Item)
                .Where(t => t.def.IsNutritionGivingIngestible
                            && ReferenceEquals(map.roomTracker.RoomAt(t.Position), cell))
                .ToList();
            Assert.Single(mealsInCell);
            Assert.Equal(JobDriver_FoodDeliver.MealsPerDelivery, mealsInCell[0].stackCount);
            Assert.Equal(hungerBefore, prisoner.needs.food.CurLevelPercentage);

            // Conserved, not conjured: exactly as many meals on the map as before, one of them moved.
            Assert.Equal(4, map.listerThings.ThingsInGroup(ThingRequestGroup.Item)
                .Where(t => t.def.IsNutritionGivingIngestible).Sum(t => t.stackCount));

            // And the giver stands down now that the cell is stocked — the loop settles instead of repeating.
            Assert.False(giver.HasJobOnThing(warden, prisoner));
        }

        [Fact]
        public void The_delivered_meal_is_what_the_prisoner_then_eats_where_it_is_held()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            SpawnFood(map, new IntVec3(9, 0, 8), count: 4);
            map.MapTick();

            warden.jobs.StartJob(new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner)!);
            RunTicks(600, warden);
            map.MapTick();

            // The warden leaves once the meal is down. It has to: a warden standing inside the cell is a
            // hostile within melee reach, and the prisoner's own think tree puts the danger tier above the
            // hunger tier — which would be the right behaviour and the wrong thing to measure here.
            warden.DeSpawn();

            float before = prisoner.needs.food!.CurLevelPercentage;
            RunTicks(600, prisoner);
            Assert.True(prisoner.needs.food.CurLevelPercentage > before,
                "the prisoner should feed itself from the meal delivered to its own room");
        }

        [Fact]
        public void An_interrupted_delivery_drops_the_meal_rather_than_destroying_it()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            SpawnFood(map, new IntVec3(9, 0, 8), count: 1); // a single meal travels as the whole Thing
            map.MapTick();

            warden.jobs.StartJob(new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner)!);
            RunTicks(40, warden); // long enough to have picked the meal up, not to have arrived
            warden.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);

            Assert.Equal(1, map.listerThings.ThingsInGroup(ThingRequestGroup.Item)
                .Where(t => t.def.IsNutritionGivingIngestible).Sum(t => t.stackCount));
        }

        // ---- the bug this giver's first ambulatory patient uncovered ----

        [Fact]
        public void A_warden_and_the_prisoner_it_holds_are_not_enemies_but_that_prisoners_free_comrade_still_is()
        {
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);

            // Custody, not pacifism: the same faction's pawn who is not in custody is still an enemy, so this
            // is not a blanket "nobody fights" and cannot hide a broken faction relation.
            Pawn raider = SpawnHuman(map, new IntVec3(8, 0, 8), "Raider");
            raider.faction = other;

            Assert.False(AttackTargetsUtility.HostileTo(warden, prisoner));
            Assert.False(AttackTargetsUtility.HostileTo(prisoner, warden));
            Assert.True(AttackTargetsUtility.HostileTo(warden, raider));
            Assert.True(AttackTargetsUtility.HostileTo(raider, warden));

            // Every prior warden job's patient was Downed, which ThreatDisabled already screens out — which
            // is why no test before this one could see the hole.
            Assert.False(AttackTargetsUtility.ThreatDisabled(prisoner));
        }

        // ---- the three feeding givers do not race each other for one meal ----

        [Fact]
        public void A_doctor_does_not_take_the_meal_a_warden_has_already_claimed()
        {
            CoreMap map = NewMap(14, 14);
            (Faction captor, Faction other) = HostileFactions();

            Pawn warden = SpawnHuman(map, new IntVec3(11, 0, 11), "Warden");
            warden.faction = captor;
            Pawn doctor = SpawnHuman(map, new IntVec3(10, 0, 11), "Doctor");
            doctor.faction = captor;

            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);

            // The doctor's own patient: a downed, hungry colonist, who is nobody's prisoner.
            Pawn patient = SpawnHuman(map, new IntVec3(10, 0, 10), "Patient");
            patient.faction = captor;
            patient.health.ForceDowned = true;
            patient.needs.food!.CurLevelPercentage = 0.1f;

            // Two piles rather than two single meals: one potato does not fill a starving patient, so a
            // doctor with a single-meal stack finishes it and goes looking for the next nearest — which, once
            // the warden has delivered, is the prisoner's. The stacks keep this test about who claims what.
            Thing nearMeal = SpawnFood(map, new IntVec3(10, 0, 9), count: 20);
            Thing farMeal = SpawnFood(map, new IntVec3(11, 0, 9), count: 20);
            map.MapTick();

            // The warden picks first and claims its stack the way any job does — in its first toil.
            Job wardenJob = new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner)!;
            Thing wardenMeal = wardenJob.GetTarget(TargetIndex.A).Thing!;
            warden.jobs.StartJob(wardenJob);
            RunTicks(1, warden);
            Assert.True(map.reservationManager.IsReservedBy(warden, wardenMeal),
                "premise: the delivery job claims its meal before it spends a tick walking");

            // The doctor, scanning immediately afterwards, does not even see that stack as a candidate.
            Job? doctorJob = new WorkGiver_FeedPatient().JobOnThing(doctor, patient);
            Assert.NotNull(doctorJob);
            Thing doctorMeal = doctorJob!.GetTarget(TargetIndex.A).Thing!;
            Assert.NotSame(wardenMeal, doctorMeal);
            Assert.Contains(doctorMeal, new[] { nearMeal, farMeal });

            // Both jobs then run side by side, until the delivery is done: the prisoner has a meal in its cell
            // and the downed patient has been fed, out of two different stacks.
            doctor.jobs.StartJob(doctorJob);

            // Ticked until these two particular jobs are done, rather than for a fixed span: whatever either
            // of them does next is not what this test is measuring, and a doctor left running long enough
            // would legitimately come back for the meal now standing in the prisoner's cell.
            int guard = 0;
            while ((ReferenceEquals(warden.jobs.curJob, wardenJob) || ReferenceEquals(doctor.jobs.curJob, doctorJob))
                   && guard++ < 4000)
            {
                RunTicks(1, warden, doctor);
            }

            Assert.True(guard < 4000, "one of the two jobs never finished");
            Assert.True(patient.needs.food.CurLevelPercentage > 0.1f, "the doctor's patient went unfed");

            map.MapTick();
            SimWorld.Building.Room cell = map.roomTracker.RoomAt(prisoner.Position)!;
            Assert.Contains(map.listerThings.ThingsInGroup(ThingRequestGroup.Item),
                t => t.def.IsNutritionGivingIngestible && ReferenceEquals(map.roomTracker.RoomAt(t.Position), cell));

            // And the warden's pile is down by exactly the one meal it delivered — the doctor never reached
            // into it, which is the claim this whole test exists to make.
            Assert.Equal(20 - JobDriver_FoodDeliver.MealsPerDelivery, wardenMeal.stackCount);
        }

        [Fact]
        public void With_one_meal_between_them_the_second_giver_produces_no_job_at_all()
        {
            CoreMap map = NewMap(14, 14);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(11, 0, 11), "Warden");
            warden.faction = captor;
            Pawn doctor = SpawnHuman(map, new IntVec3(10, 0, 11), "Doctor");
            doctor.faction = captor;

            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);

            Pawn patient = SpawnHuman(map, new IntVec3(10, 0, 10), "Patient");
            patient.faction = captor;
            patient.health.ForceDowned = true;
            patient.needs.food!.CurLevelPercentage = 0.1f;

            SpawnFood(map, new IntVec3(10, 0, 9));
            map.MapTick();

            warden.jobs.StartJob(new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner)!);
            RunTicks(1, warden);

            // Refused honestly rather than silently shared: with the only stack claimed, the doctor has
            // nothing to do this scan and will find the same patient again once the claim is released.
            Assert.Null(new WorkGiver_FeedPatient().JobOnThing(doctor, patient));
            Assert.False(new WorkGiver_FeedPatient().HasJobOnThing(doctor, patient));
        }

        // ---- Scribe ----

        [Fact]
        public void Scribe_round_trip_of_a_delivery_in_flight()
        {
            // This module adds no persistent state of its own — a WorkGiver is stateless and no JobDriver in
            // this port is Scribed. What a delivery in flight does leave in the save is its claim on the meal
            // and on the prisoner, in the map's ReservationManager, and that is what has to survive: a loaded
            // game whose reservations came back empty would let a second warden spend a meal already spoken
            // for. Saved through the whole Map rather than the manager alone, because a Reservation's
            // claimant is a Scribe_References pointer and only resolves against a document that deep-saves
            // the Pawn too — see CaptureTests' CaptureRoot for the same constraint.
            CoreMap map = NewMap(12, 12);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(9, 0, 9), "Warden");
            warden.faction = captor;
            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = CaptureAmbulatoryPrisoner(map, warden, other, inside);
            // Far enough that one tick leaves the warden walking toward the meal rather than already holding
            // it: a Thing that is off the map mid-carry is not deep-saved and its claim cannot re-link, which
            // is JobDriver_HaulToCell's own documented carry-tracker gap, not this job's.
            Thing meal = SpawnFood(map, new IntVec3(9, 0, 5));
            map.MapTick();

            warden.jobs.StartJob(new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner)!);
            RunTicks(1, warden);
            Assert.True(meal.Spawned, "premise: the meal is still on the map when the save is taken");
            Assert.True(map.reservationManager.IsReservedBy(warden, meal));
            Assert.True(map.reservationManager.IsReservedBy(warden, prisoner));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            // A Faction round-trips through the World/FactionManager save, never through a lone Map's — the
            // same constraint StructuresTests records, and the only thing unresolved here.
            Assert.All(errors, e => Assert.Contains("Pawn.faction", e));

            Thing loadedMeal = loaded.listerThings.AllThings.First(t => t.thingIDNumber == meal.thingIDNumber);
            Thing loadedPrisoner = loaded.listerThings.AllThings.First(t => t.thingIDNumber == prisoner.thingIDNumber);
            Thing loadedWarden = loaded.listerThings.AllThings.First(t => t.thingIDNumber == warden.thingIDNumber);

            Assert.True(loaded.reservationManager.IsReservedBy((Pawn)loadedWarden, loadedMeal));
            Assert.True(loaded.reservationManager.IsReservedBy((Pawn)loadedWarden, loadedPrisoner));
        }
    }
}
