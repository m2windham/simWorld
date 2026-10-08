using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// The medicine a doctor carries to a patient is carried, not parked in a driver field
    /// (<see cref="JobDriver_TendPatient"/> through <see cref="Pawn_CarryTracker"/>). Two ways the driver's own
    /// field lost it: a tend that found nothing to treat (the patient healed, or was tended by someone else,
    /// while the doctor walked) handed the unit to <see cref="TendUtility.DoTendWithMedicine"/>, which spends it
    /// only on a tend that lands — the unit was off the map and nobody had it; and a save taken mid-carry could
    /// not find it, because nothing in a <see cref="JobDriver"/> is Scribed.
    /// <para/>
    /// Conservation again, not a coordinate: a unit of medicine is on the map, in the doctor's hands, or spent
    /// by a tend that landed — never nowhere.
    /// </summary>
    public class TendMedicineCarryTests : ContentTestBase
    {
        public TendMedicineCarryTests(CoreContentFixture content) : base(content)
        {
        }

        public enum Interruption
        {
            Downed,
            Killed,
            OrderedAway,
            JobEnded,
        }

        private static ThingDef Herbal => DefDatabase<ThingDef>.GetNamed("MedicineHerbal");

        private static CoreMap NewMap() => new CoreMap(8, 8, TerrainDefOf.Soil);

        private static readonly IntVec3 MedicineCell = new IntVec3(1, 0, 1);

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void MakeBleedingWound(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), 6f, hitPart: Part(p, "left arm")), p);

        /// <summary>A doctor, a patient across the map (bleeding when <paramref name="wounded"/>) and a pile of
        /// medicine on the way, with the tend job the work giver would hand out.</summary>
        private static (CoreMap map, Pawn doctor, Pawn patient, Thing medicine, Job job) TendSite(int units, bool wounded = true)
        {
            CoreMap map = NewMap();
            Pawn doctor = NewHuman("Doctor");
            GenSpawn.Spawn(doctor, new IntVec3(0, 0, 0), map);
            Pawn patient = NewHuman("Patient");
            GenSpawn.Spawn(patient, new IntVec3(6, 0, 6), map);
            if (wounded) MakeBleedingWound(patient);
            Thing medicine = ThingMaker.MakeThing(Herbal);
            medicine.stackCount = units;
            GenSpawn.Spawn(medicine, MedicineCell, map);
            return (map, doctor, patient, medicine, new Job(JobDefOf.TendPatient, patient, medicine));
        }

        private static int MedicineOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Herbal).Where(t => t.Spawned).Sum(t => t.stackCount);

        /// <summary>Ticks until a unit has left the map without being spent — the doctor has it in hand.</summary>
        private static void RunUntilMedicineIsInHand(CoreMap map, Pawn doctor, int units, int maxTicks = 1000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (MedicineOnMap(map) < units)
                {
                    Assert.Equal(JobDefOf.TendPatient, doctor.jobs.curJob?.def);
                    return;
                }
                RunTicks(1, doctor);
            }
            Assert.Fail("The doctor never picked the medicine up within " + maxTicks + " ticks.");
        }

        private static void Interrupt(Pawn doctor, Interruption how)
        {
            switch (how)
            {
                case Interruption.Downed:
                    doctor.health.ForceDowned = true;
                    break;
                case Interruption.Killed:
                    doctor.health.Kill(null, null);
                    break;
                case Interruption.OrderedAway:
                    doctor.jobs.StartJob(new Job(DutyJobDefOf.Goto, new IntVec3(0, 0, 7)) { playerForced = true }, JobCondition.InterruptForced);
                    break;
                case Interruption.JobEnded:
                    doctor.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    break;
            }
        }

        // ---- interruption ----

        /// <summary>However the doctor is pulled away with medicine in hand, the unit is back on the map at
        /// the doctor's feet — a pile of five (one split off) and a lone unit (carried whole) alike.</summary>
        [Theory]
        [InlineData(Interruption.Downed, 5)]
        [InlineData(Interruption.Downed, 1)]
        [InlineData(Interruption.Killed, 5)]
        [InlineData(Interruption.OrderedAway, 5)]
        [InlineData(Interruption.OrderedAway, 1)]
        [InlineData(Interruption.JobEnded, 5)]
        [InlineData(Interruption.JobEnded, 1)]
        public void An_interrupted_tend_leaves_the_medicine_it_carried_on_the_map(Interruption how, int units)
        {
            (CoreMap map, Pawn doctor, _, _, Job job) = TendSite(units);
            doctor.jobs.StartJob(job);

            RunUntilMedicineIsInHand(map, doctor, units);
            IntVec3 where = doctor.Position;

            Interrupt(doctor, how);

            Assert.NotEqual(JobDefOf.TendPatient, doctor.jobs.curJob?.def);
            Assert.Null(doctor.carryTracker.CarriedThing);
            Assert.Equal(units, MedicineOnMap(map));
            Assert.Contains(map.thingGrid.ThingsListAt(where), t => t.def == Herbal && t.stackCount >= JobDriver_TendPatient.MedicinePerTend);
        }

        // ---- the ordinary case, and the leak ----

        /// <summary>An uninterrupted tend still stops the bleeding, spends exactly one unit and leaves the
        /// doctor empty-handed.</summary>
        [Fact]
        public void An_uninterrupted_tend_spends_exactly_one_unit_and_leaves_the_doctor_empty_handed()
        {
            (CoreMap map, Pawn doctor, Pawn patient, Thing medicine, Job job) = TendSite(5);
            doctor.jobs.StartJob(job);

            RunTicks(3000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
            Assert.Equal(5 - JobDriver_TendPatient.MedicinePerTend, MedicineOnMap(map));
            Assert.Equal(4, medicine.stackCount);
            Assert.Null(doctor.carryTracker.CarriedThing);
        }

        /// <summary>A lone unit is consumed entirely when the tend lands, not left at zero or put back.</summary>
        [Fact]
        public void An_uninterrupted_tend_of_a_lone_unit_consumes_it()
        {
            (CoreMap map, Pawn doctor, Pawn patient, Thing medicine, Job job) = TendSite(1);
            doctor.jobs.StartJob(job);

            RunTicks(3000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
            Assert.True(medicine.Destroyed);
            Assert.Equal(0, MedicineOnMap(map));
            Assert.Null(doctor.carryTracker.CarriedThing);
        }

        /// <summary>
        /// The leak the driver's own field had: the doctor arrives and there is nothing to tend (the patient
        /// healed, or somebody else got there first). A tend that treats nothing spends no medicine, so the unit
        /// must be somewhere afterwards — back on the ground — not in a field that was already forgotten.
        /// </summary>
        [Theory]
        [InlineData(5)]
        [InlineData(1)]
        public void Medicine_carried_to_a_patient_with_nothing_to_tend_is_put_down_not_lost(int units)
        {
            (CoreMap map, Pawn doctor, Pawn patient, _, Job job) = TendSite(units, wounded: false);
            doctor.jobs.StartJob(job);

            RunTicks(3000, doctor, patient);

            Assert.NotEqual(JobDefOf.TendPatient, doctor.jobs.curJob?.def);
            Assert.Null(doctor.carryTracker.CarriedThing);
            Assert.Equal(units, MedicineOnMap(map));
        }

        // ---- Scribe ----

        /// <summary>
        /// A save taken mid-carry keeps the medicine: it comes back in the loaded doctor's hands, the loaded
        /// job's medicine target is that very Thing, and the doctor goes on to tend with it. The unit is spent
        /// once.
        /// </summary>
        [Theory]
        [InlineData(5)]
        [InlineData(1)]
        public void A_doctor_saved_mid_carry_still_holds_the_medicine_after_loading_and_tends_with_it(int units)
        {
            (CoreMap map, Pawn doctor, Pawn patient, _, Job job) = TendSite(units);
            doctor.jobs.StartJob(job);
            RunUntilMedicineIsInHand(map, doctor, units);
            Assert.Equal(JobDriver_TendPatient.MedicinePerTend, doctor.carryTracker.CarriedThing!.stackCount);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedDoctor = loaded.mapPawns.AllPawns.Single(p => p.thingIDNumber == doctor.thingIDNumber);
            Pawn loadedPatient = loaded.mapPawns.AllPawns.Single(p => p.thingIDNumber == patient.thingIDNumber);
            Thing? held = loadedDoctor.carryTracker.CarriedThing;
            Assert.NotNull(held);
            Assert.Same(Herbal, held!.def);
            Assert.Equal(JobDriver_TendPatient.MedicinePerTend, held.stackCount);
            Assert.False(held.Spawned);
            Assert.Equal(JobDefOf.TendPatient, loadedDoctor.jobs.curJob?.def);
            Assert.Same(held, loadedDoctor.jobs.curJob!.GetTarget(TargetIndex.B).Thing);
            Assert.Equal(units, MedicineOnMap(loaded) + held.stackCount);

            RunTicks(3000, loadedDoctor, loadedPatient);

            Assert.Equal(0f, loadedPatient.health.hediffSet.BleedRateTotal);
            Assert.Equal(units - JobDriver_TendPatient.MedicinePerTend, MedicineOnMap(loaded));
            Assert.Null(loadedDoctor.carryTracker.CarriedThing);
        }
    }
}
