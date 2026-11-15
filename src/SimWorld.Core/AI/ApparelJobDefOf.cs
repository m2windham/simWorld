using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The JobDefs <see cref="JobGiver_OptimizeApparel"/> and
    /// <see cref="JobGiver_PickUpOpportunisticWeapon"/> issue (RimWorld: <c>JobDefOf.Wear</c>,
    /// <c>JobDefOf.Equip</c>), in their own <c>[DefOf]</c> class rather than appended to the shared
    /// <see cref="JobDefOf"/> (CLAUDE.md).</summary>
    [DefOf]
    public static class ApparelJobDefOf
    {
        /// <summary>Walks to a piece of apparel and puts it on (<see cref="JobDriver_Wear"/>).</summary>
        public static JobDef Wear = null!;

        /// <summary>Walks to a weapon and takes it up as primary (<see cref="JobDriver_Equip"/>).</summary>
        public static JobDef Equip = null!;
    }
}
