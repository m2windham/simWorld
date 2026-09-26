using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Task #104, parts 2 and 3: tend quality now spends real medicine
    /// (<see cref="TendUtility.CalculateBaseTendQuality"/>: doctor skill x medicine potency + bed offset,
    /// capped at the medicine's own quality ceiling), carried in by <see cref="JobDriver_TendPatient"/> and
    /// found by <see cref="WorkGiver_Tend"/> through <see cref="MedicineUtility.FindBestMedicine"/>, gated by
    /// the settlement's own <see cref="MedicalCareCategory"/> (<see cref="Map.Map.medicalCare"/>).
    /// </summary>
    public class MedicineTests : ContentTestBase
    {
        public MedicineTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static and ContentTestBase does not reset it — see DoctorAITests.
            Find.FactionManager = new FactionManager();
        }

        private static ThingDef MedicineHerbal => DefDatabase<ThingDef>.GetNamed("MedicineHerbal");
        private static ThingDef MedicineIndustrial => DefDatabase<ThingDef>.GetNamed("MedicineIndustrial");
        private static ThingDef BedDef => DefDatabase<ThingDef>.GetNamed("Bed");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

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

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, ThingDef def, int count)
        {
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void MakeBleedingWound(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), 6f, hitPart: Part(p, "left arm")), p);

        // -------------------------------------------------------------------------------------------
        // CalculateBaseTendQuality: none / herbal / industrial, and the bed offset, as bands
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Content_ships_RimWorlds_own_medicine_numbers()
        {
            Assert.Empty(Content.Result.Errors);
            // Wiki-sourced (rimworldwiki.com/wiki/Herbal_medicine, rimworldwiki.com/wiki/Medicine).
            Assert.Equal(0.6f, MedicineHerbal.GetStatValue(HealthStatDefOf.MedicalPotency), 3);
            Assert.Equal(0.7f, MedicineHerbal.GetStatValue(HealthStatDefOf.MedicalQualityMax), 3);
            Assert.Equal(1.0f, MedicineIndustrial.GetStatValue(HealthStatDefOf.MedicalPotency), 3);
            Assert.Equal(1.0f, MedicineIndustrial.GetStatValue(HealthStatDefOf.MedicalQualityMax), 3);
            Assert.True(MedicineIndustrial.IsMedicine);
            Assert.False(DefDatabase<ThingDef>.GetNamed("RawPotatoes").IsMedicine);
        }

        [Fact]
        public void Tend_quality_bands_none_below_herbal_below_industrial()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), f, "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(2, 0, 2), f, "Patient");

            float none = TendUtility.CalculateBaseTendQuality(doctor, patient, medicineDef: null);
            float herbal = TendUtility.CalculateBaseTendQuality(doctor, patient, MedicineHerbal);
            float industrial = TendUtility.CalculateBaseTendQuality(doctor, patient, MedicineIndustrial);

            Assert.True(none < herbal, $"none ({none}) should be worse than herbal ({herbal})");
            Assert.True(herbal < industrial, $"herbal ({herbal}) should be worse than industrial ({industrial})");

            // Each stays under its own medicine's ceiling (RimWorld: CalculateBaseTendQuality's own clamp).
            Assert.True(none <= TendUtility.MaxQualityNoMedicine + 1e-4f);
            Assert.True(herbal <= MedicineHerbal.GetStatValue(HealthStatDefOf.MedicalQualityMax) + 1e-4f);
            Assert.True(industrial <= MedicineIndustrial.GetStatValue(HealthStatDefOf.MedicalQualityMax) + 1e-4f);
        }

        /// <summary>
        /// This port's one shipped <c>Bed</c> carries no <c>MedicalTendQualityOffset</c> — RimWorld's own
        /// regular bed carries none either (Buildings_Structural.xml's own comment); only a hospital bed,
        /// not shipped here, is ever above zero. So the offset's wiring is proved by mutating the real,
        /// shared <c>Bed</c> def's <c>statBases</c> just long enough to see the effect, then restoring it —
        /// <c>DefDatabase.Global</c>'s <c>Bed</c> is shared by the whole "GlobalDefs" collection, so a leaked
        /// mutation would corrupt every other test in it. Guarded both ways per CLAUDE.md: the mutation is
        /// asserted to have actually changed the read value, and the restore is asserted to put it back to
        /// the exact original list reference and count.
        /// </summary>
        [Fact]
        public void A_beds_offset_raises_tend_quality_over_the_same_tend_on_the_ground()
        {
            CoreMap map = NewMap(6, 6);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), f, "Doctor");
            Pawn onGround = SpawnHuman(map, new IntVec3(2, 0, 2), f, "OnGround");

            var bedCell = new IntVec3(4, 0, 4);
            GenSpawn.Spawn(ThingMaker.MakeThing(BedDef), bedCell, map);
            Pawn inBed = SpawnHuman(map, bedCell, f, "InBed");

            float qualityBeforeMutation = TendUtility.CalculateBaseTendQuality(doctor, inBed, MedicineIndustrial);
            Assert.Equal(0f, BedDef.GetStatValue(HealthStatDefOf.MedicalTendQualityOffset), 4);

            System.Collections.Generic.List<StatModifier> originalStatBases = BedDef.statBases!;
            int originalCount = originalStatBases.Count;
            var mutatedStatBases = new System.Collections.Generic.List<StatModifier>(originalStatBases)
            {
                new StatModifier(HealthStatDefOf.MedicalTendQualityOffset, 0.15f),
            };
            BedDef.statBases = mutatedStatBases;
            try
            {
                // Guard 1: the mutation actually changed what GetStatValue reads — never pass quietly.
                float mutatedOffset = BedDef.GetStatValue(HealthStatDefOf.MedicalTendQualityOffset);
                Assert.Equal(0.15f, mutatedOffset, 4);
                Assert.NotEqual(0f, mutatedOffset);

                float groundQuality = TendUtility.CalculateBaseTendQuality(doctor, onGround, MedicineIndustrial);
                float bedQuality = TendUtility.CalculateBaseTendQuality(doctor, inBed, MedicineIndustrial);
                Assert.True(bedQuality > groundQuality, $"bed ({bedQuality}) should beat ground ({groundQuality})");
                Assert.Equal(qualityBeforeMutation + 0.15f, bedQuality, 4);
            }
            finally
            {
                BedDef.statBases = originalStatBases;
            }

            // Guard 2: the restore is exact — same reference, same count, same reading as before the mutation.
            Assert.Same(originalStatBases, BedDef.statBases);
            Assert.Equal(originalCount, BedDef.statBases!.Count);
            Assert.Equal(0f, BedDef.GetStatValue(HealthStatDefOf.MedicalTendQualityOffset), 4);
            Assert.Equal(qualityBeforeMutation, TendUtility.CalculateBaseTendQuality(doctor, inBed, MedicineIndustrial), 4);
        }

        // -------------------------------------------------------------------------------------------
        // The tend job fetches and consumes one medicine
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_tend_job_carries_medicine_as_its_second_target_and_spends_exactly_one_unit()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), f, "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(4, 0, 4), f, "Patient");
            MakeBleedingWound(patient);
            Thing medicine = SpawnStack(map, new IntVec3(1, 0, 1), MedicineHerbal, count: 5);

            var giver = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Job? job = giver.JobOnThing(doctor, patient);
            Assert.NotNull(job);
            Assert.Same(medicine, job!.GetTarget(TargetIndex.B).Thing);

            doctor.jobs.StartJob(job);
            RunTicks(3000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
            Assert.Equal(4, medicine.stackCount);
        }

        [Fact]
        public void A_single_unit_stack_is_consumed_entirely_not_left_at_zero()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), f, "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(4, 0, 4), f, "Patient");
            MakeBleedingWound(patient);
            Thing medicine = SpawnStack(map, new IntVec3(1, 0, 1), MedicineHerbal, count: 1);

            var giver = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Job? job = giver.JobOnThing(doctor, patient);
            doctor.jobs.StartJob(job!);
            RunTicks(3000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
            Assert.True(medicine.Destroyed, "one unit spent from a one-unit stack should leave nothing behind");
        }

        // -------------------------------------------------------------------------------------------
        // A patient with no medicine available is still tended (medicine-less tend)
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void No_medicine_anywhere_still_tends_at_the_no_medicine_ceiling()
        {
            CoreMap map = NewMap(8, 8);
            Faction f = NewFaction("Colony");
            Pawn doctor = SpawnHuman(map, new IntVec3(0, 0, 0), f, "Doctor");
            Pawn patient = SpawnHuman(map, new IntVec3(4, 0, 4), f, "Patient");
            MakeBleedingWound(patient);

            Assert.Null(MedicineUtility.FindBestMedicine(doctor, patient));

            var giver = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Job? job = giver.JobOnThing(doctor, patient);
            Assert.NotNull(job);
            Assert.False(job!.GetTarget(TargetIndex.B).HasThing, "nothing to carry when nothing qualifies");

            doctor.jobs.StartJob(job);
            RunTicks(3000, doctor, patient);

            Assert.Equal(0f, patient.health.hediffSet.BleedRateTotal);
        }
    }
}
