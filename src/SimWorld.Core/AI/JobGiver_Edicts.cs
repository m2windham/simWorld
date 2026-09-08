using System.Collections.Generic;
using SimWorld.God;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// The think-tree half of the god layer's edicts (<c>docs/spec/simworld-spec.md</c> §7.4, §10): scans only
    /// the <see cref="WorkGiverDef"/>s belonging to the active edicts' <see cref="EdictDef.prioritizedWork"/>
    /// types, ahead of <see cref="JobGiver_Work"/>'s own routine scan but behind
    /// <see cref="JobGiver_DirectedOrder"/> — a per-pawn order the god queued explicitly still outranks a
    /// standing civ-scale push, and both sit below every need/mental-state guard earlier in
    /// <c>ThinkTrees_Humanlike.xml</c>. That ordering is the "citizens keep full agency" clause from §10: an
    /// edict can only ever win a job a pawn would otherwise have picked up on its own routine work; it can
    /// never pre-empt eating, resting, or a direct order.
    /// <para/>
    /// <b>Deliberately never touches <see cref="Pawn_WorkSettings"/></b>: an edict is a temporary, civilization-
    /// scale nudge to what the think tree tries first, not a permanent edit to what a citizen prefers, so it
    /// reads <see cref="WorkTypeDef.WorkGivers"/> directly rather than going through a pawn's own priority
    /// grid. Deactivating an edict therefore needs no cleanup on this side at all — the very next tick simply
    /// stops finding this giver in <see cref="Find.God"/>'s active list, and every priority a pawn had before
    /// the edict is exactly what it still has after.
    /// </summary>
    public sealed class JobGiver_Edicts : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike || !pawn.workSettings.EverWork || pawn.Map == null) return null;

            IReadOnlyList<EdictDef> active = Find.God.ActiveEdicts;
            if (active.Count == 0) return null;

            List<WorkGiverDef>? givers = null;
            for (int e = 0; e < active.Count; e++)
            {
                EdictDef edict = active[e];
                if (!edict.Worker.AppliesTo(pawn)) continue;

                List<WorkTypeDef> workTypes = edict.prioritizedWork;
                for (int w = 0; w < workTypes.Count; w++)
                {
                    WorkTypeDef workType = workTypes[w];
                    if (pawn.WorkTypeIsDisabled(workType)) continue;

                    IReadOnlyList<WorkGiverDef> givenByType = workType.WorkGivers;
                    for (int g = 0; g < givenByType.Count; g++)
                    {
                        WorkGiverDef giver = givenByType[g];
                        givers ??= new List<WorkGiverDef>();
                        if (!givers.Contains(giver)) givers.Add(giver);
                    }
                }
            }

            return givers == null ? null : WorkGiverScanUtility.TryGiveJobInGivers(pawn, givers);
        }
    }
}
