using System;

using SimWorld.Director;

namespace SimWorld.Sim
{
    /// <summary>
    /// Service locator for the running simulation (RimWorld: <c>Verse.Find</c>). Ported code reads
    /// <c>Find.TickManager.TicksGame</c> everywhere; keeping the name keeps those call sites. Per thread,
    /// so tests and worker threads each own a clock. Assign explicitly when constructing a game.
    /// </summary>
    public static class Find
    {
        [ThreadStatic] private static TickManager? tickManager;
        [ThreadStatic] private static SimWorld.Research.ResearchManager? researchManager;
        [ThreadStatic] private static Storyteller? storyteller;
        [ThreadStatic] private static SimWorld.Factions.FactionManager? factionManager;
        [ThreadStatic] private static SimWorld.Letters.LetterStack? letterStack;
        [ThreadStatic] private static SimWorld.Quests.QuestManager? questManager;
        [ThreadStatic] private static SimWorld.Scenario.Scenario? scenario;
        [ThreadStatic] private static SimWorld.Pawns.FamilyManager? familyManager;

        public static TickManager TickManager
        {
            get => tickManager ??= new TickManager();
            set => tickManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Research progress and the current project (RimWorld: <c>Verse.Find.ResearchManager</c>).</summary>
        public static SimWorld.Research.ResearchManager ResearchManager
        {
            get => researchManager ??= new SimWorld.Research.ResearchManager();
            set => researchManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The active threat director. Assign explicitly with a real StorytellerDef/DifficultyDef when constructing a game.</summary>
        public static Storyteller Storyteller
        {
            get => storyteller ??= new Storyteller();
            set => storyteller = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every civilization and its diplomatic relations (RimWorld: <c>Verse.Find.FactionManager</c>).</summary>
        public static SimWorld.Factions.FactionManager FactionManager
        {
            get => factionManager ??= new SimWorld.Factions.FactionManager();
            set => factionManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every letter waiting for the player (RimWorld: <c>Verse.Find.LetterStack</c>).</summary>
        public static SimWorld.Letters.LetterStack LetterStack
        {
            get => letterStack ??= new SimWorld.Letters.LetterStack();
            set => letterStack = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every generated quest (RimWorld: <c>RimWorld.Find.QuestManager</c>).</summary>
        public static SimWorld.Quests.QuestManager QuestManager
        {
            get => questManager ??= new SimWorld.Quests.QuestManager();
            set => questManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The scenario the running game started from (RimWorld: <c>Verse.Find.Scenario</c>).</summary>
        public static SimWorld.Scenario.Scenario Scenario
        {
            get => scenario ??= new SimWorld.Scenario.Scenario();
            set => scenario = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every household and the marriage/birth/death-from-age sweep that grows or shrinks them
        /// (SimWorld's own; RimWorld has no equivalent to port).</summary>
        public static SimWorld.Pawns.FamilyManager FamilyManager
        {
            get => familyManager ??= new SimWorld.Pawns.FamilyManager();
            set => familyManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Drops the thread's services so the next access starts fresh (tests). Every service above must be
        /// cleared here: a missed one leaks state between tests that call this expecting a clean slate.
        /// </summary>
        public static void Reset()
        {
            tickManager = null;
            researchManager = null;
            storyteller = null;
            factionManager = null;
            letterStack = null;
            questManager = null;
            scenario = null;
            familyManager = null;
        }
    }
}
