using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// What happens when a civilization enters a new era (<c>docs/spec/simworld-spec.md</c> §10: "era
    /// completion gates content and scales threats"). SimWorld's own — RimWorld has no era ladder to port.
    /// <para/>
    /// Kept out of <see cref="ResearchManager"/> so the manager stays about research: the manager detects the
    /// crossing and says so, this decides what the world does about it. <see cref="ResearchManager.EraReached"/>
    /// is the hook for anything else that wants to react.
    /// </summary>
    public static class EraTransitionUtility
    {
        /// <summary>Records the Chronicle line and pushes the letter for an era the civilization has entered.</summary>
        public static void Notify_EraReached(EraDef? from, EraDef to)
        {
            if (to == null) return;

            Find.Storyteller.RecordChronicle(ChronicleLine(from, to));
            Find.LetterStack.ReceiveLetter(LetterLabel(to), LetterText(from, to), LetterDefOf.EraReached);
        }

        /// <summary>
        /// The threat-point multiplier for the era the civilization is in right now, 1 when there is no era
        /// ladder loaded at all. See <see cref="EraDef.threatPointsFactor"/> for why this exists and what it
        /// stands in for.
        /// </summary>
        public static float CurrentEraThreatPointsFactor() => Find.ResearchManager.CurrentEra?.threatPointsFactor ?? 1f;

        /// <summary>
        /// The chronicle headline for an era. "Reached" is the vocabulary the rest of the module already uses
        /// (<see cref="ResearchManager.CurrentEra"/> is "the furthest era reached"), and it means the era's
        /// spine is finished — a civilization reaches the bronze age by having learned bronze, not by
        /// starting to. The prefixed-headline shape matches the other free-form chronicle lines
        /// ("Quest offered: …", "Birth: …") and, unlike a sentence built around the label, stays grammatical
        /// whatever content names an era.
        /// </summary>
        internal static string ChronicleLine(EraDef? from, EraDef to) =>
            from == null
                ? "Era reached: " + Name(to) + "."
                : "Era reached: " + Name(to) + ", after the " + Name(from) + ".";

        private static string LetterLabel(EraDef to) => "A new era: " + Name(to);

        /// <summary>The era's label for prose, falling back to its defName when content gave it none.</summary>
        private static string Name(EraDef era) => era.label ?? era.defName;

        private static string LetterText(EraDef? from, EraDef to)
        {
            string body = ChronicleLine(from, to);
            return string.IsNullOrEmpty(to.description) ? body : body + "\n\n" + to.description;
        }
    }
}
