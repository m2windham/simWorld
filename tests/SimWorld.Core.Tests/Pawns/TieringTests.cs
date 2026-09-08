using System.Collections.Generic;
using System.Linq;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>Citizen tiering / level of detail (system 11, spec §11.3). <see cref="PawnHolder"/> (defined in
    /// <c>PawnTests.cs</c>) is reused for the Scribe round-trip test.</summary>
    public class TieringTests : ContentTestBase
    {
        public TieringTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
            Find.FamilyManager = new FamilyManager();
        }

        private static Pawn NewAdult(float age = 25f, Gender? gender = null) =>
            PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedGender: gender, fixedBiologicalAge: age));

        // ---- default behaviour is unchanged ----

        [Fact]
        public void Default_tier_is_Full_matching_pre_tiering_behaviour()
        {
            Pawn p = NewHuman();
            Assert.Equal(PawnTier.Full, p.tier.Tier);
            Assert.Equal(TickerType.Normal, p.TickerType);
            Assert.False(p.tier.Significant);
        }

        // ---- tier-aware ticking: a demoted pawn leaves the tick list, it is not skipped inside it ----

        [Fact]
        public void Demotion_and_promotion_move_the_pawn_between_tick_lists()
        {
            Pawn p = NewHuman();
            RunTicks(1, p); // registers on the Normal list (Full) via the shared helper

            TickManager tm = Find.TickManager;
            Assert.True(tm.TickListFor(TickerType.Normal)!.Contains(p));
            Assert.False(tm.TickListFor(TickerType.Long)!.Contains(p));

            p.tier.Notify_AttentionChanged(false); // nobody significant: Full -> Interval

            Assert.Equal(PawnTier.Interval, p.tier.Tier);
            Assert.False(tm.TickListFor(TickerType.Normal)!.Contains(p));
            Assert.True(tm.TickListFor(TickerType.Long)!.Contains(p));

            p.tier.DemoteToStatistical(); // Interval -> Statistical shares the same list (TickerType.Long)

            Assert.Equal(PawnTier.Statistical, p.tier.Tier);
            Assert.True(tm.TickListFor(TickerType.Long)!.Contains(p));

            p.tier.Notify_AttentionChanged(true); // attended again: straight back to Full, no two-step climb

            Assert.Equal(PawnTier.Full, p.tier.Tier);
            Assert.True(tm.TickListFor(TickerType.Normal)!.Contains(p));
            Assert.False(tm.TickListFor(TickerType.Long)!.Contains(p));
        }

        [Fact]
        public void A_pawn_with_a_role_stays_Full_even_once_attention_leaves()
        {
            Pawn p = NewHuman();
            p.tier.Notify_RoleChanged(true);
            Assert.Equal(PawnTier.Full, p.tier.Tier);

            p.tier.Notify_AttentionChanged(false); // attention leaves, but the role remains
            Assert.Equal(PawnTier.Full, p.tier.Tier);

            p.tier.Notify_RoleChanged(false); // now nothing holds
            Assert.Equal(PawnTier.Interval, p.tier.Tier);
        }

        [Fact]
        public void DemoteToStatistical_only_succeeds_from_an_insignificant_Interval_pawn()
        {
            Pawn full = NewHuman();
            full.tier.DemoteToStatistical(); // no-op: still Full
            Assert.Equal(PawnTier.Full, full.tier.Tier);

            Pawn significant = NewHuman();
            significant.tier.Notify_ChronicleNamed();
            significant.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Full, significant.tier.Tier); // chronicle mention alone keeps it Full
            significant.tier.DemoteToStatistical(); // no-op: not Interval, and significant anyway
            Assert.Equal(PawnTier.Full, significant.tier.Tier);

            Pawn insignificant = NewHuman();
            insignificant.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, insignificant.tier.Tier);
            insignificant.tier.DemoteToStatistical();
            Assert.Equal(PawnTier.Statistical, insignificant.tier.Tier);
        }

        // ---- identity is lossless across a demotion/promotion round trip ----

        [Fact]
        public void Identity_and_relationships_round_trip_through_Statistical_and_back_to_Full()
        {
            Pawn husband = NewAdult(age: 30f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 28f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);

            string nameBefore = husband.Label;
            int familyIdBefore = husband.relations.familyId;
            int spouseIdBefore = husband.relations.spouseId;
            int generationBefore = husband.relations.generation;
            float ageBefore = husband.ageTracker.AgeBiologicalYearsFloat;

            husband.tier.Notify_AttentionChanged(false); // Full -> Interval
            husband.tier.DemoteToStatistical();          // Interval -> Statistical
            Assert.Equal(PawnTier.Statistical, husband.tier.Tier);

            // Thirty years pass while nobody is looking at this citizen at all.
            int elapsedTicks = 30 * GenDate.TicksPerYear;
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + elapsedTicks);

            husband.tier.Notify_AttentionChanged(true); // promoted back to Full; catch-up runs

            Assert.Equal(PawnTier.Full, husband.tier.Tier);
            Assert.Equal(TickerType.Normal, husband.TickerType);

            // Identity: exactly who they were.
            Assert.Equal(nameBefore, husband.Label);
            Assert.Equal(familyIdBefore, husband.relations.familyId);
            Assert.Equal(generationBefore, husband.relations.generation);
            Assert.Equal(spouseIdBefore, husband.relations.spouseId);
            Assert.Equal(wife.thingIDNumber, husband.relations.spouseId);
            Assert.Equal(family.id, husband.relations.familyId);

            // Aged correctly (the AgeTickMothballed model), not frozen and not a newborn.
            float ageAfter = husband.ageTracker.AgeBiologicalYearsFloat;
            Assert.InRange(ageAfter - ageBefore, 29.99f, 30.01f);

            // Not a corpse, and not left however-stale at whatever the needs read the instant of demotion:
            // arrives with a plausible, freshly cohort-sampled level.
            Assert.False(husband.Dead);
            Assert.NotNull(husband.needs.mood);
            Assert.InRange(husband.needs.mood!.CurLevelPercentage, TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax);

            // No job left over from decades at a coarser tier.
            Assert.Null(husband.jobs.curJob);
        }

        // ---- a relationship attaches an insignificant pawn to someone already promoted ----

        [Fact]
        public void PromoteRelativesOf_promotes_parents_one_hop_but_not_a_sibling_two_hops_away()
        {
            Pawn parentA = NewAdult(age: 55f, gender: Gender.Male);
            Pawn parentB = NewAdult(age: 52f, gender: Gender.Female);
            Find.FamilyManager.FoundHousehold(parentA, parentB, 0);

            Pawn promotedChild = NewAdult(age: 25f, gender: Gender.Male);
            promotedChild.relations.SetParents(parentA.thingIDNumber, parentB.thingIDNumber);
            parentA.relations.Notify_ChildBorn(promotedChild.thingIDNumber);
            parentB.relations.Notify_ChildBorn(promotedChild.thingIDNumber);

            // A sibling: also a child of parentA/parentB, but reachable from promotedChild only via the
            // parent -- two hops, not one -- so PromoteRelativesOf must not reach it.
            Pawn sibling = NewAdult(age: 22f, gender: Gender.Female);
            sibling.relations.SetParents(parentA.thingIDNumber, parentB.thingIDNumber);
            parentA.relations.Notify_ChildBorn(sibling.thingIDNumber);
            parentB.relations.Notify_ChildBorn(sibling.thingIDNumber);

            foreach (Pawn relative in new[] { parentA, parentB, sibling })
            {
                relative.tier.Notify_AttentionChanged(false);
                relative.tier.DemoteToStatistical();
            }

            var population = new Dictionary<int, Pawn>
            {
                [parentA.thingIDNumber] = parentA,
                [parentB.thingIDNumber] = parentB,
                [promotedChild.thingIDNumber] = promotedChild,
                [sibling.thingIDNumber] = sibling,
            };

            promotedChild.tier.Notify_ChronicleNamed(); // promotedChild becomes significant on its own
            Assert.Equal(PawnTier.Full, promotedChild.tier.Tier);

            FamilyManager.PromoteRelativesOf(promotedChild, population);

            Assert.Equal(PawnTier.Full, parentA.tier.Tier);
            Assert.Equal(PawnTier.Full, parentB.tier.Tier);
            Assert.True(parentA.tier.RelatedToPromoted);
            Assert.True(parentB.tier.RelatedToPromoted);

            // The sibling is not spouse/parent/child of promotedChild -- one hop only.
            Assert.Equal(PawnTier.Statistical, sibling.tier.Tier);
            Assert.False(sibling.tier.RelatedToPromoted);
        }

        // ---- a Statistical citizen still participates in demography ----

        [Fact]
        public void Statistical_citizens_still_marry_and_bear_children()
        {
            Rand.Current = new RandomStream(9001);
            var population = new List<Pawn>();
            for (int i = 0; i < 6; i++)
            {
                Pawn p = NewAdult(age: 20f + i, gender: i % 2 == 0 ? Gender.Male : Gender.Female);
                p.tier.Notify_AttentionChanged(false);
                p.tier.DemoteToStatistical();
                population.Add(p);
            }
            List<Pawn> founders = new List<Pawn>(population);

            int tick = 0;
            for (int year = 0; year < 40; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                Find.TickManager.DebugSetTicksGame(tick);
                // The real per-citizen mechanism (age + cohort-sampled needs), not a test-only shortcut.
                foreach (Pawn p in population) if (!p.Dead) p.TickLong();
                Find.FamilyManager.DemographyTick(population);
            }

            Assert.True(population.Count > founders.Count,
                "expected at least one birth to have grown the population; got " + population.Count);
            Assert.Contains(founders, p => p.relations.IsMarried);
            Assert.All(founders, p => Assert.NotEqual(PawnTier.Full, p.tier.Tier)); // nobody was ever attended
        }

        [Fact]
        public void Statistical_citizen_dies_of_age_via_its_own_coarse_tick()
        {
            Pawn p = NewAdult(age: 30f);
            p.tier.Notify_AttentionChanged(false);
            p.tier.DemoteToStatistical();

            // Push it well past any rolled lifespan budget (humans: [65, 95]) purely through the tier's own
            // coarse tick -- it is never on the Normal tick list at all.
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 150 * GenDate.TicksPerYear);
            p.TickLong();

            Assert.True(p.ageTracker.ShouldDieOfAge());
            Assert.False(p.Dead); // ShouldDieOfAge is a predicate; DemographyTick is what actually kills.

            var population = new List<Pawn> { p };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            Assert.True(p.Dead);
            Assert.True(p.health.Dead);
        }

        // ---- cohort sampling is deterministic from the seeded stream ----

        [Fact]
        public void Cohort_sampling_is_deterministic_for_the_same_pawn_id_and_tick()
        {
            Pawn a = NewHuman("A");
            a.tier.Notify_AttentionChanged(false);
            a.tier.DemoteToStatistical();
            Find.TickManager.DebugSetTicksGame(12345);
            a.TickLong();
            float sampledMoodA = a.needs.mood!.CurLevelPercentage;
            float sampledFoodA = a.needs.food!.CurLevelPercentage;
            float sampledHealthA = a.tier.SampledHealthFraction;

            Find.TickManager = new TickManager();
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn b = NewHuman("A"); // same thingIDNumber (0) as 'a' had
            b.tier.Notify_AttentionChanged(false);
            b.tier.DemoteToStatistical();
            Find.TickManager.DebugSetTicksGame(12345); // same tick
            b.TickLong();

            Assert.Equal(sampledMoodA, b.needs.mood!.CurLevelPercentage);
            Assert.Equal(sampledFoodA, b.needs.food!.CurLevelPercentage);
            Assert.Equal(sampledHealthA, b.tier.SampledHealthFraction);

            // A later tick resamples to a (deterministically) different value at least once across a spread
            // of checks -- not the same constant forever.
            Find.TickManager.DebugSetTicksGame(999999);
            b.TickLong();
            bool anyDifferent = b.needs.mood!.CurLevelPercentage != sampledMoodA
                || b.needs.food!.CurLevelPercentage != sampledFoodA
                || b.tier.SampledHealthFraction != sampledHealthA;
            Assert.True(anyDifferent, "expected at least one sampled value to change at a different tick");
        }

        // ---- Interval tier: the full state is real and advances in bulk, not per tick ----

        [Fact]
        public void Interval_tier_needs_decay_for_real_in_bulk_not_by_sampling()
        {
            Pawn p = NewHuman();
            p.needs.food!.CurLevel = p.needs.food.MaxLevel; // full
            p.tier.Notify_AttentionChanged(false); // Full -> Interval

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenTicks.TickLongInterval);
            p.TickLong();

            Assert.Equal(PawnTier.Interval, p.tier.Tier);
            Assert.True(p.needs.food.CurLevel < p.needs.food.MaxLevel, "food should have fallen for real over one Long-tick bucket");
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Scribe_round_trips_tier_and_significance_flags()
        {
            Pawn p = NewHuman();
            p.tier.Notify_RoleChanged(true);
            p.tier.Notify_AttentionChanged(false); // role keeps it Full
            Assert.Equal(PawnTier.Full, p.tier.Tier);
            p.tier.Notify_RoleChanged(false);
            p.tier.DemoteToStatistical();
            Assert.Equal(PawnTier.Statistical, p.tier.Tier);
            p.tier.Notify_ChronicleNamed();
            Assert.Equal(PawnTier.Full, p.tier.Tier); // chronicle mention promoted it back

            var holder = new PawnHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn lp = loaded.pawns!.Single();
            Assert.Equal(PawnTier.Full, lp.tier.Tier);
            Assert.True(lp.tier.ChronicleNamed);
            Assert.False(lp.tier.HasRole);
            Assert.False(lp.tier.Attending);
        }
    }
}
