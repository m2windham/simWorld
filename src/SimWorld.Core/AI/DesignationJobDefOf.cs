using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The jobs the player's designations need that nothing else in this port issued
    /// (RimWorld: <c>JobDefOf.Slaughter</c>, <c>JobDefOf.Deconstruct</c>). Their own <c>[DefOf]</c> class and
    /// their own <c>JobDefs_Designations.xml</c>, so no shared file changes.</summary>
    [DefOf]
    public static class DesignationJobDefOf
    {
        public static JobDef Slaughter = null!;
        public static JobDef Deconstruct = null!;
    }
}
