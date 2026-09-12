using System;

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
    /// The social-fight escalation roll shared by any interaction that can trigger one (today: insult only),
    /// then starting <see cref="SocialMentalStateDefOf.SocialFighting"/> — the existing
    /// <see cref="MindState.MentalStateHandler"/> machinery, not a parallel fight system — on both
    /// participants at once, since a fight is never one-sided.
    ///
    /// <para/><b>What was here before, and why it was wrong.</b> This used to gate the roll behind two hard
    /// thresholds — "opinion of the aggressor below −20 <i>and</i> the recipient's mood below 0.4" — and then
    /// roll a flat 15%, and its own doc called that combination "RimWorld's own". It is not.
    /// <c>Pawn_InteractionsTracker.SocialFightChance</c> has no mood term at all and no opinion threshold; it
    /// takes a per-interaction base chance and multiplies it by a chain of <i>continuous</i> factors, so a
    /// fight is possible between any two people and simply very unlikely between people who get on. The
    /// numbers that chain produces are far smaller than a flat 15%: the base chance for an insult is 4% and
    /// the opinion factor only reaches 1.6× at opinion −20. That whole chain is ported below.
    ///
    /// <para/><b>And it now asks whether a fight is physically possible at all</b>
    /// (<see cref="SocialFightPossible"/>, RimWorld's own method). The clause that matters most here is the
    /// one RimWorld gets for free and this port does not: <see cref="SocialInteractionManager"/> deliberately
    /// has no spatial model — "any two living, humanlike pawns in the given population can interact" — while
    /// a <i>fight</i> is two people standing next to each other hitting each other. Before this, the citizens
    /// of a settlement with no map in existence brawled, bled and died. See
    /// <see cref="MindState.MentalState_SocialFighting"/> for the measurement.
    /// </summary>
    public static class SocialFightUtility
    {
        public static bool TryStartSocialFight(Pawn initiator, Pawn recipient)
        {
            float chance = SocialFightChance(initiator, recipient);
            if (chance <= 0f) return false;
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

        /// <summary>
        /// Whether these two <i>could</i> come to blows at all (RimWorld:
        /// <c>Pawn_InteractionsTracker.SocialFightPossible</c>). Ported clause by clause, with two
        /// substitutions this port's data forces and one addition its design forces:
        /// <list type="bullet">
        /// <item>RimWorld's <c>HasAnyVerbForSocialFight</c> walks <c>verbTracker.AllVerbs</c> for a usable
        /// melee verb; no race in this port declares <c>tools</c>, so the question is instead whether
        /// <see cref="AI.AttackVerbUtility.NaturalWeaponFor"/> can build fists for them — the same
        /// substitution that method already documents.</item>
        /// <item>RimWorld's three <c>DevelopmentalStage</c> clauses (no babies; children only within six
        /// years of each other; no adult against anyone under 13) collapse to a single minimum age here,
        /// because this port has no developmental stages to ask about.
        /// <see cref="MinSocialFightAgeYears"/> is RimWorld's own 13.</item>
        /// <item><b>Both parties must be spawned on the same map.</b> RimWorld never states this because it
        /// cannot fail there — its interactions only ever happen between spawned pawns standing near each
        /// other. This port's interaction sweep is deliberately non-spatial, so the fight has to say it.</item>
        /// </list>
        /// <para/>
        /// <b>The consequence, stated plainly:</b> citizens of a settlement nobody has opened no longer brawl
        /// at all. They still insult each other and still carry the mood for it; they cannot punch each other
        /// in a world that does not exist. That is a real behavioural difference between a watched and an
        /// unwatched settlement, and it is the same kind the tiering already accepts — an unwatched
        /// settlement does not farm, hunt or cook either.
        /// </summary>
        public static bool SocialFightPossible(Pawn pawn, Pawn otherPawn)
        {
            if (pawn == null || otherPawn == null) return false;
            if (!pawn.RaceProps.Humanlike || !otherPawn.RaceProps.Humanlike) return false;
            if (pawn.Dead || otherPawn.Dead) return false;
            if (pawn.Downed || otherPawn.Downed) return false;
            if (!pawn.Spawned || !otherPawn.Spawned) return false;
            if (!ReferenceEquals(pawn.Map, otherPawn.Map)) return false;
            if (AI.AttackVerbUtility.NaturalWeaponFor(pawn) == null) return false;
            if (AI.AttackVerbUtility.NaturalWeaponFor(otherPawn) == null) return false;
            if (pawn.ageTracker.AgeBiologicalYears < MinSocialFightAgeYears) return false;
            if (otherPawn.ageTracker.AgeBiologicalYears < MinSocialFightAgeYears) return false;
            return true;
        }

        /// <summary>Youngest either party may be. RimWorld's own child/adult boundary, and the age below
        /// which its <c>SocialFightPossible</c> refuses an adult a fight.</summary>
        public const int MinSocialFightAgeYears = 13;

        /// <summary>
        /// The chance an insult from <paramref name="initiator"/> tips <paramref name="recipient"/> into a
        /// fight (RimWorld: <c>Pawn_InteractionsTracker.SocialFightChance</c>, whose every factor below is
        /// its own, in its own order). Read from the recipient's side throughout, as RimWorld's is — there it
        /// is a method on the recipient's own interactions tracker.
        /// <para/>
        /// Two of RimWorld's factors are absent rather than changed, and both are absences of data rather
        /// than decisions: its per-hediff-stage <c>socialFightChanceFactor</c> (this port's
        /// <see cref="Health.HediffStage"/> carries no such field) and its gene factors (no genes module).
        /// Both are multiplicative and default to 1, so leaving them out is exactly RimWorld's own answer for
        /// a pawn with no relevant hediff and no genes.
        /// </summary>
        public static float SocialFightChance(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null) return 0f;
            if (!SocialFightPossible(recipient, initiator)) return 0f;

            float chance = SocialTuning.InsultSocialFightBaseChance;
            chance *= GenMath.InverseLerp(0.3f, 1f, recipient.health.capacities.GetLevel(Health.PawnCapacityDefOf.Manipulation));
            chance *= GenMath.InverseLerp(0.3f, 1f, recipient.health.capacities.GetLevel(Health.PawnCapacityDefOf.Moving));

            float opinion = recipient.relations.OpinionOf(initiator);
            chance *= opinion < 0f
                ? LerpDouble(-100f, 0f, 4f, 1f, opinion)
                : LerpDouble(0f, 100f, 1f, 0.6f, opinion);

            chance *= recipient.story.traits.SocialFightChanceFactor();

            int ageDifference = Math.Abs(recipient.ageTracker.AgeBiologicalYears - initiator.ageTracker.AgeBiologicalYears);
            if (ageDifference > 10)
            {
                chance *= LerpDouble(10f, 50f, 1f, 0.25f, Math.Min(ageDifference, 50));
            }

            return GenMath.Clamp01(chance);
        }

        /// <summary>RimWorld's <c>GenMath.LerpDouble</c>: maps <paramref name="x"/> from one range onto
        /// another, unclamped. Private here rather than added to <see cref="GenMath"/>, which is a file
        /// several lanes share.</summary>
        private static float LerpDouble(float inFrom, float inTo, float outFrom, float outTo, float x) =>
            outFrom + (x - inFrom) / (inTo - inFrom) * (outTo - outFrom);
    }
}
