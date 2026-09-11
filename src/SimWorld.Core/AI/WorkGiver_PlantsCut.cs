using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Cuts a standing plant that is in something's way (RimWorld: <c>RimWorld.WorkGiver_PlantsCut</c>).
    /// <c>PlantsCut</c>'s <c>giverClass</c> in <c>WorkGivers.xml</c>; before this it fell back to
    /// <see cref="WorkGiver_Pending"/>, so the whole PlantCutting work type sat in every pawn's priority
    /// list and could never produce a job.
    /// <para/>
    /// <b>Translation — designations become derived reasons.</b> RimWorld's giver scans
    /// <c>Designation.CutPlant</c>/<c>HarvestPlant</c>: a player drew a box over some plants. There is no
    /// Designation system in this codebase at all (nothing anywhere declares one), and spec §10's whole
    /// premise is that nobody is drawing boxes — so the port keeps the giver and replaces its input with the
    /// two reasons the settlement can work out for itself, in <see cref="ShouldBeCut"/>. Both are cases
    /// RimWorld really does cut plants for; it just reaches them from somewhere other than this class, and in
    /// each case the class it reaches them from does not do that here:
    /// <list type="bullet">
    /// <item><description><b>Wrong crop in a growing zone.</b> RimWorld's own <c>WorkGiver_GrowerSow</c>
    /// hands back a <c>CutPlant</c> job when the cell it wants to sow holds a plant that is not the zone's
    /// chosen crop. This port's <see cref="WorkGiver_GrowerSow"/> instead just refuses the cell
    /// (<c>thingGrid.CellContains(cell, ThingCategory.Plant)</c> and it moves on), so a single wrong plant
    /// blocks that cell forever and nothing clears it. It is genuinely unserved, not duplicated — which is
    /// why it belongs here rather than being left to the grower.</description></item>
    /// <item><description><b>Plant standing where something is being built.</b> RimWorld clears a plant
    /// blocking a blueprint through its construction path (the blocking-thing handling the deliver-resources
    /// giver runs before hauling). This port's <see cref="GenConstruct.CanPlaceBlueprintAt"/> does not even
    /// look at plants, and <see cref="Frame.CompleteConstruction"/> spawns the finished building straight on
    /// top of whatever plant is still standing there — so without this, a settlement builds walls through
    /// bushes. See this module's report: that <see cref="GenConstruct"/> gap is worth closing on its own too,
    /// but it is not this lane's file to change.</description></item>
    /// </list>
    /// <b>Not a general "cut everything" sweep.</b> Wild growth away from a zone and away from a building
    /// site is left alone deliberately: with no designation there is nobody asking for it, and a giver that
    /// targeted every plant on the map would have every idle pawn strip the biome bare. A mature crop
    /// standing anywhere is already <see cref="WorkGiver_GrowerHarvest"/>'s work, and Growing outranks
    /// PlantCutting by <c>naturalPriority</c>, so for a fully grown plant the harvester gets there first and
    /// this giver never has to compete with it.
    /// </summary>
    public sealed class WorkGiver_PlantsCut : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)) yield return t;
        }

        /// <summary>
        /// This port's replacement for "a cut designation sits on this plant" — see the class doc for where
        /// each of the two reasons comes from in RimWorld.
        /// </summary>
        public static bool ShouldBeCut(Plant plant)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned) return false;
            Map.Map map = plant.Map!;
            return BlocksSowing(plant, map) || BlocksConstruction(plant, map);
        }

        /// <summary>The plant stands in an actively-sowing <see cref="Zone_Growing"/> and is not that zone's
        /// crop, so the cell can never be sown while it is there.</summary>
        private static bool BlocksSowing(Plant plant, Map.Map map)
        {
            if (!(map.zoneManager.ZoneAt(plant.Position) is Zone_Growing growing)) return false;
            if (!growing.allowSow) return false;
            ThingDef? wanted = growing.plantDefToGrow;
            return wanted != null && !ReferenceEquals(wanted, plant.def);
        }

        /// <summary>The plant shares its cell with a <see cref="Blueprint"/> or a <see cref="Frame"/>.</summary>
        private static bool BlocksConstruction(Plant plant, Map.Map map) =>
            map.thingGrid.CellContains(plant.Position, ThingCategory.Blueprint) ||
            map.thingGrid.CellContains(plant.Position, ThingCategory.Frame);

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Plant plant) || !ShouldBeCut(plant)) return false;
            if (!Reachability.CanReach(pawn, plant, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, plant);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(PlantCuttingJobDefOf.CutPlant, thing);
    }
}
