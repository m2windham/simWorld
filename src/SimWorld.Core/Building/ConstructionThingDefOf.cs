using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The buildable Defs <see cref="SettlementConstructionInitiative"/> knows how to want, bound by
    /// defName the same way every other per-module <c>[DefOf]</c> class here is (e.g. <see cref="BuildingJobDefOf"/>).</summary>
    [DefOf]
    public static class ConstructionThingDefOf
    {
        public static ThingDef Bed = null!;
        public static ThingDef Wall = null!;
        public static ThingDef StorageHut = null!;
    }
}
