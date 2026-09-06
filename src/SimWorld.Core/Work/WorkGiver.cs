using SimWorld.Pawns;

namespace SimWorld.Work
{
    /// <summary>
    /// Runtime behaviour behind one <see cref="WorkGiverDef"/> (RimWorld: <c>Verse.WorkGiver</c>). Only the
    /// eligibility surface other systems need now is here; scanning the map for jobs lands with system 9.
    /// </summary>
    public abstract class WorkGiver
    {
        public WorkGiverDef def = null!;

        /// <summary>Early-out before any scan; the default never skips.</summary>
        public virtual bool ShouldSkip(Pawn pawn, bool forced = false) => false;

        /// <summary>True when the pawn lacks one of <see cref="WorkGiverDef.requiredCapacities"/>.</summary>
        public bool MissingRequiredCapacity(Pawn pawn)
        {
            if (pawn == null) throw new System.ArgumentNullException(nameof(pawn));
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
    /// Deliberately just a marker shape for now — <c>Prioritized</c>/<c>PathEndMode</c> and the actual scan land
    /// with the job system (system 9).
    /// </summary>
    public abstract class WorkGiver_Scanner : WorkGiver
    {
    }

    /// <summary>No-op concrete worker content can point at until real scanners exist.</summary>
    public class WorkGiver_Pending : WorkGiver
    {
    }
}
