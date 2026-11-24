using System;
using System.Collections.Generic;

using SimWorld.Letters;
using SimWorld.Map;

namespace SimWorld.God.View
{
    /// <summary>
    /// Where an event happened, as values: the settlement (by its world tile and name) and, when it happened on
    /// a generated map, the cell it centres on and the cells it touched. Carried by <see cref="LetterView"/>,
    /// <see cref="ChronicleLine"/> and <see cref="EventMark"/> so a letter can offer "jump to" and the map can
    /// mark the spot — the playtest's two quakes that brought down 128 structures were never visible anywhere.
    ///
    /// <para/>RimWorld's letters jump to a <c>GlobalTargetInfo</c>; this is that idea for a host that never
    /// holds a live object. The cells are a record of the moment, not a live query: the wall that fell is gone,
    /// and the cell it stood on is the only true thing left to point at.
    /// </summary>
    public sealed class EventLocationView
    {
        internal EventLocationView(int tile, string? settlementName, IntVec3? cell, IReadOnlyList<IntVec3> affectedCells)
        {
            Tile = tile;
            SettlementName = settlementName;
            Cell = cell;
            AffectedCells = affectedCells;
        }

        /// <summary>The world tile — the same handle <see cref="SettlementSummary.Tile"/> carries, and what
        /// <see cref="GodCommands.FocusSettlement"/> takes to open the settlement. -1 when the event belongs to
        /// no tile in particular.</summary>
        public int Tile { get; }

        /// <summary>The name of the settlement standing on <see cref="Tile"/> when this was read, or null when
        /// none does (it may have been destroyed since).</summary>
        public string? SettlementName { get; }

        /// <summary>The cell on that settlement's map to centre on, or null when the event was resolved off
        /// any map (an unwatched settlement with no interior). Null is the honest answer there, not a gap: the
        /// event happened to the settlement and nowhere more precise.</summary>
        public IntVec3? Cell { get; }

        /// <summary>The cells it touched — where each structure fell, where each raider came in — at most
        /// <see cref="EventLocation.MaxAffectedCells"/>. Empty when only the centre is known.</summary>
        public IReadOnlyList<IntVec3> AffectedCells { get; }

        internal static EventLocationView? Of(EventLocation? location)
        {
            if (location == null) return null;
            World.Settlement? settlement = location.tile >= 0 ? AttentionManager.SettlementAt(location.tile) : null;
            IReadOnlyList<IntVec3> cells = location.affectedCells.Count == 0
                ? Array.Empty<IntVec3>()
                : location.affectedCells.ToArray();
            return new EventLocationView(
                location.tile, settlement?.name, location.HasCell ? location.cell : (IntVec3?)null, cells);
        }
    }

    /// <summary>Which record an <see cref="EventMark"/> was read from.</summary>
    public enum EventMarkSource
    {
        /// <summary>A letter still on the stack; <see cref="EventMark.LetterID"/> names it.</summary>
        Letter,

        /// <summary>A chronicle entry.</summary>
        Chronicle,
    }

    /// <summary>
    /// One recent event that happened somewhere, for the map to mark and a list to jump from — see
    /// <see cref="GodViewSnapshot.RecentEvents"/>.
    /// </summary>
    public sealed class EventMark
    {
        internal EventMark(int tick, string label, EventMarkSource source, string? letterID, string? letterDefName,
            EventLocationView location)
        {
            Tick = tick;
            Label = label;
            Source = source;
            LetterID = letterID;
            LetterDefName = letterDefName;
            Location = location;
        }

        /// <summary>The game tick it happened on.</summary>
        public int Tick { get; }

        /// <summary>What happened, in words: the letter's title, or the chronicle line.</summary>
        public string Label { get; }

        public EventMarkSource Source { get; }

        /// <summary>The letter's handle when <see cref="Source"/> is <see cref="EventMarkSource.Letter"/>, else
        /// null.</summary>
        public string? LetterID { get; }

        /// <summary>The letter's kind (<c>ThreatBig</c>, <c>NegativeEvent</c>, <c>Death</c> …) for choosing a
        /// marker, or null for a chronicle line.</summary>
        public string? LetterDefName { get; }

        public EventLocationView Location { get; }
    }
}
