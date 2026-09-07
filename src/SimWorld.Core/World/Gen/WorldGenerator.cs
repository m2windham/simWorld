using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Entry point of world generation (RimWorld: <c>Verse.WorldGenerator</c>). Builds the icosphere grid,
    /// then runs every loaded <see cref="WorldGenStepDef"/> in ascending <see cref="WorldGenStepDef.order"/>,
    /// each seeded from the world seed and its own defName (see <see cref="WorldGenStep.GenerateFresh"/>).
    /// </summary>
    public static class WorldGenerator
    {
        /// <summary>
        /// Generates a fresh world. <paramref name="subdivisionOverride"/> is not part of RimWorld's API —
        /// production callers should normally omit it and let <paramref name="planetCoverage"/> pick the
        /// subdivision level; tests pass it directly for small, fast, exact grids (subdivision 2-4).
        /// </summary>
        public static World GenerateWorld(
            string seedString,
            float planetCoverage,
            OverallRainfall rainfall,
            OverallTemperature temperature,
            OverallPopulation population,
            string name = "World",
            int? subdivisionOverride = null)
        {
            if (seedString == null) throw new ArgumentNullException(nameof(seedString));
            int subdivision = subdivisionOverride ?? SubdivisionForCoverage(planetCoverage);

            var info = new WorldInfo
            {
                name = name,
                seedString = seedString,
                seed = GenText.StableStringHash(seedString),
                planetCoverage = planetCoverage,
                overallRainfall = rainfall,
                overallTemperature = temperature,
                overallPopulation = population,
                subdivisionLevel = subdivision,
            };

            var world = new World(info, WorldGrid.Generate(subdivision));
            RunSteps(info, world, includeAll: true);
            return world;
        }

        /// <summary>
        /// Rebuilds terrain/biomes/rivers/roads on a freshly-regenerated <see cref="WorldGrid"/> (called from
        /// <see cref="global::SimWorld.World.World.RegenerateGrid"/> during Scribe's PostLoadInit). Skips any
        /// step whose <see cref="WorldGenStepDef.regenerateOnLoad"/> is false — Factions, whose output
        /// (<see cref="global::SimWorld.World.World.factions"/>/<see cref="global::SimWorld.World.World.worldObjects"/>)
        /// the save restores directly instead.
        /// </summary>
        internal static void RegenerateTerrain(WorldInfo info, World world)
        {
            RunSteps(info, world, includeAll: false);
        }

        private static void RunSteps(WorldInfo info, World world, bool includeAll)
        {
            var steps = new List<WorldGenStepDef>(DefDatabase<WorldGenStepDef>.AllDefsListForReading);
            steps.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (WorldGenStepDef stepDef in steps)
            {
                if (!includeAll && !stepDef.regenerateOnLoad)
                {
                    continue;
                }
                stepDef.Worker.GenerateFresh(info.seedString, world);
            }
        }

        /// <summary>
        /// Maps 0..1 planet coverage to an icosphere subdivision level. RimWorld's real coverage→tile-count
        /// mapping is not published; this is our own monotonic approximation landing near subdivision 6
        /// (≈41k tiles) at 100% coverage, as the spec calls for.
        /// </summary>
        public static int SubdivisionForCoverage(float planetCoverage)
        {
            float t = GenMath.Clamp(planetCoverage, 0.05f, 1f);
            float level = GenMath.Lerp(2f, 6f, (t - 0.05f) / 0.95f);
            return GenMath.Clamp((int)Math.Round(level), 2, 6);
        }
    }
}
