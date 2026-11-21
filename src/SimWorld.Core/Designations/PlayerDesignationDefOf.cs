using SimWorld.Defs;

namespace SimWorld.Designations
{
    /// <summary>
    /// The designations the player places through <c>Map.View.MapCommands.Designate</c>, beside
    /// <see cref="DesignationDefOf.Mine"/> (which a settlement places too). RimWorld's names, from its
    /// <c>DesignationDefOf</c>; their own <c>[DefOf]</c> class so <c>DesignationDef.cs</c> stays as it was.
    /// RimWorld's Forbid/Unforbid are absent: this port has no <c>CompForbiddable</c> to designate against.
    /// </summary>
    [DefOf]
    public static class PlayerDesignationDefOf
    {
        public static DesignationDef CutPlant = null!;
        public static DesignationDef HarvestPlant = null!;
        public static DesignationDef Hunt = null!;
        public static DesignationDef Tame = null!;
        public static DesignationDef Slaughter = null!;
        public static DesignationDef Deconstruct = null!;
        public static DesignationDef HaulUrgently = null!;
    }
}
