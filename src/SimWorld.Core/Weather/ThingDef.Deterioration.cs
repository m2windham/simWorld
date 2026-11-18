namespace SimWorld.Defs
{
    /// <summary>Deterioration half of <see cref="ThingDef"/>, its own partial file as
    /// <c>ThingDef.Filth.cs</c> is.</summary>
    public partial class ThingDef
    {
        /// <summary>Whether weather can wear this down at all (RimWorld: <c>ThingDef.CanEverDeteriorate</c>,
        /// recalled as items with hit points; RimWorld's extra plant case has nothing to apply to here, since
        /// no plant here carries a deterioration rate).</summary>
        public bool CanEverDeteriorate => useHitPoints && category == ThingCategory.Item;
    }
}
