using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Economy
{
    /// <summary>
    /// What an individual pawn is worth against their race's base (RimWorld: <c>RimWorld.PriceUtility</c>,
    /// read by <c>StatWorker_MarketValue</c> whenever the thing being priced is a pawn). A person is not worth
    /// the same as the species: a hale, skilled adult is worth more than the base and a wounded, unskilled
    /// child much less, and that spread is what makes the storyteller's wealth term (<see cref="Director.WealthWatcher.WealthPawns"/>)
    /// move when a settlement grows, heals, trains or buries someone.
    ///
    /// <para/><b>Ported from the 1.0 decompile</b> (<c>RimWorld/PriceUtility.cs</c>, <c>PawnQualityPriceFactor</c>).
    /// The factor starts at 1 and is multiplied by, in order: health (<see cref="SummaryHealthImpact"/> of the
    /// way to nothing at zero health), each pawn capacity (<see cref="MissingCapacityFactor"/> if the pawn
    /// cannot do it at all, else halfway to nothing at level zero — <see cref="CapacityImpact"/>), the average
    /// skill level through <see cref="AverageSkillCurve"/>, and the current life stage's
    /// <see cref="LifeStageDef.marketValueFactor"/>; each trait then adds its
    /// <see cref="TraitDegreeData.marketValueFactorOffset"/>; the whole is floored at <see cref="MinFactor"/>.
    /// Every constant is the decompile's.
    ///
    /// <para/><b>Left out, and why.</b> (1) <c>PawnQualityPriceOffset</c>: it adds the market value of the item a
    /// hediff would spawn when removed (a bionic arm, a harvestable organ); this port's added-part hediff has
    /// no link to its item (<c>HediffDef</c> carries no <c>spawnThingOnRemoved</c>), so there is nothing to
    /// add. (2) The current game's +20% of the pawn's Beauty stat, which 1.0 does not have. (3) Skills only
    /// exist on a humanlike in RimWorld (an animal's <c>pawn.skills</c> is null); this port gives every pawn a
    /// skill tracker at level zero, so the skill term is applied to humanlikes only, which is RimWorld's
    /// outcome and not a different rule.
    /// </summary>
    public static class PriceUtility
    {
        /// <summary>The least a pawn's factor can be (RimWorld: <c>PriceUtility.MinFactor</c>).</summary>
        public const float MinFactor = 0.1f;

        /// <summary>Fraction of value lost as summary health falls from full to nothing (RimWorld: <c>SummaryHealthImpact</c>).</summary>
        public const float SummaryHealthImpact = 0.8f;

        /// <summary>Fraction of value lost per capacity as its level falls from full to nothing (RimWorld: <c>CapacityImpact</c>).</summary>
        public const float CapacityImpact = 0.5f;

        /// <summary>Multiplier for each capacity the pawn cannot do at all (RimWorld: <c>MissingCapacityFactor</c>).</summary>
        public const float MissingCapacityFactor = 0.6f;

        /// <summary>Average skill level to value multiplier (RimWorld: <c>PriceUtility.AverageSkillCurve</c>).</summary>
        public static readonly SimpleCurve AverageSkillCurve = new SimpleCurve
        {
            { 0f, 0.2f },
            { 5.5f, 1f },
            { 20f, 3f },
        };

        /// <summary>
        /// The multiplier on <paramref name="pawn"/>'s race base market value. See the class doc for the terms.
        /// <para/>
        /// <paramref name="sampledHealthFraction"/> is for a pawn the tiering system does not run in full
        /// (<see cref="PawnTier.Statistical"/>): its hediff set is deliberately never opened to answer a
        /// question like this one (<see cref="God.GodRollup"/> and <c>Pawn_TierTracker.SampledHealthFraction</c>
        /// say why), so the coarse readout stands in for summary health and the per-capacity terms — which
        /// need that hediff set — are skipped (treated as full capacity). A pawn in any other tier passes null
        /// and is priced from its real health, exactly as RimWorld does.
        /// </summary>
        public static float PawnQualityPriceFactor(Pawn pawn, float? sampledHealthFraction = null)
        {
            if (pawn == null) throw new System.ArgumentNullException(nameof(pawn));

            float factor = 1f;
            float health = sampledHealthFraction ?? pawn.health.summaryHealth.SummaryHealthPercent;
            factor *= GenMath.Lerp(1f - SummaryHealthImpact, 1f, GenMath.Clamp01(health));

            if (sampledHealthFraction == null)
            {
                IReadOnlyList<PawnCapacityDef> capacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading;
                for (int i = 0; i < capacities.Count; i++)
                {
                    if (!pawn.health.capacities.CapableOf(capacities[i]))
                    {
                        factor *= MissingCapacityFactor;
                    }
                    else
                    {
                        // Mathf.Lerp clamps its t; GenMath.Lerp does not, and a capacity above 1 (a bionic) must
                        // not be worth more than a whole one.
                        factor *= GenMath.Lerp(1f - CapacityImpact, 1f, GenMath.Clamp01(pawn.health.capacities.GetLevel(capacities[i])));
                    }
                }
            }

            if (pawn.RaceProps.Humanlike && pawn.skills != null && pawn.skills.skills.Count > 0)
            {
                float sum = 0f;
                List<SkillRecord> skills = pawn.skills.skills;
                for (int i = 0; i < skills.Count; i++) sum += skills[i].Level;
                factor *= AverageSkillCurve.Evaluate(sum / skills.Count);
            }

            factor *= pawn.ageTracker?.CurLifeStage?.marketValueFactor ?? 1f;

            TraitSet? traits = pawn.story?.traits;
            if (traits != null)
            {
                for (int i = 0; i < traits.allTraits.Count; i++)
                {
                    factor += traits.allTraits[i].CurrentData.marketValueFactorOffset;
                }
            }

            return factor < MinFactor ? MinFactor : factor;
        }
    }
}
