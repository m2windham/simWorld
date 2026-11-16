using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Things;

namespace SimWorld.World.Siting
{
    /// <summary>
    /// Whether a founding band could farm a tile at all: some ground its map can be made of
    /// (<see cref="GenStep_Terrain.BaseTerrainsFor"/>) is fertile enough to sow a food crop on, by the same
    /// test <see cref="FarmingInitiative.CropFor"/> uses to pick one. <c>Sim.Game.PickStartingTile</c> prefers
    /// such tiles for the founding settlement only.
    ///
    /// <para/><b>Why.</b> RimWorld's player sees a tile's terrain and growing season before settling and does
    /// not found a tribe on bare sand by accident; this port picks the founding tile itself. With RimWorld's
    /// fertility (sand 10%, below every food crop's sowing floor) an arid tile's all-sand map could feed nobody,
    /// and a threat-free band starved on one (<c>docs/perf/storyteller-pressure</c>, seed 2024). Later
    /// settlements choose by <see cref="SiteScorer"/> alone and may still land on hard ground.
    /// </summary>
    public static class FoundingGround
    {
        public static bool CanFarm(Tile tile)
        {
            float best = 0f;
            foreach (TerrainDef terrain in GenStep_Terrain.BaseTerrainsFor(tile))
            {
                if (terrain.fertility > best) best = terrain.fertility;
            }
            if (best <= 0f) return false;

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def.category != ThingCategory.Plant || def.plant == null || !def.plant.SowableInGround) continue;
                if (def.plant.sowMinFertility > best) continue;
                if (FarmingInitiative.NutritionPerCellPerDay(def) > 0f) return true;
            }
            return false;
        }
    }
}
