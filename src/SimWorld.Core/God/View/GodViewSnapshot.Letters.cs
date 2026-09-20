using System.Collections.Generic;

using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The read half of the letters/quests seam — see <c>GodCommands.Letters.cs</c> for the write half and
    /// <c>docs/design/player-first.md</c> §2 for why this is the one lever this port's own audit found with no
    /// way to answer it at all.
    /// </summary>
    public sealed partial class GodViewSnapshot
    {
        private IReadOnlyList<PendingLetterView>? pendingLetters;

        /// <summary>
        /// Every <see cref="Letters.ChoiceLetter"/> the player has not yet answered, timed out, or otherwise
        /// left the stack.
        ///
        /// <para/><b>Why this property, alone on <see cref="GodViewSnapshot"/>, is not built inside
        /// <see cref="Capture(int)"/> the way everything else here is.</b> <see cref="GodViewSnapshot"/>'s
        /// constructor is private and lives in <c>GodViewSnapshot.cs</c>, which this lane does not touch —
        /// another is adding its own partial file to the same type at the same time, and the two only stay
        /// disjoint if neither edits the shared file (see CLAUDE.md, "add a file rather than edit a shared
        /// one"). A partial file cannot widen a constructor it does not declare, so this cannot be threaded
        /// through the constructor the way <see cref="Conditions"/> or <see cref="Losses"/> are. It is instead
        /// computed the first time a caller reads it and cached from then on, from <see cref="Find.LetterStack"/>
        /// — the same ambient service every field <see cref="Capture(int)"/> itself builds already reads
        /// through <c>Find</c>.
        ///
        /// <para/>The cache means two reads of the <em>same</em> snapshot object always agree with each other,
        /// even if the simulation ticks in between — a resolved-twice bug could not hide behind a snapshot
        /// that quietly changed its own answer. What it does not guarantee is agreement with the instant
        /// <see cref="Capture(int)"/> itself ran, if nothing reads this property until later. In practice a
        /// caller reads a snapshot's properties together, right after taking it, the same as every other one
        /// here — nothing in this codebase holds a <see cref="GodViewSnapshot"/> across a tick before reading
        /// it — so this is the correct trade given the constraint, not merely the only one available under it.
        /// </summary>
        public IReadOnlyList<PendingLetterView> PendingLetters =>
            pendingLetters ??= PendingLetterView.PendingOn(Find.LetterStack);
    }
}
