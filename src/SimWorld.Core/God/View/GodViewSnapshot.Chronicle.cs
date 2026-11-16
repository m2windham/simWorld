using System.Collections.Generic;

using SimWorld.Director;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    public sealed partial class GodViewSnapshot
    {
        /// <summary>
        /// The chronicle as a feed: every entry recorded after <paramref name="afterTick"/>, oldest first, each
        /// with its <see cref="ChronicleLine.Kind"/>, the text the core wrote, and the thing ids it involves. A
        /// host that remembers the last tick it showed passes it back and gets only what is new; pass -1 for
        /// everything still held. Empty before a game starts.
        ///
        /// <para/>Reads the storyteller's chronicle, which keeps the last
        /// <see cref="Storyteller.ChronicleCapacity"/> entries; a host that falls further behind than that has
        /// missed the oldest of them, and <see cref="Moments"/> (never trimmed) is what still holds the ones
        /// worth remembering. Ask between ticks, as every read of this seam is meant to be: two entries recorded
        /// on the same tick share one tick, so a feed read halfway through a tick could miss the second.
        /// </summary>
        public static IReadOnlyList<ChronicleLine> ChronicleSince(int afterTick)
        {
            IReadOnlyList<ChronicleEntry> entries = Find.Storyteller.Chronicle;

            // The chronicle is appended in tick order, so walk back to the first entry past afterTick.
            int start = entries.Count;
            while (start > 0 && entries[start - 1].tick > afterTick) start--;

            var lines = new List<ChronicleLine>(entries.Count - start);
            for (int i = start; i < entries.Count; i++) lines.Add(ChronicleLine.Of(entries[i]));
            return lines;
        }
    }
}
