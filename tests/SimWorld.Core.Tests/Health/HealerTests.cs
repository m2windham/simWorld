using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
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
    /// The settlement lets its best healers doctor (<see cref="HealerUtility"/>): routine tending goes to the
    /// top Medicine skills, anyone may still give first aid, and the work falls back to whoever stands when the
    /// healers are down. Behaviour, not the <see cref="HealerUtility.CitizensPerHealer"/> literal.
    /// </summary>
    public class HealerTests : ContentTestBase
    {
        public HealerTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static WorkGiver_Tend Routine => new() { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
        private static WorkGiver_Tend Emergency => new() { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendEmergency") };

        private static Faction NewFaction()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Colony", "F_Colony");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn Citizen(CoreMap map, Faction f, IntVec3 cell, int medicine, string name)
        {
            Pawn p = NewHuman(name);
            p.skills.GetSkill(SkillDefOf.Medicine)!.Level = medicine;
            GenSpawn.Spawn(p, cell, map);
            p.faction = f;
            return p;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void Bruise(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Blunt").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Blunt"), 3f, hitPart: Part(p, "left foot")), p);

        private static void BleedUrgently(Pawn p)
        {
            string[] parts = { "left arm", "right arm", "left leg", "right leg", "torso" };
            for (int i = 0; i < 20 && !HealthAIUtility.ShouldBeTendedNowUrgent(p); i++)
            {
                DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), 6f, hitPart: Part(p, parts[i % parts.Length])), p);
            }
            Assert.True(HealthAIUtility.ShouldBeTendedNowUrgent(p));
        }

        /// <summary>A small band: one skilled healer, one novice, one routine patient.</summary>
        private static (Pawn healer, Pawn novice, Pawn patient) Band()
        {
            var map = new CoreMap(12, 12, TerrainDefOf.Soil);
            Faction f = NewFaction();
            Pawn novice = Citizen(map, f, new IntVec3(1, 0, 1), 1, "Novice");
            Pawn healer = Citizen(map, f, new IntVec3(10, 0, 10), 8, "Healer");
            Pawn patient = Citizen(map, f, new IntVec3(2, 0, 2), 3, "Patient");
            Bruise(patient);
            Assert.False(TendUtility.NeedsEmergencyTend(patient));
            return (healer, novice, patient);
        }

        [Fact]
        public void The_best_doctor_takes_routine_tending_and_a_novice_leaves_it_to_them()
        {
            (Pawn healer, Pawn novice, Pawn patient) = Band();

            Assert.True(HealerUtility.IsHealer(healer, patient));
            Assert.False(HealerUtility.IsHealer(novice, patient));
            Assert.True(Routine.HasJobOnThing(healer, patient));
            Assert.False(Routine.HasJobOnThing(novice, patient));
        }

        [Fact]
        public void With_the_healer_downed_the_novice_tends()
        {
            (Pawn healer, Pawn novice, Pawn patient) = Band();
            healer.health.ForceDowned = true;

            Assert.True(HealerUtility.IsHealer(novice, patient));
            Assert.True(Routine.HasJobOnThing(novice, patient));
        }

        [Fact]
        public void A_healer_who_is_the_patient_hands_the_work_to_the_next_best()
        {
            (Pawn healer, Pawn novice, Pawn nextBest) = Band();
            Bruise(healer);

            Assert.True(HealerUtility.IsHealer(nextBest, healer));
            Assert.True(Routine.HasJobOnThing(nextBest, healer));
            Assert.False(Routine.HasJobOnThing(novice, healer));
        }

        [Fact]
        public void First_aid_and_a_direct_order_stay_open_to_anyone()
        {
            (Pawn _, Pawn novice, Pawn patient) = Band();
            Assert.True(Routine.HasJobOnThing(novice, patient, forced: true));

            BleedUrgently(patient);
            Assert.True(Emergency.HasJobOnThing(novice, patient));
        }

        [Fact]
        public void A_town_keeps_more_healers_than_a_band_and_never_none()
        {
            Assert.Equal(0, HealerUtility.HealerCount(0));
            Assert.Equal(1, HealerUtility.HealerCount(1));
            int band = HealerUtility.HealerCount(HealerUtility.CitizensPerHealer * 3);
            int town = HealerUtility.HealerCount(HealerUtility.CitizensPerHealer * 10);
            Assert.True(band > 1 && town > band);
            Assert.True(town < HealerUtility.CitizensPerHealer * 10);
        }

        [Fact]
        public void Healers_are_the_top_skills_whatever_the_spawn_order()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            Faction f = NewFaction();
            int[] levels = { 2, 9, 0, 4, 7, 1, 3, 5, 6, 2, 1, 8 };
            var all = new List<Pawn>();
            for (int i = 0; i < levels.Length; i++) all.Add(Citizen(map, f, new IntVec3(i, 0, 1), levels[i], "C" + i));
            Assert.Equal(levels.Length, map.mapPawns.AllPawnsSpawned.Count);

            int k = HealerUtility.HealerCount(levels.Length);
            var healers = all.FindAll(p => HealerUtility.IsHealer(p));
            Assert.Equal(k, healers.Count);
            int worstHealer = int.MaxValue;
            foreach (Pawn h in healers) worstHealer = System.Math.Min(worstHealer, h.skills.GetSkill(SkillDefOf.Medicine)!.Level);
            foreach (Pawn p in all)
            {
                if (!healers.Contains(p)) Assert.True(p.skills.GetSkill(SkillDefOf.Medicine)!.Level <= worstHealer);
            }
        }

        [Fact]
        public void A_landed_tend_teaches_medicine_and_more_with_medicine_than_without()
        {
            (Pawn healer, Pawn _, Pawn patient) = Band();
            SkillRecord skill = healer.skills.GetSkill(SkillDefOf.Medicine)!;
            float before = skill.XpTotalEarned;

            healer.jobs.StartJob(new Job(JobDefOf.TendPatient, patient));
            RunTicks(1500, healer, patient);

            Assert.False(TendUtility.HasAnythingToTend(patient));
            Assert.True(skill.XpTotalEarned > before);
            Assert.True(JobDriver_TendPatient.TendXp(patient, withMedicine: true) > JobDriver_TendPatient.TendXp(patient, withMedicine: false));
            Assert.True(JobDriver_TendPatient.TendXp(patient, withMedicine: false) > 0f);
        }

        [Fact]
        public void Doctors_share_a_stack_of_medicine_a_unit_each_until_it_runs_out()
        {
            (Pawn healer, Pawn novice, Pawn patient) = Band();
            CoreMap map = healer.Map!;
            Thing herbs = ThingMaker.MakeThing(MedicineDefOf.MedicineHerbal);
            herbs.stackCount = 2;
            GenSpawn.Spawn(herbs, new IntVec3(5, 0, 5), map);
            Pawn third = Citizen(map, healer.faction!, new IntVec3(6, 0, 6), 5, "Third");

            foreach (Pawn doctor in new[] { healer, novice })
            {
                Assert.Same(herbs, MedicineUtility.FindBestMedicine(doctor, patient));
                Assert.True(map.reservationManager.Reserve(doctor, herbs, JobDriver_TendPatient.MedicineMaxClaimants, JobDriver_TendPatient.MedicinePerTend));
            }
            Assert.Null(MedicineUtility.FindBestMedicine(third, patient));
        }

        [Fact]
        public void Who_heals_survives_a_save_and_load()
        {
            (Pawn healer, Pawn novice, Pawn patient) = Band();
            CoreMap map = healer.Map!;
            Faction faction = healer.faction!;
            foreach (Pawn p in new[] { healer, novice, patient }) p.faction = null;

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn L(string name) => System.Linq.Enumerable.First(loaded.mapPawns.AllPawns, p => p.Label == name);
            Pawn lHealer = L("Healer"), lNovice = L("Novice"), lPatient = L("Patient");
            foreach (Pawn p in new[] { lHealer, lNovice, lPatient }) p.faction = faction;

            Assert.Equal(8, lHealer.skills.GetSkill(SkillDefOf.Medicine)!.Level);
            Assert.True(HealerUtility.IsHealer(lHealer, lPatient));
            Assert.False(HealerUtility.IsHealer(lNovice, lPatient));
            Assert.True(Routine.HasJobOnThing(lHealer, lPatient));
            Assert.False(Routine.HasJobOnThing(lNovice, lPatient));
        }
    }
}
