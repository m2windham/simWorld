using System.Collections.Generic;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Mines the rock on <c>Mine</c>-designated cells (RimWorld: <c>RimWorld.WorkGiver_Miner</c>): it scans the
    /// map's Mine designations, offers the mineable on each one that has a walkable neighbour, and takes a
    /// designated, reservable, reachable rock that is not holding a roof up.
    ///
    /// <para/>RimWorld's player places the designations; here <see cref="MiningInitiative"/> places them for
    /// the settlement, by need. Before that layer existed this giver scanned every mineable edifice on the map,
    /// so mining never ran out and a band with nothing above Mining's priority to do mined the mountain, all of
    /// it at once. The roof gate stays as well, because a designation is placed passes before it is dug and a
    /// neighbour mined in between can leave it holding a ceiling up. See
    /// <see cref="Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved"/>.
    /// </summary>
    public sealed class WorkGiver_Miner : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Mine);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Designation> designations = map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine);
            for (int i = 0; i < designations.Count; i++)
            {
                IntVec3 cell = designations[i].target.Cell;
                if (!MayBeAccessible(cell, map)) continue;
                Thing? mineable = FirstMineable(cell, map);
                if (mineable != null) yield return mineable;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.def.mineable || thing.Destroyed || !thing.Spawned) return false;
            if (pawn.Map!.designationManager.DesignationAt(thing.Position, DesignationDefOf.Mine) == null) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, thing)) return false;
            // Before the reachability check, not after, and the order is load-bearing in its own right: a
            // rejecting predicate multiplies whatever runs before it in the scan, and Reachability.CanReach is a
            // region-graph search while this is a bounded walk over the roof grid that gives up early.
            if (Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved(thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Mine, thing);

        /// <summary>Any of the eight cells around <paramref name="c"/> walkable (RimWorld's <c>mayBeAccessible</c>):
        /// a designated rock deep in the face waits until the rock in front of it is gone.</summary>
        public static bool MayBeAccessible(IntVec3 c, Map.Map map)
        {
            for (int j = 0; j < GenAdj.AdjacentCells.Length; j++)
            {
                IntVec3 nb = c + GenAdj.AdjacentCells[j];
                if (GenGrid.InBounds(nb, map) && map.pathGrid.Walkable(nb)) return true;
            }
            return false;
        }

        /// <summary>The mineable edifice on <paramref name="c"/>, if any (RimWorld: <c>GridsUtility.GetFirstMineable</c>).</summary>
        public static Thing? FirstMineable(IntVec3 c, Map.Map map)
        {
            Thing? edifice = map.edificeGrid[c];
            return edifice != null && edifice.def.mineable ? edifice : null;
        }
    }
}
