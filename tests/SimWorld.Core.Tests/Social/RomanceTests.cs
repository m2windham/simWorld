using System.Linq;

using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Social
{
    /// <summary>
    /// Courtship and its ending (<c>social.romance</c>). Demography already owned marriage; what is pinned
    /// here is everything on both sides of one — who may court whom, that being liked is what makes a
    /// romance likely, and that a couple who stop getting on can actually part.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RomanceTests : ContentTestBase
    {
        public RomanceTests(CoreContentFixture content) : base(content)
        {
        }

        private Pawn Adult()
        {
            Pawn pawn = NewHuman();
            pawn.ageTracker.ageBiologicalTicks = (long)(25 * GenDate.TicksPerYear);
            return pawn;
        }

        /// <summary>Friends who have talked properly: +12 from the relation, +6 from the memory. Memories
        /// have per-pawn stack limits, so raising opinion means adding different things, not the same thing
        /// repeatedly — which is the shipped behaviour, not a workaround.</summary>
        private static void Like(Pawn a, Pawn b)
        {
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Friend);
            a.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadDeepTalk, b);
            b.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadDeepTalk, a);
        }

        private static void LikeMore(Pawn a, Pawn b)
        {
            a.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadChitchat, b);
            b.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.HadChitchat, a);
        }

        /// <summary>Drives the memory ledger between two people firmly negative. Insults stack up to three
        /// per person at -8 each, so this is deterministic — no compatibility term, no relation label.</summary>
        private static void FallOut(Pawn a, Pawn b)
        {
            for (int i = 0; i < 3; i++)
            {
                a.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, b);
                b.needs.mood!.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, a);
            }
        }

        private static void Dislike(Pawn a, Pawn b) =>
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Rival);

        [Fact]
        public void Two_liking_adults_may_court_each_other()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            Like(a, b);

            Assert.True(RomanceUtility.CanCourt(a, b));
            Assert.True(RomanceUtility.AcceptanceChance(a, b) > 0f);
        }

        [Fact]
        public void A_child_is_never_courted_and_never_courts()
        {
            Pawn adult = Adult();
            Pawn child = NewHuman();
            child.ageTracker.ageBiologicalTicks = (long)(9 * GenDate.TicksPerYear);
            Like(adult, child);

            Assert.False(RomanceUtility.CanCourt(adult, child));
            Assert.False(RomanceUtility.CanCourt(child, adult));
        }

        [Fact]
        public void Close_kin_are_never_courted_however_well_they_get_on()
        {
            Pawn parent = Adult();
            Pawn child = Adult();
            child.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);
            parent.relations.Notify_ChildBorn(child.thingIDNumber);
            Like(parent, child);
            Assert.False(RomanceUtility.CanCourt(parent, child));

            Pawn siblingA = Adult();
            Pawn siblingB = Adult();
            siblingA.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);
            siblingB.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);
            Like(siblingA, siblingB);
            Assert.True(RomanceUtility.AreCloseKin(siblingA, siblingB));
            Assert.False(RomanceUtility.CanCourt(siblingA, siblingB));

            // Two people with no recorded parents are not siblings by virtue of both having none.
            Pawn orphanA = Adult();
            Pawn orphanB = Adult();
            Assert.False(RomanceUtility.AreCloseKin(orphanA, orphanB));
        }

        [Fact]
        public void A_married_citizen_does_not_court_anyone()
        {
            Pawn married = Adult();
            Pawn spouse = Adult();
            married.relations.spouseId = spouse.thingIDNumber;
            spouse.relations.spouseId = married.thingIDNumber;

            Pawn other = Adult();
            Like(married, other);

            // Infidelity is deliberately not modelled — see RomanceUtility's own remarks.
            Assert.False(RomanceUtility.CanCourt(married, other));
            Assert.False(RomanceUtility.CanCourt(other, married));
        }

        [Fact]
        public void Being_better_liked_makes_acceptance_likelier()
        {
            Pawn suitor = Adult();
            Pawn liked = Adult();
            Like(suitor, liked);
            float modest = RomanceUtility.AcceptanceChance(suitor, liked);

            LikeMore(suitor, liked);
            float better = RomanceUtility.AcceptanceChance(suitor, liked);

            Assert.True(better > modest, "a better-liked suitor was no likelier to be accepted");
        }

        [Fact]
        public void Nobody_barely_tolerated_is_courted_at_all()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            Dislike(a, b);

            Assert.Equal(0f, RomanceUtility.AcceptanceChance(a, b));
            Assert.Equal(0f, new InteractionWorker_RomanceAttempt().RandomSelectionWeight(a, b));
        }

        [Fact]
        public void An_accepted_romance_makes_them_lovers_on_both_sides()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            RomanceUtility.BecomeLovers(a, b);

            Assert.True(RomanceUtility.AreLovers(a, b));
            Assert.True(RomanceUtility.AreLovers(b, a));
            Assert.False(RomanceUtility.CanCourt(a, b), "a couple were still offered courtship");
        }

        [Fact]
        public void Lovers_who_stop_getting_on_can_part_and_a_happy_couple_cannot()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            RomanceUtility.BecomeLovers(a, b);

            var worker = new InteractionWorker_Breakup();
            Assert.Equal(0f, worker.RandomSelectionWeight(a, b));

            FallOut(a, b);
            Assert.True(worker.RandomSelectionWeight(a, b) > 0f, "an unhappy couple were never offered a breakup");

            Assert.True(RomanceUtility.TryBreakUp(a, b));
            Assert.False(RomanceUtility.AreLovers(a, b));
        }

        [Fact]
        public void A_marriage_ends_as_a_divorce_and_leaves_an_ex_spouse_behind()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            a.relations.spouseId = b.thingIDNumber;
            b.relations.spouseId = a.thingIDNumber;

            Assert.True(RomanceUtility.TryBreakUp(a, b));

            Assert.Equal(Pawn_RelationsTracker.None, a.relations.spouseId);
            Assert.Equal(Pawn_RelationsTracker.None, b.relations.spouseId);
            Assert.True(a.relations.HasDirectRelation(PawnRelationDefOf.ExSpouse, b.thingIDNumber));
            Assert.True(b.relations.HasDirectRelation(PawnRelationDefOf.ExSpouse, a.thingIDNumber));

            // Divorce ends the marriage, not the lineage: the household is a line of descent here.
            Assert.Equal(a.relations.familyId, a.relations.familyId);
        }

        [Fact]
        public void A_marriage_is_harder_to_leave_than_an_affair()
        {
            Pawn lonerA = Adult();
            Pawn lonerB = Adult();
            RomanceUtility.BecomeLovers(lonerA, lonerB);

            Pawn wedA = Adult();
            Pawn wedB = Adult();
            wedA.relations.spouseId = wedB.thingIDNumber;
            wedB.relations.spouseId = wedA.thingIDNumber;

            FallOut(lonerA, lonerB);
            FallOut(wedA, wedB);

            var worker = new InteractionWorker_Breakup();
            Assert.True(worker.RandomSelectionWeight(wedA, wedB) < worker.RandomSelectionWeight(lonerA, lonerB),
                "leaving a marriage was as easy as ending an affair");
        }

        [Fact]
        public void Nothing_happens_when_two_people_who_are_not_together_break_up()
        {
            Pawn a = Adult();
            Pawn b = Adult();
            Assert.False(RomanceUtility.TryBreakUp(a, b));
            Assert.False(a.relations.HasDirectRelation(PawnRelationDefOf.ExSpouse, b.thingIDNumber));
        }

        [Fact]
        public void Romance_and_breakup_are_ordinary_interactions_the_population_sweep_can_pick()
        {
            var defs = SimWorld.Defs.DefDatabase<InteractionDef>.AllDefsListForReading;
            Assert.Contains(defs, d => d.defName == "RomanceAttempt");
            Assert.Contains(defs, d => d.defName == "Breakup");
            Assert.IsType<InteractionWorker_RomanceAttempt>(InteractionDefOf.RomanceAttempt.Worker);
            Assert.IsType<InteractionWorker_Breakup>(InteractionDefOf.Breakup.Worker);
        }
    }
}
