using System.Collections.Generic;
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
        /// The chronicle headline for an era. "Reached" means entered: the civilization has mastered the era
        /// before (its research spine and its conditions) and is now in this one, learning it
        /// (<see cref="ResearchManager.CurrentEra"/>). It used to mean the era's own spine was finished, which
        /// put a fresh start "in" an era it had not learned and refused an Agrarian village its own edicts. The
        /// prefixed-headline shape matches the other free-form chronicle lines ("Quest offered: …", "Birth: …")
        /// and, unlike a sentence built around the label, stays grammatical whatever content names an era.
        /// </summary>
        internal static string ChronicleLine(EraDef? from, EraDef to) =>
            from == null
                ? "Era reached: " + Name(to) + "."
                : "Era reached: " + Name(to) + ", after " + Name(from) + ".";

        private static string LetterLabel(EraDef to) => "A new era: " + Name(to);

        /// <summary>The era's label as a name, capitalised ("Agrarian", "Sticks and stones": content writes labels
        /// in lower case for mid-sentence use, and an era is named, not described), falling back to its defName
        /// when content gave it none.</summary>
        private static string Name(EraDef era) => era.label != null ? era.LabelCap : era.defName;

        private static string LetterText(EraDef? from, EraDef to)
        {
            string body = ChronicleLine(from, to);
            if (!string.IsNullOrEmpty(to.description)) body += "\n\n" + to.description;
            List<string> changes = EraUnlocks.Describe(to);
            if (changes.Count > 0) body += "\n\n" + string.Join("\n", changes);
            return body;
        }
    }
}
