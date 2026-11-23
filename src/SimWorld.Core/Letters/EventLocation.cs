using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Letters
{
    /// <summary>
    /// Where something happened, in a form that outlives what it happened to (RimWorld: the
    /// <c>GlobalTargetInfo</c> a letter's <c>LookTargets</c> jumps to — a world tile, and on a generated map a
    /// cell). Carried by a <see cref="Letter"/> and a <see cref="Director.ChronicleEntry"/> so the god view can
    /// offer "jump to" and mark the spot: a quake that brought down 128 structures, a pack that came in at the
    /// map edge.
    ///
    /// <para/><b>Values, not references.</b> The thing an event is about is usually gone by the time anyone
    /// reads about it — the wall fell, the raider died — so a cell is the only target that is still true.
    /// <see cref="Letter.lookTargets"/> keeps doing the job for things that survive (a pawn who broke).
    ///
    /// <para/><b>Tile always, cell when there is a map.</b> An event resolved abstractly (an unwatched
    /// settlement with no interior) happened on a tile and nowhere more precise; <see cref="HasCell"/> is false
    /// and that is the honest answer, not a missing one.
    /// </summary>
    public sealed class EventLocation : IExposable
    {
        /// <summary>How many affected cells one event keeps. Enough to mark the damage on a map and to frame
        /// a camera around it; a quake that flattens a town does not need every footprint to say where it hit.
        /// A display cap, not sourced from anything.</summary>
        public const int MaxAffectedCells = 24;

        /// <summary>The world tile it happened on; -1 when it happened nowhere in particular.</summary>
        public int tile = -1;

        /// <summary>The cell it centres on, or <see cref="IntVec3.Invalid"/> when it was resolved off-map.</summary>
        public IntVec3 cell = IntVec3.Invalid;

        /// <summary>Cells it touched — a destroyed structure's position, where a raider came in. At most
        /// <see cref="MaxAffectedCells"/>, in the order they were added.</summary>
        public List<IntVec3> affectedCells = new List<IntVec3>();

        public bool HasCell => cell.IsValid;

        public EventLocation()
        {
        }

        public EventLocation(int tile)
        {
            this.tile = tile;
        }

        public EventLocation(int tile, IntVec3 cell)
        {
            this.tile = tile;
            this.cell = cell;
        }

        /// <summary>The spot <paramref name="map"/> sits on, centred on <paramref name="cell"/>.</summary>
        public static EventLocation On(Map.Map map, IntVec3 cell) => new EventLocation(map.tile, cell);

        /// <summary>Where <paramref name="thing"/> stands right now, or null when it is not on a map (an
        /// unspawned citizen of an unwatched settlement happened nowhere a map can show).</summary>
        public static EventLocation? Of(Things.Thing? thing)
        {
            if (thing == null || !thing.Spawned) return null;
            return On(thing.Map!, thing.Position);
        }

        /// <summary>Records one more affected cell, ignoring it once <see cref="MaxAffectedCells"/> are held.
        /// The first cell added also becomes <see cref="cell"/> when none was given.</summary>
        public void AddAffected(IntVec3 c)
        {
            if (!cell.IsValid) cell = c;
            if (affectedCells.Count < MaxAffectedCells && !affectedCells.Contains(c)) affectedCells.Add(c);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            List<IntVec3>? cells = affectedCells;
            Scribe_Collections.Look(ref cells, "affectedCells", LookMode.Value);
            affectedCells = cells ?? new List<IntVec3>();
        }
    }
}
