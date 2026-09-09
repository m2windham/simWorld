using SimWorld.AI;
using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>The JobDefs this module's own job givers issue (system 16: Building — plant growth), bound
    /// by defName the same way <c>Map.MapDefOf</c>'s own per-module <c>[DefOf]</c> classes are, rather than
    /// added to <c>AI.JobDefOf</c> (outside this pass's owned files).</summary>
    [DefOf]
    public static class BuildingJobDefOf
    {
        public static JobDef Sow = null!;
        public static JobDef Harvest = null!;
    }
}
