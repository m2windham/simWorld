using SimWorld.Defs;

namespace SimWorld.Map
{
    /// <summary>
    /// The designation kinds this port ships (RimWorld: <c>RimWorld.DesignationDefOf</c>, which lists
    /// seventeen). Only <see cref="Mine"/> is here: a <c>[DefOf]</c> field must exist in content, and a field
    /// for a kind nothing reads would be a seam the wiring audit exists to catch.
    ///
    /// <para/>A module that brings another designation kind (hunt, tame, cut plant) adds its own
    /// <c>[DefOf]</c> class and its own <c>Designations_*.xml</c> beside this one — <c>DefOfHelper</c> binds
    /// by scanning every <c>[DefOf]</c> type and <c>DefLoader</c> loads every file under the Def folder — so
    /// neither this file nor <c>Designations_Mine.xml</c> is ever contended.
    /// </summary>
    [DefOf]
    public static class DesignationDefOf
    {
        /// <summary>Dig this cell out (RimWorld: <c>DesignationDefOf.Mine</c>). Read by
        /// <see cref="AI.WorkGiver_Miner"/> and <see cref="AI.JobDriver_Mine"/>; ended by the rock being
        /// mined out (<see cref="Things.Mineable.DeSpawn"/>).</summary>
        public static DesignationDef Mine = null!;
    }
}
