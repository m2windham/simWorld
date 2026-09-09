using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// health.addictions: CompDrug's tolerance/addiction model (see its own remarks) — build with use, relieve
    /// with a further dose, decay with abstinence — and the withdrawal it produces reaching Need_Mood purely
    /// through content pointed at the pre-existing ThoughtWorker_Hediff hook.
    /// </summary>
    public class DrugTests : ContentTestBase
    {
        public DrugTests(CoreContentFixture content) : base(content)
        {
        }

        private static ChemicalDef Alcohol => DefDatabase<ChemicalDef>.GetNamed("Alcohol");
        private static HediffDef AlcoholTolerance => DefDatabase<HediffDef>.GetNamed("AlcoholTolerance");
        private static HediffDef AlcoholAddiction => DefDatabase<HediffDef>.GetNamed("AlcoholAddiction");

        private static ThingWithComps NewBeer() => (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Beer"));

        // ---- content ----

        [Fact]
        public void Beer_carries_a_real_CompDrug_bound_to_Alcohol()
        {
            ThingWithComps beer = NewBeer();
            CompDrug comp = beer.GetComp<CompDrug>()!;
            Assert.Same(Alcohol, comp.Props.chemical);
            Assert.Same(AlcoholTolerance, Alcohol.toleranceHediff);
            Assert.Same(AlcoholAddiction, Alcohol.addictionHediff);
        }

        // ---- tolerance ----

        [Fact]
        public void A_dose_raises_tolerance_and_repeated_doses_raise_it_further()
        {
            Pawn p = NewHuman();
            NewBeer().GetComp<CompDrug>()!.PostIngested(p);
            float afterOne = p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)?.Severity ?? 0f;
            Assert.True(afterOne > 0f);

            NewBeer().GetComp<CompDrug>()!.PostIngested(p);
            float afterTwo = p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)!.Severity;
            Assert.True(afterTwo > afterOne);
        }

        [Fact]
        public void Tolerance_decays_over_days_of_abstinence()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, AlcoholTolerance, 0.5f);
            float before = p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)!.Severity;

            RunTicks(GenDate.TicksPerDay, p);

            float after = p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)!.Severity;
            Assert.True(after < before, "tolerance should fall over a day with no further doses");
        }

        // ---- addiction ----

        private static CompDrug DrugWithChance(float toleranceGain, float addictionChance, float relief = 0.35f)
        {
            var props = new CompProperties_Drug
            {
                chemical = Alcohol,
                toleranceGainPerDose = toleranceGain,
                addictionChancePerDoseAboveThreshold = addictionChance,
                addictionInitialSeverity = 0.2f,
                doseSatisfiesSeverity = relief,
            };
            var thingDef = new ThingDef
            {
                defName = "Test_DrugThing",
                category = ThingCategory.Item,
                thingClass = typeof(ThingWithComps),
                comps = new List<CompProperties> { props },
            };
            var thing = (ThingWithComps)ThingMaker.MakeThing(thingDef);
            return thing.GetComp<CompDrug>()!;
        }

        [Fact]
        public void No_addiction_starts_below_the_chemicals_tolerance_threshold_however_lucky_the_roll()
        {
            Pawn p = NewHuman();
            // minToleranceToAddict is 0.3 for Alcohol; a single small dose stays well under it, and the
            // addiction chance is pinned to 1 (would always fire if the threshold check didn't gate it first).
            CompDrug drug = DrugWithChance(toleranceGain: 0.05f, addictionChance: 1f);
            drug.PostIngested(p);

            Assert.False(p.health.hediffSet.HasHediff(AlcoholAddiction));
        }

        [Fact]
        public void Addiction_starts_once_tolerance_is_at_the_threshold_and_the_roll_hits()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, AlcoholTolerance, 0.3f); // exactly minToleranceToAddict
            CompDrug drug = DrugWithChance(toleranceGain: 0f, addictionChance: 1f);

            drug.PostIngested(p);

            Assert.True(p.health.hediffSet.HasHediff(AlcoholAddiction));
        }

        [Fact]
        public void Addiction_never_starts_when_the_chance_is_zero_even_above_threshold()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, AlcoholTolerance, 0.9f);
            CompDrug drug = DrugWithChance(toleranceGain: 0f, addictionChance: 0f);

            for (int i = 0; i < 20; i++) drug.PostIngested(p);

            Assert.False(p.health.hediffSet.HasHediff(AlcoholAddiction));
        }

        [Fact]
        public void A_further_dose_once_addicted_relieves_severity_instead_of_starting_a_second_addiction()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, AlcoholAddiction, 0.6f);
            CompDrug drug = DrugWithChance(toleranceGain: 0.1f, addictionChance: 1f, relief: 0.35f);

            drug.PostIngested(p);

            Hediff addiction = p.health.hediffSet.GetFirstHediffOfDef(AlcoholAddiction)!;
            Assert.Equal(0.25f, addiction.Severity, 3);
            // Tolerance still builds even on a dose that relieves an existing addiction.
            Assert.True((p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)?.Severity ?? 0f) > 0f);
        }

        // ---- withdrawal reaching mood (content only: ThoughtWorker_Hediff already existed) ----

        [Fact]
        public void Withdrawal_stage_drives_a_real_mood_offset_through_the_existing_ThoughtWorker_Hediff_hook()
        {
            Pawn p = NewHuman();
            p.needs.mood!.thoughts.situational.Recalculate();
            float baseline = p.needs.mood.thoughts.TotalMoodOffset();

            HealthUtility.AdjustSeverity(p, AlcoholAddiction, 0.8f); // severe-withdrawal stage
            p.needs.mood.thoughts.situational.Notify_SituationalThoughtsDirty();
            p.needs.mood.thoughts.situational.Recalculate();

            float withdrawing = p.needs.mood.thoughts.TotalMoodOffset();
            Assert.True(withdrawing < baseline, "an active severe-withdrawal stage should pull mood down");
        }

        [Fact]
        public void Worse_withdrawal_stages_pull_mood_down_further()
        {
            Pawn mild = NewHuman("Mild");
            HealthUtility.AdjustSeverity(mild, AlcoholAddiction, 0.1f);
            mild.needs.mood!.thoughts.situational.Recalculate();

            Pawn severe = NewHuman("Severe");
            HealthUtility.AdjustSeverity(severe, AlcoholAddiction, 0.9f);
            severe.needs.mood!.thoughts.situational.Recalculate();

            Assert.True(severe.needs.mood.thoughts.TotalMoodOffset() < mild.needs.mood.thoughts.TotalMoodOffset());
        }

        // ---- ingestion utility ----

        [Fact]
        public void Ingest_feeds_nutrition_fires_the_drug_comp_and_removes_one_unit()
        {
            Pawn p = NewHuman();
            p.needs.food!.CurLevel = 0f;
            ThingWithComps beer = NewBeer();
            beer.stackCount = 3;

            DrugIngestUtility.Ingest(p, beer);

            Assert.True(p.needs.food.CurLevel > 0f, "Beer's own nutrition should have fed the pawn");
            Assert.Equal(2, beer.stackCount);
            Assert.True((p.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)?.Severity ?? 0f) > 0f);
        }

        [Fact]
        public void Ingest_destroys_the_stack_once_the_last_unit_is_consumed()
        {
            Pawn p = NewHuman();
            ThingWithComps beer = NewBeer();
            beer.stackCount = 1;

            DrugIngestUtility.Ingest(p, beer);

            Assert.True(beer.Destroyed);
        }

        [Fact]
        public void A_dose_with_joy_grants_the_pawns_Joy_need_through_the_Chemical_JoyKindDef()
        {
            Pawn p = NewHuman();
            p.needs.joy!.CurLevel = 0f;

            NewBeer().GetComp<CompDrug>()!.PostIngested(p);

            Assert.True(p.needs.joy.CurLevel > 0f);
        }

        // ---- Scribe ----

        [Fact]
        public void Tolerance_and_addiction_hediffs_survive_a_save_round_trip()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, AlcoholTolerance, 0.4f);
            HealthUtility.AdjustSeverity(p, AlcoholAddiction, 0.55f);

            string xml = Scribe.SaveToString(p, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Equal(0.4f, loaded.health.hediffSet.GetFirstHediffOfDef(AlcoholTolerance)!.Severity, 3);
            Assert.Equal(0.55f, loaded.health.hediffSet.GetFirstHediffOfDef(AlcoholAddiction)!.Severity, 3);
        }
    }
}
