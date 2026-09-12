using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Mines reachable, unreserved mineable edifices that are not holding a roof up (RimWorld:
    /// <c>RimWorld.WorkGiver_Miner</c>). The one concrete <see cref="WorkGiver_Scanner"/> this pass wires to
    /// real content (<c>Mine</c>'s <c>giverClass</c> in <c>WorkGivers.xml</c>) — every other
    /// <c>WorkGiverDef</c> keeps its <c>WorkGiver_Pending</c> default, which is fine:
    /// <see cref="JobGiver_Work"/> just finds no job from those and moves on.
    ///
    /// <para/><b>Two translations live here, not one, and the second was missing.</b> RimWorld's giver scans
    /// the player's <c>Mine</c> designations; this port has no designation layer, so it scans every mineable
    /// edifice instead — that substitution was recorded. What went with the player, and was not recorded, is
    /// the <i>judgement</i> they were making: a player picking a cell to dig is looking at the overhead-
    /// mountain overlay while they pick. Without it this giver mined out the supports of its own settlement's
    /// roof, which is the largest killer in the game's history
    /// (<c>docs/WORK-REGISTER.md</c> §10). The gate below restores that judgement and nothing else — see
    /// <see cref="Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved"/>, where the translation is
    /// written down in full.
    /// </summary>
    public sealed class WorkGiver_Miner : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].def.mineable) yield return buildings[i];
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.def.mineable || thing.Destroyed || !thing.Spawned) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            // Before the reachability check, not after, and the order is load-bearing in its own right.
            // WorkGiverScanUtility only lowers its "nearest so far" bound when a candidate is *accepted*, so
            // every rejection leaves the bound high and lets more candidates through to be tested. Adding a
            // rejecting predicate therefore multiplies whatever runs before it — and Reachability.CanReach is
            // a region-graph search, while this is a bounded walk over the roof grid that gives up on the
            // first cell it finds unsupported. Measured on the one in-game day a settlement does most of its
            // mining, with this check behind CanReach: five times the tick cost. In front of it: near parity.
            if (Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved(thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Mine, thing);
    }
}
