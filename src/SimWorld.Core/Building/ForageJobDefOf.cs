using SimWorld.AI;
using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The JobDef <see cref="WorkGiver_ForageWild"/> issues: a harvest of a wild plant, under its own
    /// name so a citizen gathering berries reads as foraging, not as bringing in a field.</summary>
    [DefOf]
    public static class ForageJobDefOf
    {
        public static JobDef Forage = null!;
    }
}
