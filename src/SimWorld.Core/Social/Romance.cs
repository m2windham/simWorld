using System;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social
{
    /// <summary>
    /// Courtship and its ending (<c>docs/status.json</c>, <c>social.romance</c>).
    ///
    /// <para/><b>What was already there, and what was not.</b> Demography owns marriage: a couple marries in
    /// <c>FamilyManager</c>'s own sweep and founds a household, and widowhood already frees the survivor to
    /// remarry. The <c>Lover</c> and <c>ExSpouse</c> relation defs shipped with the Social module — and
    /// nothing ever created either of them. So what was missing was everything on both sides of a marriage:
    /// the courtship that leads to one, and the ending that is not a death.
    ///
    /// <para/><b>Built as interactions, not as a new sweep.</b> A romance attempt and a breakup are things one
    /// person does to another, which is exactly what <see cref="InteractionDef"/> already models, and
    /// <see cref="SocialInteractionManager"/> already walks the population picking interactions by weight. So
    /// romance needs no tick of its own: it is two more workers whose weight is zero for almost every pair.
    /// The alternative — a second population sweep on its own interval — would have been a parallel mechanism
    /// for no gain.
    ///
    /// <para/><b>Deliberately not modelled.</b> Infidelity: an initiator who is married never attempts
    /// romance, so a marriage cannot be broken by a third person here. RimWorld models cheating in some
    /// depth (opinion of the spouse, a cheating-risk roll, a discovery event and its fallout), and half of it
    /// would be worse than none. Orientation is not modelled either — this port carries no orientation on a
    /// pawn, so a romance attempt is gated on age, kinship and opinion alone, and adding a gate the data
    /// cannot support would be inventing it.
    ///
    /// <para/><b>Divorce leaves the household standing.</b> Ending a marriage clears both spouse ids and
    /// records an <c>ExSpouse</c> relation, the same shape widowhood already uses. It does not split the
    /// <c>Family</c>: a household in this port is a lineage rather than a residence, and dissolving one
    /// because a marriage ended would rewrite the civilization's ancestry. Stated here rather than hidden.
    /// </summary>
    public static class RomanceUtility
    {
        /// <summary>
        /// True when <paramref name="initiator"/> could plausibly court <paramref name="recipient"/>: both
        /// adults, neither married, not already lovers, and not close kin. Kinship is checked through the
        /// relation ids demography already keeps — parents, children, siblings and spouse — rather than a new
        /// consanguinity model.
        /// </summary>
        public static bool CanCourt(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null || ReferenceEquals(initiator, recipient)) return false;
            if (initiator.Dead || recipient.Dead) return false;
            if (!IsAdult(initiator) || !IsAdult(recipient)) return false;
            if (initiator.relations.IsMarried || recipient.relations.IsMarried) return false;
            if (AreLovers(initiator, recipient)) return false;
            return !AreCloseKin(initiator, recipient);
        }

        public static bool AreLovers(Pawn a, Pawn b) =>
            a.relations.HasDirectRelation(PawnRelationDefOf.Lover, b.thingIDNumber);

        public static bool AreMarried(Pawn a, Pawn b) =>
            a.relations.spouseId == b.thingIDNumber && b.relations.spouseId == a.thingIDNumber;

        private static bool IsAdult(Pawn pawn) =>
            pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalYears >= SocialTuning.RomanceMinAgeYears;

        /// <summary>Parent, child, sibling or spouse — the relations demography actually records. A cousin is
        /// not caught, because nothing in this port knows what a cousin is.</summary>
        public static bool AreCloseKin(Pawn a, Pawn b)
        {
            Pawn_RelationsTracker ra = a.relations;
            Pawn_RelationsTracker rb = b.relations;
            int idA = a.thingIDNumber;
            int idB = b.thingIDNumber;

            if (ra.spouseId == idB || rb.spouseId == idA) return true;
            if (ra.parentIdA == idB || ra.parentIdB == idB) return true;
            if (rb.parentIdA == idA || rb.parentIdB == idA) return true;
            if (ra.childIds.Contains(idB) || rb.childIds.Contains(idA)) return true;

            // Siblings: any shared parent. None (-1) is not a parent, so two orphans are not siblings.
            if (ra.parentIdA != Pawn_RelationsTracker.None
                && (ra.parentIdA == rb.parentIdA || ra.parentIdA == rb.parentIdB)) return true;
            if (ra.parentIdB != Pawn_RelationsTracker.None
                && (ra.parentIdB == rb.parentIdA || ra.parentIdB == rb.parentIdB)) return true;

            return false;
        }

        /// <summary>
        /// Chance a romance attempt is accepted. Opinion is most of it — being liked is the prerequisite for
        /// being loved — with the pair's stable compatibility factor as the rest, so two people who simply do
        /// not click stay unlikely however polite they are to each other. Every constant here is this port's
        /// own; the tests pin the trend, never the number.
        /// </summary>
        public static float AcceptanceChance(Pawn initiator, Pawn recipient)
        {
            float opinion = recipient.relations.OpinionOf(initiator);
            if (opinion < SocialTuning.RomanceMinOpinion) return 0f;

            float fromOpinion = GenMath.Clamp01(
                (opinion - SocialTuning.RomanceMinOpinion) / (SocialTuning.MaxOpinion - SocialTuning.RomanceMinOpinion));
            float compatibility = SocialUtility.CompatibilityFactor(initiator.thingIDNumber, recipient.thingIDNumber)
                / SocialTuning.CompatibilityRange;
            float chance = SocialTuning.RomanceBaseAcceptChance
                + fromOpinion * SocialTuning.RomanceOpinionAcceptWeight
                + compatibility * SocialTuning.RomanceCompatibilityAcceptWeight;
            return GenMath.Clamp01(chance);
        }

        /// <summary>Rolls a romance attempt and applies whichever outcome. Returns true if it was accepted.</summary>
        public static bool TryRomance(Pawn initiator, Pawn recipient)
        {
            if (!CanCourt(initiator, recipient)) return false;

            if (!Rand.Current.Chance(AcceptanceChance(initiator, recipient)))
            {
                // Being turned down stings; being hit on by someone you do not want is its own small harm.
                initiator.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.RebuffedMyRomanceAttempt, recipient);
                recipient.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.FailedRomanceAttemptOnMe, initiator);
                return false;
            }

            BecomeLovers(initiator, recipient);
            return true;
        }

        public static void BecomeLovers(Pawn a, Pawn b)
        {
            SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.Lover);
            a.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.BecameLovers, b);
            b.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.BecameLovers, a);
            // "Category: detail", like every other free-form line in this codebase. Without the prefix
            // MomentCurator.CategoryForFreeform takes the whole sentence as the category, and since every
            // couple's sentence is unique, *every* romance was a "first of its kind" and went into
            // MomentCurator.Moments — a list that is deliberately never trimmed. The civilization's
            // landmarks were filling up with who was seeing whom, unboundedly, at a rate that grows with the
            // population. Found while writing ChronicleFame, which reads that list.
            Find.Storyteller?.RecordChronicle("Romance: " + a.Label + " and " + b.Label + " became lovers.");
        }

        /// <summary>
        /// Ends whatever the two of them had. A marriage ends as a divorce — both spouse ids cleared, a mutual
        /// <c>ExSpouse</c> recorded, the household left standing — and anything less ends as a breakup. Both
        /// leave a memory on each side; a divorce leaves a heavier one, because it was more.
        /// </summary>
        public static bool TryBreakUp(Pawn a, Pawn b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;

            if (AreMarried(a, b))
            {
                a.relations.spouseId = Pawn_RelationsTracker.None;
                b.relations.spouseId = Pawn_RelationsTracker.None;
                SocialUtility.RemoveMutualRelation(a, b, PawnRelationDefOf.Lover);
                SocialUtility.AddMutualRelation(a, b, PawnRelationDefOf.ExSpouse);
                GainBothWays(a, b, SocialThoughtDefOf.Divorced);
                // Prefixed for the same reason as BecomeLovers' own line above — see that comment.
                Find.Storyteller?.RecordChronicle("Divorce: " + a.Label + " and " + b.Label + " divorced.");
                return true;
            }

            if (!AreLovers(a, b)) return false;

            SocialUtility.RemoveMutualRelation(a, b, PawnRelationDefOf.Lover);
            GainBothWays(a, b, SocialThoughtDefOf.BrokeUpWithMe);
            return true;
        }

        private static void GainBothWays(Pawn a, Pawn b, Thoughts.ThoughtDef def)
        {
            a.needs.mood?.thoughts.memories.TryGainMemory(def, b);
            b.needs.mood?.thoughts.memories.TryGainMemory(def, a);
        }
    }

    /// <summary>
    /// One person making a pass at another. Weight is zero for the overwhelming majority of pairs — anyone
    /// married, related, too young, already together, or simply not liked enough — so the population sweep
    /// almost never picks it, which is the correct frequency for it.
    /// </summary>
    public sealed class InteractionWorker_RomanceAttempt : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!RomanceUtility.CanCourt(initiator, recipient)) return 0f;
            float opinion = initiator.relations.OpinionOf(recipient);
            if (opinion < SocialTuning.RomanceMinOpinion) return 0f;

            // Scales with how much the initiator likes the recipient: attraction is not uniform over everyone
            // who clears the bar.
            float over = (opinion - SocialTuning.RomanceMinOpinion) / (SocialTuning.MaxOpinion - SocialTuning.RomanceMinOpinion);
            return SocialTuning.RomanceBaseWeight * (1f + over);
        }

        public override void Interacted(Pawn initiator, Pawn recipient) =>
            RomanceUtility.TryRomance(initiator, recipient);
    }

    /// <summary>
    /// The end of it. Only ever selectable between two people who are actually together and have stopped
    /// getting on — a couple who still like each other are never offered this, whatever else happens to them.
    /// </summary>
    public sealed class InteractionWorker_Breakup : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            bool together = RomanceUtility.AreMarried(initiator, recipient) || RomanceUtility.AreLovers(initiator, recipient);
            if (!together) return 0f;
            if (SocialUtility.SocialMemoryOpinionOffset(initiator, recipient) > SocialTuning.BreakupMaxMemoryOpinion) return 0f;

            // A marriage is harder to leave than an affair — the same unhappiness ends one sooner than the
            // other. This port's own judgement; pinned by a test comparing the two weights, not the numbers.
            return RomanceUtility.AreMarried(initiator, recipient)
                ? SocialTuning.BreakupBaseWeight * SocialTuning.DivorceWeightFactor
                : SocialTuning.BreakupBaseWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient) =>
            RomanceUtility.TryBreakUp(initiator, recipient);
    }
}
