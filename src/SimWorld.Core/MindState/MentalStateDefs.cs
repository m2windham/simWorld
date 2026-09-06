using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.MindState
{
    public enum MentalStateCategory
    {
        Undefined,
        Aggro,
        Malicious,
        Misc,
        Indulgent,
    }

    public enum MentalBreakIntensity
    {
        None,
        Minor,
        Major,
        Extreme,
    }

    /// <summary>A mental state a pawn can be in (RimWorld: <c>Verse.AI.MentalStateDef</c>); behaviour comes with the AI module.</summary>
    public class MentalStateDef : Def
    {
        public Type stateClass = typeof(MentalState);
        public MentalStateCategory category;
        public int minTicksBeforeRecovery = 500;
        public int maxTicksBeforeRecovery = 10000;
        public float recoveryMtbDays = 1f;
        public bool recoverFromSleep;
        public bool recoverFromDowned = true;
        public bool blockNormalThoughts;
        public bool prisonersCanDo = true;
        public bool colonistsOnly;
        public string? beginLetterLabel;
        public string? beginLetter;
        public string? baseInspectLine;

        /// <summary>Memory gained on recovery from a mood-caused instance (Catharsis in vanilla).</summary>
        public ThoughtDef? moodRecoveryThought;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(MentalState).IsAssignableFrom(stateClass)) yield return "stateClass must derive from MentalState.";
            if (minTicksBeforeRecovery > maxTicksBeforeRecovery) yield return "minTicksBeforeRecovery exceeds maxTicksBeforeRecovery.";
        }
    }

    /// <summary>A way a pawn can break under low mood (RimWorld: <c>RimWorld.MentalBreakDef</c>).</summary>
    public class MentalBreakDef : Def
    {
        public MentalStateDef mentalState = null!;
        public MentalBreakIntensity intensity = MentalBreakIntensity.Minor;
        public float baseCommonality = 1f;
        public TraitDef? requiredTrait;
        public int requiredTraitDegree = int.MinValue;
        public Type workerClass = typeof(MentalBreakWorker);

        private MentalBreakWorker? workerInt;

        public MentalBreakWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (MentalBreakWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (mentalState == null) yield return "mentalState is required.";
            if (intensity == MentalBreakIntensity.None) yield return "intensity must be Minor, Major or Extreme.";
            if (!typeof(MentalBreakWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from MentalBreakWorker.";
        }
    }

    /// <summary>Eligibility and weighting of a break for a pawn (RimWorld: <c>RimWorld.MentalBreakWorker</c>).</summary>
    public class MentalBreakWorker
    {
        public MentalBreakDef def = null!;

        public virtual float CommonalityFor(Pawn pawn, bool moodCaused = false)
        {
            return def.baseCommonality;
        }

        public virtual bool BreakCanOccur(Pawn pawn)
        {
            if (def.requiredTrait != null)
            {
                bool has = def.requiredTraitDegree != int.MinValue
                    ? pawn.story.traits.HasTrait(def.requiredTrait, def.requiredTraitDegree)
                    : pawn.story.traits.HasTrait(def.requiredTrait);
                if (!has) return false;
            }
            var traits = pawn.story.traits.allTraits;
            for (int i = 0; i < traits.Count; i++)
            {
                TraitDegreeData data = traits[i].CurrentData;
                if (data.theOnlyAllowedMentalBreaks != null && !data.theOnlyAllowedMentalBreaks.Contains(def)) return false;
                if (data.disallowedMentalBreaks != null && data.disallowedMentalBreaks.Contains(def)) return false;
            }
            if (def.mentalState.colonistsOnly && !pawn.RaceProps.Humanlike) return false;
            return true;
        }

        public virtual bool TryStart(Pawn pawn, string? reason, bool causedByMood)
        {
            return pawn.mindState.mentalStateHandler.TryStartMentalState(def.mentalState, reason, forced: false, causedByMood: causedByMood);
        }
    }
}
