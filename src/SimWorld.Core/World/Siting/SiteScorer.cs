using System;

namespace SimWorld.World.Siting
{
    /// <summary>
    /// Scores a candidate settlement tile: hard necessities (multiplicative, able to zero the score) times
    /// era-weighted advantages (spec §5b.2). Advisory for the player's own founding, decisive for emergent
    /// and NPC foundings — this class only produces the number; which of those two readings applies is a
    /// later system's call. The necessities include the biome itself: an
    /// <see cref="BiomeDef.isExtremeBiome"/> tile is never a site anything chooses on its own (see
    /// <see cref="NecessityMultiplier"/>).
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

        /// <summary>
        /// Hard necessities, multiplied: fresh water in reach, food in reach, a survivable climate — and a
        /// biome the group would not choose to found in at all.
        ///
        /// <para/><b>The biome gate (<see cref="BiomeDef.isExtremeBiome"/>).</b> RimWorld marks its ice sheet
        /// and extreme desert this way and keeps them out of ordinary starting-site selection; the exact call
        /// site is not sourceable in this sandbox, so what is ported is the rule rather than a copied line,
        /// and it is pinned by a test asserting an extreme tile scores zero however good its deposits are.
        /// It sits here, in the score, rather than in <c>Sim.Game.PickStartingTile</c>, because every
        /// automatic founding path in this port — the player's start, <see cref="EmergenceManager"/>'s new
        /// civilizations and its expansions — chooses its tile by this number, so one gate covers all three
        /// and a fourth caller cannot forget it. A player who names a starting tile explicitly
        /// (<c>Game.NewGame</c>'s <c>startTile</c>) still gets it, exactly as RimWorld lets you settle an ice
        /// sheet on purpose; what stops is the game <em>choosing</em> one for you.
        ///
        /// <para/>Deliberately not applied to <c>Gen.WorldGenStep_Factions</c>, which places NPC settlements
        /// by <see cref="BiomeDef.settlementSelectionWeight"/> instead: content gives the ice sheet a small
        /// but non-zero weight on purpose, so a world can still have somebody living up there.
        /// </summary>
        private static float NecessityMultiplier(WorldGrid grid, int tileId, Tile tile)
        {
            if (tile.biome != null && tile.biome.isExtremeBiome) return 0f;

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

        /// <summary>
        /// The best <paramref name="def"/> magnitude at <paramref name="tileId"/> or any of its neighbours —
        /// "in reach" of a settlement centred there. Used for necessity gating here; also the shared "does
        /// this settlement have this deposit at all" check <c>Economy.CoalSupply</c> reuses to decide whether
        /// a settlement needs to trade for coal rather than duplicating the neighbour-scan logic.
        /// </summary>
        public static float BestNearbyMagnitude(WorldGrid grid, int tileId, DepositDef def)
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
