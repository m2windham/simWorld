using SimWorld.Defs;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// What the world does when a civilization opens an age past the end of the authored ladder.
    ///
    /// <para/>The same shape as <see cref="EraTransitionUtility"/>, and for the same reason: the manager
    /// detects the crossing and says so, this decides what the world makes of it. What it buys is the
    /// difference between endless tech reading as a continuing history and reading as a counter — a
    /// civilization that has outlived the last authored era still gets a named age it entered, in the
    /// chronicle and in a letter, exactly as it did for the eight before.
    ///
    /// <para/>An age is not an <see cref="EraDef"/> (see <see cref="EndlessAgeDef"/>), so this writes its own
    /// letter def rather than reusing <see cref="LetterDefOf.EraReached"/>: the two read the same to a player
    /// and must not be confused by anything reading the stack, because only one of them moved the ladder.
    /// </summary>
    public static class EndlessAgeUtility
    {
        /// <summary>Records the chronicle line and pushes the letter for an age a civilization has opened.</summary>
        public static void Notify_AgeOpened(EndlessAgeDef ages, int ageIndex, int seed)
        {
            if (ages == null) return;

            string label = ages.LabelFor(ageIndex, seed);
            Find.Storyteller.RecordChronicle(ChronicleLine(label));
            Find.LetterStack.ReceiveLetter(LetterLabel(label), LetterText(ages, ageIndex, seed, label), EndlessResearchDefOf.EndlessAgeOpened);
        }

        /// <summary>
        /// "Age opened", not "age reached". An era is <i>reached</i> by finishing the one below it, and that
        /// is history the civilization has already lived. An age is opened at its frontier — it is what the
        /// civilization is about to spend the next stretch of its existence on, and nobody has lived it yet.
        /// The prefixed-headline shape matches the other free-form chronicle lines ("Era reached: …",
        /// "Quest offered: …") and stays grammatical whatever content names an age.
        /// </summary>
        internal static string ChronicleLine(string ageLabel) => "Age opened: " + ageLabel + ".";

        private static string LetterLabel(string ageLabel) => "A new age: " + ageLabel;

        private static string LetterText(EndlessAgeDef ages, int ageIndex, int seed, string ageLabel)
        {
            string body = ChronicleLine(ageLabel);
            string? description = ages.DescriptionFor(ageIndex, seed);
            return string.IsNullOrEmpty(description) ? body : body + "\n\n" + description;
        }
    }

    /// <summary>
    /// Defs endless research reaches for by name. Its own class and its own content file rather than an
    /// addition to <see cref="LetterDefOf"/> or <see cref="ResearchProjectDefOf"/>: <c>DefOfHelper</c> binds by
    /// scanning every <c>[DefOf]</c> type, so an additive binding never has to touch a file another lane is
    /// editing (CLAUDE.md, "add a file rather than edit a shared one").
    /// </summary>
    [DefOf]
    public static class EndlessResearchDefOf
    {
        /// <summary>The civilization has opened an age past the end of the authored era ladder.</summary>
        public static LetterDef EndlessAgeOpened = null!;

        /// <summary>The register the endless tail names its ages in.</summary>
        public static EndlessAgeDef EndlessAges = null!;
    }
}
