namespace SimWorld.Crafting
{
    /// <summary>RimWorld: <c>RimWorld.CompProperties_Milkable</c>.</summary>
    public class CompProperties_Milkable : CompProperties_HasGatherableBodyResource
    {
        public CompProperties_Milkable()
        {
            compClass = typeof(CompMilkable);
        }
    }

    /// <summary>RimWorld: <c>RimWorld.CompMilkable</c>.</summary>
    public class CompMilkable : CompHasGatherableBodyResource
    {
    }
}
