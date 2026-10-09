using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The grave, bound by defName. Its own <c>[DefOf]</c> class rather than another field on
    /// <see cref="ConstructionThingDefOf"/>, per <c>CLAUDE.md</c>: a binding in its own file cannot conflict with a
    /// lane editing the shared one.</summary>
    [DefOf]
    public static class GraveDefOf
    {
        public static ThingDef Grave = null!;
    }
}
