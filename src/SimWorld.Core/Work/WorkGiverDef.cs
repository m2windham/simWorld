using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Work
{
    /// <summary>
    /// One job source within a <see cref="WorkTypeDef"/> (RimWorld: <c>Verse.WorkGiverDef</c>) — "haul this",
    /// "tend that patient". Scanning the map and producing actual jobs lands with the job system (system 9);
    /// for now this carries only the ordering and eligibility data other systems (and tests) need.
    /// </summary>
    public class WorkGiverDef : Def
    {
        /// <summary>Runtime worker; defaults to the no-op placeholder until real scanners exist.</summary>
        public Type giverClass = typeof(WorkGiver_Pending);

        public WorkTypeDef workType = null!;

        /// <summary>Break ties within a work type; higher runs first.</summary>
        public int priorityInType;

        public string? verb;
        public string? gerund;

        /// <summary>Runs ahead of everything else in its work type, before the normal priority order.</summary>
        public bool emergency;

        /// <summary>Every listed capacity must be present or a pawn skips this giver entirely.</summary>
        public List<PawnCapacityDef>? requiredCapacities;

        public bool prioritizeSustains;
        public bool scanThings = true;
        public bool scanCells;
        public bool canBeDoneByNonColonists;
        public bool nonColonistsCanDo;

        private WorkGiver? workerInt;

        public WorkGiver Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (WorkGiver)Activator.CreateInstance(giverClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (workType == null) yield return "workGiver has no workType.";
            if (!typeof(WorkGiver).IsAssignableFrom(giverClass)) yield return "giverClass must derive from WorkGiver.";
        }
    }
}
