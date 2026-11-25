using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Designations;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Deconstructs what the settlement itself marked to come down: a house its household has moved out of to the
    /// one that replaced it (<see cref="DwellingPlanner.Demolish"/>). RimWorld's <c>WorkGiver_Deconstruct</c> and
    /// job, unchanged; the only difference is whose marks it works. The player's marks are
    /// <see cref="WorkGiver_Deconstruct"/>'s and come first (its <c>priorityInType</c> is higher), as a player's
    /// marks always do here (<see cref="DesignationSource"/>). A settlement whose deconstruct initiative the player
    /// has switched off (<see cref="DesignationManager.SettlementInitiativeOn"/>) leaves its own marks standing.
    /// </summary>
    public sealed class WorkGiver_DeconstructRenewal : WorkGiver_Scanner
    {
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null || !map.designationManager.SettlementInitiativeOn(PlayerDesignationDefOf.Deconstruct)) return true;
            IReadOnlyList<Designation> list = map.designationManager.SpawnedDesignationsOfDef(PlayerDesignationDefOf.Deconstruct);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].source == DesignationSource.Settlement) return false;
            }
            return true;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Designation> list = map.designationManager.SpawnedDesignationsOfDef(PlayerDesignationDefOf.Deconstruct);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].source != DesignationSource.Settlement) continue;
                if (list[i].target.Thing is Thing t && t.Spawned && t.Map == map) yield return t;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.Destroyed || !thing.Spawned || pawn.Map == null) return false;
            Designation? des = pawn.Map.designationManager.DesignationOn(thing, PlayerDesignationDefOf.Deconstruct);
            if (des == null || des.source != DesignationSource.Settlement) return false;
            // Never the piece holding a roof up: what the settlement takes down on its own must not drop a
            // roof on anyone (DwellingPlanner.Demolish withdraws the mark; this covers the pass in between).
            if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(thing)) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(DesignationJobDefOf.Deconstruct, thing);
    }
}
