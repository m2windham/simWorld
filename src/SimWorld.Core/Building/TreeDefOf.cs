using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The tree (<c>Plants_Trees.xml</c>), bound in its own <c>[DefOf]</c> class the way
    /// <see cref="WildPlantDefOf"/> is. <c>MapGen.GenStep_Trees</c> places it, <see cref="WildPlantSpawner"/>
    /// regrows it and <see cref="WorkGiver_ConstructChopWood"/> fells it.</summary>
    [DefOf]
    public static class TreeDefOf
    {
        public static ThingDef Plant_TreePoplar = null!;
    }
}
