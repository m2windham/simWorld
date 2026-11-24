using System;
using SimWorld.Pawns;

namespace SimWorld.Health
{
    /// <summary>
    /// The one health figure a person is shown with. RimWorld's own summary health
    /// (<see cref="SummaryHealthHandler"/>, ported here) counts wounds and missing parts by their share of the
    /// body and nothing else, which is right for what RimWorld uses it for (threat points, the storyteller) and
    /// wrong as a readout: the second playtest found a downed colonist, starving and missing a jaw, shown at 99%.
    /// RimWorld's health tab sets the capacities beside that number, so a reader sees both; one number has to
    /// carry both, so this one is no higher than the weakest capacity a person needs to get about and stay
    /// alive, and a downed person never reads as more than <see cref="DownedCeiling"/>. Display only: the
    /// storyteller and the raid resolver keep reading <see cref="SummaryHealthHandler"/> as RimWorld does.
    /// </summary>
    public static class HealthReadout
    {
        /// <summary>The most a downed person reads as: well short of half, so "on the ground" never looks like
        /// "mostly fine". This port's own figure, not a RimWorld one.</summary>
        public const float DownedCeiling = 0.25f;

        /// <summary>Health in 0..1: body summary, capped by the weakest of consciousness, moving, manipulation,
        /// breathing and blood pumping (a capacity above 1, from a bionic part, adds nothing), and by
        /// <see cref="DownedCeiling"/> while downed. Zero when dead.</summary>
        public static float Of(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawn.Dead) return 0f;
            float health = pawn.health.summaryHealth.SummaryHealthPercent;
            PawnCapacitiesHandler capacities = pawn.health.capacities;
            health = Math.Min(health, capacities.GetLevel(PawnCapacityDefOf.Consciousness));
            health = Math.Min(health, capacities.GetLevel(PawnCapacityDefOf.Moving));
            // An animal has no hands to lose; RimWorld does not show it manipulation either.
            if (pawn.RaceProps.Humanlike) health = Math.Min(health, capacities.GetLevel(PawnCapacityDefOf.Manipulation));
            health = Math.Min(health, capacities.GetLevel(PawnCapacityDefOf.Breathing));
            health = Math.Min(health, capacities.GetLevel(PawnCapacityDefOf.BloodPumping));
            if (pawn.Downed) health = Math.Min(health, DownedCeiling);
            return Math.Max(0f, health);
        }
    }
}
