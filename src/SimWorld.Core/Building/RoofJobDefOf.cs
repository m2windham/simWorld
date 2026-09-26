using SimWorld.AI;
using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The JobDefs this module's roof work givers issue (system 95: Roofs), bound by defName in
    /// this module's own <c>[DefOf]</c> class the way <see cref="BuildingJobDefOf"/>/<see cref="RoofCollapseDefOf"/>
    /// already are, rather than added to <c>AI.JobDefOf</c> (a shared file outside this pass's own).</summary>
    [DefOf]
    public static class RoofJobDefOf
    {
        public static JobDef BuildRoof = null!;
        public static JobDef RemoveRoof = null!;
    }
}
