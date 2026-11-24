using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Building;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A baby carried out of the cold (RimWorld Biotech: <c>WorkGiver_BringBabyToSafety</c>,
    /// <c>ChildcareUtility</c>). The long-run reading that asked for it: on band 40 seed 201 every exposure
    /// death the lane traced was a baby lying in an unheated room, unable to walk to warmth. Temperatures here
    /// are only either side of the baby's comfortable range, never a tuned value.
    /// </summary>
    public class ChildcareTests : ContentTestBase
    {
        public ChildcareTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static CoreMap NewMap(int size, float outdoor)
        {
            var map = new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };
            map.MapTick();
            return map;
        }

        private static Pawn SpawnAdult(CoreMap map, IntVec3 cell, Faction f, string name = "Parent")
        {
            Pawn p = NewHuman(name);
            p.faction = f;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnBaby(CoreMap map, IntVec3 cell, Faction f, int household = Pawn_RelationsTracker.None)
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            baby.faction = f;
            baby.relations.familyId = household;
            GenSpawn.Spawn(baby, cell, map);
            Assert.True(ChildcareUtility.IsBaby(baby), "fixture assumption: a newborn is in the baby stage");
            return baby;
        }

        private static Thing SpawnBed(CoreMap map, IntVec3 cell)
        {
            Thing bed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed"));
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        private static void SpawnEdifice(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);

        /// <summary>A walled, roofed room (5x5 inside, x 2..6, z 3..7) with a bed at (3, 4) and a door on its
        /// east side; its air set to <paramref name="inside"/>. Returns the bed.</summary>
        private static Thing BuildRoomWithBed(CoreMap map, float inside)
        {
            var shell = new CellRect(1, 2, 7, 7);
            var door = new IntVec3(7, 0, 5);
            foreach (IntVec3 c in shell.EdgeCells) SpawnEdifice(map, c, c == door ? "Door" : "Wall");
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            Thing bed = SpawnBed(map, new IntVec3(3, 0, 4));
            map.MapTick();
            SetRoom(map, bed, inside);
            return bed;
        }

        private static void SetRoom(CoreMap map, Thing bed, float temperature)
        {
            Room room = map.roomTracker.RoomAt(bed.GetSleepingSlotPos())!;
            Assert.False(room.Group.UsesOutdoorTemperature);
            room.Group.temperature = temperature;
        }

        [Fact]
        public void A_baby_in_the_cold_is_carried_to_a_bed_in_comfortable_air_past_a_nearer_cold_one()
        {
            CoreMap map = NewMap(16, -10f);
            Faction f = NewFaction("Colony");
            Thing warm = BuildRoomWithBed(map, 20f);
            SpawnBed(map, new IntVec3(13, 0, 11)); // in the open, beside the baby
            Pawn baby = SpawnBaby(map, new IntVec3(13, 0, 13), f);
            Pawn parent = SpawnAdult(map, new IntVec3(10, 0, 2), f);

            Assert.True(ChildcareUtility.NeedsMovingForTemperature(baby));
            var giver = new WorkGiver_BringBabyToSafety();
            Assert.Contains(baby, giver.PotentialWorkThingsGlobal(parent));
            Assert.True(giver.HasJobOnThing(parent, baby));
            Job job = giver.JobOnThing(parent, baby)!;
            Assert.Same(ChildcareJobDefOf.BringBabyToSafety, job.def);
            Assert.Same(warm, job.GetTarget(TargetIndex.B).Thing);

            parent.jobs.StartJob(job);
            for (int i = 0; i < 3000 && baby.Position != warm.GetSleepingSlotPos(); i++)
            {
                SetRoom(map, warm, 20f); // hold the room warm against the cold outside
                RunTicks(1, parent, baby);
            }
            Assert.Equal(warm.GetSleepingSlotPos(), baby.Position);
            Assert.True(baby.InBed());
            Assert.False(ChildcareUtility.NeedsMovingForTemperature(baby));
            Assert.False(giver.HasJobOnThing(parent, baby));
        }

        [Fact]
        public void With_nowhere_comfortable_the_baby_is_not_carried_from_cold_to_cold()
        {
            CoreMap map = NewMap(16, -10f);
            Faction f = NewFaction("Colony");
            BuildRoomWithBed(map, 5f); // out of the wind but under the baby's comfortable minimum
            Pawn baby = SpawnBaby(map, new IntVec3(13, 0, 13), f);
            Pawn parent = SpawnAdult(map, new IntVec3(10, 0, 2), f);

            Assert.True(ChildcareUtility.NeedsMovingForTemperature(baby));
            Assert.Null(ChildcareUtility.SafeBedForBaby(baby, parent));
            Assert.False(new WorkGiver_BringBabyToSafety().HasJobOnThing(parent, baby));
        }

        [Fact]
        public void A_baby_in_comfortable_air_and_an_adult_in_the_cold_are_both_left_alone()
        {
            CoreMap mild = NewMap(16, 20f);
            Faction f = NewFaction("Colony");
            BuildRoomWithBed(mild, 20f);
            Pawn baby = SpawnBaby(mild, new IntVec3(13, 0, 13), f);
            Pawn parent = SpawnAdult(mild, new IntVec3(10, 0, 2), f);
            Assert.False(new WorkGiver_BringBabyToSafety().HasJobOnThing(parent, baby));

            CoreMap cold = NewMap(16, -10f);
            BuildRoomWithBed(cold, 20f);
            Pawn adult = SpawnAdult(cold, new IntVec3(13, 0, 13), f, "Downed");
            adult.health.ForceDowned = true;
            Pawn helper = SpawnAdult(cold, new IntVec3(10, 0, 2), f, "Helper");
            Assert.False(ChildcareUtility.NeedsMovingForTemperature(adult));
            Assert.False(new WorkGiver_BringBabyToSafety().HasJobOnThing(helper, adult));
        }

        [Fact]
        public void A_baby_of_another_faction_is_not_carried()
        {
            CoreMap map = NewMap(16, -10f);
            BuildRoomWithBed(map, 20f);
            Pawn baby = SpawnBaby(map, new IntVec3(13, 0, 13), NewFaction("Strangers"));
            Pawn adult = SpawnAdult(map, new IntVec3(10, 0, 2), NewFaction("Colony"));
            Assert.False(new WorkGiver_BringBabyToSafety().HasJobOnThing(adult, baby));
        }

        /// <summary>RimWorld puts a baby in its own crib first; here a household owns its house's beds, so its
        /// own house comes first even when another household's warm house is nearer, and another household's
        /// house is never used at all (<see cref="RestUtility.FindBedFor"/>'s rule).</summary>
        [Fact]
        public void A_baby_goes_to_its_own_households_warm_house_and_never_into_anothers()
        {
            CoreMap map = NewMap(24, -10f);
            Faction f = NewFaction("Colony");
            DwellingDef rung = ShelterKit.Rung("House_Paleo");
            Dwelling own = ShelterKit.Raise(map, rung, new IntVec3(2, 0, 2), f, householdId: 7);
            Dwelling theirs = ShelterKit.Raise(map, rung, new IntVec3(14, 0, 2), f, householdId: 8);
            ShelterKit.Roof(map, own);
            ShelterKit.Roof(map, theirs);
            map.MapTick();
            Thing ownBed = own.Beds(map).First();
            SetRoom(map, ownBed, 20f);
            SetRoom(map, theirs.Beds(map).First(), 20f);

            Pawn baby = SpawnBaby(map, new IntVec3(20, 0, 14), f, household: 7);
            Pawn parent = SpawnAdult(map, new IntVec3(10, 0, 14), f);
            Thing? bed = ChildcareUtility.SafeBedForBaby(baby, parent);
            Assert.NotNull(bed);
            Assert.Same(own, DwellingUtility.DwellingOfBed(bed!));

            Pawn stranger = SpawnBaby(map, new IntVec3(20, 0, 16), f, household: 9);
            Assert.Null(ChildcareUtility.SafeBedForBaby(stranger, parent));
        }

        /// <summary>Emergency basic work: every adult does it, and it is among the givers a sleeper is woken for
        /// (<see cref="JobDriver_LayDown"/> asks <see cref="JobGiver_Work.TryGiveEmergencyJob"/>).</summary>
        [Fact]
        public void Carrying_a_cold_baby_is_emergency_work_a_sleeper_is_woken_for()
        {
            WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("BringBabyToSafety");
            Assert.True(def.emergency);
            Assert.Same(DefDatabase<WorkTypeDef>.GetNamed("BasicWorker"), def.workType);
            Assert.IsType<WorkGiver_BringBabyToSafety>(def.Worker);

            CoreMap map = NewMap(16, -10f);
            Faction f = NewFaction("Colony");
            Thing warm = BuildRoomWithBed(map, 20f);
            Pawn baby = SpawnBaby(map, new IntVec3(13, 0, 13), f);
            Pawn parent = SpawnAdult(map, new IntVec3(10, 0, 2), f);
            Job? job = JobGiver_Work.TryGiveEmergencyJob(parent);
            Assert.NotNull(job);
            Assert.Same(ChildcareJobDefOf.BringBabyToSafety, job!.def);
            Assert.Same(baby, job.GetTarget(TargetIndex.A).Thing);
            Assert.Same(warm, job.GetTarget(TargetIndex.B).Thing);
        }

        /// <summary>The giver keeps no state; what a save has to carry is a carry under way, which is a
        /// <see cref="Job"/> on the hauler's tracker.</summary>
        [Fact]
        public void A_carry_under_way_survives_a_save_and_load()
        {
            var job = new Job(ChildcareJobDefOf.BringBabyToSafety, new LocalTargetInfo(new IntVec3(4, 0, 7)), new LocalTargetInfo(new IntVec3(3, 0, 4)));
            Job read = Scribe.Load<Job>(Scribe.SaveToString(job, "job"), "job");
            Assert.Same(ChildcareJobDefOf.BringBabyToSafety, read.def);
            Assert.Equal(new IntVec3(4, 0, 7), read.targetA.Cell);
            Assert.Equal(new IntVec3(3, 0, 4), read.targetB.Cell);
        }
    }
}
