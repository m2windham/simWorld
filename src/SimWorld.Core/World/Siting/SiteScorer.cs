using System;

namespace SimWorld.World.Siting
{
    /// <summary>
    /// Scores a candidate settlement tile: hard necessities (multiplicative, able to zero the score) times
    /// era-weighted advantages (spec §5b.2). Advisory for the player's own founding, decisive for emergent
    /// and NPC foundings — this class only produces the number; which of those two readings applies is a
    /// later system's call.
    /// </summary>
    public static class SiteScorer
    {
        /// <summary>
        /// <paramref name="tradePositionScore"/> is <see cref="TradePositionScorer"/>'s 0..1 output for this
        /// tile (0 when the caller has none, e.g. scoring a single tile in isolation).
        /// </summary>
        public static float Score(WorldGrid grid, int tileId, SiteWeightDef weights, float tradePositionScore = 0f)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (weights == null) throw new ArgumentNullException(nameof(weights));

            Tile tile = grid.Tiles[tileId];
            if (tile.WaterCovered) return 0f;

            float necessity = NecessityMultiplier(grid, tileId, tile);
            if (necessity <= 0f) return 0f;

            return necessity * AdvantageSum(tile, weights, tradePositionScore);
        }

        private static float NecessityMultiplier(WorldGrid grid, int tileId, Tile tile)
        {
            float water = GenMath.Clamp01(BestNearbyMagnitude(grid, tileId, DepositDefOf.FreshWater) / SiteTuning.FreshWaterNecessityFloor);
            if (water <= 0f) return 0f;

            float food = GenMath.Clamp01(BestNearbyFoodMagnitude(grid, tileId) / SiteTuning.FoodNecessityFloor);
            if (food <= 0f) return 0f;

            float climate = ClimateNecessity(tile);
            return water * food * climate;
        }

        private static float ClimateNecessity(Tile tile)
        {
            float t = tile.temperature;
            if (t <= SiteTuning.SurvivableTemperatureMinC || t >= SiteTuning.SurvivableTemperatureMaxC) return 0f;

            float lowRamp = GenMath.Clamp01((t - SiteTuning.SurvivableTemperatureMinC) / SiteTuning.TemperatureRampMarginC);
            float highRamp = GenMath.Clamp01((SiteTuning.SurvivableTemperatureMaxC - t) / SiteTuning.TemperatureRampMarginC);
            return Math.Min(lowRamp, highRamp);
        }

        private static float BestNearbyMagnitude(WorldGrid grid, int tileId, DepositDef def)
        {
            float best = grid.Tiles[tileId].DepositMagnitude(def);
            foreach (int n in grid.NeighborsOf(tileId))
            {
                Tile neighbor = grid.Tiles[n];
                if (neighbor.WaterCovered) continue;
                float m = neighbor.DepositMagnitude(def);
                if (m > best) best = m;
            }
            return best;
        }

        private static float BestNearbyFoodMagnitude(WorldGrid grid, int tileId)
        {
            float best = BestNearbyMagnitude(grid, tileId, DepositDefOf.ArableSoil);
            best = Math.Max(best, BestNearbyMagnitude(grid, tileId, DepositDefOf.Game));
            best = Math.Max(best, BestNearbyMagnitude(grid, tileId, DepositDefOf.Timber));
            return best;
        }

        private static float AdvantageSum(Tile tile, SiteWeightDef weights, float tradePositionScore)
        {
            float sum = 0f;
            for (int i = 0; i < weights.depositWeights.Count; i++)
            {
                DepositWeight dw = weights.depositWeights[i];
                sum += dw.weight * tile.DepositMagnitude(dw.deposit);
            }
            sum += weights.tradePositionWeight * tradePositionScore;
            return sum;
        }
    }
}
