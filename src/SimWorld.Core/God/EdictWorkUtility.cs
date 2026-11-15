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
