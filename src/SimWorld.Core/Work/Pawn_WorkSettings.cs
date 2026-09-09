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

        /// <summary>Priority a <see cref="RoleDef"/> sets for its own <see cref="RoleDef.emphasizedWorkTypes"/>
        /// (<see cref="ApplyRole"/>) — the best (lowest-number) priority, so a role's specialty is always tried
        /// before a citizen's other, merely-default-priority work, without needing <see cref="useWorkPriorities"/>
        /// to be on (role emphasis is meant to show up for every pawn, not only ones the player switched to
        /// detailed priorities).</summary>
        public const int EmphasizedPriority = 1;

        private readonly Pawn pawn;
        private Dictionary<WorkTypeDef, int>? priorities;

        /// <summary>Work types a caller set directly via <see cref="SetPriority"/> — "a person's own preference"
        /// in the sense <c>docs/status.json</c>'s <c>work.policy</c> translation means it: a role's own writes
        /// (<see cref="ApplyRole"/>) always skip these, so assigning or changing a citizen's role can never
        /// silently undo a specific priority someone set for them. Not itself Scribed as a separate concept from
        /// <see cref="priorities"/> in the sense that losing this set would only mean a loaded pawn's next role
        /// (re)application treats their prior explicit edits as role-writable again — see <see cref="ExposeData"/>.</summary>
        private HashSet<WorkTypeDef>? manualPriorities;

        /// <summary>The standing role assigned to this pawn (<c>work.policy</c>'s translation of a per-pawn
        /// priority grid into "what does this citizen do in the civilization" — see <see cref="RoleDef"/>'s own
        /// doc). Null means no standing role: priorities sit wherever <see cref="EnableAndInitialize"/> or a
        /// person's own edits left them.</summary>
        public RoleDef? Role { get; private set; }

        public bool useWorkPriorities;

        private List<WorkGiverDef>? cachedGiversNormal;
        private List<WorkGiverDef>? cachedGiversEmergency;

        public Pawn_WorkSettings(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        /// <summary>False until <see cref="EnableAndInitialize"/> runs (never, for a pawn that cannot work at all).</summary>
        public bool EverWork => priorities != null;

        /// <summary>Zeroes every work type, then sets every one the pawn is not disabled from to <see cref="DefaultPriority"/>.
        /// Re-applies <see cref="Role"/> afterward (skipping anything <see cref="manualPriorities"/> still protects)
        /// so a role already assigned survives a grid rebuild instead of being silently flattened back to default.</summary>
        public void EnableAndInitialize()
        {
            priorities = new Dictionary<WorkTypeDef, int>();
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                priorities[workType] = pawn.WorkTypeIsDisabled(workType) ? 0 : DefaultPriority;
            }
            if (Role != null) ApplyRole();
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

        /// <summary>1..4 = on at that priority, 0 = off. Silently stays 0 for a work type the pawn is disabled from.
        /// Marks <paramref name="workType"/> as manually set (<see cref="manualPriorities"/>) — a caller reaching
        /// for this method is by definition setting a specific number for this specific pawn, the "person's own
        /// preference" a standing <see cref="Role"/> is never allowed to overwrite (see <see cref="ApplyRole"/>).</summary>
        public void SetPriority(WorkTypeDef workType, int priority)
        {
            if (priority < 0 || priority > LowestPriority) throw new ArgumentOutOfRangeException(nameof(priority));
            priorities ??= new Dictionary<WorkTypeDef, int>();
            priorities[workType] = pawn.WorkTypeIsDisabled(workType) ? 0 : priority;
            (manualPriorities ??= new HashSet<WorkTypeDef>()).Add(workType);
            DirtyCaches();
        }

        /// <summary>
        /// Assigns (or clears, with null) this pawn's standing <see cref="RoleDef"/> and immediately re-derives
        /// every non-manually-set, non-disabled work type's priority from it (<see cref="ApplyRole"/>) — a role
        /// is fully re-derivable from its own def at any time, so nothing here is "additive on top of a hidden
        /// base": clearing a role reverts everything it touched back to <see cref="DefaultPriority"/>, exactly
        /// as if the pawn had never held one, which is the standing-policy version of an edict's own "leaves no
        /// trace" guarantee (<c>SimWorld.AI.JobGiver_Edicts</c>'s own doc). A no-op on a pawn that never got a
        /// grid at all (<see cref="EverWork"/> false) beyond recording the role for whenever it does — see
        /// <see cref="EnableAndInitialize"/>, which re-applies whatever role is already set.
        /// <para/>
        /// <b>Assigning a real role turns <see cref="useWorkPriorities"/> on.</b> <see cref="GetPriority"/>
        /// collapses every active work type to the same <see cref="DefaultPriority"/> while that flag is off
        /// (RimWorld's own "no detailed priorities = no numeric distinction, only on/off" semantics — see
        /// <see cref="GetPriority"/> and the test pinning it), which would silently erase a role's whole point
        /// for the common case of a citizen nobody ever opted into detailed priorities. A role is, in effect,
        /// the civilization making that detailed-priority choice on the citizen's behalf, so setting one flips
        /// the switch that lets it mean anything. Clearing a role (null) does not flip it back off — a player
        /// or another system may have turned it on for an unrelated reason since, and forcing it off here could
        /// silently discard that; <see cref="useWorkPriorities"/> is a pawn-wide flag, not a per-work-type entry,
        /// so it sits outside what <see cref="manualPriorities"/> was built to protect.
        /// </summary>
        public void SetRole(RoleDef? role)
        {
            Role = role;
            if (role != null) useWorkPriorities = true;
            if (priorities == null) return; // recorded for EnableAndInitialize to apply later; nothing to write yet
            ApplyRole();
            DirtyCaches();
        }

        /// <summary>
        /// Re-derives every work type in <see cref="priorities"/> that is neither disabled nor in
        /// <see cref="manualPriorities"/>: <see cref="EmphasizedPriority"/> for one <see cref="Role"/> names in
        /// <see cref="RoleDef.emphasizedWorkTypes"/>, <see cref="DefaultPriority"/> otherwise (or when
        /// <see cref="Role"/> is null). Never flips a disabled work type on — it is left at 0, matching
        /// <see cref="SetPriority"/>'s own rule — and never touches a manually-set one at all, which is the one
        /// property this whole mechanism exists to protect (see the class and <see cref="RoleDef"/> docs).
        /// </summary>
        private void ApplyRole()
        {
            if (priorities == null) return;
            foreach (WorkTypeDef workType in priorities.Keys.ToList())
            {
                if (manualPriorities != null && manualPriorities.Contains(workType)) continue;
                if (pawn.WorkTypeIsDisabled(workType))
                {
                    priorities[workType] = 0;
                    continue;
                }
                priorities[workType] = Role != null && Role.emphasizedWorkTypes.Contains(workType)
                    ? EmphasizedPriority
                    : DefaultPriority;
            }
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

            RoleDef? role = Role;
            Scribe_Defs.Look(ref role, "role");
            Role = role;

            List<WorkTypeDef>? manualList = manualPriorities == null ? null : new List<WorkTypeDef>(manualPriorities);
            Scribe_Collections.Look(ref manualList, "manualPriorities", LookMode.Def);
            manualPriorities = manualList == null ? null : new HashSet<WorkTypeDef>(manualList);

            if (Scribe.mode == LoadSaveMode.PostLoadInit) DirtyCaches();
        }
    }
}
