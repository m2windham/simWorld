using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// A doorway (RimWorld: <c>Verse.Building_Door</c>). <b>Deviation:</b> real RimWorld doors swing open for
    /// an approaching pawn and block everyone else meanwhile; this pass's content sets a Door's
    /// <c>passability</c> to <c>Standable</c> outright (always passable, no swing animation, no faction-allowed
    /// check). What a Door does model is the part that matters for temperature: it is a wall to
    /// <see cref="RoomTracker"/>'s flood fill, so each side is its own <see cref="Room"/> with its own
    /// temperature, and heat crosses it only through <see cref="RoomTracker"/>'s door exchange, which runs
    /// far faster while the door <see cref="IsOpen"/> than while it is shut.
    /// <para/>
    /// <b>When it is open</b> (RimWorld's timings as read in the B18-era decompile of <c>Building_Door</c>):
    /// a pawn stepping into the doorway opens it; it stays open at least <see cref="MinOpenTicks"/> after
    /// opening and <see cref="CloseDelayTicks"/> after the doorway was last occupied. RimWorld's hold-open
    /// toggle and its powered/stuff open speed are not ported (no player holds doors here, and the door
    /// never blocks anyone while opening).
    /// </summary>
    public class Door : Building
    {
        /// <summary>RimWorld: a door stays open at least 300 ticks once a pawn opens it.</summary>
        public const int MinOpenTicks = 300;

        /// <summary>RimWorld: once the doorway is clear the door closes after 110 ticks.</summary>
        public const int CloseDelayTicks = 110;

        private int openedTick = int.MinValue / 2;
        private int lastOccupiedTick = int.MinValue / 2;

        /// <summary>Whether the door currently stands open, at game tick <paramref name="now"/>.</summary>
        public bool IsOpenAt(int now) => now - openedTick < MinOpenTicks || now - lastOccupiedTick < CloseDelayTicks;

        public bool IsOpen => IsOpenAt(Find.TickManager?.TicksGame ?? 0);

        /// <summary>A pawn is standing in the doorway at <paramref name="now"/>: opens a shut door and keeps an
        /// open one from closing. Called by <see cref="RoomTracker"/> each tick it finds one there.</summary>
        public void Notify_Occupied(int now)
        {
            if (!IsOpenAt(now)) openedTick = now;
            lastOccupiedTick = now;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref openedTick, "openedTick", int.MinValue / 2);
            Scribe_Values.Look(ref lastOccupiedTick, "lastOccupiedTick", int.MinValue / 2);
        }
    }
}
