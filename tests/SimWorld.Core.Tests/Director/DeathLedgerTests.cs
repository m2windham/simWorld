using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// Every death of ours arrives somewhere, classified — which two of the five causes previously did not.
    ///
    /// <para/><b>The hole this closes.</b> <c>Storyteller.RecordDeath</c> had exactly two production callers:
    /// <c>FamilyManager.HandleDeath</c> for age and <c>SettlementRaidResolver</c> for an abstractly-resolved
    /// raid. A citizen who starved and a citizen who died of plague both reach
    /// <c>Pawn_HealthTracker.Kill</c> through the health system's own funnel and nothing routed them back, so
    /// neither reached the chronicle at all — <see cref="DeathCause"/>'s own doc said so in as many words.
    /// The two causes that a pressure on this civilization would actually kill people with were the two
    /// nothing could see, which makes "how many did that cost us" unanswerable and any measurement built on
    /// it worthless.
    ///
    /// <para/><b>Why the counters are not the chronicle.</b> Asserted here as behaviour rather than trusted:
    /// the chronicle is capped and the ledger is not, so a long enough run disagrees with itself.
    /// </summary>
    public class DeathLedgerTests : ContentTestBase
    {
        public DeathLedgerTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            CorpseDefGenerator.EnsureGenerated();
        }

        private static DeathLedger Ledger => Find.Storyteller.deaths;

        /// <summary>Registers a civilization holding these pawns — the roster that decides whose death is
        /// ours, asked with the same call the adaptation charge beside it asks.</summary>
        private static void CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
        }

        private static Hediff HediffOf(string defName, Pawn pawn) =>
            HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(defName), pawn);

        // ---- the two causes that had no sink at all ----

        [Fact]
        public void A_citizen_who_starves_is_counted_as_starvation()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            p.health.Kill(null, HediffOf("Malnutrition", p));

            Assert.Equal(DeathCause.Starvation, p.health.CauseOfDeath);
            Assert.Equal(1, Ledger[DeathCause.Starvation]);
            Assert.Equal(1, Ledger.Total);
        }

        [Fact]
        public void A_citizen_who_dies_of_plague_is_counted_as_disease()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            p.health.Kill(null, HediffOf("Plague", p));

            Assert.Equal(DeathCause.Disease, p.health.CauseOfDeath);
            Assert.Equal(1, Ledger[DeathCause.Disease]);
        }

        // ---- the discriminator, which is the part that could silently rot ----

        [Fact]
        public void Blood_loss_is_an_injury_finishing_its_job_not_a_disease()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            // BloodLoss and Plague both arrive as a lethal hediff with no damage behind them. The only thing
            // separating them is that a body builds immunity to one of them, which is why the classifier
            // reads the comp rather than a list of defNames that content would outgrow.
            p.health.Kill(null, HediffOf("BloodLoss", p));

            Assert.Equal(DeathCause.Injury, p.health.CauseOfDeath);
            Assert.Equal(0, Ledger[DeathCause.Disease]);
            Assert.Equal(1, Ledger[DeathCause.Injury]);
        }

        [Fact]
        public void The_shipped_diseases_are_all_immunizable_and_the_other_lethal_hediffs_are_not()
        {
            // The rule the classifier leans on, asserted against content rather than against itself: if a
            // disease ever ships without an immunity comp this fails here rather than silently reclassifying
            // every death it causes as an injury.
            foreach (string disease in new[] { "WoundInfection", "Flu", "Plague" })
            {
                Assert.True(
                    DeathCauseClassifier.IsImmunizable(DefDatabase<HediffDef>.GetNamed(disease)),
                    disease + " must be immunizable for the disease classifier to see it");
            }

            foreach (string notDisease in new[] { "BloodLoss", "Malnutrition" })
            {
                Assert.False(
                    DeathCauseClassifier.IsImmunizable(DefDatabase<HediffDef>.GetNamed(notDisease)),
                    notDisease + " must not read as a disease");
            }
        }

        // ---- the causes that already worked, pinned so this change did not move them ----

        [Fact]
        public void A_killer_that_knows_why_it_killed_is_believed_over_the_evidence()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            // What FamilyManager.HandleDeath does: no damage, no culprit, but it knows perfectly well this
            // was age. Unclassified that would read as Unknown.
            p.health.Kill(null, null, DeathCause.Age);

            Assert.Equal(DeathCause.Age, p.health.CauseOfDeath);
            Assert.Equal(1, Ledger[DeathCause.Age]);
            Assert.Equal(0, Ledger[DeathCause.Unknown]);
        }

        [Fact]
        public void Damage_behind_a_death_makes_it_an_injury()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            p.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(DeathCause.Injury, p.health.CauseOfDeath);
            Assert.Equal(1, Ledger[DeathCause.Injury]);
        }

        [Fact]
        public void A_death_with_nothing_to_go_on_is_unknown_rather_than_guessed()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            p.health.Kill(null, null);

            Assert.Equal(DeathCause.Unknown, p.health.CauseOfDeath);
            Assert.Equal(1, Ledger[DeathCause.Unknown]);
        }

        // ---- whose deaths count, and how many times ----

        [Fact]
        public void A_raider_dying_is_not_a_cost_to_the_civilization()
        {
            Pawn ours = NewHuman();
            Pawn raider = NewHuman();
            CivilizationOf(ours);   // raider deliberately left out of the roster

            raider.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(DeathCause.Injury, raider.health.CauseOfDeath);   // still classified
            Assert.Equal(0, Ledger.Total);                                  // just not ours
        }

        [Fact]
        public void One_body_is_counted_once_however_many_times_it_is_killed()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);

            p.health.Kill(null, HediffOf("Malnutrition", p));
            p.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);
            p.health.Kill(null, null, DeathCause.Age);

            // Kill returns early for a pawn already dead, which is why the ledger needs no memory of who it
            // has already seen. If that ever stops being true this is where it shows up.
            Assert.Equal(1, Ledger.Total);
            Assert.Equal(1, Ledger[DeathCause.Starvation]);
        }

        [Fact]
        public void The_causes_are_counted_apart_from_each_other()
        {
            var starved = new List<Pawn>();
            for (int i = 0; i < 3; i++) starved.Add(NewHuman());
            Pawn plagued = NewHuman();
            Pawn shot = NewHuman();

            var all = new List<Pawn>(starved) { plagued, shot };
            CivilizationOf(all.ToArray());

            foreach (Pawn p in starved) p.health.Kill(null, HediffOf("Malnutrition", p));
            plagued.health.Kill(null, HediffOf("Plague", plagued));
            shot.health.Kill(new DamageInfo(DamageDefOf.Bullet, 999f), null);

            Assert.Equal(3, Ledger[DeathCause.Starvation]);
            Assert.Equal(1, Ledger[DeathCause.Disease]);
            Assert.Equal(1, Ledger[DeathCause.Injury]);
            Assert.Equal(0, Ledger[DeathCause.Age]);
            Assert.Equal(5, Ledger.Total);
        }

        // ---- why it is a counter and not a reading of the chronicle ----

        [Fact]
        public void The_ledger_outlives_the_chronicle_that_evicts_its_own_oldest_entries()
        {
            Pawn narrator = NewHuman();
            CivilizationOf(narrator);

            // Fill the chronicle past its cap with ordinary lines, the way a long run does.
            for (int i = 0; i < global::SimWorld.Director.Storyteller.ChronicleCapacity + 50; i++)
            {
                Find.Storyteller.RecordChronicle("a quiet day");
            }

            narrator.health.Kill(null, HediffOf("Malnutrition", narrator));

            Assert.Equal(
                global::SimWorld.Director.Storyteller.ChronicleCapacity,
                Find.Storyteller.Chronicle.Count);
            Assert.Equal(1, Ledger[DeathCause.Starvation]);
        }

        // ---- scribe ----

        [Fact]
        public void Scribe_round_trip_of_the_ledger()
        {
            var ledger = new DeathLedger();
            ledger.Record(DeathCause.Starvation);
            ledger.Record(DeathCause.Starvation);
            ledger.Record(DeathCause.Disease);
            ledger.Record(DeathCause.Age);

            string xml = Scribe.SaveToString(ledger, "deaths");
            DeathLedger loaded = Scribe.Load<DeathLedger>(xml, "deaths", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(2, loaded[DeathCause.Starvation]);
            Assert.Equal(1, loaded[DeathCause.Disease]);
            Assert.Equal(1, loaded[DeathCause.Age]);
            Assert.Equal(0, loaded[DeathCause.Injury]);
            Assert.Equal(4, loaded.Total);
        }

        [Fact]
        public void The_ledger_survives_a_save_of_the_whole_storyteller()
        {
            Pawn p = NewHuman();
            CivilizationOf(p);
            p.health.Kill(null, HediffOf("Plague", p));

            Find.Storyteller.def = DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic");
            Find.Storyteller.difficulty = DefDatabase<DifficultyDef>.GetNamed("Medium");

            string xml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            var loaded = Scribe.Load<global::SimWorld.Director.Storyteller>(
                xml, "storyteller", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(1, loaded.deaths[DeathCause.Disease]);
        }
    }
}
