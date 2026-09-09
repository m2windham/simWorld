using System;
using System.Collections.Generic;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    /// <summary>Result of one <see cref="RitualUtility.PerformRitual"/> call — the rolled quality and, when
    /// the ritual grants a memory, which of its stages it landed on.</summary>
    public readonly struct RitualOutcome
    {
        public float Quality { get; }
        public int StageIndex { get; }
        public int ParticipantCount { get; }

        public RitualOutcome(float quality, int stageIndex, int participantCount)
        {
            Quality = quality;
            StageIndex = stageIndex;
            ParticipantCount = participantCount;
        }
    }

    /// <summary>
    /// Ritual quality resolution — RimWorld rolls ritual quality from participants, role holders and the
    /// setting (<see cref="RitualTuning"/>'s own header has the full sourcing note: the shape is ported, the
    /// magnitudes are this port's own, and the physical-setting term is substituted with participants' own
    /// mean mood since no room/building concept reaches this module). The one behaviour this port commits to
    /// and pins with a test regardless of the exact numbers: more and better participants never produce a
    /// worse ritual.
    /// </summary>
    public static class RitualUtility
    {
        /// <summary>Quality in [0, 1] before the deterministic roll's own jitter — the deterministic half of
        /// <see cref="PerformRitual"/>, exposed on its own so a caller (or a test) can inspect the trend
        /// without needing to also account for <see cref="RitualTuning.QualityJitter"/>.</summary>
        public static float ComputeQuality(RitualDef def, IReadOnlyList<Pawn> participants, Ideo? ideo)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (participants == null) throw new ArgumentNullException(nameof(participants));

            float quality = def.baseQuality;
            quality += RitualTuning.ParticipantCountQualityCurve.Evaluate(participants.Count);
            quality += OfficiantBonus(def, participants, ideo);
            quality += MeanMoodOffset(participants);

            return GenMath.Clamp(quality, 0f, 1f);
        }

        private static float OfficiantBonus(RitualDef def, IReadOnlyList<Pawn> participants, Ideo? ideo)
        {
            if (def.officiantRole == null || ideo == null) return 0f;
            for (int i = 0; i < participants.Count; i++)
            {
                if (ideo.Roles.RoleOf(participants[i]) == def.officiantRole)
                {
                    return RitualTuning.OfficiantPresentBonus + def.officiantRole.ritualQualityOffset;
                }
            }
            return 0f;
        }

        private static float MeanMoodOffset(IReadOnlyList<Pawn> participants)
        {
            float sum = 0f;
            int counted = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                Need_Mood? mood = participants[i]?.needs?.mood;
                if (mood == null) continue;
                sum += mood.CurLevelPercentage;
                counted++;
            }
            if (counted == 0) return 0f;
            float meanMood = sum / counted;
            return (meanMood - 0.5f) * RitualTuning.MeanMoodQualityWeight;
        }

        /// <summary>
        /// Performs the ritual: rolls quality (<see cref="ComputeQuality"/> plus a small deterministic jitter),
        /// then grants every living participant <see cref="RitualDef.attendeeMemory"/> — when the def carries
        /// one — forced to the stage the rolled quality proportionally lands on (quality 0 → stage 0, quality
        /// 1 → the memory's last stage), the same "quality picks a stage on a graded thought" idiom
        /// <see cref="Thought_Memory.forcedStage"/> already exists to support.
        /// </summary>
        public static RitualOutcome PerformRitual(RitualDef def, IReadOnlyList<Pawn> participants, Ideo? ideo)
        {
            float quality = ComputeQuality(def, participants, ideo);
            float jittered = GenMath.Clamp(quality + Rand.Range(-RitualTuning.QualityJitter, RitualTuning.QualityJitter), 0f, 1f);

            int stageIndex = -1;
            if (def.attendeeMemory != null && def.attendeeMemory.stages.Count > 0)
            {
                int stageCount = def.attendeeMemory.stages.Count;
                stageIndex = (int)(jittered * stageCount);
                if (stageIndex >= stageCount) stageIndex = stageCount - 1;
                if (stageIndex < 0) stageIndex = 0;

                for (int i = 0; i < participants.Count; i++)
                {
                    Pawn p = participants[i];
                    if (p == null || p.Dead || p.needs?.mood == null) continue;
                    Thought_Memory? memory = p.needs.mood.thoughts.memories.TryGainMemory(def.attendeeMemory);
                    if (memory != null) memory.forcedStage = stageIndex;
                }
            }

            return new RitualOutcome(jittered, stageIndex, participants.Count);
        }
    }
}
