using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// A ceremony an ideoligion can perform (RimWorld: <c>RimWorld.RitualPatternDef</c>/<c>PreceptDef</c>'s own
    /// ritual half, trimmed to what this pass needs — see <see cref="RitualUtility"/>'s own doc for the
    /// quality-resolution shape ported and what could not be sourced).
    /// </summary>
    public class RitualDef : Def
    {
        /// <summary>The role whose presence among the participants improves this ritual's quality
        /// (RimWorld: the officiant/organizer bonus). Optional — a ritual with no named role still resolves,
        /// it just never gets this particular bonus.</summary>
        public IdeoRoleDef? officiantRole;

        /// <summary>Memory thought granted to every living participant on completion
        /// (<see cref="RitualUtility.PerformRitual"/>), forced to the stage the rolled quality lands on — see
        /// that method's own doc. Must be a memory thought (<see cref="ThoughtDef.IsMemory"/>), not situational.</summary>
        public ThoughtDef? attendeeMemory;

        /// <summary>Starting point <see cref="RitualUtility.ComputeQuality"/> adds every other factor to,
        /// before participants, role and mood. Not sourced from RimWorld's own per-ritual baselines (its
        /// <c>RitualOutcomeEffectDef</c> table); this port's own value, in [0, 1] like the quality it seeds.</summary>
        public float baseQuality = 0.3f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (attendeeMemory != null && !attendeeMemory.IsMemory)
            {
                yield return "attendeeMemory must be a memory thought (durationDays > 0), not a situational one.";
            }
            if (baseQuality < 0f || baseQuality > 1f)
            {
                yield return "baseQuality must be in [0, 1].";
            }
        }
    }
}
