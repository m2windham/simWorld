using System;
using System.Collections.Generic;
using System.Globalization;

namespace SimWorld.God.View
{
    /// <summary>
    /// One letter on the stack, of any kind (RimWorld: an envelope in the letter stack at the right of the
    /// screen). <see cref="PendingLetterView"/> lists only the letters that ask a question; this is the whole
    /// feed — deaths, mental breaks, raids, conditions — so the player can follow what happens.
    /// </summary>
    public sealed class LetterView
    {
        internal LetterView(
            string letterID, string defName, string label, string text, int arrivalTick,
            IReadOnlyList<int> lookTargetThingIds, bool isChoice, EventLocationView? location,
            IReadOnlyList<LookTargetNow> lookTargetsNow)
        {
            Location = location;
            LookTargetsNow = lookTargetsNow;
            LetterID = letterID;
            DefName = defName;
            Label = label;
            Text = text;
            ArrivalTick = arrivalTick;
            LookTargetThingIds = lookTargetThingIds;
            IsChoice = isChoice;
        }

        /// <summary>The handle <see cref="GodCommands.DismissLetter"/> and
        /// <see cref="GodCommands.ResolveLetterChoice"/> take. Stable across a save.</summary>
        public string LetterID { get; }

        /// <summary>The <c>LetterDef</c>'s defName (e.g. <c>NegativeEvent</c>, <c>ThreatBig</c>, <c>Death</c>) —
        /// what kind of envelope to draw.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Text { get; }

        /// <summary>The game tick it arrived on (<c>TickManager.TicksGame</c>).</summary>
        public int ArrivalTick { get; }

        /// <summary>
        /// Thing ids (<c>thingIDNumber</c>) of what the letter is about — the pawn who broke, the one who died —
        /// the same ids the roster and the map view carry. The core stores look targets as load ids
        /// (<c>Thing_&lt;defName&gt;&lt;id&gt;</c>); this is the id read back off the end of each. A target
        /// that is not a thing is left out rather than guessed at.
        /// </summary>
        public IReadOnlyList<int> LookTargetThingIds { get; }

        /// <summary>A letter asking for an answer (<see cref="PendingLetterView"/> lists its options). These are
        /// answered with <see cref="GodCommands.ResolveLetterChoice"/>, not dismissed.</summary>
        public bool IsChoice { get; }

        /// <summary>Where it happened — the settlement, and the cells on its map when it happened on one — so
        /// the letter can offer "jump to" and the map can mark it. Null for a letter about nowhere in
        /// particular (an era reached, a quest offered). A letter about a person carries
        /// <see cref="LookTargetThingIds"/> as well; this is the place, which outlives them.</summary>
        public EventLocationView? Location { get; }

        /// <summary>Where each of <see cref="LookTargetThingIds"/> stands now, for those still on a map — the
        /// raiders and the pack where they have got to, not where they came in. Read when the snapshot is
        /// taken; empty when nobody the letter is about is on a map.</summary>
        public IReadOnlyList<LookTargetNow> LookTargetsNow { get; }

        internal static List<LetterView> On(Letters.LetterStack stack)
        {
            IReadOnlyList<Letters.Letter> letters = stack.LettersListForReading;
            var views = new List<LetterView>(letters.Count);
            var ids = new IReadOnlyList<int>[letters.Count];
            var wanted = new HashSet<int>();
            for (int i = 0; i < letters.Count; i++)
            {
                ids[i] = ThingIdsOf(letters[i].lookTargets);
                wanted.UnionWith(ids[i]);
            }
            Dictionary<int, LookTargetNow> now = LookTargetNow.Find(wanted);

            for (int i = 0; i < letters.Count; i++)
            {
                Letters.Letter l = letters[i];
                var here = new List<LookTargetNow>();
                foreach (int id in ids[i])
                {
                    if (now.TryGetValue(id, out LookTargetNow? at)) here.Add(at);
                }
                views.Add(new LetterView(
                    l.letterID,
                    l.def?.defName ?? "",
                    l.label,
                    l.text,
                    l.arrivalTick,
                    ids[i],
                    l is Letters.ChoiceLetter,
                    EventLocationView.Of(l.location),
                    here));
            }
            return views;
        }

        /// <summary>The trailing integer of each <c>Thing_…</c> load id (see <c>Thing.GetUniqueLoadID</c>,
        /// which is <c>"Thing_" + def.defName + thingIDNumber</c>).</summary>
        internal static IReadOnlyList<int> ThingIdsOf(List<string>? loadIds)
        {
            if (loadIds == null || loadIds.Count == 0) return Array.Empty<int>();

            var ids = new List<int>(loadIds.Count);
            for (int i = 0; i < loadIds.Count; i++)
            {
                string id = loadIds[i];
                if (id == null || !id.StartsWith("Thing_", StringComparison.Ordinal)) continue;

                int start = id.Length;
                while (start > 0 && char.IsDigit(id[start - 1])) start--;
                if (start == id.Length || start <= "Thing_".Length) continue;

                if (int.TryParse(id.Substring(start), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) ids.Add(n);
            }
            return ids;
        }
    }
}
