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

        /// <summary>Resets the per-run simulation state: a fresh clock, a fresh seeded RNG, ids from zero.</summary>
        public static void ResetSim(int seed)
        {
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
        }
    }
}
