using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Tests.Content;
using SimWorld.Things;
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

        /// <summary>A small map plus two pawns standing on it. A social fight is a physical act between two
        /// people on the same map (<see cref="SocialFightUtility.SocialFightPossible"/>), so every test of
        /// one that expects it to happen has to put them somewhere.</summary>
        private static (SimWorld.Map.Map map, Pawn a, Pawn b) SpawnPair(int gap = 1)
        {
            var map = new SimWorld.Map.Map(12, 12, SimWorld.Map.TerrainDefOf.Soil);
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            GenSpawn.Spawn(a, new IntVec3(3, 0, 3), map);
            GenSpawn.Spawn(b, new IntVec3(3 + gap, 0, 3), map);
            return (map, a, b);
        }

        [Fact]
        public void A_bad_opinion_can_escalate_an_insult_into_a_social_fight()
        {
            bool everStarted = false;
            for (int seed = 0; seed < 300 && !everStarted; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                (_, Pawn a, Pawn b) = SpawnPair();
                // Overdetermined on purpose: Rival + ExSpouse (-26) plus three stacked Insulted memories
                // (~-18.5 after geometric stacking) puts the base opinion at about -44.5 even against the
                // worst-case +/-15 compatibility swing for this specific pawn pair — the point of this test
                // is the fight roll, not fishing for a favourable pair of ids.
                SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Rival);
                SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.ExSpouse);
                for (int i = 0; i < 3; i++) b.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, a);
                b.needs.mood!.CurLevel = 0f;

                Assert.True(b.relations.OpinionOf(a) < 0);

                if (SocialFightUtility.TryStartSocialFight(a, b))
                {
                    everStarted = true;
                    Assert.True(a.InMentalState);
                    Assert.True(b.InMentalState);
                    Assert.Equal("SocialFighting", a.MentalStateDef!.defName);
                    Assert.Equal("SocialFighting", b.MentalStateDef!.defName);

                    // TryStartMentalState only ever knows about the one pawn it belongs to; SocialFightUtility
                    // itself is responsible for pairing the two freshly-created instances together.
                    var fightA = Assert.IsType<MentalState_SocialFighting>(a.mindState.mentalStateHandler.CurState);
                    var fightB = Assert.IsType<MentalState_SocialFighting>(b.mindState.mentalStateHandler.CurState);
                    Assert.Same(b, fightA.otherPawn);
                    Assert.Same(a, fightB.otherPawn);
                }
            }
            Assert.True(everStarted, "expected the social fight roll to succeed at least once across 300 seeds");
        }

        /// <summary>
        /// RimWorld's fight chance has no opinion <i>threshold</i> — <c>SocialFightChance</c> scales a base
        /// chance continuously, 4× at opinion -100 down to 0.6× at +100 — so two friends can in principle
        /// come to blows and simply almost never do. The old version of this test asserted "never", which
        /// was this port's own hard gate rather than RimWorld's behaviour. What is actually true, and what is
        /// asserted here, is the ordering: the worse the opinion the likelier the fight, by a wide margin.
        /// </summary>
        [Fact]
        public void A_good_relationship_is_far_less_likely_to_start_a_social_fight_than_a_bad_one()
        {
            (_, Pawn friendA, Pawn friendB) = SpawnPair();
            SocialUtility.AddMutualRelation(friendA, friendB, PawnRelationDefOf.Friend);
            for (int i = 0; i < 3; i++) friendB.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadDeepTalk, friendA);
            float friendly = SocialFightUtility.SocialFightChance(friendA, friendB);

            Pawn.ResetThingIdCounter();
            (_, Pawn rivalA, Pawn rivalB) = SpawnPair();
            SocialUtility.AddMutualRelation(rivalA, rivalB, PawnRelationDefOf.Rival);
            SocialUtility.AddMutualRelation(rivalA, rivalB, PawnRelationDefOf.ExSpouse);
            for (int i = 0; i < 3; i++) rivalB.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, rivalA);
            float hostile = SocialFightUtility.SocialFightChance(rivalA, rivalB);

            Assert.True(friendly > 0f, "RimWorld's chance is continuous, never zero for a pawn who could fight");
            Assert.True(hostile > friendly * 1.5f,
                $"a rival should be markedly likelier to swing than a friend: {hostile:F4} vs {friendly:F4}");
        }

        /// <summary>
        /// The gate the old code did not have and the reason an unwatched settlement's citizens used to beat
        /// each other in a world that did not exist: a fight is two people on the same map.
        /// </summary>
        [Fact]
        public void Two_citizens_who_are_not_on_a_map_cannot_start_a_social_fight()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Rival);
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.ExSpouse);
            b.needs.mood!.CurLevel = 0f;

            Assert.False(SocialFightUtility.SocialFightPossible(b, a));
            Assert.Equal(0f, SocialFightUtility.SocialFightChance(a, b));
            for (int seed = 0; seed < 50; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Assert.False(SocialFightUtility.TryStartSocialFight(a, b));
            }
        }

        // ---- social fights: MentalState_SocialFighting mechanics (blows, ending) ----

        /// <summary>Starts the mental state on both sides directly, bypassing SocialFightUtility's own
        /// opinion/mood/chance gate (already covered above) — these tests are about what the state itself
        /// does once running, not about whether it gets triggered.</summary>
        private static void StartFightDirect(Pawn a, Pawn b)
        {
            Assert.True(a.mindState.mentalStateHandler.TryStartMentalState(SocialMentalStateDefOf.SocialFighting, "test"));
            Assert.True(b.mindState.mentalStateHandler.TryStartMentalState(SocialMentalStateDefOf.SocialFighting, "test"));
            ((MentalState_SocialFighting)a.mindState.mentalStateHandler.CurState!).otherPawn = b;
            ((MentalState_SocialFighting)b.mindState.mentalStateHandler.CurState!).otherPawn = a;
        }

        [Fact]
        public void Social_fight_never_outlives_its_maxTicksBeforeRecovery()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            StartFightDirect(a, b);

            MentalStateDef def = SocialMentalStateDefOf.SocialFighting;
            RunTicks(def.maxTicksBeforeRecovery + 100, a, b);

            Assert.False(a.InMentalState, "a social fight must not outlive its Def's maxTicksBeforeRecovery");
            Assert.False(b.InMentalState);
        }

        /// <summary>
        /// The blows now come from a job (<c>JobGiver_SocialFighting</c> → <c>JobDriver_SocialFight</c>),
        /// driven by the think tree's mental-state tier, not from the mental state's own tick — so this test
        /// puts both pawns on a map next to each other and lets the ordinary job machinery run.
        /// </summary>
        [Fact]
        public void Social_fight_actually_trades_blows_through_the_job_system()
        {
            bool sawDamage = false;
            for (int seed = 0; seed < 20 && !sawDamage; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                (_, Pawn a, Pawn b) = SpawnPair();
                StartFightDirect(a, b);

                RunTicks(SocialMentalStateDefOf.SocialFighting.maxTicksBeforeRecovery + 100, a, b);

                if (a.health.hediffSet.hediffs.Count > 0 || b.health.hediffSet.hediffs.Count > 0) sawDamage = true;
            }
            Assert.True(sawDamage, "expected at least one landed blow across 20 seeds of a full-duration fight");
        }

        /// <summary>
        /// The other half of the same defect: a fight that cannot reach its opponent lands nothing at all.
        /// The old mental state cast its verb at range zero from its own tick, so two pawns on opposite sides
        /// of a map — or, worse, on no map — hurt each other anyway.
        /// </summary>
        [Fact]
        public void A_social_fight_across_an_unreachable_gap_lands_no_blows()
        {
            var map = new SimWorld.Map.Map(12, 12, SimWorld.Map.TerrainDefOf.Soil);
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            GenSpawn.Spawn(a, new IntVec3(1, 0, 1), map);
            // Unspawned: there is nobody there to hit, which is exactly the unwatched-settlement case.
            StartFightDirect(a, b);

            RunTicks(SocialMentalStateDefOf.SocialFighting.maxTicksBeforeRecovery + 100, a, b);

            Assert.Empty(a.health.hediffSet.hediffs);
            Assert.Empty(b.health.hediffSet.hediffs);
        }

        /// <summary>
        /// RimWorld hands both parties a <c>HadCatharticFight</c> or <c>HadAngeringFight</c> memory when the
        /// state ends, half the time each. This port had neither, so a scuffle could only ever cost mood and
        /// never clear the air. Asserted as "one of the two, every time" rather than as a 50/50 frequency.
        /// </summary>
        [Fact]
        public void A_finished_social_fight_leaves_both_parties_a_memory_of_it()
        {
            (_, Pawn a, Pawn b) = SpawnPair();
            StartFightDirect(a, b);
            RunTicks(SocialMentalStateDefOf.SocialFighting.maxTicksBeforeRecovery + 100, a, b);

            Assert.False(a.InMentalState);
            foreach (Pawn p in new[] { a, b })
            {
                if (p.Dead) continue;
                bool hasMemory = p.needs.mood!.thoughts.memories.Memories.Any(m =>
                    m.def == SocialFightThoughtDefOf.HadCatharticFight || m.def == SocialFightThoughtDefOf.HadAngeringFight);
                Assert.True(hasMemory, p.Label + " came out of a social fight with no memory of having had one");
            }
        }

        /// <summary>
        /// Half of what made brawling feed itself: the fight had to be able to end well sometimes. Over many
        /// seeds both outcomes have to show up, and the good one has to actually be good for mood.
        /// </summary>
        [Fact]
        public void A_social_fight_ends_cathartically_about_as_often_as_it_ends_badly()
        {
            int cathartic = 0, angering = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                (_, Pawn a, Pawn b) = SpawnPair();
                StartFightDirect(a, b);
                RunTicks(SocialMentalStateDefOf.SocialFighting.maxTicksBeforeRecovery + 100, a, b);
                if (a.Dead) continue;

                foreach (Thought_Memory m in a.needs.mood!.thoughts.memories.Memories)
                {
                    if (m.def == SocialFightThoughtDefOf.HadCatharticFight) cathartic++;
                    else if (m.def == SocialFightThoughtDefOf.HadAngeringFight) angering++;
                }
            }
            Assert.True(cathartic > 0 && angering > 0,
                $"both fight outcomes should occur over 60 seeds (cathartic {cathartic}, angering {angering})");
            Assert.True(SocialFightThoughtDefOf.HadCatharticFight.stages[0].baseMoodEffect > 0f);
            Assert.True(SocialFightThoughtDefOf.HadAngeringFight.stages[0].baseMoodEffect < 0f);
        }

        [Fact]
        public void Social_fight_ends_for_both_sides_the_moment_one_participant_goes_down()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            StartFightDirect(a, b);
            Assert.True(a.InMentalState);
            Assert.True(b.InMentalState);

            a.health.ForceDowned = true;
            RunTicks(1, a, b);

            Assert.False(a.InMentalState, "a downed participant recovers immediately (recoverFromDowned)");
            Assert.False(b.InMentalState, "the other side should stand down too once its opponent is downed");
        }

        [Fact]
        public void Social_fight_fist_power_reads_gentler_than_every_content_melee_weapon()
        {
            float weakestWeaponPower = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.tools != null)
                .SelectMany(d => d.tools!)
                .Min(t => t.power);
            Assert.True(SocialTuning.SocialFightFistPower < weakestWeaponPower,
                $"a bare-fisted social fight should read as gentler than the weakest weapon in content: " +
                $"{SocialTuning.SocialFightFistPower} vs {weakestWeaponPower}");
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

        [Fact]
        public void Scribe_round_trips_a_social_fight_including_the_cross_linked_otherPawn()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            StartFightDirect(a, b);
            RunTicks(30, a, b); // let some age/verb state accumulate before saving

            var holder = new SocialHolder { pawns = new List<Pawn> { a, b } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            SocialHolder loaded = Scribe.Load<SocialHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn la = loaded.pawns![0];
            Pawn lb = loaded.pawns[1];

            Assert.True(la.InMentalState);
            Assert.True(lb.InMentalState);
            Assert.Equal("SocialFighting", la.MentalStateDef!.defName);
            var fightA = Assert.IsType<MentalState_SocialFighting>(la.mindState.mentalStateHandler.CurState);
            var fightB = Assert.IsType<MentalState_SocialFighting>(lb.mindState.mentalStateHandler.CurState);
            Assert.Same(lb, fightA.otherPawn);
            Assert.Same(la, fightB.otherPawn);

            // The loaded state keeps working: a fresh Verb_MeleeAttack rebuilds against the loaded pawns
            // without error, and the fight (well short of its 100-tick minimum) is still running.
            RunTicks(5, la, lb);
            Assert.True(la.InMentalState);
            Assert.True(lb.InMentalState);
        }

        // ---- situational mood thoughts from social life (content: Thoughts_Social.xml) ----

        [Fact]
        public void Having_a_friend_or_a_rival_drives_the_matching_situational_mood_thought()
        {
            Pawn p = NewHuman("P");
            Pawn friend = NewHuman("Friend");
            Pawn rival = NewHuman("Rival");
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            Assert.Equal(0f, thoughts.TotalMoodOffset());

            SocialUtility.AddMutualRelation(p, friend, PawnRelationDefOf.Friend);
            thoughts.situational.Notify_SituationalThoughtsDirty();
            Assert.Equal(3f, thoughts.TotalMoodOffset());

            SocialUtility.AddMutualRelation(p, rival, PawnRelationDefOf.Rival);
            thoughts.situational.Notify_SituationalThoughtsDirty();
            Assert.Equal(0f, thoughts.TotalMoodOffset(), 3); // +3 friend and -3 rival cancel out

            SocialUtility.RemoveMutualRelation(p, friend, PawnRelationDefOf.Friend);
            thoughts.situational.Notify_SituationalThoughtsDirty();
            Assert.Equal(-3f, thoughts.TotalMoodOffset());

            SocialUtility.RemoveMutualRelation(p, rival, PawnRelationDefOf.Rival);
            thoughts.situational.Notify_SituationalThoughtsDirty();
            Assert.Equal(0f, thoughts.TotalMoodOffset());
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
            Assert.Equal(typeof(MentalState_SocialFighting), def.stateClass);
        }

        [Fact]
        public void HasFriend_and_HasRival_are_content_defined_situational_thoughts()
        {
            ThoughtDef hasFriend = DefDatabase<ThoughtDef>.GetNamed("HasFriend");
            ThoughtDef hasRival = DefDatabase<ThoughtDef>.GetNamed("HasRival");
            Assert.True(hasFriend.IsSituational);
            Assert.True(hasRival.IsSituational);
            Assert.Same(PawnRelationDefOf.Friend, hasFriend.requiredDirectRelation);
            Assert.Same(PawnRelationDefOf.Rival, hasRival.requiredDirectRelation);
        }

        [Fact]
        public void IsSocial_is_true_only_for_thoughts_that_carry_an_opinion_effect()
        {
            Assert.True(SocialThoughtDefOf.HadChitchat.IsSocial);
            Assert.True(SocialThoughtDefOf.HadDeepTalk.IsSocial);
            Assert.True(SocialThoughtDefOf.Insulted.IsSocial);
            Assert.True(SocialThoughtDefOf.WasSlighted.IsSocial);
            // Mood-only memories and situational thoughts never move opinion.
            Assert.False(DefDatabase<ThoughtDef>.GetNamed("Catharsis").IsSocial);
            Assert.False(DefDatabase<ThoughtDef>.GetNamed("HasFriend").IsSocial);
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
