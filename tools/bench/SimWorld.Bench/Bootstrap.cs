using System;
using System.Linq;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench
{
    /// <summary>
    /// Stands up the same global state <c>ContentTestBase</c> gives every test: the shipped core content
    /// loaded into <see cref="DefDatabase.Global"/>, a fresh <see cref="TickManager"/>, a seeded
    /// <see cref="Rand"/> stream and a reset thing-id counter. See
    /// tests/SimWorld.Core.Tests/Content/CoreContentFixture.cs for the pattern this mirrors.
    /// </summary>
    internal static class Bootstrap
    {
        private static bool contentLoaded;

        /// <summary>Loads core content once per process. Throws if the content fails to load cleanly.</summary>
        public static void LoadContentOnce()
        {
            if (contentLoaded) return;
            var database = new DefDatabase();
            DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true });
            if (!result.Success)
            {
                string details = string.Join(Environment.NewLine, result.Errors.Select(e => "  - " + e));
                throw new InvalidOperationException("Core content failed to load:" + Environment.NewLine + details);
            }
            DefDatabase.Global = database;
            contentLoaded = true;
        }

        /// <summary>
        /// Resets the per-run simulation state: every thread-static service dropped, a fresh clock, a fresh
        /// seeded RNG, thing ids and map ids from zero.
        ///
        /// <para/><b>The first two of those were missing and the doc above claimed otherwise.</b> This method
        /// said it gave "the same global state <c>ContentTestBase</c> gives every test" while omitting
        /// <see cref="Find.Reset"/> and <c>Map.ResetMapIdCounter</c>, so a second run in the same process
        /// inherited the first one's world, storyteller, factions and finished research. The probe suite found
        /// it: two arms seeded identically produced different founding bands, visibly, before a single tick had
        /// been run. <c>ContentTestBase</c>'s own comment names both traps — leaked research made a test pass
        /// alone and fail in a subset, and <c>Building.WildPlantSpawner</c> seeds its rolls from the map's id,
        /// so a map-using run's outcome depended on how many maps earlier runs had built.
        ///
        /// <para/>Every suite that runs more than one trial per process was exposed to this, not just the
        /// probe. A perf number is less obviously wrong than a divergent population, which is precisely why it
        /// went unnoticed.
        /// </summary>
        public static void ResetSim(int seed)
        {
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
            SimWorld.Map.Map.ResetMapIdCounter();
        }
    }
}
