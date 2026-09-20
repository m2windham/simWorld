using System.Collections.Generic;

namespace SimWorld.God.View
{
    /// <summary>
    /// The read half of the trade seam — see <c>GodCommands.Trade.cs</c> for the write half.
    ///
    /// <para/><b>The gap this closes.</b> Nothing on this snapshot could say a trader was anywhere. The
    /// director generated real caravans with real priced stock and the read model had no word for them, so
    /// even after the arrival machinery existed (<c>Economy.TraderCaravan</c>) the player could not have
    /// learned that a caravan was standing at one of their settlements, what it carried, or when it would
    /// leave. A command surface with nothing to aim it at is not a lever.
    ///
    /// <para/><b>Why this sits on <see cref="GodViewSnapshot"/> rather than on a settlement's own view.</b> A
    /// caravan's arrival is a civilization-scale event: it moves the civilization's stores, it depends on
    /// relations between civilizations, and it is decided by the storyteller against the civilization as a
    /// whole. What a settlement looks like inside is the map layer's business; who is at the gates is not.
    ///
    /// <para/><b>Computed on first read, like <see cref="PendingLetters"/> and for the identical reason.</b>
    /// <see cref="GodViewSnapshot"/>'s constructor is private and lives in <c>GodViewSnapshot.cs</c>, which
    /// this lane does not touch — other lanes are adding their own partial files to the same type at the same
    /// time, and the only thing that keeps them disjoint is that none of them edits the shared file (CLAUDE.md,
    /// "add a file rather than edit a shared one"). A partial file cannot widen a constructor it does not
    /// declare. The cache means two reads of the <i>same</i> snapshot always agree with each other; see
    /// <see cref="PendingLetters"/>'s own doc for the full argument and its one limitation.
    ///
    /// <para/><b>This read never changes the world.</b> It does not prune departed caravans, even though it
    /// walks straight past them — a snapshot that mutated the simulation would break the promise this class's
    /// own doc makes ("the host may hold it, diff it, render it twice, and none of that can touch the
    /// simulation"). A departed caravan is simply not present and so is not listed; the sweep belongs to the
    /// write paths, and <c>Economy.TraderArrival.PruneDeparted</c> explains why it is bounded at one.
    /// </summary>
    public sealed partial class GodViewSnapshot
    {
        private IReadOnlyList<VisitingTraderView>? visitingTraders;

        /// <summary>
        /// Every trade caravan standing at one of this civilization's settlements right now, each with the
        /// whole table of what can move in either direction and what it would cost. Empty — never null — when
        /// nobody is visiting, which is most of the time and is a state the view draws rather than an error.
        ///
        /// <para/>Each entry names its settlement by <see cref="VisitingTraderView.SettlementTile"/>, which is
        /// exactly what <see cref="GodCommands.BuyFromTrader"/> and <see cref="GodCommands.SellToTrader"/>
        /// take, so a host never derives a handle of its own.
        /// </summary>
        public IReadOnlyList<VisitingTraderView> VisitingTraders =>
            visitingTraders ??= VisitingTraderView.PresentNow();
    }
}
