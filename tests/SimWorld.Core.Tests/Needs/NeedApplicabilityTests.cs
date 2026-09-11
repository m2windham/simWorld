using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Needs
{
    /// <summary>
    /// Which needs a pawn actually has (<see cref="Pawn_NeedsTracker.ShouldHaveNeed"/>).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> The method consulted
    /// <c>minIntelligence</c> and <c>RaceProps.needsRest</c> and nothing else, so every applicability flag a
    /// <see cref="NeedDef"/> carries was ignored and every pawn was given every need its species could hold.
    /// The pointed case is a prisoner keeping a recreation need: this port gives a prisoner no way to obtain
    /// joy at all, so the need could only fall, and a need that can only fall is a permanent mood penalty
    /// nobody authored.
    ///
    /// <para/><b>The second half is when the question is asked.</b> A pawn is built before it is enfactioned
    /// and can be captured or released long afterwards, so the answer taken in the constructor goes stale.
    /// The sweep re-runs from <c>Pawn.faction</c>'s setter and from <c>CaptureUtility.Capture</c>; the tests
    /// below drive those transitions rather than calling the sweep.
    /// </summary>
    public class NeedApplicabilityTests : ContentTestBase
    {
        public NeedApplicabilityTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static NeedDef Need(string name) => DefDatabase<NeedDef>.GetNamed(name);

        private static Faction NewFaction(string defName, string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static bool Has(Pawn p, string needDefName) => p.needs.TryGetNeed(Need(needDefName)) != null;

        /// <summary>A captor and a victim of hostile factions, the victim down and takeable.</summary>
        private static (Pawn captor, Pawn victim) CaptureReadyPair()
        {
            Faction ours = NewFaction("PlayerCivilization", "Ours");
            Faction theirs = NewFaction("RoughOutlanders", "Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);

            Pawn captor = NewHuman("Captor");
            captor.faction = ours;
            Pawn victim = NewHuman("Victim");
            victim.faction = theirs;
            victim.health.ForceDowned = true;
            return (captor, victim);
        }

        // ---- the headline ----

        [Fact]
        public void A_prisoner_loses_the_recreation_need_they_have_no_way_to_satisfy()
        {
            (Pawn captor, Pawn victim) = CaptureReadyPair();
            Assert.True(Has(victim, "Joy"), "a free pawn should have recreation, or this proves nothing");

            Assert.NotNull(CaptureUtility.Capture(captor, victim));

            Assert.False(Has(victim, "Joy"));
            Assert.Null(victim.needs.joy);
            // Everything else they still are: hunger and exhaustion do not care who is holding you.
            Assert.True(Has(victim, "Food"));
            Assert.True(Has(victim, "Rest"));
            Assert.True(Has(victim, "Mood"));
        }

        [Fact]
        public void Being_recruited_gives_it_back()
        {
            (Pawn captor, Pawn victim) = CaptureReadyPair();
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captor, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
            tracker.resistance = 0f;
            Assert.False(Has(victim, "Joy"));

            Assert.Equal(WardenUtility.InteractionOutcome.Recruited, WardenUtility.TryInteract(captor, victim));

            Assert.True(Has(victim, "Joy"));
            Assert.NotNull(victim.needs.joy);
        }

        [Fact]
        public void Being_released_gives_it_back_too()
        {
            (Pawn captor, Pawn victim) = CaptureReadyPair();
            CaptureUtility.Capture(captor, victim);
            Assert.False(Has(victim, "Joy"));

            Assert.True(WardenUtility.Release(victim));

            Assert.True(Has(victim, "Joy"));
        }

        // ---- the sweep runs when the answer can change ----

        [Fact]
        public void Gaining_a_faction_re_asks_the_question()
        {
            // A pawn is constructed before anything enfactions it, so the constructor's answer is the answer
            // for a factionless pawn. Without the sweep on the setter, a need gated on whose side a pawn is
            // on could never be granted to anybody — it would be withheld at construction and never revisited.
            NeedDef probe = Need("Beauty");
            bool was = probe.colonistsOnly;
            try
            {
                probe.colonistsOnly = true;
                Pawn p = NewHuman();
                Assert.False(Has(p, "Beauty"), "a factionless pawn should not have a colonists-only need");

                p.faction = NewFaction("PlayerCivilization", "Ours");

                Assert.True(Has(p, "Beauty"));
                Assert.True(PawnUtility.IsColonist(p));

                // And the other direction: losing the faction takes it away again.
                p.faction = null;
                Assert.False(Has(p, "Beauty"));
            }
            finally
            {
                probe.colonistsOnly = was;
            }
        }

        [Fact]
        public void The_flags_decide_and_they_are_asked_in_RimWorlds_order()
        {
            // Read off ShouldHaveNeed directly for the flags no shipped need sets, so the reader is pinned
            // even while the content stays silent (see dormant-seams.txt for why it does).
            Pawn colonist = NewHuman("Colonist");
            colonist.faction = NewFaction("PlayerCivilization", "Ours");
            Pawn outsider = NewHuman("Outsider");
            outsider.faction = NewFaction("RoughOutlanders", "Theirs");

            NeedDef probe = Need("Beauty");
            bool colonistsOnlyWas = probe.colonistsOnly;
            bool bothWas = probe.colonistAndPrisonersOnly;
            try
            {
                probe.colonistsOnly = true;
                Assert.True(colonist.needs.ShouldHaveNeed(probe));
                Assert.False(outsider.needs.ShouldHaveNeed(probe));

                probe.colonistsOnly = false;
                probe.colonistAndPrisonersOnly = true;
                Assert.True(colonist.needs.ShouldHaveNeed(probe));
                Assert.False(outsider.needs.ShouldHaveNeed(probe));

                // Species first, and absolutely: an animal is refused before anyone asks whose side it is on.
                Pawn dog = new Pawn(Husky, "Dog");
                dog.faction = colonist.faction;
                Assert.False(dog.needs.ShouldHaveNeed(probe));
            }
            finally
            {
                // These tests share one loaded DefDatabase for the whole run — put the shipped values back,
                // not the defaults, or every test after this one sees content nobody shipped.
                probe.colonistsOnly = colonistsOnlyWas;
                probe.colonistAndPrisonersOnly = bothWas;
            }
        }

        // ---- content ----

        [Fact]
        public void Recreation_is_the_one_shipped_need_a_prisoner_does_not_get()
        {
            List<string> neverOnPrisoner = DefDatabase<NeedDef>.AllDefsListForReading
                .Where(n => n.neverOnPrisoner).Select(n => n.defName).ToList();

            Assert.Equal(new[] { "Joy" }, neverOnPrisoner);
        }

        // ---- persistence ----

        [Fact]
        public void A_prisoners_missing_need_survives_a_save_and_load()
        {
            (Pawn captor, Pawn victim) = CaptureReadyPair();
            CaptureUtility.Capture(captor, victim);
            Assert.False(Has(victim, "Joy"));

            // Cleared before saving because Scribe resolves references within one save and the factions are
            // outside this one. It is also worth asserting on its own: the prisoner record lives on the
            // faction holding them, not on the pawn, so losing a faction does not free anybody.
            victim.faction = null;
            Assert.False(Has(victim, "Joy"));

            string xml = Scribe.SaveToString(victim, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Null(loaded.needs.TryGetNeed(Need("Joy")));
            Assert.NotNull(loaded.needs.TryGetNeed(Need("Food")));
        }
    }
}
