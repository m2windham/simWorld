using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Hauls a needed resource to the nearest site still short of it (RimWorld:
    /// <c>RimWorld.WorkGiver_ConstructDeliverResources</c>, abstract base of the two concrete scanners below —
    /// RimWorld itself splits blueprints and frames into two WorkGiverDefs rather than one, presumably so a
    /// player can prioritise them separately; this port keeps that split).
    /// </summary>
    public abstract class WorkGiver_ConstructDeliverResources : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.Spawned) return false;
            // RimWorld hands a builder the blocking-thing job instead of a delivery while something stands in
            // the way (GenConstruct.HandleBlockingThingJob), and for another pawn that is no job at all: wait
            // for them to move. A pawn on the blueprint of an impassable building would be walled in by its frame.
            if (thing is Blueprint blueprint && GenConstruct.FirstBlockingPawn(blueprint, pawn) != null) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            if (CostsNothing(thing)) return true;
            return FindNeededResource(pawn, thing) != null;
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            // RimWorld: a blueprint with no material cost is turned into its frame by PlaceNoCostFrame.
            if (CostsNothing(thing)) return new Job(NoCostFrameJobDefOf.PlaceNoCostFrame, thing);
            Thing? resource = FindNeededResource(pawn, thing);
            return resource != null ? new Job(JobDefOf.HaulToBuildingSite, resource, thing) : null;
        }

        private static bool CostsNothing(Thing site) =>
            site is Blueprint blueprint && blueprint.TotalMaterialCost().Count == 0;

        private static Thing? FindNeededResource(Pawn pawn, Thing site)
        {
            Map.Map map = pawn.Map!;
            // The stuff-adjusted cost (RimWorld: IConstructible.TotalMaterialCost), so a stuff-built site asks
            // for its costStuffCount of its chosen stuff as well as its costList.
            List<ThingDefCountClass>? cost = site switch
            {
                Frame frame => frame.TotalMaterialCost(),
                Blueprint blueprint => blueprint.TotalMaterialCost(),
                _ => null,
            };
            if (cost == null) return null;

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            for (int i = 0; i < cost.Count; i++)
            {
                ThingDefCountClass entry = cost[i];
                int stillNeeded = site is Frame f ? f.MaterialStillNeeded(entry.thingDef) : entry.count;
                if (stillNeeded <= 0) continue;

                IReadOnlyList<Thing> candidates = map.listerThings.ThingsOfDef(entry.thingDef);
                for (int c = 0; c < candidates.Count; c++)
                {
                    Thing candidate = candidates[c];
                    if (!candidate.Spawned || candidate.stackCount <= 0) continue;
                    if (!map.reservationManager.CanReserve(pawn, candidate)) continue;
                    if (!Reachability.CanReach(pawn, candidate, PathEndMode.ClosestTouch)) continue;
                    int distSq = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                    if (distSq >= bestDistSq) continue;
                    best = candidate;
                    bestDistSq = distSq;
                }
            }
            return best;
        }
    }

    /// <summary>Delivers resources to Blueprints (RimWorld: <c>RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints</c>).</summary>
    public sealed class WorkGiver_ConstructDeliverResourcesToBlueprints : WorkGiver_ConstructDeliverResources
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)) yield return t;
        }
    }

    /// <summary>Delivers resources to Frames (RimWorld: <c>RimWorld.WorkGiver_ConstructDeliverResourcesToFrames</c>).</summary>
    public sealed class WorkGiver_ConstructDeliverResourcesToFrames : WorkGiver_ConstructDeliverResources
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)) yield return t;
        }
    }
}
