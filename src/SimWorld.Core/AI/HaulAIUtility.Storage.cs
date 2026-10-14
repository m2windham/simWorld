using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// The storage-priority half of <see cref="HaulAIUtility"/> (RimWorld: <c>HaulAIUtility.HaulToStorageJob</c>).
    /// Its own file because <see cref="HaulAIUtility"/> is shared; <c>partial</c> lets this add to it without
    /// touching what other lanes are editing.
    /// </summary>
    public static partial class HaulAIUtility
    {
        /// <summary>
        /// The job that takes <paramref name="t"/> to the best place better than where it lies now, or null when
        /// there is none (RimWorld: <c>HaulAIUtility.HaulToStorageJob</c>). A stockpile cell is a
        /// <see cref="JobDefOf.HaulToCell"/>; a destination that is a building — a grave — is a
        /// <see cref="GraveJobDefOf.HaulToContainer"/>, which carries the Thing <i>into</i> it. Which of the two
        /// is decided by storage priority (<see cref="StoreUtility.TryFindBestBetterStorageFor"/>), so a body
        /// that a grave will take never goes to a dumping stockpile while the grave is free.
        /// </summary>
        public static Job? HaulToStorageJob(Pawn p, Thing t)
        {
            StoragePriority current = StoreUtility.CurrentStoragePriorityOf(t);
            if (!StoreUtility.TryFindBestBetterStorageFor(t, p, current, out IntVec3 cell, out IHaulDestination? destination))
            {
                return null;
            }

            if (destination == null) return new Job(JobDefOf.HaulToCell, t, cell) { haulMode = HaulMode.ToCellStorage };
            if (destination is Thing container) return new Job(GraveJobDefOf.HaulToContainer, t, container) { haulMode = HaulMode.ToContainer, count = t.stackCount };
            return null; // a destination that is not a Thing has nowhere to be walked to
        }
    }
}
