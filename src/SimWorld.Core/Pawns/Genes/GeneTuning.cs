namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// Tuning constants for the Genes module. RimWorld's own per-gene inheritance-selection weighting and its
    /// metabolism-to-hunger-rate curve were not available to source in this sandbox (no decompiled source, no
    /// network access — see CLAUDE.md's rule on numbers that cannot be sourced), so every constant here is
    /// SimWorld's own: chosen to be simple and defensible, and pinned by tests
    /// (<c>tests/SimWorld.Core.Tests/Pawns/GenesTests.cs</c>) that assert the property each constant is meant
    /// to produce — a sign, a direction, a bound — rather than the literal value.
    /// </summary>
    public static class GeneTuning
    {
        /// <summary>
        /// Chance <see cref="GeneInheritanceUtility"/> keeps an endogene carried by only one parent. A gene
        /// both parents share is always inherited (see <see cref="GeneInheritanceUtility.InheritEndogenesFrom"/>);
        /// this is the coin-flip half — one independent <see cref="Sim.Rand.Chance"/> roll per single-parent
        /// gene, a flat 50% standing in for whatever per-gene selection-weight roll RimWorld's own
        /// <c>Pawn_GeneTracker</c> inheritance path actually uses.
        /// </summary>
        public const float SingleParentInheritanceChance = 0.5f;

        /// <summary>Hunger-rate change per point of a pawn's total <see cref="GeneDef.biostatMet"/>. Positive
        /// metabolism raises hunger; negative lowers it — the one directional fact this port is confident
        /// RimWorld's real curve shares, even without its exact shape.</summary>
        public const float HungerRateChangePerMetabolismPoint = 0.1f;

        /// <summary>Floor and ceiling on the metabolism-derived hunger factor, so a heavily gene-edited pawn's
        /// <see cref="Pawn.HungerRate"/> never reaches zero (starves instantly regardless of food) or an
        /// absurd multiple.</summary>
        public const float MinHungerRateFactorFromMetabolism = 0.2f;

        public const float MaxHungerRateFactorFromMetabolism = 3f;

        /// <summary>Converts a pawn's <see cref="Pawn_GeneTracker.MetabolismTotal"/> into the factor
        /// <see cref="Pawn.HungerRate"/> multiplies in, clamped to the bounds above.</summary>
        public static float HungerRateFactorFromMetabolism(int metabolismTotal)
        {
            float factor = 1f + metabolismTotal * HungerRateChangePerMetabolismPoint;
            return GenMath.Clamp(factor, MinHungerRateFactorFromMetabolism, MaxHungerRateFactorFromMetabolism);
        }
    }
}
