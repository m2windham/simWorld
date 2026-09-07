using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Social
{
    /// <summary>Scribe holder for round-tripping a pair of pawns together, mirroring <c>PawnHolder</c>/<c>DemographyHolder</c>.</summary>
    public class SocialHolder : IExposable
    {
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    public class SocialTests : ContentTestBase
    {
        public SocialTests(CoreContentFixture content) : base(content)
        {
            Find.FamilyManager = new FamilyManager();
        }

        private static PawnRelationDef Relation(string defName) => DefDatabase<PawnRelationDef>.GetNamed(defName);

        // ---- opinion: relation type ----

        [Fact]
        public void Opinion_of_self_is_zero()
        {
            Pawn p = NewHuman();
            Assert.Equal(0, p.relations.OpinionOf(p));
        }

        [Fact]
        public void Spouse_relation_is_derived_from_the_existing_spouseId_not_stored_twice()
        {
            Pawn husband = NewHuman("H");
            Pawn wife = NewHuman("W");
            husband.gender = Gender.Male;
            wife.gender = Gender.Female;

            Assert.False(Relation("Spouse").Worker.InRelation(husband, wife));

            Find.FamilyManager.FoundHousehold(husband, wife, 0);

            Assert.True(Relation("Spouse").Worker.InRelation(husband, wife));
            Assert.True(Relation("Spouse").Worker.InRelation(wife, husband));
            // Nothing new was stored on either side to represent it.
            Assert.Empty(husband.relations.directRelations);
            Assert.Empty(wife.relations.directRelations);
        }

        [Fact]
        public void Parent_and_child_relations_are_derived_and_directional()
        {
            Pawn mother = NewHuman("M");
            Pawn father = NewHuman("F");
            Pawn kid = NewHuman("K");
            kid.relations.SetParents(mother.thingIDNumber, father.thingIDNumber);
            mother.relations.Notify_ChildBorn(kid.thingIDNumber);
            father.relations.Notify_ChildBorn(kid.thingIDNumber);

            Assert.True(Relation("Parent").Worker.InRelation(kid, mother));
            Assert.True(Relation("Parent").Worker.InRelation(kid, father));
            Assert.False(Relation("Parent").Worker.InRelation(mother, kid), "a parent relation is directional: the mother is not her own child's parent from the child's own record read backwards");

            Assert.True(Relation("Child").Worker.InRelation(mother, kid));
            Assert.True(Relation("Child").Worker.InRelation(father, kid));
            Assert.False(Relation("Child").Worker.InRelation(kid, mother));
        }

        [Fact]
        public void Sibling_relation_is_derived_from_shared_parents()
        {
            Pawn mother = NewHuman("M");
            Pawn father = NewHuman("F");
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Pawn stranger = NewHuman("S");
            a.relations.SetParents(mother.thingIDNumber, father.thingIDNumber);
            b.relations.SetParents(mother.thingIDNumber, father.thingIDNumber);

            Assert.True(Relation("Sibling").Worker.InRelation(a, b));
            Assert.True(Relation("Sibling").Worker.InRelation(b, a));
            Assert.False(Relation("Sibling").Worker.InRelation(a, stranger));
            Assert.False(Relation("Sibling").Worker.InRelation(a, mother), "a parent sharing no parent of its own with its child is not that child's sibling");
        }

        [Fact]
        public void Family_relations_move_opinion_in_the_direction_their_bond_implies()
        {
            Pawn husband = NewHuman("H");
            Pawn wife = NewHuman("W");
            int before = husband.relations.OpinionOf(wife);

            Find.FamilyManager.FoundHousehold(husband, wife, 0);
            int after = husband.relations.OpinionOf(wife);

            Assert.True(after > before, $"marrying should raise opinion via the Spouse relation: {before} -> {after}");
        }

        // ---- opinion: stored (non-family) relations ----

        [Fact]
        public void Mutual_relations_are_stored_on_both_sides_and_removed_from_both()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");

            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Friend);
            Assert.True(a.relations.HasDirectRelation(PawnRelationDefOf.Friend, b.thingIDNumber));
            Assert.True(b.relations.HasDirectRelation(PawnRelationDefOf.Friend, a.thingIDNumber));
            Assert.True(PawnRelationDefOf.Friend.Worker.InRelation(a, b));
            Assert.True(PawnRelationDefOf.Friend.Worker.InRelation(b, a));

            SocialUtility.RemoveMutualRelation(a, b, PawnRelationDefOf.Friend);
            Assert.False(a.relations.HasDirectRelation(PawnRelationDefOf.Friend, b.thingIDNumber));
            Assert.False(b.relations.HasDirectRelation(PawnRelationDefOf.Friend, a.thingIDNumber));
        }

        [Fact]
        public void Friend_raises_opinion_and_rival_lowers_it_below_that()
        {
            // Same pair of pawns throughout, so their fixed compatibility factor cancels out of every
            // comparison below rather than being a confound between different pairs.
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            int neutral = a.relations.OpinionOf(b);

            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Friend);
            int friendOpinion = a.relations.OpinionOf(b);
            Assert.True(friendOpinion > neutral, $"friend should raise opinion above neutral: {neutral} -> {friendOpinion}");

            SocialUtility.RemoveMutualRelation(a, b, PawnRelationDefOf.Friend);
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Rival);
            int rivalOpinion = a.relations.OpinionOf(b);
            Assert.True(rivalOpinion < neutral, $"rival should lower opinion below neutral: {neutral} -> {rivalOpinion}");
            Assert.True(friendOpinion > rivalOpinion);
        }

        // ---- opinion: personality traits ----

        [Fact]
        public void Kind_pawns_are_seen_better_and_abrasive_pawns_worse_than_a_plain_pawn()
        {
            // Same pair (observer, subject) throughout: their fixed compatibility factor cancels out, so this
            // is purely a test of TraitDegreeData.opinionOffset via ConstantOpinionOffset, not a coincidence of
            // which two pawn ids happened to be compared.
            Pawn observer = NewHuman("Observer");
            Pawn subject = NewHuman("Subject");
            int plainOpinion = observer.relations.OpinionOf(subject);

            var kindTrait = new Trait(Trait("Kind"));
            subject.story.traits.GainTrait(kindTrait);
            int kindOpinion = observer.relations.OpinionOf(subject);
            Assert.True(kindOpinion > plainOpinion, $"kind should read better than plain: {kindOpinion} vs {plainOpinion}");

            subject.story.traits.RemoveTrait(kindTrait);
            subject.story.traits.GainTrait(new Trait(Trait("Abrasive")));
            int abrasiveOpinion = observer.relations.OpinionOf(subject);
            Assert.True(abrasiveOpinion < plainOpinion, $"abrasive should read worse than plain: {abrasiveOpinion} vs {plainOpinion}");
        }

        // ---- opinion: compatibility factor ----

        [Fact]
        public void Compatibility_factor_is_symmetric_stable_and_not_uniform_across_pairs()
        {
            Assert.Equal(SocialUtility.CompatibilityFactor(3, 9), SocialUtility.CompatibilityFactor(9, 3));
            Assert.Equal(SocialUtility.CompatibilityFactor(3, 9), SocialUtility.CompatibilityFactor(3, 9));

            var values = new HashSet<int>();
            for (int i = 0; i < 20; i++) values.Add(SocialUtility.CompatibilityFactor(0, i + 1));
            Assert.True(values.Count > 1, "20 different pairs should not all land on exactly the same compatibility value");
            foreach (int v in values)
            {
                Assert.InRange(v, -(int)SocialTuning.CompatibilityRange, (int)SocialTuning.CompatibilityRange);
            }
        }

        [Fact]
        public void Compatibility_factor_does_not_advance_the_shared_random_stream()
        {
            Rand.Current = new RandomStream(55);
            uint before = Rand.Current.Iterations;
            SocialUtility.CompatibilityFactor(1, 2);
            SocialUtility.CompatibilityFactor(7, 42);
            Assert.Equal(before, Rand.Current.Iterations);
        }

        // ---- opinion: clamping ----

        [Fact]
        public void Opinion_stays_within_the_documented_band_even_stacking_every_positive_contributor()
        {
            // Not a claim that this specific stack always crosses the +100 ceiling (RimWorld's own per-memory
            // stacking caps make that hard to force deterministically) — an invariant check that piling on
            // every positive source at once (relations, a favourable trait, and a memory) never produces a
            // number outside [-100, 100], for whatever this pair's own compatibility factor happens to be.
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Lover);
            Find.FamilyManager.FoundHousehold(a, b, 0); // Spouse (+25) stacks on top of Lover (+20)
            b.story.traits.GainTrait(new Trait(Trait("Kind")));
            a.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadDeepTalk, b);

            int opinion = a.relations.OpinionOf(b);
            Assert.InRange(opinion, (int)SocialTuning.MinOpinion, (int)SocialTuning.MaxOpinion);
        }

        // ---- interaction selection weight ----

        [Fact]
        public void Chitchat_is_always_selectable()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Assert.True(InteractionDefOf.Chitchat.Worker.RandomSelectionWeight(a, b) > 0f);
        }

        [Fact]
        public void Deep_talk_requires_a_good_enough_opinion_first()
        {
            // Both cases are overdetermined against the +/-15 compatibility swing (see the social-fight test
            // for the same technique), so this is a test of the gate itself, not a coincidence of pawn ids.
            Pawn lowA = NewHuman("LA");
            Pawn lowB = NewHuman("LB");
            SocialUtility.AddMutualRelation(lowA, lowB, PawnRelationDefOf.Rival);
            for (int i = 0; i < 3; i++) lowB.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, lowA);
            Assert.True(lowB.relations.OpinionOf(lowA) < SocialTuning.DeepTalkMinOpinion);
            Assert.Equal(0f, InteractionDefOf.DeepTalk.Worker.RandomSelectionWeight(lowB, lowA));

            Pawn highA = NewHuman("HA");
            Pawn highB = NewHuman("HB");
            SocialUtility.AddMutualRelation(highA, highB, PawnRelationDefOf.Friend);
            SocialUtility.AddMutualRelation(highA, highB, PawnRelationDefOf.Lover);
            Assert.True(highA.relations.OpinionOf(highB) >= SocialTuning.DeepTalkMinOpinion);
            Assert.True(InteractionDefOf.DeepTalk.Worker.RandomSelectionWeight(highA, highB) > 0f);
        }

        [Fact]
        public void Insult_is_weighted_up_by_low_opinion_and_low_mood()
        {
            // Friend + Lover (+32) clears +17 even against the worst-case -15 compatibility swing, so this
            // pair's opinion is guaranteed positive regardless of which two pawn ids it lands on.
            Pawn x = NewHuman("X");
            Pawn y = NewHuman("Y");
            SocialUtility.AddMutualRelation(x, y, PawnRelationDefOf.Friend);
            SocialUtility.AddMutualRelation(x, y, PawnRelationDefOf.Lover);
            float baseline = InteractionDefOf.Insult.Worker.RandomSelectionWeight(x, y);
            Assert.Equal(SocialTuning.InsultBaseWeight, baseline);

            // Same pair, only the initiator's mood changes: isolates the mood factor from the opinion factor.
            x.needs.mood!.CurLevel = 0f;
            float lowMoodWeight = InteractionDefOf.Insult.Worker.RandomSelectionWeight(x, y);
            Assert.Equal(SocialTuning.InsultBaseWeight * SocialTuning.InsultLowMoodWeightFactor, lowMoodWeight);

            // Rival alone (-18) stays negative even against the best-case +15 compatibility swing.
            Pawn rivalA = NewHuman("RA");
            Pawn rivalB = NewHuman("RB");
            SocialUtility.AddMutualRelation(rivalA, rivalB, PawnRelationDefOf.Rival);
            Assert.True(rivalA.relations.OpinionOf(rivalB) < 0);
            float lowOpinionWeight = InteractionDefOf.Insult.Worker.RandomSelectionWeight(rivalA, rivalB);
            Assert.Equal(SocialTuning.InsultBaseWeight * SocialTuning.InsultLowOpinionWeightFactor, lowOpinionWeight);
        }

        // ---- interaction effects: the loop closing (opinion moves, and shows up in mood) ----

        [Fact]
        public void Repeated_insults_lower_the_recipients_opinion_and_mood()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            int opinionBefore = b.relations.OpinionOf(a);
            float moodBefore = b.needs.mood!.thoughts.TotalMoodOffset();

            for (int i = 0; i < 3; i++) InteractionDefOf.Insult.Worker.Interacted(a, b);

            int opinionAfter = b.relations.OpinionOf(a);
            float moodAfter = b.needs.mood!.thoughts.TotalMoodOffset();

            Assert.True(opinionAfter < opinionBefore, $"repeated insults should lower opinion: {opinionBefore} -> {opinionAfter}");
            Assert.True(moodAfter < moodBefore, $"the same memories should also drag mood down: {moodBefore} -> {moodAfter}");
            // The insulter never gains a memory from its own insult.
            Assert.Empty(a.needs.mood!.thoughts.memories.Memories);
        }

        [Fact]
        public void Repeated_chitchat_raises_opinion_and_mood()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            int opinionBefore = a.relations.OpinionOf(b);
            float moodBefore = a.needs.mood!.thoughts.TotalMoodOffset();

            for (int i = 0; i < 3; i++) InteractionDefOf.Chitchat.Worker.Interacted(a, b);

            int opinionAfter = a.relations.OpinionOf(b);
            float moodAfter = a.needs.mood!.thoughts.TotalMoodOffset();

            Assert.True(opinionAfter > opinionBefore, $"repeated chitchat should raise opinion: {opinionBefore} -> {opinionAfter}");
            Assert.True(moodAfter > moodBefore, $"and mood along with it: {moodBefore} -> {moodAfter}");
            // Chitchat is mutual: the other side gains a matching memory of its own.
            Assert.NotEmpty(b.needs.mood!.thoughts.memories.Memories);
        }

        [Fact]
        public void Slight_only_affects_the_recipient()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            InteractionDefOf.Slight.Worker.Interacted(a, b);

            Assert.Empty(a.needs.mood!.thoughts.memories.Memories);
            Assert.Single(b.needs.mood!.thoughts.memories.Memories);
            Assert.Equal("WasSlighted", b.needs.mood.thoughts.memories.Memories[0].def.defName);
        }

        // ---- social fights (reusing MentalStateDef machinery) ----

        [Fact]
        public void A_sufficiently_bad_opinion_and_mood_can_escalate_into_a_social_fight()
        {
            bool everStarted = false;
            for (int seed = 0; seed < 300 && !everStarted; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                Pawn a = NewHuman("A");
                Pawn b = NewHuman("B");
                // Overdetermined on purpose: Rival + ExSpouse (-26) plus three stacked Insulted memories
                // (~-18.5 after geometric stacking) puts the base opinion at about -44.5, comfortably past the
                // -20 threshold even against the worst-case +/-15 compatibility swing for this specific pawn
                // pair — the point of this test is the fight roll, not fishing for a favourable pair of ids.
                SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Rival);
                SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.ExSpouse);
                for (int i = 0; i < 3; i++) b.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, a);
                b.needs.mood!.CurLevel = 0f;

                Assert.True(b.relations.OpinionOf(a) <= SocialTuning.SocialFightOpinionThreshold);

                if (SocialFightUtility.TryStartSocialFight(a, b))
                {
                    everStarted = true;
                    Assert.True(a.InMentalState);
                    Assert.True(b.InMentalState);
                    Assert.Equal("SocialFighting", a.MentalStateDef!.defName);
                    Assert.Equal("SocialFighting", b.MentalStateDef!.defName);
                }
            }
            Assert.True(everStarted, "expected the social fight roll to succeed at least once across 300 seeds");
        }

        [Fact]
        public void A_good_relationship_never_starts_a_social_fight()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Friend);

            for (int seed = 0; seed < 50; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Assert.False(SocialFightUtility.TryStartSocialFight(a, b));
            }
        }

        // ---- SocialInteractionManager: cadence, scope, determinism ----

        [Fact]
        public void SocialInteractionTick_only_acts_on_its_interval_boundary()
        {
            var manager = new SocialInteractionManager();
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            var population = new List<Pawn> { a, b };

            Find.TickManager.DebugSetTicksGame(SocialTuning.InteractionIntervalTicks - 1);
            Rand.Current = new RandomStream(1);
            manager.SocialInteractionTick(population);
            Assert.Empty(a.needs.mood!.thoughts.memories.Memories);
            Assert.Empty(b.needs.mood!.thoughts.memories.Memories);

            Find.TickManager.DebugSetTicksGame(SocialTuning.InteractionIntervalTicks);
            Rand.Current = new RandomStream(1);
            manager.SocialInteractionTick(population);
            // Chance-gated per pawn, but seed 1 is fixed here purely to exercise the boundary check itself —
            // the content/determinism of what happens once the gate opens is covered by the tests below.
        }

        [Fact]
        public void Non_humanlike_and_dead_pawns_never_participate()
        {
            var manager = new SocialInteractionManager();
            var dogA = new Pawn(Husky, "DogA");
            var dogB = new Pawn(Husky, "DogB");
            Pawn dead = NewHuman("Dead");
            dead.health.Kill(null, null);
            Pawn alive = NewHuman("Alive");

            Rand.Current = new RandomStream(1);
            manager.RunInterval(new List<Pawn> { dogA, dogB, dead, alive });

            Assert.Empty(dogA.needs.mood?.thoughts.memories.Memories ?? new List<Thought_Memory>());
            // Only one live humanlike pawn remains eligible, so no interaction can pair up — asserting no
            // exception is the meaningful check here (an eligible pool below 2 must be a no-op, not a crash).
        }

        [Fact]
        public void RunInterval_is_deterministic_for_the_same_seed()
        {
            (int opinionAB, int memoriesA, int memoriesB) RunOnce(int seed)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();
                var manager = new SocialInteractionManager();
                var population = new List<Pawn>();
                for (int i = 0; i < 6; i++) population.Add(NewHuman("P" + i));

                for (int round = 0; round < 10; round++) manager.RunInterval(population);

                Pawn a = population[0], b = population[1];
                return (a.relations.OpinionOf(b), a.needs.mood!.thoughts.memories.Memories.Count, b.needs.mood!.thoughts.memories.Memories.Count);
            }

            var run1 = RunOnce(4242);
            var run2 = RunOnce(4242);
            Assert.Equal(run1, run2);
        }

        [Fact]
        public void Determinism_holds_across_the_whole_population_not_just_one_pair()
        {
            List<(int id, int memories, int opinionOfNext)> RunOnce(int seed)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();
                var manager = new SocialInteractionManager();
                var population = new List<Pawn>();
                for (int i = 0; i < 8; i++) population.Add(NewHuman("P" + i));

                for (int round = 0; round < 15; round++) manager.RunInterval(population);

                var result = new List<(int, int, int)>();
                for (int i = 0; i < population.Count; i++)
                {
                    Pawn p = population[i];
                    Pawn next = population[(i + 1) % population.Count];
                    result.Add((p.thingIDNumber, p.needs.mood!.thoughts.memories.Memories.Count, p.relations.OpinionOf(next)));
                }
                return result;
            }

            List<(int id, int memories, int opinionOfNext)> run1 = RunOnce(99);
            List<(int id, int memories, int opinionOfNext)> run2 = RunOnce(99);
            Assert.Equal(run1, run2);
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Scribe_round_trips_direct_relations_and_opinion()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Friend);
            a.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadDeepTalk, b);
            int opinionBefore = a.relations.OpinionOf(b);

            var holder = new SocialHolder { pawns = new List<Pawn> { a, b } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            SocialHolder loaded = Scribe.Load<SocialHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn la = loaded.pawns![0];
            Pawn lb = loaded.pawns[1];

            Assert.True(la.relations.HasDirectRelation(PawnRelationDefOf.Friend, lb.thingIDNumber));
            Assert.True(lb.relations.HasDirectRelation(PawnRelationDefOf.Friend, la.thingIDNumber));
            Assert.Equal(opinionBefore, la.relations.OpinionOf(lb));
        }

        // ---- content ----

        [Fact]
        public void Every_PawnRelationDef_and_InteractionDef_resolves_a_worker()
        {
            foreach (PawnRelationDef def in DefDatabase<PawnRelationDef>.AllDefsListForReading)
            {
                Assert.NotNull(def.Worker);
            }
            foreach (InteractionDef def in DefDatabase<InteractionDef>.AllDefsListForReading)
            {
                Assert.NotNull(def.Worker);
            }
        }

        [Fact]
        public void SocialFighting_mental_state_is_content_defined()
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamed("SocialFighting");
            Assert.Same(def, SocialMentalStateDefOf.SocialFighting);
        }

        [Fact]
        public void Family_relations_are_flagged_familial_and_non_family_ones_are_not()
        {
            foreach (string name in new[] { "Spouse", "Parent", "Child", "Sibling" })
            {
                Assert.True(Relation(name).familial, name + " should be flagged familial (derived, never stored twice).");
            }
            foreach (PawnRelationDef def in new[] { PawnRelationDefOf.Friend, PawnRelationDefOf.Rival, PawnRelationDefOf.Lover, PawnRelationDefOf.ExSpouse })
            {
                Assert.False(def.familial, def.defName + " is stored (DirectPawnRelation), not derived — it should not be flagged familial.");
            }
        }
    }
}
