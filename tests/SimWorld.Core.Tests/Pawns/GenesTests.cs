using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Pawns.Genes;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>pawngen.genes: Biotech's gene/xenotype system and inheritance. See GeneDef, XenotypeDef,
    /// Pawn_GeneTracker and GeneInheritanceUtility (all under Pawns/Genes/) for what was ported, what is
    /// SimWorld's own defensible stand-in where RimWorld's exact rule was not available to source in this
    /// sandbox, and why — those class docs are not repeated here.</summary>
    public class GenesTests : ContentTestBase
    {
        public GenesTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static GeneDef GeneNamed(string defName) => DefDatabase<GeneDef>.GetNamed(defName);

        private static XenotypeDef Xenotype(string defName) => DefDatabase<XenotypeDef>.GetNamed(defName);

        private static StatDef Stat(string defName) => DefDatabase<StatDef>.GetNamed(defName);

        // ---- content ----

        [Fact]
        public void Core_content_has_expected_gene_defs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(DefDatabase<GeneDef>.DefCount >= 10, "expected at least 10 GeneDefs, found " + DefDatabase<GeneDef>.DefCount);
            Assert.True(DefDatabase<XenotypeDef>.DefCount >= 4, "expected Baseliner plus at least 3 non-baseline xenotypes");

            XenotypeDef baseliner = Xenotype("Baseliner");
            Assert.True(baseliner.genes == null || baseliner.genes.Count == 0);

            // At least one shipped xenotype is non-baseline, exercised end to end by the rest of this file.
            XenotypeDef swiftbred = Xenotype("Swiftbred");
            Assert.NotEmpty(swiftbred.genes!);
        }

        [Fact]
        public void XenotypeDef_reports_a_duplicate_gene_as_a_config_error()
        {
            var duplicate = new XenotypeDef { defName = "TestDup", genes = new List<GeneDef> { GeneNamed("Gene_Nimble"), GeneNamed("Gene_Nimble") } };
            Assert.Contains(duplicate.ConfigErrors(), e => e.Contains("more than once"));
        }

        [Fact]
        public void GeneDef_reports_negative_biostatCpx_as_a_config_error()
        {
            var bad = new GeneDef { defName = "TestBadCpx", biostatCpx = -1 };
            Assert.Contains(bad.ConfigErrors(), e => e.Contains("biostatCpx"));
        }

        // ---- generation ----

        [Fact]
        public void No_Xenotype_requested_yields_a_pawn_with_no_genes()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Assert.Empty(pawn.genes.GenesListForReading);
            Assert.Null(pawn.genes.xenotypeDef);
        }

        [Fact]
        public void Requesting_a_Xenotype_grants_its_genes_as_endogenes()
        {
            XenotypeDef swiftbred = Xenotype("Swiftbred");
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, xenotype: swiftbred));

            Assert.Same(swiftbred, pawn.genes.xenotypeDef);
            Assert.Equal(swiftbred.genes!.Count, pawn.genes.GenesListForReading.Count);
            foreach (GeneDef def in swiftbred.genes)
            {
                Assert.True(pawn.genes.HasGene(def));
                Assert.Contains(pawn.genes.Endogenes, g => g.def == def);
            }
            Assert.Empty(pawn.genes.Xenogenes);
        }

        [Fact]
        public void Generation_with_a_Xenotype_does_not_change_the_RNG_stream_a_plain_request_already_relies_on()
        {
            // Pins the RNG-safety rule PawnGenerator.GenerateInternal documents: applying a named xenotype
            // costs no Rand call, so a request that asks for one must still roll byte-for-byte the same
            // age/backstories/traits/skills/name/weapon as one that does not. A fixed-seed test elsewhere in
            // this repo broke once when an earlier module was inserted mid-pipeline; this is the regression
            // guard for genes not repeating that.
            //
            // Swiftbred, deliberately: its genes bar no work. The germline is now applied before backstories
            // and traits (a gene that disables work has to be on the pawn before anything asks what work it
            // can do), so a xenotype that DOES bar work legitimately changes which backstories and traits are
            // eligible and how many skills the roll draws for. What stays invariant is the stronger and more
            // useful thing: carrying genes costs no draws of its own.
            Rand.Current = new RandomStream(9001);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn withoutGenes = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Rand.Current = new RandomStream(9001);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn withGenes = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, xenotype: Xenotype("Swiftbred")));

            Assert.Equal(withoutGenes.Name!.ToStringFull, withGenes.Name!.ToStringFull);
            Assert.Equal(withoutGenes.gender, withGenes.gender);
            Assert.Equal(withoutGenes.story.traits.allTraits.Select(t => t.def.defName), withGenes.story.traits.allTraits.Select(t => t.def.defName));
            Assert.Equal(withoutGenes.skills.skills.Select(s => s.Level), withGenes.skills.skills.Select(s => s.Level));
        }

        [Fact]
        public void Longevity_gene_extends_the_hidden_lifespan_budget_by_its_exact_bonus()
        {
            // Both pawns are Stillfolk, generated from the same seed; the only difference between the two runs
            // is the bonus on the gene itself, which costs no Rand call either way. That is what makes the
            // comparison exact.
            //
            // It used to compare a Stillfolk against a plain Colonist on one seed, which worked only while a
            // xenotype could not change anything before the lifespan roll. It can now: the germline is applied
            // before backstories and traits (so a gene that bars work can shape them, which is the whole point
            // of GeneDef.disabledWorkTags), and Stillfolk's pacifism disables Shooting and Melee, which the
            // skill roll then skips — different draws, different roll, and a comparison that was measuring the
            // stream rather than the gene.
            GeneDef longevity = GeneNamed("Gene_Longevity");
            float bonus = longevity.lifespanBonusYears;
            Assert.True(bonus > 0f, "Gene_Longevity is meant to lengthen a life");

            Rand.Current = new RandomStream(4242);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn longLived = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, xenotype: Xenotype("Stillfolk")));

            float withoutBonus;
            try
            {
                longevity.lifespanBonusYears = 0f;
                Rand.Current = new RandomStream(4242);
                Pawn.ResetThingIdCounter();
                NameUseChecker.Clear();
                Pawn baseline = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, xenotype: Xenotype("Stillfolk")));
                withoutBonus = baseline.ageTracker.DebugDeathAgeYears;
            }
            finally
            {
                longevity.lifespanBonusYears = bonus;
            }

            Assert.Equal(withoutBonus + bonus, longLived.ageTracker.DebugDeathAgeYears, 2);
        }

        // ---- stat / capacity / work-tag seams ----

        [Fact]
        public void Gene_stat_offset_raises_the_target_stat_by_exactly_its_offset()
        {
            Pawn baseline = NewHuman("Base");
            Pawn withGene = NewHuman("Gene");
            withGene.genes.AddGene(GeneNamed("Gene_PainResistant"), xenogene: false);

            float baseValue = baseline.GetStatValue(Stat("PainShockThreshold"));
            float geneValue = withGene.GetStatValue(Stat("PainShockThreshold"));

            Assert.Equal(baseValue + 0.15f, geneValue, 3);
        }

        [Fact]
        public void Gene_stat_factor_raises_MoveSpeed()
        {
            Pawn baseline = NewHuman("Base");
            Pawn fast = NewHuman("Fast");
            fast.genes.AddGene(GeneNamed("Gene_HighMetabolism"), xenogene: false);

            float baseValue = baseline.GetStatValue(Stat("MoveSpeed"));
            float fastValue = fast.GetStatValue(Stat("MoveSpeed"));

            Assert.True(fastValue > baseValue, $"expected Gene_HighMetabolism to raise MoveSpeed above {baseValue}, got {fastValue}");
        }

        [Fact]
        public void Gene_capMods_raise_a_capacity_level()
        {
            Pawn baseline = NewHuman("Base");
            Pawn keen = NewHuman("Keen");
            keen.genes.AddGene(GeneNamed("Gene_KeenSenses"), xenogene: false);

            float baseSight = baseline.health.capacities.GetLevel(PawnCapacityDefOf.Sight);
            float keenSight = keen.health.capacities.GetLevel(PawnCapacityDefOf.Sight);

            Assert.True(keenSight > baseSight, $"expected Gene_KeenSenses to raise Sight above {baseSight}, got {keenSight}");
        }

        [Fact]
        public void Gene_disabledWorkTags_bars_matching_work()
        {
            Pawn pawn = NewHuman("Pacifist");
            pawn.genes.AddGene(GeneNamed("Gene_Pacifist"), xenogene: false);

            Assert.True(pawn.WorkTagIsDisabled(WorkTags.Violent));
            Assert.True(pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting));
        }

        [Fact]
        public void Positive_metabolism_gene_raises_hunger_rate_and_negative_lowers_it()
        {
            Pawn baseline = NewHuman("Base");
            Pawn hungry = NewHuman("Hungry");
            hungry.genes.AddGene(GeneNamed("Gene_HighMetabolism"), xenogene: false);
            Pawn frugal = NewHuman("Frugal");
            frugal.genes.AddGene(GeneNamed("Gene_LowMetabolism"), xenogene: false);

            Assert.True(hungry.HungerRate > baseline.HungerRate, "expected positive metabolism to raise hunger rate");
            Assert.True(frugal.HungerRate < baseline.HungerRate, "expected negative metabolism to lower hunger rate");
        }

        // ---- exclusion / override resolution ----

        [Fact]
        public void Conflicting_endogenes_keep_only_the_one_added_first()
        {
            Pawn pawn = NewHuman("Conflicted");
            pawn.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false);
            pawn.genes.AddGene(GeneNamed("Gene_Clumsy"), xenogene: false);

            Assert.True(pawn.genes.HasGene(GeneNamed("Gene_Clumsy")), "the overridden gene should still be stored");
            Assert.True(pawn.genes.HasActiveGene(GeneNamed("Gene_Nimble")));
            Assert.False(pawn.genes.HasActiveGene(GeneNamed("Gene_Clumsy")));
        }

        [Fact]
        public void A_xenogene_overrides_a_conflicting_endogene_regardless_of_add_order()
        {
            Pawn pawn = NewHuman("Overridden");
            pawn.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false); // endogene added first
            pawn.genes.AddGene(GeneNamed("Gene_Clumsy"), xenogene: true); // xenogene added second, still wins

            Assert.True(pawn.genes.HasActiveGene(GeneNamed("Gene_Clumsy")));
            Assert.False(pawn.genes.HasActiveGene(GeneNamed("Gene_Nimble")));
        }

        [Fact]
        public void Removing_the_overriding_gene_reactivates_the_one_it_silenced()
        {
            Pawn pawn = NewHuman("Reactivate");
            pawn.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false);
            pawn.genes.AddGene(GeneNamed("Gene_Clumsy"), xenogene: true);
            Assert.False(pawn.genes.HasActiveGene(GeneNamed("Gene_Nimble")));

            Gene xeno = pawn.genes.GenesListForReading.First(g => g.xenogene);
            pawn.genes.RemoveGene(xeno);

            Assert.True(pawn.genes.HasActiveGene(GeneNamed("Gene_Nimble")));
        }

        // ---- inheritance ----

        [Fact]
        public void Newborn_inherits_a_gene_both_parents_share()
        {
            Pawn mother = NewHuman("Mother");
            Pawn father = NewHuman("Father");
            mother.genes.SetXenotype(Xenotype("Swiftbred"));
            father.genes.SetXenotype(Xenotype("Swiftbred"));

            var child = new Pawn(Human, "Child");
            GeneInheritanceUtility.InheritEndogenesFrom(child, mother, father);

            Assert.True(child.genes.HasGene(GeneNamed("Gene_Nimble")));
            Assert.True(child.genes.HasGene(GeneNamed("Gene_FastLearner")));
            Assert.True(child.genes.HasGene(GeneNamed("Gene_HighMetabolism")));
            Assert.All(child.genes.GenesListForReading, g => Assert.False(g.xenogene));
        }

        [Fact]
        public void Newborn_never_inherits_a_xenogene()
        {
            Pawn mother = NewHuman("Mother");
            mother.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: true);
            Pawn father = NewHuman("Father");

            var child = new Pawn(Human, "Child");
            GeneInheritanceUtility.InheritEndogenesFrom(child, mother, father);

            Assert.False(child.genes.HasGene(GeneNamed("Gene_Nimble")));
        }

        [Fact]
        public void Inheritance_from_two_parents_with_no_genes_touches_nothing()
        {
            Pawn mother = NewHuman("M");
            Pawn father = NewHuman("F");
            var child = new Pawn(Human, "C");

            GeneInheritanceUtility.InheritEndogenesFrom(child, mother, father);

            Assert.Empty(child.genes.GenesListForReading);
        }

        [Fact]
        public void Inheritance_of_a_single_parent_gene_is_deterministic_for_the_same_seed()
        {
            Rand.Current = new RandomStream(321);
            Pawn motherA = NewHuman("MA");
            motherA.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false);
            var childA = new Pawn(Human, "A");
            GeneInheritanceUtility.InheritEndogenesFrom(childA, motherA, NewHuman("FA"));

            Rand.Current = new RandomStream(321);
            Pawn motherB = NewHuman("MB");
            motherB.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false);
            var childB = new Pawn(Human, "B");
            GeneInheritanceUtility.InheritEndogenesFrom(childB, motherB, NewHuman("FB"));

            Assert.Equal(childA.genes.HasGene(GeneNamed("Gene_Nimble")), childB.genes.HasGene(GeneNamed("Gene_Nimble")));
        }

        [Fact]
        public void Inheritance_of_a_single_parent_gene_goes_both_ways_across_a_population()
        {
            // Property, not a literal: a gene only one parent carries is a coin flip (GeneTuning's own doc),
            // so across many independent trials it must land both ways at least once each.
            bool sawInherited = false, sawNotInherited = false;
            for (int i = 0; i < 100 && !(sawInherited && sawNotInherited); i++)
            {
                Rand.Current = new RandomStream(i);
                Pawn mother = NewHuman("M" + i);
                mother.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: false);
                var child = new Pawn(Human, "C" + i);
                GeneInheritanceUtility.InheritEndogenesFrom(child, mother, NewHuman("F" + i));
                if (child.genes.HasGene(GeneNamed("Gene_Nimble"))) sawInherited = true; else sawNotInherited = true;
            }
            Assert.True(sawInherited, "expected at least one child to inherit a single-parent gene across 100 trials");
            Assert.True(sawNotInherited, "expected at least one child to NOT inherit a single-parent gene across 100 trials");
        }

        [Fact]
        public void FamilyManager_birth_path_wires_up_inheritance()
        {
            Find.FamilyManager = new FamilyManager();
            Pawn husband = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedGender: Gender.Male, fixedBiologicalAge: 25f, xenotype: Xenotype("Swiftbred")));
            Pawn wife = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedGender: Gender.Female, fixedBiologicalAge: 25f, xenotype: Xenotype("Swiftbred")));
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, Find.TickManager.TicksGame);

            Pawn? newborn = null;
            for (int i = 0; i < 200 && newborn == null; i++)
            {
                Rand.Current = new RandomStream(i * 7919 + 13);
                husband.needs.mood!.CurLevel = 1f;
                wife.needs.mood!.CurLevel = 1f;
                husband.needs.food!.CurLevel = husband.needs.food.MaxLevel;
                wife.needs.food!.CurLevel = wife.needs.food.MaxLevel;
                family.lastBirthTick = Find.TickManager.TicksGame - DemographyTuning.MinBirthIntervalTicks;

                var population = new List<Pawn> { husband, wife };
                Find.FamilyManager.ProcessBirths(population);
                if (population.Count == 3) newborn = population[2];
            }

            Assert.NotNull(newborn);
            // Both parents carry the exact same three genes: every one of them breeds true (shared, not a coin flip).
            Assert.True(newborn!.genes.HasGene(GeneNamed("Gene_Nimble")));
            Assert.True(newborn.genes.HasGene(GeneNamed("Gene_FastLearner")));
            Assert.True(newborn.genes.HasGene(GeneNamed("Gene_HighMetabolism")));
            Assert.All(newborn.genes.GenesListForReading, g => Assert.False(g.xenogene));
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Scribe_round_trip_preserves_genes_and_xenotype()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, xenotype: Xenotype("Ironclad")));
            pawn.genes.AddGene(GeneNamed("Gene_Nimble"), xenogene: true); // exercise the xenogene half too

            var holder = new PawnHolder { pawns = new List<Pawn> { pawn } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn restored = loaded.pawns![0];
            Assert.Same(Xenotype("Ironclad"), restored.genes.xenotypeDef);
            Assert.Equal(
                pawn.genes.GenesListForReading.Select(g => (g.def.defName, g.xenogene)).OrderBy(t => t.defName),
                restored.genes.GenesListForReading.Select(g => (g.def.defName, g.xenogene)).OrderBy(t => t.defName));
        }
    }
}
