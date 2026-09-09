using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// Runtime compliance rule behind one <see cref="PreceptDef"/> (RimWorld: no direct equivalent — mirrors
    /// <see cref="God.EdictWorker"/>'s own def+worker shape, itself modelled on <see cref="ThoughtWorker"/>).
    /// The base class does only what a precept whose entire content is "the ideoligion holds this" needs: a
    /// citizen is deemed to comply for as long as they belong to the ideoligion at all (this pass has no
    /// per-citizen membership concept — see <see cref="Ideo"/>'s own doc — so "belongs" reads as "is
    /// Humanlike", the same default <see cref="God.EdictWorker.AppliesTo"/> uses). Subclass for anything
    /// conditional on the citizen's own state — see <see cref="PreceptWorkers"/>.
    /// </summary>
    public class PreceptWorker
    {
        public PreceptDef def = null!;

        /// <summary>Whether this precept's consequences (mood thought, role eligibility) reach
        /// <paramref name="pawn"/> at all. Defaults to every Humanlike pawn — an animal has no ideoligion.</summary>
        public virtual bool AppliesTo(Pawn pawn) => pawn != null && pawn.RaceProps.Humanlike;

        /// <summary>
        /// Whether <paramref name="pawn"/> currently upholds this precept, and at which
        /// <see cref="ThoughtDef"/> stage — read by <see cref="ThoughtWorker_UnderPrecept"/> once it has
        /// already established this precept is part of the active <see cref="Ideo"/> and
        /// <see cref="AppliesTo"/> holds. The base implementation is unconditionally active: a precept with no
        /// per-citizen state to check is simply true of everyone the ideoligion holds.
        /// </summary>
        public virtual ThoughtState CurrentState(Pawn pawn) => ThoughtState.ActiveDefault;
    }
}
