using SimWorld.Defs;

namespace SimWorld.Letters
{
    [DefOf]
    public static class LetterDefOf
    {
        public static LetterDef NeutralEvent = null!;
        public static LetterDef PositiveEvent = null!;
        public static LetterDef NegativeEvent = null!;
        public static LetterDef ThreatBig = null!;
        public static LetterDef AcceptQuest = null!;

        /// <summary>The civilization has entered a new era (<see cref="Research.EraTransitionUtility"/>).</summary>
        public static LetterDef EraReached = null!;
    }
}
