using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Small shared queries for roof work (RimWorld: <c>Verse.RoofUtility</c>).
    /// </summary>
    public static class RoofUtility
    {
        /// <summary>
        /// The thing at <paramref name="c"/> that stands in the way of a roof — an edifice
        /// <see cref="RoofCollapseUtility.HoldsRoof"/> would count as support (a wall, a door, unmined rock),
        /// or any other spawned Thing whose <see cref="ThingDef.passability"/> is
        /// <see cref="Traversability.Impassable"/> (RimWorld: <c>c.GetRoofHolderOrImpassable(map)</c>, there
        /// <c>thingList[i].def.holdsRoof || thingList[i].def.passability == Traversability.Impassable</c>).
        /// <b>Translation:</b> this port has no separate <c>ThingDef.holdsRoof</c> flag — see
        /// <see cref="RoofCollapseUtility"/>'s own doc on why <see cref="Defs.FillCategory.Full"/>, read
        /// straight off <see cref="Map.EdificeGrid"/>, is the substitute everywhere else in this module reads
        /// "holds a roof up", so it is here too, rather than scanning <c>thingGrid</c> for the edifice half of
        /// this question the way RimWorld's own version (which has no edifice grid to prefer) does.
        /// <see cref="AutoBuildRoofAreaSetter"/> is the one caller today.
        /// </summary>
        public static Thing? GetRoofHolderOrImpassable(IntVec3 c, Map.Map map)
        {
            Thing? edifice = map.edificeGrid[c];
            if (edifice != null && edifice.def.Fillage == FillCategory.Full) return edifice;

            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.passability == Traversability.Impassable) return things[i];
            }
            return null;
        }

        /// <summary>
        /// The live plant at <paramref name="pos"/> that a roof cannot be built through, or null (RimWorld:
        /// <c>Verse.RoofUtility.FirstBlockingThing</c>). No shipped <see cref="PlantProperties"/> sets
        /// <see cref="PlantProperties.interferesWithRoof"/> yet — see that field's own doc — so this returns
        /// null for every plant in content today; the check still runs, because a future species (a real
        /// RimWorld tree canopy, say) should not need this class touched again to be respected.
        /// </summary>
        public static Thing? FirstBlockingThing(IntVec3 pos, Map.Map map)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(pos);
            for (int i = 0; i < things.Count; i++)
            {
                PlantProperties? plant = things[i].def.plant;
                if (plant != null && plant.interferesWithRoof) return things[i];
            }
            return null;
        }

        /// <summary>True if <paramref name="worker"/> could be sent to clear <paramref name="blocker"/> right
        /// now — a reachable, reservable plant (RimWorld: <c>Verse.RoofUtility.CanHandleBlockingThing</c>;
        /// nothing else in this port's content can ever reach <see cref="FirstBlockingThing"/> at all, so the
        /// non-plant branch RimWorld's own version falls through without acting on is not reached here
        /// either).</summary>
        public static bool CanHandleBlockingThing(Thing? blocker, Pawn worker, bool forced = false)
        {
            if (blocker == null) return true;
            if (blocker.def.category != ThingCategory.Plant) return false;
            if (!Reachability.CanReach(worker, blocker, PathEndMode.ClosestTouch)) return false;
            return worker.Map != null && worker.Map.reservationManager.CanReserve(worker, blocker);
        }

        /// <summary>The job that clears <paramref name="blocker"/> out of a roof's way, or null (RimWorld:
        /// <c>Verse.RoofUtility.HandleBlockingThingJob</c>): cutting it, the one plant-cutting job this
        /// module ever hands out itself, reusing <see cref="PlantCuttingJobDefOf.CutPlant"/> exactly as
        /// <see cref="WorkGiver_ConstructChopWood"/>/<see cref="WorkGiver_PlantsCut"/> already do rather than
        /// declaring a roof-specific duplicate.</summary>
        public static Job? HandleBlockingThingJob(Thing? blocker, Pawn worker, bool forced = false)
        {
            if (blocker == null || !CanHandleBlockingThing(blocker, worker, forced)) return null;
            return new Job(PlantCuttingJobDefOf.CutPlant, blocker);
        }
    }
}
