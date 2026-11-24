using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.God
{
    /// <summary>
    /// Who an edict's work can actually fall to. One rule, read in two places so they cannot disagree:
    /// <see cref="AI.JobGiver_Edicts"/> asks it before handing a citizen the edict's work, and the god view asks
    /// it to say when an edict in force has nobody left to carry it.
    /// </summary>
    public static class EdictWorkUtility
    {
        /// <summary>
        /// The citizen can do this work and has not been told never to. An edict outranks the citizen's own
        /// ordering of their work (that is what an edict is for), but not an explicit 0 the player set for
        /// that person (<see cref="Pawn_WorkSettings.IsExplicitlyNever"/>) — the more specific order wins. A
        /// 0 that is only the default state still yields to the edict.
        /// </summary>
        public static bool MayDoEdictWork(Pawn pawn, WorkTypeDef workType) =>
            !pawn.WorkTypeIsDisabled(workType)
            && (pawn.workSettings == null || !pawn.workSettings.IsExplicitlyNever(workType));

        /// <summary>
        /// An edict in force that applies to <paramref name="pawn"/> names <paramref name="workType"/>. The
        /// edict is the order to do that work, so a settlement's own gate on it (the hunt's "only while short of
        /// food", which stands in for RimWorld's player-painted hunt designation) gives way to it.
        /// </summary>
        public static bool Demands(Pawn pawn, WorkTypeDef workType)
        {
            IReadOnlyList<EdictDef> active = Find.God.ActiveEdicts;
            for (int e = 0; e < active.Count; e++)
            {
                if (active[e].prioritizedWork.Contains(workType) && active[e].Worker.AppliesTo(pawn)) return true;
            }
            return false;
        }

        /// <summary>
        /// <paramref name="pawn"/> is an able hand for an exclusive edict in force (<see cref="EdictDef.exclusiveWork"/>):
        /// it applies to them, they may do one of its work types, and at least one of that type's givers would
        /// consider them (<see cref="WorkGiver.ShouldSkip"/>; a hunter without a ranged weapon is skipped, as in
        /// RimWorld). An able hand is offered the edict's work before anything routine; one who finds none of it
        /// to do goes back to their routine rather than standing idle.
        /// </summary>
        public static bool IsAbleHand(Pawn pawn)
        {
            IReadOnlyList<EdictDef> active = Find.God.ActiveEdicts;
            for (int e = 0; e < active.Count; e++)
            {
                EdictDef edict = active[e];
                if (!edict.exclusiveWork || !edict.Worker.AppliesTo(pawn)) continue;
                for (int w = 0; w < edict.prioritizedWork.Count; w++)
                {
                    WorkTypeDef workType = edict.prioritizedWork[w];
                    if (!MayDoEdictWork(pawn, workType)) continue;
                    IReadOnlyList<WorkGiverDef> givers = workType.WorkGivers;
                    for (int g = 0; g < givers.Count; g++)
                    {
                        if (givers[g].Worker is WorkGiver_Scanner scanner && !scanner.ShouldSkip(pawn) && !scanner.MissingRequiredCapacity(pawn)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The work types of <paramref name="edict"/> that no living citizen it applies to may do, in the
        /// edict's own order. Empty when anyone may do each of them, and empty when there are no citizens at
        /// all (before a world exists) — "nobody is allowed" is a claim about people, not about their absence.
        /// </summary>
        public static List<WorkTypeDef> WorkNobodyMayDo(EdictDef edict)
        {
            var blocked = new List<WorkTypeDef>();
            World.World? world = Find.World;
            if (world == null) return blocked;

            var people = new List<Pawn>();
            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement settlement)) continue;
                IReadOnlyList<Pawn> roster = settlement.Citizens;
                for (int c = 0; c < roster.Count; c++)
                {
                    Pawn p = roster[c];
                    if (!p.Dead && p.workSettings != null && p.workSettings.EverWork && edict.Worker.AppliesTo(p)) people.Add(p);
                }
            }
            if (people.Count == 0) return blocked;

            List<WorkTypeDef> work = edict.prioritizedWork;
            for (int w = 0; w < work.Count; w++)
            {
                bool anyone = false;
                for (int i = 0; i < people.Count && !anyone; i++) anyone = MayDoEdictWork(people[i], work[w]);
                if (!anyone) blocked.Add(work[w]);
            }
            return blocked;
        }
    }
}
