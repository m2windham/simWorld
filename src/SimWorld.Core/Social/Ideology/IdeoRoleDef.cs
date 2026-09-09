using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// A precept-granted position within an ideoligion (RimWorld: <c>RimWorld.PreceptDef.role</c>/
    /// <c>RimWorld.Precept_Role</c> — a "Moralist"/"Priest"-style title a citizen holds, distinct from every
    /// other citizen of the same ideoligion). Named <c>IdeoRoleDef</c> rather than the shorter <c>RoleDef</c>
    /// deliberately: <see cref="Work.RoleDef"/> already claims that class name, and <see cref="Defs.DefTypeResolver.GetDefType"/>
    /// resolves an XML element's Def type by bare <see cref="System.Type.Name"/> across every loaded assembly —
    /// two classes both named <c>RoleDef</c> would silently collide on the first one registered, with no error
    /// at all (<c>&lt;RoleDef&gt;</c> content meant for one type loading as the other). See this module's own
    /// report for why a second, distinct role concept exists here rather than extending <see cref="Work.RoleDef"/>:
    /// that class is <see cref="Pawns.Pawn_WorkSettings"/>'s standing work-priority policy, applicable to any
    /// number of citizens with no cap and no grant mechanism; an ideoligion role is capped
    /// (<see cref="maxHolders"/>), only exists because some <see cref="PreceptDef.grantsRole"/> names it, and
    /// its effects are declared by that precept (its <see cref="PreceptDef.moodThought"/>,
    /// <see cref="PreceptDef.workerClass"/>) rather than by this Def itself — a genuinely different
    /// relationship, not a relabeling of the same one. <c>Work/**</c> is also out of this pass's file ownership,
    /// so extending it was not an option regardless.
    /// </summary>
    public class IdeoRoleDef : Def
    {
        /// <summary>How many citizens can hold this role in one <see cref="Ideo"/> at once (RimWorld: most
        /// roles are singular — a civilization has one Leader — some are not). Enforced by
        /// <see cref="IdeoRoleTracker.TryAssign"/>.</summary>
        public int maxHolders = 1;

        /// <summary>
        /// Quality bonus a ritual gets when a holder of this role is among its participants (RimWorld: a
        /// ritual's officiant/role-holder bonus, folded into <see cref="RitualUtility.ComputeQuality"/>'s own
        /// <c>OfficiantPresentBonus</c> when the ritual's <see cref="RitualDef.officiantRole"/> is this role).
        /// Not sourced from RimWorld's real per-role table — this port's own invented magnitude, pinned by a
        /// trend test (a ritual with its officiant present scores no lower than the same ritual without one),
        /// per the module's own "pin the behaviour, not the literal" instruction.
        /// </summary>
        public float ritualQualityOffset;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (maxHolders < 1) yield return "maxHolders must be >= 1.";
        }
    }
}
