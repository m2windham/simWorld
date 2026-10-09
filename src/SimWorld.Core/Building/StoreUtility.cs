using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Where a Thing is stored now, and where it would be better stored (RimWorld: <c>RimWorld.StoreUtility</c>),
    /// across the two kinds of place this port has: a <see cref="Zone_Stockpile"/> cell and an
    /// <see cref="IHaulDestination"/> that is a Thing (a <see cref="Building_Grave"/>).
    /// <para/>
    /// <b>Why this is a second path beside <see cref="HaulAIUtility"/>.</b> That class answers "which stockpile
    /// cell", and was right to when a stockpile was the only place anything could go. A grave is not a cell: a
    /// hauler walks to a building and the body goes <i>into</i> it. <see cref="TryFindBestBetterStorageFor"/> is
    /// RimWorld's way of choosing between the two kinds, by storage priority, so that a settlement which has both
    /// a dumping stockpile and a grave buries its own rather than laying them in the dump. It is used for
    /// corpses (<see cref="WorkGiver_HaulCorpses"/>); the ordinary item hauler is left on the cell-only path.
    /// <para/>
    /// <b>A stockpile cell is Normal.</b> RimWorld's zones carry a priority of their own (a player can set a
    /// stockpile Preferred or Low); this port's <see cref="Zone_Stockpile"/> has none, so every cell counts as the
    /// default, <see cref="StockpilePriority"/>. A grave's default is <see cref="StoragePriority.Important"/>,
    /// above it, which is the whole reason a grave beats a dump.
    /// </summary>
    public static partial class StoreUtility
    {
        /// <summary>The priority every stockpile cell has in this port: RimWorld's default for a new zone.</summary>
        public const StoragePriority StockpilePriority = StoragePriority.Normal;

        /// <summary>
        /// The destination holding <paramref name="t"/> right now, if it is held by one (RimWorld:
        /// <c>StoreUtility.CurrentHaulDestinationOf</c>). A Thing on the map is on a cell, never "in" a
        /// destination; RimWorld answers a stockpile's slot-group parent here, and a stockpile is not a
        /// <see cref="IHaulDestination"/> in this port, so a Thing lying on one has no destination to name —
        /// <see cref="CurrentStoragePriorityOf"/> is what reads a stockpile cell.
        /// </summary>
        public static IHaulDestination? CurrentHaulDestinationOf(Thing t)
        {
            if (t.Spawned) return null;
            return t.ParentHolder as IHaulDestination;
        }

        /// <summary>
        /// The Thing storing <paramref name="t"/> — the grave a body lies in — or null when it is lying about,
        /// on a stockpile cell or in a hauler's hands (RimWorld: <c>StoreUtility.StoringThing</c>, which is
        /// <c>CurrentHaulDestinationOf(t) as Thing</c>). <b>A body on a stockpile cell has no storing Thing</b>:
        /// a zone is not a Thing. That is RimWorld's behaviour and it matters — a dumping stockpile does not
        /// hide its dead from the people who walk past it.
        /// </summary>
        public static Thing? StoringThing(this Thing t) => CurrentHaulDestinationOf(t) as Thing;

        /// <summary>
        /// How well stored <paramref name="t"/> is where it is (RimWorld: <c>CurrentStoragePriorityOf</c>):
        /// <see cref="StoragePriority.Unstored"/> when it is lying anywhere that does not want it, else the
        /// priority of the place that does.
        /// </summary>
        public static StoragePriority CurrentStoragePriorityOf(Thing t)
        {
            if (t.Spawned)
            {
                return HaulAIUtility.IsInValidStorage(t) ? StockpilePriority : StoragePriority.Unstored;
            }
            return StoragePriorityAtFor(CurrentHaulDestinationOf(t), t);
        }

        public static StoragePriority StoragePriorityAtFor(IHaulDestination? at, Thing t)
        {
            if (at == null || !at.Accepts(t)) return StoragePriority.Unstored;
            return at.GetStoreSettings().Priority;
        }
    }
}
