namespace SimWorld.Defs
{
    public partial class ThingDef
    {
        /// <summary>
        /// How many cells a vein of this mineable grows to when map generation places one (RimWorld:
        /// <c>BuildingProperties.mineableScatterLumpSizeRange</c>, read by <c>GenStep_ScatterLumpsMineable</c>;
        /// vanilla MineableSteel is 30~40). One cell — the default — is what <c>GenStep_RocksAndMountains</c>
        /// placed for every vein before crafting.materialladder, and still places for any vein whose content
        /// does not say otherwise. A single-cell vein is one stack of ore: on seed 105 the whole map's iron was
        /// 24 ore, ten iron and then nothing, so a bloomery-era settlement could never arm anyone.
        /// </summary>
        public IntRange mineableScatterLumpSizeRange = IntRange.One;
    }
}
