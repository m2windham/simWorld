namespace SimWorld.Crafting
{
    /// <summary>
    /// RimWorld: <c>RimWorld.CompProperties_EggLayer</c>. <b>Trimmed:</b> real RimWorld can lay a fertilized
    /// egg instead of an unfertilized one when a male of the species is nearby, feeding a breeding system;
    /// no breeding exists in this port, so every egg this comp produces is unfertilized —
    /// <see cref="CompProperties_HasGatherableBodyResource.resourceDef"/> names that one ThingDef directly.
    /// </summary>
    public class CompProperties_EggLayer : CompProperties_HasGatherableBodyResource
    {
        public CompProperties_EggLayer()
        {
            compClass = typeof(CompEggLayer);
        }
    }

    /// <summary>RimWorld: <c>RimWorld.CompEggLayer</c>.</summary>
    public class CompEggLayer : CompHasGatherableBodyResource
    {
    }
}
