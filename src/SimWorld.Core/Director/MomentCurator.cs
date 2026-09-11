using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Decides which <see cref="ChronicleEntry"/> lines are a <i>moment</i> — the narrator noticing something
    /// worth remembering, rather than a line per routine event (<c>docs/spec/simworld-spec.md</c> §10, tracker
    /// item <c>quests.moments</c>: "a first, a record, a turning point"). Owned by <see cref="Storyteller"/>
    /// (<see cref="Storyteller.moments"/>) the same way <see cref="StoryWatcher_Adaptation"/> and
    /// <see cref="IncidentQueue"/> are — a small piece of persistent bookkeeping deep-Scribed alongside it,
    /// not a separate service.
    /// <para/>
    /// <b>Three rules, each independently bounded in count</b> — this is what makes "a routine century does not
    /// produce hundreds of moments" true by construction rather than by tuning a threshold:
    /// <list type="bullet">
    /// <item><b>First of its kind.</b> <see cref="Consider"/> marks an entry the first time its <i>category</i>
    /// is ever seen (<see cref="seenCategories"/>) and never again for that category — so the running total
    /// across a whole game is bounded by the number of distinct categories that ever occur, never by how long
    /// the game runs or how many births/deaths/incidents happen. A category is an <see cref="IncidentDef"/>'s
    /// own <c>defName</c> for a fired incident, <c>"Death:" + cause</c> for a death, or the text before the
    /// first colon for a free-form line (<see cref="CategoryForFreeform"/>) — "Birth", "Edict issued", "Quest
    /// offered" and so on all already read this way with no change needed at any call site.</item>
    /// <item><b>Always a moment: reaching a new era.</b> <c>docs/spec/simworld-spec.md</c> §10 is explicit that
    /// "reaching an era is an event, not just a readout" — every era crossing is a moment, not only the first,
    /// because the era ladder is itself small and fixed (bounded by content, not by how long the game runs), so
    /// marking every one costs nothing toward the century test.</item>
    /// <item><b>Records.</b> <see cref="ConsiderDeathRecord"/> and <see cref="ConsiderLifeRecord"/> share one
    /// ratchet — the longest life this civilization has ever known, completed or still running — and mark a
    /// moment only when it is broken. By definition that fires less and less often the longer a game runs
    /// (each new record must beat every one before it), never more. The two halves are one number on purpose:
    /// a life is not a record for having ended, and a civilization that has watched someone reach ninety does
    /// not find an eighty-year-old remarkable afterwards.</item>
    /// </list>
    /// </summary>
    public sealed class MomentCurator : IExposable
    {
        /// <summary>Categories the "always a moment" rule applies to regardless of whether they have fired
        /// before — see the class doc's second bullet. Kept to exactly the one case the spec calls out by name;
        /// everything else falls back to "first of its kind".</summary>
        private static readonly HashSet<string> AlwaysMomentCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Era reached",
        };

        private readonly HashSet<string> seenCategories = new HashSet<string>();

        /// <summary>The longest life this civilization has ever known — completed (<see cref="ConsiderDeathRecord"/>)
        /// or still being lived (<see cref="ConsiderLifeRecord"/>). One ratchet for both; see the class doc.
        /// Its Scribe label is still <c>longestLifespanYearsAtDeath</c>, from when a death was the only thing
        /// that could move it, so saves written before the living half existed still load.</summary>
        private float longestLifeYearsKnown = -1f;

        private readonly List<ChronicleEntry> moments = new List<ChronicleEntry>();

        /// <summary>The curated history: every entry this curator has ever flagged, kept separately from
        /// <see cref="Storyteller.Chronicle"/>'s own rolling, capacity-bounded log
        /// (<see cref="Storyteller.ChronicleCapacity"/>) so a moment can never be silently evicted just because
        /// enough routine chronicle entries came after it — the whole point of a moments list is that it reads
        /// back as history, not as a log with a retention window.</summary>
        public IReadOnlyList<ChronicleEntry> Moments => moments;

        /// <summary>The longest life this civilization has ever known, in years, or a negative number before
        /// it has known any. Read by <see cref="ChronicleFame"/>, which is the policy that decides what a
        /// living citizen passing it means; this class only keeps the number.</summary>
        public float LongestLifeYearsKnown => longestLifeYearsKnown;

        /// <summary>
        /// Considers one freshly-recorded chronicle entry under the "first of its kind" / "always" rules
        /// (<see cref="AlwaysMomentCategories"/>) and flags it (<see cref="ChronicleEntry.isMoment"/>, plus
        /// appending to <see cref="Moments"/>) if either applies. Idempotent per category for the "first" rule:
        /// <see cref="HashSet{T}.Add"/> returning true both performs and answers the membership check in one
        /// step, so the same category cannot be flagged as a first twice.
        /// </summary>
        public void Consider(ChronicleEntry entry, string category)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            bool isMoment = AlwaysMomentCategories.Contains(category) || seenCategories.Add(category);
            if (!isMoment) return;

            entry.isMoment = true;
            moments.Add(entry);
        }

        /// <summary>
        /// The "records" rule at a death: flags <paramref name="entry"/> only when
        /// <paramref name="ageYearsAtDeath"/> beats every life this curator has known — which, since
        /// <see cref="ConsiderLifeRecord"/> exists, includes the ones still being lived. Safe to call
        /// alongside <see cref="Consider"/> on the same entry (a death that is both a "first death by this
        /// cause" and a new longevity record is only added to <see cref="Moments"/> once — the
        /// <see cref="ChronicleEntry.isMoment"/> guard prevents the double-add).
        /// </summary>
        public void ConsiderDeathRecord(ChronicleEntry entry, float ageYearsAtDeath) =>
            ConsiderLifeRecord(entry, ageYearsAtDeath);

        /// <summary>
        /// The same "records" rule read off a life still being lived: flags <paramref name="ageYears"/> as a
        /// moment only when it beats every life this civilization has known, dead or living, and advances the
        /// ratchet when it does. <see cref="ConsiderDeathRecord"/> is this method under its older name —
        /// there is one record and two ways to reach it, not two records.
        /// <para/>
        /// Silently advancing the ratchet without a moment is not a case this method has: a caller that wants
        /// to keep the number current for a citizen already distinguished passes no entry — see
        /// <see cref="AdvanceLifeRecord"/>.
        /// </summary>
        public void ConsiderLifeRecord(ChronicleEntry entry, float ageYears)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!AdvanceLifeRecord(ageYears)) return;

            if (entry.isMoment) return; // already flagged via Consider; do not add it to Moments twice.
            entry.isMoment = true;
            moments.Add(entry);
        }

        /// <summary>
        /// Moves the ratchet to <paramref name="ageYears"/> if that beats it, and answers whether it did,
        /// writing nothing either way. This is how the reigning record-holder's own ageing keeps the number
        /// honest: every year they live is a new longest life, and the civilization does not remark on it a
        /// second time (<see cref="ChronicleFame"/>'s own doc).
        /// </summary>
        public bool AdvanceLifeRecord(float ageYears)
        {
            if (ageYears <= longestLifeYearsKnown) return false;
            longestLifeYearsKnown = ageYears;
            return true;
        }

        /// <summary>Derives a stable category from a free-form chronicle line: the text before its first colon
        /// ("Birth: Ada joins family 3." -&gt; "Birth"), or the whole text when there is none. Every free-form
        /// call site in this codebase already writes "Category: detail" (birth, edicts, quests, foundings, era
        /// transitions, civilization emergence), so this needs no change at any of them to work.</summary>
        public static string CategoryForFreeform(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            int colon = text.IndexOf(':');
            return (colon >= 0 ? text.Substring(0, colon) : text).Trim();
        }

        public void ExposeData()
        {
            List<string>? categoryList = new List<string>(seenCategories);
            Scribe_Collections.Look(ref categoryList, "seenCategories", LookMode.Value);
            seenCategories.Clear();
            if (categoryList != null) foreach (string c in categoryList) seenCategories.Add(c);

            Scribe_Values.Look(ref longestLifeYearsKnown, "longestLifespanYearsAtDeath", -1f);

            List<ChronicleEntry>? momentList = new List<ChronicleEntry>(moments);
            Scribe_Collections.Look(ref momentList, "moments", LookMode.Deep);
            moments.Clear();
            if (momentList != null) moments.AddRange(momentList);
        }
    }
}
