using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Filth
{
    /// <summary>The JobDef <see cref="WorkGiver_CleanFilth"/> issues (RimWorld: <c>JobDefOf.Clean</c>), bound
    /// in its own <c>[DefOf]</c> class in its own file rather than appended to the shared
    /// <see cref="JobDefOf"/> — the same reason <c>AI.RepairJobDefOf</c> and <c>Building.BuildingJobDefOf</c>
    /// are separate (CLAUDE.md).</summary>
    [DefOf]
    public static class CleaningJobDefOf
    {
        /// <summary>Scrubs one pile of <see cref="Filth"/> away a layer at a time (see
        /// <see cref="JobDriver_CleanFilth"/>).</summary>
        public static JobDef Clean = null!;
    }

    /// <summary>The stat <see cref="JobDriver_CleanFilth"/> counts work by (RimWorld:
    /// <c>StatDefOf.CleaningSpeed</c>); ships in this module's own <c>Stats_Cleaning.xml</c>.</summary>
    [DefOf]
    public static class CleaningStatDefOf
    {
        public static StatDef CleaningSpeed = null!;
    }
}
