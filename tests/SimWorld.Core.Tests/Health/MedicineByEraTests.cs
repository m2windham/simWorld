using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// Medicine by era (health.medicalresearch): the medical projects the arc researches now change what a
    /// settlement builds and how well it tends. Classical <c>Medicine</c> brings hospital beds, Medieval
    /// <c>Hospitals</c> more of them and <c>Pharmacology</c> the apothecary's bench and its medicine.
    /// </summary>
    public class MedicineByEraTests : ContentTestBase
    {
        public MedicineByEraTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 24) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static void Finish(string project) =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(project));

        private static Thing SpawnBuilding(CoreMap map, ThingDef def, IntVec3 cell)
        {
            Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static (Settlement, CoreMap) Settled(int citizens, int size = 24)
        {
            CoreMap map = NewMap(size);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Ward", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = NewHuman("C" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(1 + i % 10, 0, 20 + i / 10), map);
            }
            return (settlement, map);
        }

        private static int PlannedOrBuilt(CoreMap map, ThingDef def) => SettlementWorksInitiative.CountBuiltOrPlanned(map, def);

        // ---- what the era wants ----

        [Fact]
        public void Hospital_beds_wait_for_Medicine_and_Hospitals_wants_more_of_them()
        {
            Assert.Equal(0, MedicalBeds.HospitalBedsWanted(20));

            Finish("Medicine");
            int sickHouse = MedicalBeds.HospitalBedsWanted(20);
            Assert.True(sickHouse >= 1);
            Assert.Equal(1, MedicalBeds.HospitalBedsWanted(1)); // any people at all: at least one bed

            Finish("Hospitals");
            int hospital = MedicalBeds.HospitalBedsWanted(20);
            Assert.True(hospital > sickHouse, "a hospital wants more beds than a sick-house: " + hospital + " vs " + sickHouse);

            for (int n = 1; n < 60; n++)
            {
                Assert.True(MedicalBeds.HospitalBedsWanted(n + 1) >= MedicalBeds.HospitalBedsWanted(n), "never fewer beds for more people");
            }
            Assert.Equal(0, MedicalBeds.HospitalBedsWanted(0));
        }

        // ---- what the bed does ----

        [Fact]
        public void A_patient_in_a_hospital_bed_is_tended_better_and_gains_immunity_faster_than_in_a_bed()
        {
            CoreMap map = NewMap();
            Thing hospital = SpawnBuilding(map, MedicalEraDefOf.HospitalBed, new IntVec3(4, 0, 4));
            Thing bed = SpawnBuilding(map, ConstructionThingDefOf.Bed, new IntVec3(10, 0, 4));
            Pawn doctor = NewHuman("Doctor");
            doctor.skills.GetSkill(SkillDefOf.Medicine)!.Level = 6;

            Pawn inHospital = NewHuman("A");
            GenSpawn.Spawn(inHospital, hospital.GetSleepingSlotPos(), map);
            Pawn inBed = NewHuman("B");
            GenSpawn.Spawn(inBed, bed.GetSleepingSlotPos(), map);

            Assert.Same(hospital, inHospital.CurrentBed());
            Assert.True(inHospital.InBed());

            ThingDef herbal = MedicineDefOf.MedicineHerbal;
            float hospitalQuality = TendUtility.CalculateBaseTendQuality(doctor, inHospital, herbal);
            float bedQuality = TendUtility.CalculateBaseTendQuality(doctor, inBed, herbal);
            Assert.True(hospitalQuality > bedQuality, "hospital " + hospitalQuality + " vs bed " + bedQuality);

            float hospitalRest = hospital.GetStatValue(HealthStatDefOf.BedRestEffectiveness);
            float bedRest = bed.GetStatValue(HealthStatDefOf.BedRestEffectiveness);
            Assert.True(hospitalRest > bedRest);
        }

        [Fact]
        public void A_patient_takes_a_free_hospital_bed_before_a_nearer_ordinary_one_and_nobody_sleeps_the_night_in_it()
        {
            CoreMap map = NewMap();
            Pawn patient = NewHuman("Patient");
            GenSpawn.Spawn(patient, new IntVec3(2, 0, 2), map);
            Thing nearBed = SpawnBuilding(map, ConstructionThingDefOf.Bed, new IntVec3(3, 0, 4));
            Thing farHospital = SpawnBuilding(map, MedicalEraDefOf.HospitalBed, new IntVec3(18, 0, 18));

            WorkGiverDef def = DefDatabase<WorkGiverDef>.GetNamed("PatientGoToBedRecuperate");
            var giver = (WorkGiver_PatientGoToBed)def.Worker;
            Thing[] offered = giver.PotentialWorkThingsGlobal(patient).ToArray();
            Assert.Equal(new[] { farHospital }, offered);
            Assert.True(giver.HasJobOnThing(patient, farHospital));

            // A sleeper looking for the night's bed is offered the ordinary bed, never the hospital bed.
            Assert.Same(nearBed, RestUtility.FindBedFor(patient));
        }

        // ---- what the settlement builds ----

        [Fact]
        public void Once_Medicine_is_known_the_settlement_lays_a_hospital_bed_in_a_free_dormitory_slot()
        {
            (Settlement settlement, CoreMap map) = Settled(6);
            SettlementCompound compound = SettlementCompound.WithInterior(new CellRect(4, 4, 6, 4));
            map.settlementCompound = compound;

            MedicalInitiative.Run(settlement, map);
            Assert.Equal(0, PlannedOrBuilt(map, MedicalEraDefOf.HospitalBed));

            Finish("Medicine");
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(1, PlannedOrBuilt(map, MedicalEraDefOf.HospitalBed));
            Blueprint bp = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().Single();
            Assert.Contains(bp.Position, compound.BedCells);

            // Wanted is met: another pass adds nothing.
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(1, PlannedOrBuilt(map, MedicalEraDefOf.HospitalBed));
        }

        [Fact]
        public void A_full_dormitory_gives_up_a_cot_nobody_needs_for_the_hospital_bed_but_never_one_it_needs()
        {
            (Settlement settlement, CoreMap map) = Settled(2);
            SettlementCompound compound = SettlementCompound.WithInterior(new CellRect(4, 4, 2, 2)); // one row: two cot slots
            map.settlementCompound = compound;
            foreach (IntVec3 c in compound.BedCells) SpawnBuilding(map, ConstructionThingDefOf.Bed, c);
            Finish("Medicine");

            // Two cots, two people sleeping in the dormitory: neither cot is spare.
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(0, PlannedOrBuilt(map, MedicalEraDefOf.HospitalBed));
            Assert.Equal(2, map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count);

            // One of them dies: a cot is spare, and the hospital bed takes its slot.
            settlement.Citizens[1].health.Kill(null, null);
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(1, PlannedOrBuilt(map, MedicalEraDefOf.HospitalBed));
            Assert.Single(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed));
        }

        [Fact]
        public void Pharmacology_brings_the_apothecarys_bench_and_a_medicine_bill_sized_by_the_citizens()
        {
            (Settlement settlement, CoreMap map) = Settled(5);
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(0, PlannedOrBuilt(map, MedicalEraDefOf.DrugLab));

            Finish("Pharmacology");
            MedicalInitiative.Run(settlement, map);
            Assert.Equal(1, PlannedOrBuilt(map, MedicalEraDefOf.DrugLab));

            var lab = (ThingWithComps)SpawnBuilding(map, MedicalEraDefOf.DrugLab, new IntVec3(12, 0, 12));
            MedicalInitiative.Run(settlement, map);
            Bill_Production bill = lab.GetComp<CompBillGiver>()!.BillStack.Bills.OfType<Bill_Production>().Single();
            Assert.Same(MedicalEraDefOf.Make_MedicineIndustrial, bill.recipe);
            Assert.Equal(BillRepeatMode.TargetCount, bill.repeatMode);
            Assert.Equal(5, bill.targetCount);
            Assert.True(bill.unpauseWhenYouHave < bill.targetCount);
        }

        [Fact]
        public void The_medicine_bill_waits_for_Pharmacology_even_at_a_standing_bench()
        {
            (Settlement settlement, CoreMap map) = Settled(3);
            var lab = (ThingWithComps)SpawnBuilding(map, MedicalEraDefOf.DrugLab, new IntVec3(12, 0, 12));
            MedicalInitiative.Run(settlement, map);
            Assert.Empty(lab.GetComp<CompBillGiver>()!.BillStack.Bills);
        }

        [Fact]
        public void Industrial_medicine_is_what_a_doctor_reaches_for_once_there_is_any()
        {
            CoreMap map = NewMap();
            Pawn doctor = NewHuman("Doctor");
            GenSpawn.Spawn(doctor, new IntVec3(2, 0, 2), map);
            Pawn patient = NewHuman("Patient");
            GenSpawn.Spawn(patient, new IntVec3(3, 0, 2), map);
            Thing herbal = ThingMaker.MakeThing(MedicineDefOf.MedicineHerbal);
            herbal.stackCount = 5;
            GenSpawn.Spawn(herbal, new IntVec3(4, 0, 4), map);
            Assert.Same(herbal, MedicineUtility.FindBestMedicine(doctor, patient));

            Thing made = ThingMaker.MakeThing(MedicineDefOf.MedicineIndustrial);
            GenSpawn.Spawn(made, new IntVec3(10, 0, 10), map);
            Assert.Same(made, MedicineUtility.FindBestMedicine(doctor, patient));
            Assert.True(TendUtility.CalculateBaseTendQuality(doctor, patient, made.def)
                > TendUtility.CalculateBaseTendQuality(doctor, patient, herbal.def));
        }

        [Fact]
        public void Herbal_medicine_near_the_bench_becomes_medicine_by_a_citizens_hand()
        {
            (Settlement settlement, CoreMap map) = Settled(1);
            Finish("Pharmacology");
            SpawnBuilding(map, MedicalEraDefOf.DrugLab, new IntVec3(8, 0, 8));
            Thing herbal = ThingMaker.MakeThing(MedicineDefOf.MedicineHerbal);
            herbal.stackCount = 4;
            GenSpawn.Spawn(herbal, new IntVec3(6, 0, 8), map);
            Pawn crafter = settlement.Citizens[0];
            crafter.workSettings.DisableAll();
            crafter.workSettings.SetPriority(WorkTypeDefOf.Crafting, 3);

            MedicalInitiative.Run(settlement, map);
            RunTicks(6000, crafter);

            Assert.True(map.listerThings.ThingsOfDef(MedicineDefOf.MedicineIndustrial).Sum(t => t.stackCount) >= 1,
                "no medicine was made; job: " + crafter.jobs.curJob?.def.defName);
        }

        [Fact]
        public void One_doctor_at_a_stack_does_not_hide_the_rest_of_it_from_another()
        {
            CoreMap map = NewMap();
            Pawn first = NewHuman("First");
            GenSpawn.Spawn(first, new IntVec3(2, 0, 2), map);
            Pawn second = NewHuman("Second");
            GenSpawn.Spawn(second, new IntVec3(2, 0, 3), map);
            Pawn patient = NewHuman("Patient");
            GenSpawn.Spawn(patient, new IntVec3(3, 0, 2), map);
            Thing herbal = ThingMaker.MakeThing(MedicineDefOf.MedicineHerbal);
            herbal.stackCount = 5;
            GenSpawn.Spawn(herbal, new IntVec3(10, 0, 10), map);

            // What the tend driver itself claims on the stack while one doctor walks to it.
            Assert.Same(herbal, MedicineUtility.FindBestMedicine(first, patient));
            Assert.True(map.reservationManager.Reserve(first, herbal, MedicineUtility.MaxClaimants, MedicineUtility.PerClaim));
            Assert.Same(herbal, MedicineUtility.FindBestMedicine(second, patient));
        }

        // ---- save and load ----

        [Fact]
        public void A_saved_ward_and_medicine_bill_load_back_and_the_next_pass_adds_nothing()
        {
            (Settlement settlement, CoreMap map) = Settled(4);
            map.settlementCompound = SettlementCompound.WithInterior(new CellRect(4, 4, 6, 4));
            Finish("Medicine");
            Finish("Pharmacology");
            SpawnBuilding(map, MedicalEraDefOf.HospitalBed, map.settlementCompound.BedCells.First());
            SpawnBuilding(map, MedicalEraDefOf.DrugLab, new IntVec3(16, 0, 16));
            MedicalInitiative.Run(settlement, map);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Single(loaded.listerThings.ThingsOfDef(MedicalEraDefOf.HospitalBed));
            var lab = (ThingWithComps)loaded.listerThings.ThingsOfDef(MedicalEraDefOf.DrugLab).Single();
            Bill_Production bill = lab.GetComp<CompBillGiver>()!.BillStack.Bills.OfType<Bill_Production>().Single();
            Assert.Equal(4, bill.targetCount);
        }
    }
}
