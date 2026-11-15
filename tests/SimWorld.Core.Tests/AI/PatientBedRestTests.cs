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
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A sick or wounded citizen goes to bed and stays there (RimWorld's Patient and Bed rest work types).
    ///
    /// <para/>Found tracing wound infections on a threat-free founding band
    /// (<c>docs/perf/storyteller-pressure</c>): the infected went on mining and sowing until the infection
    /// downed them, about a third of the illness in bed, where RimWorld's colonists spend nearly all of it
    /// gaining immunity 18% faster.
    /// </summary>
    public class PatientBedRestTests : ContentTestBase
    {
        public PatientBedRestTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap() => new CoreMap(16, 16, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction Settlers()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnCitizen(CoreMap map, IntVec3 cell, Faction faction, string name)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnBed(CoreMap map, IntVec3 cell)
        {
            Thing bed = ThingMaker.MakeThing(Def("Bed"), Def("WoodLog"));
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        private static Hediff Infect(Pawn p)
        {
            Hediff infection = p.health.AddHediff(HediffDefOf.WoundInfection, p.RaceProps.body!.GetPartByLabel("left arm"));
            infection.Severity = 0.1f;
            return infection;
        }

        private static bool IsPatientLieDown(Job? job) => job != null && job.def == JobDefOf.LayDown && job.restUntilHealed;

        [Fact]
        public void A_healthy_citizen_does_not_seek_medical_rest()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(3, 0, 3), Settlers(), "Well");
            SpawnBed(map, new IntVec3(8, 0, 8));

            Assert.False(HealthAIUtility.ShouldSeekMedicalRest(citizen));
            citizen.jobs.TryFindAndStartJob();
            Assert.False(IsPatientLieDown(citizen.jobs.curJob));
        }

        [Fact]
        public void An_infected_citizen_takes_itself_to_bed_and_gains_immunity_faster_there()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(3, 0, 3), Settlers(), "Sick");
            Thing bed = SpawnBed(map, new IntVec3(8, 0, 8));
            Infect(citizen);
            float upAndAbout = citizen.GetStatValue(StatDefOf.ImmunityGainSpeed);

            Assert.True(HealthAIUtility.ShouldSeekMedicalRest(citizen));
            citizen.jobs.TryFindAndStartJob();
            Assert.True(IsPatientLieDown(citizen.jobs.curJob), "an infected citizen's next job is to go to bed");

            RunTicks(600, citizen);

            Assert.Same(bed, citizen.CurrentBed());
            Assert.True(IsPatientLieDown(citizen.jobs.curJob));
            Assert.True(citizen.GetStatValue(StatDefOf.ImmunityGainSpeed) > upAndAbout);
        }

        [Fact]
        public void A_patient_stays_in_bed_though_fully_rested_and_gets_up_once_the_reason_is_gone()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(7, 0, 8), Settlers(), "Sick");
            Thing bed = SpawnBed(map, new IntVec3(8, 0, 8));
            Hediff infection = Infect(citizen);
            citizen.jobs.TryFindAndStartJob();
            RunTicks(300, citizen);
            Assert.Same(bed, citizen.CurrentBed());

            citizen.needs.rest!.CurLevel = 1f;
            RunTicks(JobDriver_LayDown.LookForOtherJobsIntervalTicks * 3, citizen);
            Assert.True(IsPatientLieDown(citizen.jobs.curJob), "being rested is not a reason for a patient to get up");

            citizen.health.RemoveHediff(infection);
            citizen.needs.rest!.CurLevel = 1f;
            Assert.False(HealthAIUtility.ShouldSeekMedicalRest(citizen));
            RunTicks(JobDriver_LayDown.LookForOtherJobsIntervalTicks * 3, citizen);

            Assert.False(IsPatientLieDown(citizen.jobs.curJob));
        }

        [Fact]
        public void With_no_bed_on_the_map_nobody_is_sent_to_one()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(3, 0, 3), Settlers(), "Sick");
            Infect(citizen);

            citizen.jobs.TryFindAndStartJob();

            Assert.False(IsPatientLieDown(citizen.jobs.curJob));
        }

        [Fact]
        public void A_hungry_patient_gets_up_to_eat()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(7, 0, 8), Settlers(), "Sick");
            SpawnBed(map, new IntVec3(8, 0, 8));
            Infect(citizen);
            citizen.jobs.TryFindAndStartJob();
            RunTicks(300, citizen);
            Assert.True(IsPatientLieDown(citizen.jobs.curJob));

            Thing meal = ThingMaker.MakeThing(Def("MealSimple"));
            GenSpawn.Spawn(meal, new IntVec3(3, 0, 3), map);
            citizen.needs.food!.CurLevel = 0.05f;
            bool ate = false;
            for (int i = 0; i < JobDriver_LayDown.LookForOtherJobsIntervalTicks * 2 && !ate; i++)
            {
                RunTicks(1, citizen);
                ate = citizen.jobs.curJob?.def == JobDefOf.Ingest;
            }

            Assert.True(ate, "a patient who can walk feeds itself");
        }
    }
}
