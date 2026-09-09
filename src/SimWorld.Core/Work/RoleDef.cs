using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Work
{
    /// <summary>
    /// A civilization-scale standing role — SimWorld's translation of RimWorld's per-pawn work priority grid
    /// (<c>docs/spec/simworld-spec.md</c> §10/§7.3, tracker item <c>work.policy</c>). RimWorld has the player
    /// set twelve numbers per colonist; a civilization cannot be managed one grid at a time, so here the god
    /// assigns a citizen (or a whole settlement's worth of them, via <see cref="WorkPolicyUtility"/>) a
    /// <i>role</i> instead, and the role's <see cref="emphasizedWorkTypes"/> shape which of their own already-
    /// enabled work types they take up first.
    /// <para/>
    /// <b>Standing, not temporary — and still not a permanent rewrite.</b> An <see cref="God.EdictDef"/> is
    /// deliberately temporary and never touches <see cref="Pawn_WorkSettings"/> at all (see that class's own
    /// "leaves no trace" doc); a role is the standing counterpart and *does* write into a pawn's priority grid
    /// (see <see cref="Pawn_WorkSettings.SetRole"/>), because unlike an edict a role is meant to persist. The
    /// property this system must not break is narrower than "never touches the grid" — it is "never overwrites
    /// a person's own explicit choice": <see cref="Pawn_WorkSettings"/> tracks which work types a caller set
    /// directly (<see cref="Pawn_WorkSettings.SetPriority"/>) and a role's own writes always skip those, exactly
    /// the way <see cref="God.EdictWorker.AppliesTo"/> never turns on a disabled work type. Unassigning a role
    /// (<c>SetRole(null)</c>) is fully reversible for everything it touched: every non-manual entry reverts to
    /// <see cref="Pawn_WorkSettings.DefaultPriority"/>, exactly as if <see cref="Pawn_WorkSettings.EnableAndInitialize"/>
    /// had just run for those slots — the standing-policy mirror of an edict's own "leaves no trace" guarantee.
    /// </summary>
    public class RoleDef : Def
    {
        /// <summary>Work types this role pulls to the front of a citizen's own priority grid — set to
        /// <see cref="Pawn_WorkSettings.EmphasizedPriority"/> (the highest, i.e. first-attempted, priority)
        /// rather than merely nudging their order, so a role has real teeth even for a pawn whose other work
        /// types all still sit at the same default. Never includes a work type the pawn is disabled from — see
        /// <see cref="Pawn_WorkSettings.ApplyRole"/>, which is the only place this list is read.</summary>
        public List<WorkTypeDef> emphasizedWorkTypes = new List<WorkTypeDef>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (emphasizedWorkTypes.Count == 0)
            {
                yield return "emphasizedWorkTypes is empty — a role with nothing to emphasize shapes no work at all.";
            }
        }
    }
}
