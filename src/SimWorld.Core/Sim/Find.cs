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

        /// <summary>Drops the thread's services so the next access starts fresh (tests).</summary>
        public static void Reset()
        {
            tickManager = null;
            researchManager = null;
            storyteller = null;
            factionManager = null;
        }
    }
}
