using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Filth
{
    /// <summary>
    /// "This place is filthy" — the mood cost of standing in a dirty room.
    /// <para/>
    /// <b>Translation, and it is a real one.</b> RimWorld has no thought that reads room cleanliness
    /// directly. Filth hurts mood there by a longer route: every piece of filth carries a negative
    /// <c>Beauty</c> stat, beauty is sampled from the pawn's surroundings into the <c>Beauty</c> need, and the
    /// need drives the mood thought. This port cannot take that route. It has the <c>Beauty</c>
    /// <see cref="Needs.NeedDef"/> and <see cref="Needs.Need_Environment"/> to hold it, but
    /// <see cref="Pawns.IEnvironmentSampler"/> — the seam the need reads its target through — has <b>no
    /// implementation anywhere in the core</b> (only a fake in the needs tests), so beauty sits at its def's
    /// base level forever and nothing downstream of it moves. Building a real environment sampler means
    /// inventing a <c>Beauty</c> stat, putting it on every Thing in shipped content, and deciding what a
    /// pawn's surroundings are — a module of its own, not a side effect of filth.
    /// <para/>
    /// So the shorter route is taken deliberately: a situational thought that reads
    /// <see cref="RoomCleanlinessUtility"/> directly, with the same sign and roughly the same magnitude the
    /// beauty route would have delivered. When an environment sampler does land, this is the thing to delete
    /// — filth's contribution should arrive through beauty like everything else's, and leaving both wired
    /// would count it twice.
    /// <para/>
    /// Situational thoughts are discovered by scanning every <see cref="ThoughtDef"/> with a worker
    /// (<c>SituationalThoughtHandler</c>), so this needed no edit to any shared file: a new worker class and a
    /// new <c>Thoughts_Filth.xml</c> is the whole wiring.
    /// </summary>
    public class ThoughtWorker_FilthyRoom : ThoughtWorker
    {
        /// <summary>Room cleanliness at or below which the pawn starts minding. Cleanliness is 0 for a
        /// spotless room and negative for a dirty one; these two thresholds pick the def's two stages.
        /// SimWorld's own numbers — see the class remarks for why there is no RimWorld value to source — and
        /// the test pins that a dirtier room is a worse mood, not these.</summary>
        public const float DirtyThreshold = -0.35f;

        /// <summary>Cleanliness at or below which it is properly squalid (the second, worse stage).</summary>
        public const float FilthyThreshold = -1.2f;

        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            // Animals do not have opinions about housekeeping; nor does anyone off-map.
            if (pawn == null || !pawn.Spawned || !pawn.RaceProps.Humanlike) return ThoughtState.Inactive;

            float cleanliness = RoomCleanlinessUtility.CleanlinessAround(pawn);
            if (cleanliness <= FilthyThreshold) return ThoughtState.ActiveAtStage(1);
            if (cleanliness <= DirtyThreshold) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
