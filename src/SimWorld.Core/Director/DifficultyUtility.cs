using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// The one place the rest of the simulation asks what difficulty the game is being played on.
    ///
    /// <para/><b>Why a utility rather than a read at each site.</b> Every one of these knobs is consumed far
    /// from the Director — mood in <c>Needs</c>, harvest in <c>Building</c>, research in <c>Research</c>,
    /// reward value in <c>Quests</c> — and each consumer needs the same three pieces of care: the storyteller
    /// may have no <see cref="DifficultyDef"/> at all (<see cref="Find.Storyteller"/> auto-creates a bare one
    /// for tests and for a game still being assembled), a missing difficulty must read as "no effect" rather
    /// than as zero, and a factor must never come back negative. Written out at four call sites that is four
    /// chances to get one of them wrong; here it is written once and every consumer is a single expression.
    ///
    /// <para/><b>Sourcing.</b> RimWorld applies these factors inside the systems they belong to —
    /// <c>Plant.YieldNow</c>, <c>Need_Mood.CurInstantLevel</c>, <c>ResearchManager.ResearchPerformed</c>,
    /// reward generation — and this port applies them at the same places, through these properties. The exact
    /// RimWorld call sites could not be read off a decompiled source here, so each consumer records that in
    /// its own comment and the behaviour is pinned by tests that assert the *direction and ordering* between
    /// two difficulties rather than any literal (<c>tests/…/Director/DifficultyWiringTests.cs</c>).
    /// </summary>
    public static class DifficultyUtility
    {
        /// <summary>The difficulty in play, or null when the storyteller has not been given one.</summary>
        public static DifficultyDef? Current => Find.Storyteller.difficulty;

        /// <summary>Whether <see cref="IncidentCategoryDefOf.ThreatBig"/> incidents may fire at all. True
        /// with no difficulty set: an unconfigured game is not a peaceful one.</summary>
        public static bool AllowsBigThreats => Current?.allowBigThreats ?? true;

        /// <summary>Whether the storyteller's scripted early-game threat may fire — see
        /// <see cref="StorytellerComp_ClassicIntro"/>.</summary>
        public static bool AllowsIntroThreats => Current?.allowIntroThreats ?? true;

        /// <summary>Flat mood offset for the civilization's own people, in mood points out of 100 (the unit
        /// <see cref="DifficultyDef.colonistMoodOffset"/> is authored in). Zero with no difficulty set.</summary>
        public static float ColonistMoodOffset => Current?.colonistMoodOffset ?? 0f;

        /// <summary>Multiplier on what a harvested plant yields.</summary>
        public static float CropYieldFactor => Factor(Current?.cropYieldFactor);

        /// <summary>Multiplier on the value of a quest reward as it is generated.</summary>
        public static float QuestRewardValueFactor => Factor(Current?.questRewardValueFactor);

        /// <summary>Multiplier on research points earned per unit of work.</summary>
        public static float ResearchSpeedFactor => Factor(Current?.researchSpeedFactor);

        /// <summary>A multiplicative knob: absent reads as 1 (leave the value alone), and a negative one is
        /// floored at 0 rather than inverting the thing it scales.</summary>
        private static float Factor(float? value)
        {
            if (value == null) return 1f;
            return value.Value > 0f ? value.Value : 0f;
        }
    }
}
