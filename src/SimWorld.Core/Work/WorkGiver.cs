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
    /// A work giver that scans the map for things or cells to act on (RimWorld: <c>Verse.WorkGiver_Scanner</c>).
    /// <see cref="JobGiver_Work"/> is the only caller, via <see cref="AI.WorkGiverScanUtility"/>: for a
    /// thing-scanning giver (<see cref="WorkGiverDef.scanThings"/>, the default) it walks
    /// <see cref="PotentialWorkThingsGlobal"/> for the nearest candidate <see cref="HasJobOnThing"/> accepts,
    /// then asks <see cref="JobOnThing"/> for the actual job; for a cell-scanning giver
    /// (<see cref="WorkGiverDef.scanCells"/>) the same shape runs over <see cref="PotentialWorkCellsGlobal"/>/
    /// <see cref="HasJobOnCell"/>/<see cref="JobOnCell"/> instead.
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

        /// <summary>Cell-scanning counterpart for work that targets bare cells rather than Things — consulted
        /// by <see cref="AI.WorkGiverScanUtility"/> instead of <see cref="PotentialWorkThingsGlobal"/> when
        /// <see cref="WorkGiverDef.scanCells"/> is set (system 16: Building — <c>WorkGiver_GrowerSow</c>
        /// targets an empty, sowable cell inside a growing zone, which has no Thing of its own to scan for).</summary>
        public virtual IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Array.Empty<IntVec3>();

        public virtual bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) => false;

        public virtual Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => null;

        /// <summary>Cell-scanning counterpart of <see cref="HasJobOnThing"/>; consulted by <see cref="AI.WorkGiverScanUtility"/>
        /// only when <see cref="WorkGiverDef.scanCells"/> is set (system 16: Building — plant growth is the
        /// first giver this pass ships that needs it; see that def field's own remarks).</summary>
        public virtual bool HasJobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => false;

        public virtual Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => null;
    }

    /// <summary>No-op concrete worker content points at until a real scanner is wired for that WorkGiverDef.</summary>
    public class WorkGiver_Pending : WorkGiver
    {
    }
}
