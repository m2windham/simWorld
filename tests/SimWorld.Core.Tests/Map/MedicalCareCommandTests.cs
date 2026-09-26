using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// Task #104's player lever: <see cref="MapCommands.SetMedicalCare"/>, a settlement-wide
    /// <see cref="MedicalCareCategory"/> — RimWorld's own per-pawn dropdown, translated to one value per
    /// settlement (see <see cref="MedicalCareCategory"/>'s own doc for why). The lever test
    /// (CLAUDE.md/<c>docs/design/player-first.md</c>) is proved here rather than assumed: setting
    /// <see cref="MedicalCareCategory.NoCare"/> is not a flag <see cref="AI.WorkGiver_Tend"/> merely carries —
    /// it is the same field the command writes, read fresh by the same job-giving machinery every other tend
    /// decision goes through.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MedicalCareCommandTests : ContentTestBase
    {
        public MedicalCareCommandTests(CoreContentFixture content) : base(content)
        {
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        /// <summary>Same two-step every live-map test in this codebase uses (see
        /// <c>MapCommandsStandingRulesTests.OpenedSettlement</c>'s own doc) — <see cref="MapCommands"/> resolves
        /// its map off <c>Find.God.Attention.FocusedSettlement</c>, so nothing short of an opened settlement's
        /// interior lets the command run at all.</summary>
        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static void MakeBleedingWound(Pawn p) =>
            DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), 6f, hitPart: Part(p, "left arm")), p);

        // -------------------------------------------------------------------------------------------
        // The command itself
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void SetMedicalCare_refuses_only_with_no_map_open()
        {
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.SetMedicalCare(MedicalCareCategory.NoCare).Outcome);
        }

        [Fact]
        public void SetMedicalCare_defaults_to_Best_and_changes_on_command()
        {
            Settlement settlement = OpenedSettlement("medcare-default");
            CoreMap map = settlement.InteriorMap!;

            Assert.Equal(MedicalCareCategory.Best, map.medicalCare);

            MapCommandResult result = MapCommands.SetMedicalCare(MedicalCareCategory.NoMeds);
            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Equal(MedicalCareCategory.NoMeds, map.medicalCare);
        }

        // -------------------------------------------------------------------------------------------
        // The lever test: the value the command writes is the value the job-giving machinery reads —
        // no separate "outcome setter" duplicating what SetMedicalCare changed.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void NoCare_makes_WorkGiver_Tend_refuse_a_job_it_would_otherwise_take()
        {
            Settlement settlement = OpenedSettlement("medcare-nocare");
            CoreMap map = settlement.InteriorMap!;
            Pawn doctor = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            Pawn patient = settlement.Citizens.First(p => p.Spawned && p.Map == map && p != doctor);
            MakeBleedingWound(patient);

            var ordinary = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Assert.True(ordinary.HasJobOnThing(doctor, patient), "Sanity: tendable before the care level changes anything.");

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetMedicalCare(MedicalCareCategory.NoCare).Outcome);

            Assert.False(ordinary.HasJobOnThing(doctor, patient),
                "NoCare is the settlement's own standing rule (RimWorld: HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer) — no doctor should pick this patient up any more.");
        }

        [Fact]
        public void NoMeds_stops_the_settlement_spending_medicine_but_still_tends()
        {
            Settlement settlement = OpenedSettlement("medcare-nomeds");
            CoreMap map = settlement.InteriorMap!;
            Pawn doctor = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            Pawn patient = settlement.Citizens.First(p => p.Spawned && p.Map == map && p != doctor);
            MakeBleedingWound(patient);

            Thing medicine = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MedicineHerbal"));
            medicine.stackCount = 5;
            GenSpawn.Spawn(medicine, patient.Position, map);

            Assert.NotNull(MedicineUtility.FindBestMedicine(doctor, patient));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetMedicalCare(MedicalCareCategory.NoMeds).Outcome);

            Assert.Null(MedicineUtility.FindBestMedicine(doctor, patient));
            var ordinary = new WorkGiver_Tend { def = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend") };
            Assert.True(ordinary.HasJobOnThing(doctor, patient), "NoMeds still tends — only NoCare refuses outright.");
        }

        // -------------------------------------------------------------------------------------------
        // Scribe round trip
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void MedicalCare_round_trips_through_scribe()
        {
            CoreMap map = new CoreMap(6, 6, TerrainDefOf.Soil);
            map.medicalCare = MedicalCareCategory.HerbalOrWorse;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(MedicalCareCategory.HerbalOrWorse, loaded.medicalCare);
        }

        [Fact]
        public void MedicalCare_at_its_default_is_not_written_to_the_save_at_all()
        {
            // Scribe_Values.Look's own space-saving default (see its own doc): a value equal to the default
            // is omitted, and ScribeExtractor.ValueFromNode restores the same default on load with nothing to
            // read — proved here rather than assumed, since this is the one field task #104 added to Map.
            CoreMap map = new CoreMap(6, 6, TerrainDefOf.Soil);
            Assert.Equal(MedicalCareCategory.Best, map.medicalCare);

            string xml = Scribe.SaveToString(map, "map");
            Assert.DoesNotContain("medicalCare", xml);

            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(MedicalCareCategory.Best, loaded.medicalCare);
        }
    }
}
