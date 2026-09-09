using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social
{
    /// <summary>Small talk: always available, no gate — the mundane default every pair can fall back on
    /// (RimWorld: <c>InteractionWorker_Chitchat</c>, the highest-commonality interaction with no requirement).</summary>
    public sealed class InteractionWorker_Chitchat : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient) => SocialTuning.ChitchatBaseWeight;

        public override void Interacted(Pawn initiator, Pawn recipient) =>
            GainMemoryBothWays(initiator, recipient, SocialThoughtDefOf.HadChitchat);
    }

    /// <summary>
    /// A real conversation: only selectable between pawns who already get on (RimWorld: <c>InteractionWorker_DeepTalk</c>
    /// requires a positive opinion first) — otherwise weight 0, so it never displaces chitchat for a stranger
    /// or a rival.
    /// </summary>
    public sealed class InteractionWorker_DeepTalk : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator.relations.OpinionOf(recipient) < SocialTuning.DeepTalkMinOpinion) return 0f;
            return SocialTuning.DeepTalkBaseWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient) =>
            GainMemoryBothWays(initiator, recipient, SocialThoughtDefOf.HadDeepTalk);
    }

    /// <summary>
    /// A deliberate put-down: rare by default, far likelier from an initiator who already dislikes the
    /// recipient or is in a bad mood (RimWorld's own combination for <c>InteractionWorker_Insult</c>'s
    /// weighting). Only the recipient gains a memory (<c>Insulted</c>, already shipped with the mood system);
    /// the insult can escalate into a social fight — see <see cref="SocialFightUtility.TryStartSocialFight"/>.
    /// </summary>
    public sealed class InteractionWorker_Insult : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            float weight = SocialTuning.InsultBaseWeight;
            if (initiator.relations.OpinionOf(recipient) < 0) weight *= SocialTuning.InsultLowOpinionWeightFactor;
            float mood = initiator.needs.mood?.CurLevelPercentage ?? 0.5f;
            if (mood < SocialTuning.InsultLowMoodThreshold) weight *= SocialTuning.InsultLowMoodWeightFactor;
            return weight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient)
        {
            recipient.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.Insulted, initiator);
            SocialFightUtility.TryStartSocialFight(initiator, recipient);
        }
    }

    /// <summary>A milder, often unintentional put-down: the same low-opinion gate as insult but no fight
    /// escalation and a smaller memory (<c>WasSlighted</c>) — RimWorld's own lighter negative interaction
    /// alongside the full insult.</summary>
    public sealed class InteractionWorker_Slight : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            float weight = SocialTuning.SlightBaseWeight;
            if (initiator.relations.OpinionOf(recipient) < 0) weight *= SocialTuning.SlightLowOpinionWeightFactor;
            return weight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient) =>
            recipient.needs.mood?.thoughts.memories.TryGainMemory(SocialThoughtDefOf.WasSlighted, initiator);
    }

    /// <summary>
    /// The social-fight escalation roll shared by any interaction that can trigger one (today: insult only).
    /// Needs both a bad opinion of the aggressor *and* a bad mood on the recipient's side before it even rolls
    /// (RimWorld's own combination), then starts <see cref="SocialMentalStateDefOf.SocialFighting"/> — the
    /// existing <see cref="MindState.MentalStateHandler"/> machinery, not a parallel fight system — on both
    /// participants at once, since a fight is never one-sided.
    /// </summary>
    public static class SocialFightUtility
    {
        public static bool TryStartSocialFight(Pawn initiator, Pawn recipient)
        {
            if (recipient.relations.OpinionOf(initiator) > SocialTuning.SocialFightOpinionThreshold) return false;
            float mood = recipient.needs.mood?.CurLevelPercentage ?? 1f;
            if (mood > SocialTuning.SocialFightMoodThreshold) return false;

            float chance = SocialTuning.BaseSocialFightChance * recipient.story.traits.SocialFightChanceFactor();
            if (!Rand.Chance(chance)) return false;

            bool started = recipient.mindState.mentalStateHandler.TryStartMentalState(SocialMentalStateDefOf.SocialFighting, "SocialFight");
            bool startedOther = initiator.mindState.mentalStateHandler.TryStartMentalState(SocialMentalStateDefOf.SocialFighting, "SocialFight");
            // Each TryStartMentalState above only ever knows about the one pawn it belongs to (see
            // MentalStateHandler.TryStartMentalState) — pair the two freshly-created instances together here,
            // the only place that has both pawns in hand at once.
            if (started && recipient.mindState.mentalStateHandler.CurState is MentalState_SocialFighting recipientFight)
            {
                recipientFight.otherPawn = initiator;
            }
            if (startedOther && initiator.mindState.mentalStateHandler.CurState is MentalState_SocialFighting initiatorFight)
            {
                initiatorFight.otherPawn = recipient;
            }
            return started;
        }
    }
}
