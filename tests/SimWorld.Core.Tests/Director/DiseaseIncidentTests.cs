using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="IncidentWorker_Disease"/>'s two closed gaps: it used to tell the player nothing, and a
    /// disease death had no instigator for <see cref="StorytellerDeathEvents.SourceOf"/> to ask, so it went
    /// uncredited to the incident that caused it. <see cref="Health.AbstractDiseaseResolverTests"/> covers the
    /// resolver itself; this file covers the incident worker around it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DiseaseIncidentTests : ContentTestBase
    {
        public DiseaseIncidentTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.LetterStack = new LetterStack();
        }

        private static IncidentDef Plague => DefDatabase<IncidentDef>.GetNamed("Disease_Plague");

        private static IncidentDef Flu => DefDatabase<IncidentDef>.GetNamed("Disease_Flu");

        private static HediffDef PlagueHediff => DefDatabase<HediffDef>.GetNamed("Plague");

        private static CivilizationTarget CivilizationOf(params Pawn[] members)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.pawns.AddRange(members);
            return target;
        }

        // ---- the letter ----

        [Fact]
        public void The_disease_incident_tells_the_player()
        {
            Pawn victim = NewHuman("Villager");
            CivilizationTarget target = CivilizationOf(victim);

            int before = Find.LetterStack.LettersListForReading.Count;
            bool fired = Flu.Worker.TryExecute(new IncidentParms { target = target, points = 40f });

            Assert.True(fired);
            Assert.True(
                Find.LetterStack.LettersListForReading.Count > before,
                "a citizen falling ill that nobody is told about is indistinguishable from nothing happening");
            Assert.Contains(
                Find.LetterStack.LettersListForReading,
                l => l.label.Contains("Disease", System.StringComparison.Ordinal));
        }

        // ---- attribution ----

        [Fact]
        public void A_disease_death_is_attributed_to_the_incident_that_caused_it()
        {
            Pawn victim = NewHuman("Victim");
            CivilizationTarget target = CivilizationOf(victim);

            bool fired = Plague.Worker.TryExecute(new IncidentParms { target = target, points = 40f });
            Assert.True(fired);

            Hediff plague = victim.health.hediffSet.GetFirstHediffOfDef(PlagueHediff)!;
            Assert.Equal("Disease_Plague", plague.sourceIncident);

            // Kill through the exact Severity -> CheckForStateChange -> Kill funnel
            // AbstractDiseaseResolver itself uses to resolve a lethal case.
            plague.Severity = plague.def.lethalSeverity;

            Assert.True(victim.Dead);
            Assert.Equal(1, Find.Storyteller.deaths.Total);
            Assert.Equal(1, Find.Storyteller.deaths.AttributedTo("Disease_Plague"));
        }

        // ---- the invariant: an instrument may under-claim, never over-claim ----

        /// <summary>
        /// The exact shape of the bug this repository has already paid for once: a pawn nobody's civilization
        /// roster names still dies, and its hediff still carries provenance, but the ledger's own membership
        /// gate must keep it off both axes — never a death "attributed" that the ledger never counted.
        /// </summary>
        [Fact]
        public void Attribution_never_exceeds_counted_deaths_for_a_non_member_victim()
        {
            // Deliberately not registered with any CivilizationTarget: nobody's roster names this pawn, so
            // StorytellerPawnEvents.IsCivilizationMember must read false for it.
            Pawn outsider = NewHuman("Outsider");
            Hediff hediff = outsider.health.AddHediff(PlagueHediff);
            hediff.sourceIncident = "Disease_Plague";

            hediff.Severity = PlagueHediff.lethalSeverity;

            Assert.True(outsider.Dead);
            Assert.Equal(0, Find.Storyteller.deaths.Total);
            Assert.Equal(0, Find.Storyteller.deaths.AttributedTo("Disease_Plague"));
        }

        /// <summary>Both edges together: one death that counts and is attributed, one that neither counts nor
        /// is attributed, and the ledger's own arithmetic never lets the second axis outrun the first.</summary>
        [Fact]
        public void Attribution_never_exceeds_counted_deaths_across_a_mixed_outbreak()
        {
            Pawn ours = NewHuman("Ours");
            CivilizationOf(ours);
            Pawn stranger = NewHuman("Stranger"); // never registered anywhere

            Hediff a = ours.health.AddHediff(PlagueHediff);
            a.sourceIncident = "Disease_Plague";
            Hediff b = stranger.health.AddHediff(PlagueHediff);
            b.sourceIncident = "Disease_Plague";

            a.Severity = PlagueHediff.lethalSeverity;
            b.Severity = PlagueHediff.lethalSeverity;

            Assert.True(ours.Dead);
            Assert.True(stranger.Dead);
            Assert.True(
                Find.Storyteller.deaths.TotalAttributed <= Find.Storyteller.deaths.Total,
                $"attributed {Find.Storyteller.deaths.TotalAttributed} of {Find.Storyteller.deaths.Total} counted");
            Assert.Equal(1, Find.Storyteller.deaths.Total);
            Assert.Equal(1, Find.Storyteller.deaths.AttributedTo("Disease_Plague"));
        }
    }
}
