using System;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Rolls a <see cref="QualityCategory"/> for a freshly made item (RimWorld: <c>RimWorld.QualityUtility</c>).
    /// <b>Deviation:</b> RimWorld's real crafted-quality table is a published, non-Gaussian distribution per
    /// skill level; this approximates its shape with a Gaussian centred on the skill (Poor-ish at level 0,
    /// Excellent-ish at level 20, spread of one tier), +2 tiers for an Inspired creation (RimWorld's
    /// Inspiration_Creativity — modelled here as a plain bool since no Inspiration system exists yet). Tests
    /// assert the trend (higher skill / inspiration raises mean quality and unlocks higher tiers), not
    /// RimWorld's exact per-level percentages.
    /// </summary>
    public static class QualityUtility
    {
        public static float LerpDouble(float inFrom, float inTo, float outFrom, float outTo, float x)
        {
            float span = inTo - inFrom;
            float t = span == 0f ? 0f : (x - inFrom) / span;
            return outFrom + (outTo - outFrom) * t;
        }

        /// <summary>
        /// Awful(0)..Masterwork(5) only without <paramref name="inspired"/>; Legendary(6) is reachable solely
        /// through the +2 inspired bonus, matching RimWorld where Legendary is exclusively an Inspired-creativity
        /// (or similarly rare) outcome.
        /// </summary>
        public static QualityCategory GenerateQualityCreatedByPawn(int relevantSkillLevel, bool inspired)
        {
            float center = LerpDouble(0f, 20f, 0.5f, 4.2f, relevantSkillLevel);
            int q = GenMath.Clamp((int)Math.Round((double)Rand.Gaussian(center, 1f), MidpointRounding.AwayFromZero), 0, 5);
            if (inspired) q = Math.Min(q + 2, 6);
            return (QualityCategory)q;
        }

        public static QualityCategory GenerateQualityCreatedByPawn(Pawn pawn, SkillDef relevantSkill, bool inspired = false)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            int level = relevantSkill != null ? pawn.skills?.GetSkill(relevantSkill)?.Level ?? 0 : 0;
            return GenerateQualityCreatedByPawn(level, inspired);
        }

        /// <summary>Every tier equally likely.</summary>
        public static QualityCategory GenerateQualityRandomEqualChance() => (QualityCategory)Rand.Range(0, 7);

        /// <summary>Weighted toward Normal (RimWorld: how a trader's stock rolls quality).</summary>
        public static QualityCategory GenerateQualityTraderItem()
        {
            var weights = new (QualityCategory q, float w)[]
            {
                (QualityCategory.Poor, 10f),
                (QualityCategory.Normal, 60f),
                (QualityCategory.Good, 25f),
                (QualityCategory.Excellent, 5f),
            };
            GenCollection.TryRandomElementByWeight(weights, e => e.w, Rand.Current, out var picked);
            return picked.q;
        }

        /// <summary>Base map-generation roll (furniture already standing on a generated map); same shape as a trader item.</summary>
        public static QualityCategory GenerateQualityBaseGen() => GenerateQualityTraderItem();
    }
}
