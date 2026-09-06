using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;

namespace SimWorld.Work
{
    /// <summary>How eager a pawn is to use a skill (RimWorld: <c>RimWorld.Passion</c>); scales <see cref="SkillRecord.LearnRateFactor"/>.</summary>
    public enum Passion
    {
        None,
        Minor,
        Major,
    }

    /// <summary>
    /// One of the twelve learnable abilities (RimWorld: <c>RimWorld.SkillDef</c>). Whether a pawn can use the
    /// skill at all is computed from disqualifying work tags and, failing that, whether every work type the
    /// skill feeds into is itself disabled — see <see cref="IsDisabled"/>.
    /// </summary>
    public class SkillDef : Def
    {
        /// <summary>Sort order for skill lists; also the order <see cref="Pawn_SkillTracker"/> creates records in.</summary>
        public int listOrder;

        /// <summary>Work types this skill is normally tied to; documentation only until Pawn Generation lands.</summary>
        public List<WorkTypeDef>? usuallyDefinedWorkTypes;

        /// <summary>When set, a pawn can never lose this skill just because every relevant work type is disabled.</summary>
        public bool neverDisabledBasedOnWorkTypes;

        /// <summary>Any of these work tags on the pawn disables the skill outright (e.g. Violent disables Shooting/Melee).</summary>
        public WorkTags disablingWorkTags = WorkTags.None;

        public string? skillLabel;
        public string? pawnLabel;

        /// <summary>
        /// True when <paramref name="combinedDisabledWorkTags"/> intersects <see cref="disablingWorkTags"/>; else,
        /// unless <see cref="neverDisabledBasedOnWorkTypes"/>, true only when at least one <see cref="WorkTypeDef"/>
        /// lists this skill as relevant and every such work type is among <paramref name="disabledWorkTypes"/>.
        /// </summary>
        public bool IsDisabled(WorkTags combinedDisabledWorkTags, IEnumerable<WorkTypeDef> disabledWorkTypes)
        {
            if (disablingWorkTags != WorkTags.None && (disablingWorkTags & combinedDisabledWorkTags) != WorkTags.None)
            {
                return true;
            }
            if (neverDisabledBasedOnWorkTypes)
            {
                return false;
            }

            bool foundRelevant = false;
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (workType.relevantSkills == null || !workType.relevantSkills.Contains(this))
                {
                    continue;
                }
                foundRelevant = true;
                if (!disabledWorkTypes.Contains(workType))
                {
                    return false;
                }
            }
            return foundRelevant;
        }
    }
}
