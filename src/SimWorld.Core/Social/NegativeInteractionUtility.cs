using System;

using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Social
{
    /// <summary>The two traits RimWorld's negative-interaction weight reads by name. Their own class, per the
    /// repository rule that a new <c>[DefOf]</c> binding does not go in a shared file.</summary>
    [DefOf]
    public static class NegativeInteractionTraitDefOf
    {
        public static TraitDef Kind = null!;
        public static TraitDef Abrasive = null!;
    }

    /// <summary>
    /// How likely a pawn is to pick a put-down for someone (RimWorld:
    /// <c>RimWorld.NegativeInteractionUtility</c>), shared by the two interactions that wound a relationship,
    /// <see cref="InteractionWorker_Insult"/> and <see cref="InteractionWorker_Slight"/>.
    /// <para/>
    /// <b>Ported:</b> the opinion curve (a pair that likes each other is almost never rude: ×0.1 at +50, ×0 at
    /// +100; a pair that does not is up to ×6 as likely), the Abrasive multiplier (×2.3) and the Kind trait
    /// (never). <b>Not ported, on purpose:</b> RimWorld's second curve, on the pair's
    /// <c>CompatibilityWith</c>. This port folds the pair's constant compatibility into
    /// <see cref="SocialUtility.OpinionOf"/> itself (<see cref="SocialUtility.CompatibilityFactor"/>), so the
    /// opinion curve already carries it, and applying a compatibility curve to the same number again would
    /// count it twice.
    /// <para/>
    /// What this replaced: a flat ×6 below zero opinion, nothing at all above it (a pair of close friends was
    /// exactly as likely to insult each other as strangers), and a ×3 when the insulter's mood was low that
    /// RimWorld does not have.
    /// </summary>
    public static class NegativeInteractionUtility
    {
        /// <summary>RimWorld's <c>NegativeInteractionUtility.AbrasiveSelectionChanceFactor</c>.</summary>
        public const float AbrasiveSelectionChanceFactor = 2.3f;

        /// <summary>RimWorld's <c>OpinionFactorCurve</c>, point for point.</summary>
        private static readonly SimpleCurve OpinionFactorCurve = new SimpleCurve
        {
            { -100f, 6f },
            { -50f, 4f },
            { -25f, 2f },
            { 0f, 1f },
            { 50f, 0.1f },
            { 100f, 0f },
        };

        /// <summary>The multiplier on a negative interaction's base weight for this initiator towards this
        /// recipient. 0 for a Kind pawn.</summary>
        public static float NegativeInteractionChanceFactor(Pawn initiator, Pawn recipient)
        {
            if (initiator == null) throw new ArgumentNullException(nameof(initiator));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));

            if (initiator.story.traits.HasTrait(NegativeInteractionTraitDefOf.Kind)) return 0f;

            float factor = OpinionFactorCurve.Evaluate(initiator.relations.OpinionOf(recipient));
            if (initiator.story.traits.HasTrait(NegativeInteractionTraitDefOf.Abrasive)) factor *= AbrasiveSelectionChanceFactor;
            return factor;
        }
    }
}
