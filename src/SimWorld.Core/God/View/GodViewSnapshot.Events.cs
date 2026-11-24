using System.Collections.Generic;

using SimWorld.Director;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    public sealed partial class GodViewSnapshot
    {
        /// <summary>How many events <see cref="RecentEvents"/> returns when the caller does not say. A display
        /// default: a handful of markers on a map, not a history.</summary>
        public const int DefaultRecentEventCount = 8;

        /// <summary>
        /// The most recent events that happened somewhere, newest first: every letter still on the stack and
        /// every chronicle line that recorded a place, merged, at most <paramref name="count"/> of them. What
        /// the map marks and what a "recent events" list jumps from — the quake, the pack, the raid, the death.
        ///
        /// <para/><b>One event, one mark.</b> A raid writes both a letter and a chronicle line carrying the same
        /// place; the letter wins, because it is the one the player can still open and dismiss. Two records are
        /// the same event when they share a tick, a tile and a centre cell.
        ///
        /// <para/>Static and taken when called, like <see cref="ChronicleSince"/>: cheap (the letter stack and
        /// the chronicle's capped tail), and never a slice of a snapshot the host is holding.
        /// </summary>
        /// <param name="count">How many to return. Clamped at zero.</param>
        public static IReadOnlyList<EventMark> RecentEvents(int count = DefaultRecentEventCount)
        {
            var marks = new List<EventMark>();
            if (count <= 0) return marks;

            var seen = new HashSet<(int Tick, int Tile, int X, int Z)>();

            IReadOnlyList<Letter> letters = Find.LetterStack.LettersListForReading;
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                Letter l = letters[i];
                EventLocationView? where = EventLocationView.Of(l.location);
                if (where == null || !seen.Add(KeyOf(l.arrivalTick, l.location!))) continue;
                marks.Add(new EventMark(l.arrivalTick, l.label, EventMarkSource.Letter, l.letterID, l.def?.defName, where));
            }

            IReadOnlyList<ChronicleEntry> chronicle = Find.Storyteller.Chronicle;
            for (int i = chronicle.Count - 1; i >= 0; i--)
            {
                ChronicleEntry e = chronicle[i];
                if (e.location == null || !seen.Add(KeyOf(e.tick, e.location))) continue;
                ChronicleLine line = ChronicleLine.Of(e);
                marks.Add(new EventMark(e.tick, line.Text, EventMarkSource.Chronicle, null, null, line.Location!));
            }

            // Both walks run newest first; the merge orders by tick, and a stable sort keeps a letter ahead of
            // a chronicle line from the same tick.
            List<EventMark> ordered = MergeNewestFirst(marks);
            if (ordered.Count > count) ordered.RemoveRange(count, ordered.Count - count);
            return ordered;
        }

        private static (int, int, int, int) KeyOf(int tick, EventLocation location) =>
            (tick, location.tile, location.cell.x, location.cell.z);

        private static List<EventMark> MergeNewestFirst(List<EventMark> marks)
        {
            var ordered = new List<EventMark>(marks.Count);
            for (int i = 0; i < marks.Count; i++)
            {
                int at = ordered.Count;
                while (at > 0 && ordered[at - 1].Tick < marks[i].Tick) at--;
                ordered.Insert(at, marks[i]);
            }
            return ordered;
        }
    }
}
