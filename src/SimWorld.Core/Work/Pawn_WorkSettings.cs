using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Work
{
    /// <summary>
    /// A colonist's work priority grid (RimWorld: <c>RimWorld.Pawn_WorkSettings</c>). Non-Humanlike pawns never
    /// get one initialized (<see cref="EverWork"/> stays false). <b>Deviation:</b> RimWorld keeps "use detailed
    /// priorities" as a single global on <c>Prefs</c>; here it is <see cref="useWorkPriorities"/>, a field per
    /// tracker, so independent pawns and parallel tests never share it through a process-global.
    /// </summary>
    public class Pawn_WorkSettings : IExposable
    {
        /// <summary>Priority 4: still active, but last in line.</summary>
        public const int LowestPriority = 4;

        /// <summary>What every non-disabled work type gets on <see cref="EnableAndInitialize"/> and what "on" collapses to when <see cref="useWorkPriorities"/> is off.</summary>
        public const int DefaultPriority = 3;

        private readonly Pawn pawn;
        private Dictionary<WorkTypeDef, int>? priorities;

        public bool useWorkPriorities;

        private List<WorkGiverDef>? cachedGiversNormal;
        private List<WorkGiverDef>? cachedGiversEmergency;

        public Pawn_WorkSettings(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        /// <summary>False until <see cref="EnableAndInitialize"/> runs (never, for a pawn that cannot work at all).</summary>
        public bool EverWork => priorities != null;

        /// <summary>Zeroes every work type, then sets every one the pawn is not disabled from to <see cref="DefaultPriority"/>.</summary>
        public void EnableAndInitialize()
        {
            priorities = new Dictionary<WorkTypeDef, int>();
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                priorities[workType] = pawn.WorkTypeIsDisabled(workType) ? 0 : DefaultPriority;
            }
            DirtyCaches();
        }

        public void DisableAll()
        {
            if (priorities == null) return;
            foreach (WorkTypeDef workType in priorities.Keys.ToList()) priorities[workType] = 0;
            DirtyCaches();
        }

        public void Disable(WorkTypeDef workType)
        {
            priorities ??= new Dictionary<WorkTypeDef, int>();
            priorities[workType] = 0;
            DirtyCaches();
        }

        /// <summary>0 if the pawn never got a grid or this work type isn't in it; else the stored priority, collapsed
        /// to 3-or-0 when <see cref="useWorkPriorities"/> is off.</summary>
        public int GetPriority(WorkTypeDef workType)
        {
            if (priorities == null || !priorities.TryGetValue(workType, out int p)) return 0;
            if (!useWorkPriorities) return p > 0 ? DefaultPriority : 0;
            return p;
        }

        /// <summary>Like <see cref="GetPriority"/> but a work type missing from the grid (e.g. added after this
        /// pawn was initialized) falls back to <see cref="DefaultPriority"/> instead of 0, unless disabled.</summary>
        public int GetPriorityOrDefault(WorkTypeDef workType)
        {
            if (priorities != null && priorities.TryGetValue(workType, out int p)) return useWorkPriorities ? p : (p > 0 ? DefaultPriority : 0);
            if (!EverWork) return 0;
            return pawn.WorkTypeIsDisabled(workType) ? 0 : DefaultPriority;
        }

        /// <summary>1..4 = on at that priority, 0 = off. Silently stays 0 for a work type the pawn is disabled from.</summary>
        public void SetPriority(WorkTypeDef workType, int priority)
        {
            if (priority < 0 || priority > LowestPriority) throw new ArgumentOutOfRangeException(nameof(priority));
            priorities ??= new Dictionary<WorkTypeDef, int>();
            priorities[workType] = pawn.WorkTypeIsDisabled(workType) ? 0 : priority;
            DirtyCaches();
        }

        public bool WorkIsActive(WorkTypeDef workType) => GetPriority(workType) > 0;

        /// <summary>Re-zeroes any work type the pawn has just become disabled from (a trait was gained, say).</summary>
        public void Notify_DisabledWorkTypesChanged()
        {
            if (priorities == null) return;
            foreach (WorkTypeDef workType in priorities.Keys.ToList())
            {
                if (pawn.WorkTypeIsDisabled(workType)) priorities[workType] = 0;
            }
            DirtyCaches();
        }

        /// <summary>
        /// Active work givers (workType priority &gt; 0), ordered priority ascending (only distinguishing when
        /// <see cref="useWorkPriorities"/> is on — otherwise every active work type reports 3) then
        /// <see cref="WorkTypeDef.naturalPriority"/> descending then <see cref="WorkGiverDef.priorityInType"/>
        /// descending. Split by <see cref="WorkGiverDef.emergency"/>.
        /// </summary>
        public IReadOnlyList<WorkGiverDef> WorkGiversInOrderNormal
        {
            get
            {
                RecomputeGiverCachesIfNeeded();
                return cachedGiversNormal!;
            }
        }

        public IReadOnlyList<WorkGiverDef> WorkGiversInOrderEmergency
        {
            get
            {
                RecomputeGiverCachesIfNeeded();
                return cachedGiversEmergency!;
            }
        }

        private void RecomputeGiverCachesIfNeeded()
        {
            if (cachedGiversNormal != null && cachedGiversEmergency != null) return;

            List<WorkGiverDef> ordered = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(g => GetPriority(g.workType) > 0)
                .OrderBy(g => GetPriority(g.workType))
                .ThenByDescending(g => g.workType.naturalPriority)
                .ThenByDescending(g => g.priorityInType)
                .ToList();

            var normal = new List<WorkGiverDef>();
            var emergency = new List<WorkGiverDef>();
            foreach (WorkGiverDef giver in ordered)
            {
                (giver.emergency ? emergency : normal).Add(giver);
            }
            cachedGiversNormal = normal;
            cachedGiversEmergency = emergency;
        }

        private void DirtyCaches()
        {
            cachedGiversNormal = null;
            cachedGiversEmergency = null;
        }

        public void ExposeData()
        {
            Dictionary<WorkTypeDef, int>? dict = priorities;
            Scribe_Collections.Look(ref dict, "priorities", LookMode.Def, LookMode.Value);
            priorities = dict;
            Scribe_Values.Look(ref useWorkPriorities, "useWorkPriorities");

            if (Scribe.mode == LoadSaveMode.PostLoadInit) DirtyCaches();
        }
    }
}
