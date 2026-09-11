using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a reachable, unclaimed <see cref="ResearchWorkDefOf.ResearchBench"/> and spends work ticks
    /// advancing <see cref="Research.ResearchManager.CurrentProj"/> there (RimWorld: <c>RimWorld.WorkGiver_ResearchStudy</c>
    /// against RimWorld's own simple/hi-tech bench pair, collapsed to this port's single bench tier —
    /// <c>Research</c>'s own <c>giverClass</c> in <c>WorkGivers.xml</c>).
    /// <para/>
    /// <b>Judgment call — a bench is required, matching RimWorld.</b> RimWorld itself never lets a colonist
    /// research without one; this port keeps that rule rather than inventing a bench-less fallback, so a
    /// civilization that has not yet built <see cref="ResearchWorkDefOf.ResearchBench"/> produces no research
    /// job at all — <see cref="PotentialWorkThingsGlobal"/> simply finds nothing to scan, the same "no bench,
    /// no job" outcome <see cref="HasJobOnThing"/> would also reach if a bench existed but were unreachable or
    /// already claimed. See this giver's own report for why: a research bench, unlike a bed or a stockpile,
    /// only unlocks *itself* (no researchPrerequisites — see <c>Buildings_Research.xml</c>), so this is never
    /// a chicken-and-egg lock a civilization cannot get out of, only ever a "build one first" prompt.
    /// </summary>
    public sealed class WorkGiver_Research : WorkGiver_Scanner
    {
        /// <summary>RimWorld's own per-ThingDef interaction cell; this port treats it identically to
        /// <see cref="PathEndMode.Touch"/> (see that enum member's own remarks) — a pawn stands adjacent to
        /// the bench, never on top of it, matching the bench's own <c>Impassable</c> passability.</summary>
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        /// <summary>
        /// No current project at all: skip the scan outright rather than walking every bench on the map only
        /// to find nothing to do at any of them (RimWorld: <c>Find.ResearchManager.GetProject() == null</c> —
        /// the "what happens when CurrentProj is null" case this giver's brief calls for).
        /// </summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => Find.ResearchManager.CurrentProj == null;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].def == ResearchWorkDefOf.ResearchBench) yield return buildings[i];
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.def != ResearchWorkDefOf.ResearchBench || thing.Destroyed || !thing.Spawned) return false;
            if (Find.ResearchManager.CurrentProj == null) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(ResearchWorkDefOf.Research, thing);
    }
}
