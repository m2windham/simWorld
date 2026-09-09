using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Creates every civilization and places its settlements (RimWorld: <c>Verse.WorldGenStep_Populate</c> +
    /// <c>RimWorld.Faction</c> generation, combined and simplified here). Every <see cref="FactionDef"/> not
    /// marked <see cref="FactionDef.hidden"/> gets between <see cref="FactionDef.requiredCountAtGameStart"/>
    /// and <see cref="FactionDef.maxCountAtGameStart"/> instances, scaled by <see cref="OverallPopulation"/>;
    /// each instance's settlements land on weighted-random <see cref="BiomeDef.canBuildBase"/> land tiles,
    /// rejecting any candidate closer than <see cref="MinSettlementDistance"/> tiles to one already placed.
    /// <para/>
    /// <b>Every placed settlement is a real <see cref="World.Settlement"/>, not a bare <see cref="WorldObject"/>
    /// (spec §5b.5's "a settlement in the world is a Settlement").</b> All of them go through
    /// <see cref="SettlementFounder.FoundColony"/> rather than <see cref="SettlementFounder.Found"/>: a
    /// settlement placed at world generation is backstory the player never watched happen — the same
    /// "history begins there" reasoning <c>Research.ResearchManager.SetProjectFinishedForSetup</c> already
    /// applies to a scenario's starting era — so it gets a plausible established population
    /// (<see cref="SettlementTuning.EstablishedColonyPopulationRange"/>) and no chronicle entry, rather than a
    /// freshly-rolled live founding band and a "Founding: ..." line for something nobody watched found. A
    /// civilization's real, witnessed founding is <see cref="EmergenceManager"/>'s job now (spec §5b.4).
    /// </summary>
    public class WorldGenStep_Factions : WorldGenStep
    {
        /// <summary>
        /// RimWorld's world-gen "overall population" setting as a count multiplier (this port's own invention — see <see cref="OverallPopulation"/>).
        /// Public so <see cref="Factions.FactionGenerator"/> scales faction counts the same way this step scales settlement counts.
        /// </summary>
        public static readonly Dictionary<OverallPopulation, float> PopulationMultiplier = new Dictionary<OverallPopulation, float>
        {
            [OverallPopulation.AlmostNone] = 0.15f,
            [OverallPopulation.Little] = 0.4f,
            [OverallPopulation.LittleBitLess] = 0.7f,
            [OverallPopulation.Normal] = 1.0f,
            [OverallPopulation.LittleBitMore] = 1.3f,
            [OverallPopulation.High] = 1.6f,
            [OverallPopulation.VeryHigh] = 2.2f,
        };

        /// <summary>Settlements per faction instance before the population multiplier and <see cref="FactionDef.minSettlements"/> floor apply.</summary>
        public static readonly IntRange SettlementsPerFactionRange = new IntRange(1, 4);

        public override void GenerateFresh(string seed, World world)
        {
            RandomStream rand = SeededStream(seed);
            WorldGrid grid = world.grid;
            float popMultiplier = PopulationMultiplier[world.info.overallPopulation];
            int minDistance = MinSettlementDistance(grid.TilesCount);

            var candidateTiles = new List<int>();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                if (!tile.WaterCovered && tile.biome != null && tile.biome.canBuildBase)
                {
                    candidateTiles.Add(i);
                }
            }

            var placedTiles = new List<int>();

            // Factions (identity, naming, initial relations) are created by FactionGenerator; this step
            // only places their settlements, exactly as before the Factions system existed.
            List<Faction> factions = FactionGenerator.GenerateFactionsIntoWorld(world, Find.FactionManager, rand);

            foreach (Faction faction in factions)
            {
                // The player's own first settlement is not world-generation's to place. It is the opening
                // moment of the game — a region chosen, a site chosen, a founding band of 20-40 people and a
                // chronicle entry (spec §5b.3) — and Game.NewGame founds it through SettlementFounder.Found
                // for exactly that reason. Placing an already-established colony here as well would leave the
                // player's civilization holding two settlements at tick zero, one of which nobody founded.
                if (faction.def.isPlayer) continue;

                int settlementCount = SettlementCountFor(faction.def, popMultiplier, rand);
                for (int s = 0; s < settlementCount; s++)
                {
                    int? tile = PickSettlementTile(grid, candidateTiles, placedTiles, minDistance, rand);
                    if (tile == null) break;
                    placedTiles.Add(tile.Value);
                    int population = rand.Range(SettlementTuning.EstablishedColonyPopulationRange);
                    SettlementFounder.FoundColony(world, tile.Value, faction, population, rand, recordChronicle: false);
                }
            }
        }

        /// <summary>RimWorld: ~20 tiles apart at full (100k+ tile) size; scaled down for smaller grids, floored
        /// at 2 so tiny test worlds can still place several settlements. Public so <see cref="EmergenceManager"/>
        /// sites a settlement founded during play against the exact same spacing rule world generation used.</summary>
        public static int MinSettlementDistance(int tilesCount)
        {
            double scaled = 20.0 * Math.Sqrt(tilesCount / 100000.0);
            return Math.Max(2, (int)Math.Round(scaled));
        }

        /// <summary>Public so <see cref="Factions.FactionGenerator"/> uses the same required/max/population-scaling rule this step used to apply inline.</summary>
        public static int FactionCountFor(FactionDef def, float popMultiplier)
        {
            if (def.maxCountAtGameStart <= def.requiredCountAtGameStart)
            {
                return Math.Max(def.requiredCountAtGameStart, 0);
            }
            float desired = def.requiredCountAtGameStart + (def.maxCountAtGameStart - def.requiredCountAtGameStart) * popMultiplier;
            return GenMath.Clamp((int)Math.Round(desired), def.requiredCountAtGameStart, def.maxCountAtGameStart);
        }

        private static int SettlementCountFor(FactionDef def, float popMultiplier, RandomStream rand)
        {
            int min = SettlementsPerFactionRange.min;
            int max = SettlementsPerFactionRange.max;
            int floor = Math.Max(1, def.minSettlements ?? min);
            float desired = GenMath.Lerp(min, max, rand.Value) * popMultiplier;
            int count = GenMath.Clamp((int)Math.Round(desired), floor, max);
            return Math.Max(1, count);
        }

        private static int? PickSettlementTile(WorldGrid grid, List<int> candidates, List<int> placed, int minDistance, RandomStream rand)
        {
            const int MaxAttempts = 200;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (!GenCollection.TryRandomElementByWeight(candidates, t => Weight(grid, t), rand, out int candidate))
                {
                    return null;
                }
                bool tooClose = false;
                foreach (int p in placed)
                {
                    if (grid.ApproxDistanceInTiles(candidate, p) < minDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose) return candidate;
            }
            return null;
        }

        private static float Weight(WorldGrid grid, int tileId) => grid.Tiles[tileId].biome?.settlementSelectionWeight ?? 0f;
    }
}
