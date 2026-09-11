using SimWorld.Building;
using SimWorld.Map;

namespace SimWorld.Filth
{
    /// <summary>
    /// What stops cleaning being infinite (RimWorld: <c>Map.areaManager.Home</c>, consulted by
    /// <c>WorkGiver_CleanFilth.HasJobOnThing</c> and by <c>ListerFilthInHomeArea</c> before it).
    /// <para/>
    /// <b>This is the load-bearing decision in this module, so it is written down rather than buried in a
    /// predicate.</b> Filth appears wherever anyone walks, including the whole outdoors. If cleaning had no
    /// bound, a colonist would walk out into the wild, clean a patch of dirt, and find more dirt — there is no
    /// end to a map's worth of soil, and the job would never finish. RimWorld's answer is the home area: a
    /// player-painted region that cleaning (and firefighting, and hauling-by-default) is confined to.
    /// <para/>
    /// <b>What this port actually has.</b> <see cref="Area"/>/<see cref="AreaManager.Home"/> exist — the
    /// Building module shipped them — but nothing populates the home area and nothing ever read it, and there
    /// is no player UI in an engine-free core to paint one. Bounding on the home area alone would therefore
    /// have meant an empty home area, no cleanable filth ever, and a work giver that looks wired but can
    /// never fire: the dormancy trap this module was explicitly told to avoid. RimWorld does not hit it
    /// because its home area auto-expands over rooms as you build them; this port has no such hook, and
    /// adding one means editing <c>Building/RoomTracker.cs</c>, which this lane does not own.
    /// <para/>
    /// <b>The translation, then:</b> the home area is the authority <i>when something has set one</i>, and
    /// "any enclosed, roofed room" is the fallback when nothing has. Both halves say the same thing about the
    /// outdoors, which is the property that matters — the open map is never cleaning work — and the fallback
    /// is what RimWorld's own auto-expansion would have produced for a colony that has built rooms and
    /// painted nothing. The moment a host or a later lane starts filling
    /// <see cref="AreaManager.Home"/>, that takes over with no change here.
    /// <para/>
    /// <b>Rooms are lazy.</b> <see cref="RoomTracker.RoomAt"/> answers null for every cell until
    /// <see cref="RoomTracker.RoomTrackerTick"/> has flooded the map once, which <c>Map.MapTick</c> does (and
    /// a real game runs every tick via <c>Game</c>'s post-tickers). A caller driving a map by hand — a test —
    /// must tick the map at least once or every cell reads as unbounded and nothing is cleanable.
    /// </summary>
    public static class CleaningBounds
    {
        /// <summary>
        /// Whether filth standing on <paramref name="cell"/> is inside the region colonists clean. See this
        /// class's remarks for the whole argument; in one line: the home area if one has been set, otherwise
        /// an enclosed roofed room, and never the open outdoors.
        /// </summary>
        public static bool IsCleanable(Map.Map? map, IntVec3 cell)
        {
            if (map == null || !GenGrid.InBounds(cell, map)) return false;

            // A painted home area wins outright, exactly as in RimWorld — including the case where it
            // deliberately excludes a room somebody does not want cleaned.
            if (map.areaManager.Home.TrueCount > 0) return map.areaManager.Home[cell];

            Room? room = map.roomTracker.RoomAt(cell);
            return room != null && !room.TouchesOutside;
        }

        /// <summary>Convenience for the common "is this filth cleanable" question.</summary>
        public static bool IsCleanable(Filth filth) =>
            filth != null && filth.Spawned && IsCleanable(filth.Map, filth.Position);
    }
}
