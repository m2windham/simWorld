using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Work
{
    /// <summary>
    /// Runtime behaviour behind one <see cref="WorkGiverDef"/> (RimWorld: <c>Verse.WorkGiver</c>).
    /// </summary>
    public abstract class WorkGiver
    {
        public WorkGiverDef def = null!;

        /// <summary>Early-out before any scan; the default never skips.</summary>
        public virtual bool ShouldSkip(Pawn pawn, bool forced = false) => false;

        /// <summary>True when the pawn lacks one of <see cref="WorkGiverDef.requiredCapacities"/>.</summary>
        public bool MissingRequiredCapacity(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (def.requiredCapacities == null) return false;
            for (int i = 0; i < def.requiredCapacities.Count; i++)
            {
                if (!pawn.health.capacities.CapableOf(def.requiredCapacities[i])) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A work giver that scans the map for things (or, later, cells) to act on (RimWorld:
    /// <c>Verse.WorkGiver_Scanner</c>). <see cref="JobGiver_Work"/> is the only caller: it walks
    /// <see cref="PotentialWorkThingsGlobal"/> for the nearest candidate <see cref="HasJobOnThing"/> accepts,
    /// then asks <see cref="JobOnThing"/> for the actual job.
    /// </summary>
    public abstract class WorkGiver_Scanner : WorkGiver
    {
        /// <summary>Where the pather should end up relative to a target thing.</summary>
        public virtual PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>Whether candidates should be visited nearest-first; every giver this pass ships does, so
        /// true is the sensible default (RimWorld defaults it per-giver instead; nothing here needs otherwise).</summary>
        public virtual bool Prioritized => true;

        /// <summary>Every Thing on the map this giver could possibly act on, before eligibility narrows it down.</summary>
        public virtual IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => Array.Empty<Thing>();

        /// <summary>Cell-scanning counterpart for work that targets bare cells rather than Things; no giver
        /// this pass ships needs it (hauling/building/growing all depend on systems not yet built).</summary>
        public virtual IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Array.Empty<IntVec3>();

        public virtual bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) => false;

        public virtual Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => null;

        public virtual Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => null;
    }

    /// <summary>No-op concrete worker content points at until a real scanner is wired for that WorkGiverDef.</summary>
    public class WorkGiver_Pending : WorkGiver
    {
    }
}
