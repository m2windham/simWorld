using SimWorld.Defs;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>The wild plant that bears food. Bound by defName in its own <c>[DefOf]</c> class rather than
    /// appended to <see cref="WildPlantDefOf"/>, per CLAUDE.md: <c>DefOfHelper</c> binds by scanning every
    /// <c>[DefOf]</c> type, so a binding in its own file is wired exactly as if it sat in the shared one and
    /// cannot conflict with a lane editing that file.</summary>
    [DefOf]
    public static class WildFoodDefOf
    {
        public static ThingDef Plant_Berry = null!;
    }

    /// <summary>
    /// How much of a map's undergrowth bears food, and therefore how much a band living off this land can
    /// forage (systems: mapgen scatterers, building — plant growth, and the food economy that stands on both).
    ///
    /// <para/><b>The gap this closes.</b> <see cref="BiomeDef.forageability"/> — "0..1; how much food a
    /// forager can find here", that field's own words, authored for all eleven land biomes from an extreme
    /// desert's 0.02 to a tropical rainforest's 0.5 — was read by exactly one line of the core, and that line
    /// (<c>MapGen.GenStep_Terrain</c>) uses it as a dryness proxy to decide where to paint sand. Nothing
    /// anywhere turned it into food. Meanwhile a generated interior carried no nutrition at all, so a
    /// settlement founded the ordinary way starved inside a day. This is the number that was already in
    /// content waiting to answer that, and this class is what finally reads it as what it says.
    ///
    /// <para/><b>Two readers, one formula</b> — the same discipline <c>MapGen.MapGenTuning.WildPlantSignal</c>
    /// already imposes on ordinary undergrowth. <c>MapGen.GenStep_Scatterers</c> places the standing stock a
    /// new map is born with, and <see cref="WildPlantSpawner"/> regrows it toward the same figure at the
    /// biome's own <see cref="BiomeDef.wildPlantRegrowDays"/> pace. A map that regrew toward a second,
    /// separately-tuned density would drift away from its own tile over a game's length.
    ///
    /// <para/><b>What the settlement actually gets out of it, and why that is deliberately not enough.</b>
    /// The standing stock is a larder the founders can draw down; what regrows is the land's sustainable
    /// yield, and for a settlement of any size that yield is well under what its mouths burn. So foraging
    /// carries a young settlement through its first weeks and then stops being sufficient — which is the
    /// point at which its own fields (<see cref="FarmingInitiative"/>) and its kitchen
    /// (<c>Crafting.CookingInitiative</c>, which turns 0.5 nutrition of raw into a 0.9-nutrition meal) have to
    /// be carrying it. A wilderness that could feed a town forever would make both of those decoration.
    /// </summary>
    public static class WildFoodTuning
    {
        /// <summary>
        /// Cells of open ground per food-bearing plant on a land of <see cref="BiomeDef.forageability"/> 1.
        /// <b>This port's own number</b> — RimWorld's wild plant tables are per-species commonalities in
        /// <c>BiomeDef.wildPlants</c>, a structure this port's <see cref="BiomeDef"/> does not carry, so there
        /// is nothing to copy and this stands in for the whole table. The shipped biomes top out at 0.5, so
        /// in practice a tropical rainforest bears food on one cell in forty and a temperate forest on one in
        /// fifty. Pinned by behaviour — a founded settlement can feed itself off the land while its first
        /// fields come in, and a desert supports fewer foragers than a forest — never by this literal.
        /// </summary>
        public const float CellsPerFoodPlantAtFullForageability = 20f;

        /// <summary>Cells of map per food plant on <paramref name="tile"/>, or 0 where nothing edible grows
        /// (forageability 0 — the ice sheet, and every water biome).</summary>
        public static float FoodPlantCellsPerItem(Tile? tile)
        {
            float forageability = tile?.biome?.forageability ?? 0f;
            if (forageability <= 0f) return 0f;
            return CellsPerFoodPlantAtFullForageability / forageability;
        }

        /// <summary>How many food-bearing plants a map of <paramref name="numGridCells"/> cells on
        /// <paramref name="tile"/> should carry. Generation places this many; <see cref="WildPlantSpawner"/>
        /// keeps it there.</summary>
        public static int DesiredFoodPlantCount(int numGridCells, Tile? tile)
        {
            float cellsPerItem = FoodPlantCellsPerItem(tile);
            if (cellsPerItem <= 0f || numGridCells <= 0) return 0;
            return (int)(numGridCells / cellsPerItem);
        }
    }
}
