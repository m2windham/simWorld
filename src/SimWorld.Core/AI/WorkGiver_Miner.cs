using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Mines the cells somebody has marked for digging (RimWorld: <c>RimWorld.WorkGiver_Miner</c>): a
    /// reachable, unreserved <see cref="Mineable"/> standing in a cell that carries a
    /// <see cref="DesignationDefOf.Mine"/> designation, and that is not holding a roof up. The one concrete
    /// <see cref="WorkGiver_Scanner"/> wired to the <c>Mine</c> work type (<c>giverClass</c> in
    /// <c>WorkGivers.xml</c>).
    ///
    /// <para/><b>Whose marks.</b> Anyone's: the player's, through <c>MapCommands.DesignateMine</c>, or the
    /// settlement's own, through <see cref="MiningInitiative"/> — this giver neither knows nor cares which,
    /// exactly as RimWorld's does not. What it refuses to do is invent work. It used to scan every mineable
    /// edifice on the map, and measured on a seed-777 settlement that took the limestone from 12,985 cells to
    /// 242 in six days, two hundred rocks an hour from the thirty-second, and left no mountain standing
    /// anywhere on the map. Now it scans the designations: a handful of cells, not twelve thousand.
    ///
    /// <para/><b>One translation is left, and it is not the designation.</b> A player picking a cell to dig
    /// is looking at the overhead-mountain overlay while they pick. That judgement went missing with the
    /// player once and mined out the supports of the settlement's own roof, which is the largest killer in the
    /// game's history (<c>docs/WORK-REGISTER.md</c> §10). <see cref="HasJobOnThing"/> restores it and nothing
    /// else — see <see cref="Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved"/>, where the translation
    /// is written down in full. It survives designations because a mark does not make a cell safe: a player
    /// can mark a roof's support, and an initiative's mark goes stale when a neighbour is taken.
    /// </summary>
    public sealed class WorkGiver_Miner : WorkGiver_Scanner
    {
        /// <summary>The expiry a mining job carries (RimWorld: <c>WorkGiver_Miner.MiningJobTicks</c>), a
        /// backstop for a dig that somehow never finishes rather than a measure of how long one takes.</summary>
        public const int MiningJobTicks = 20000;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// Every marked cell that holds rock and has at least one open neighbour (RimWorld:
        /// <c>PotentialWorkThingsGlobal</c>, including its "may be accessible" pre-filter). A mark inside a
        /// mountain with solid rock all round it cannot be reached yet and is not offered; the moment the
        /// rock next to it is mined out it is.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;

            foreach (Designation des in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine))
            {
                IntVec3 cell = des.target.Cell;
                bool mayBeAccessible = false;
                for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
                {
                    IntVec3 c = cell + GenAdj.AdjacentCells[i];
                    if (GenGrid.InBounds(c, map) && GenGrid.Walkable(c, map))
                    {
                        mayBeAccessible = true;
                        break;
                    }
                }
                if (!mayBeAccessible) continue;

                Mineable? mineable = MineableUtility.GetFirstMineable(cell, map);
                if (mineable != null) yield return mineable;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.def.mineable || thing.Destroyed || !thing.Spawned) return false;
            if (pawn.Map!.designationManager.DesignationAt(thing.Position, DesignationDefOf.Mine) == null) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, thing)) return false;
            // Before the reachability check, not after, and the order is load-bearing in its own right.
            // WorkGiverScanUtility only lowers its "nearest so far" bound when a candidate is *accepted*, so
            // every rejection leaves the bound high and lets more candidates through to be tested. Adding a
            // rejecting predicate therefore multiplies whatever runs before it — and Reachability.CanReach is
            // a region-graph search, while this is a bounded walk over the roof grid that gives up on the
            // first cell it finds unsupported. Measured on the one in-game day a settlement does most of its
            // mining, with this check behind CanReach: five times the tick cost. In front of it: near parity.
            // (That was measured when the scan was every rock on the map. With the scan now the marked cells
            // the multiplier is a handful rather than twelve thousand, and the order is kept regardless: it
            // costs nothing and the reasoning has not changed.)
            if (Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved(thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(JobDefOf.Mine, thing) { expiryInterval = MiningJobTicks };
    }
}
