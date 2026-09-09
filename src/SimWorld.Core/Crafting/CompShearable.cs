namespace SimWorld.Crafting
{
    /// <summary>RimWorld: <c>RimWorld.CompProperties_Shearable</c>.</summary>
    public class CompProperties_Shearable : CompProperties_HasGatherableBodyResource
    {
        public CompProperties_Shearable()
        {
            compClass = typeof(CompShearable);
        }
    }

    /// <summary>RimWorld: <c>RimWorld.CompShearable</c>.</summary>
    public class CompShearable : CompHasGatherableBodyResource
    {
    }
}
