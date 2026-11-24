using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// The health a person is shown with. The second playtest showed a downed colonist, starving and missing a
    /// jaw, at 99%: the body summary counts wounds by their share of the body, and a jaw is a sliver of it.
    /// </summary>
    public class HealthReadoutTests : ContentTestBase
    {
        public HealthReadoutTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_whole_healthy_person_reads_full_health()
        {
            Pawn p = NewHuman();
            Assert.Equal(1f, HealthReadout.Of(p), 3);
            Assert.Equal(p.health.summaryHealth.SummaryHealthPercent, HealthReadout.Of(p), 3);
        }

        [Fact]
        public void A_downed_person_missing_a_jaw_reads_low_though_the_body_summary_stays_high()
        {
            Pawn p = NewHuman();
            p.health.AddHediff(HediffDefOf.MissingBodyPart, p.RaceProps.body!.GetPartByLabel("jaw"));
            p.health.ForceDowned = true;
            Assert.True(p.Downed);

            // The ported RimWorld figure barely moves for a jaw; the readout says "on the ground".
            Assert.True(p.health.summaryHealth.SummaryHealthPercent > 0.9f);
            Assert.True(HealthReadout.Of(p) <= HealthReadout.DownedCeiling);
        }

        [Fact]
        public void Malnutrition_that_dulls_consciousness_pulls_the_readout_down_with_it()
        {
            Pawn p = NewHuman();
            float before = HealthReadout.Of(p);
            Hediff starving = p.health.AddHediff(HediffDefOf.Malnutrition);
            starving.Severity = 0.7f;
            p.health.capacities.Notify_CapacityLevelsDirty();

            float consciousness = p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            Assert.True(consciousness < 1f, "severe malnutrition should dull consciousness");
            Assert.True(HealthReadout.Of(p) < before);
            Assert.True(HealthReadout.Of(p) <= consciousness + 1e-4f);
        }

        [Fact]
        public void An_animal_is_not_marked_down_for_having_no_hands()
        {
            var dog = new Pawn(Husky, "Rex");
            Assert.Equal(1f, HealthReadout.Of(dog), 3);
        }

        [Fact]
        public void The_dead_read_zero()
        {
            Pawn p = NewHuman();
            p.health.Kill(null, null);
            Assert.Equal(0f, HealthReadout.Of(p));
        }
    }
}
