using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The JobDef <see cref="WorkGiver_Repair"/> issues (RimWorld: <c>JobDefOf.Repair</c>), bound by
    /// defName in its own <c>[DefOf]</c> class the way <c>Building.BuildingJobDefOf</c> already is, rather
    /// than appended to <see cref="JobDefOf"/> — a new class in a new file cannot collide with another lane
    /// mid-edit on that shared one (CLAUDE.md: "add a file rather than edit a shared one").</summary>
    [DefOf]
    public static class RepairJobDefOf
    {
        /// <summary>Restores one damaged building's HitPoints a point at a time until it is whole again
        /// (system: work — <see cref="WorkGiver_Repair"/>/<see cref="JobDriver_Repair"/>).</summary>
        public static JobDef Repair = null!;
    }
}
