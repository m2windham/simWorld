using System;

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

        /// <summary>Drops the thread's services so the next access starts fresh (tests).</summary>
        public static void Reset()
        {
            tickManager = null;
            researchManager = null;
        }
    }
}
