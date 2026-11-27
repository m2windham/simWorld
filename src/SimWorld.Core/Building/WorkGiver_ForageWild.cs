using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Gathers wild food: a harvestable plant standing outside any growing zone (a berry bush, wild rice). In
    /// RimWorld the wild is brought in by a player's harvest designation, worked as PlantCutting
    /// (<c>WorkGiver_PlantsCut</c> on <c>HarvestPlant</c>); there is no player here to draw it, so the band
    /// forages on its own, as <see cref="WorkGiver_GrowerHarvest"/> used to do for every plant on the map.
    ///
    /// <para/><b>Growing work, ranked below sowing.</b> It stays in the Growing work type so the same hands gather
    /// food as before, with the same ripe-unless-hungry rule (<see cref="WorkGiver_GrowerHarvest.EarlyHarvestWanted"/>);
    /// its place in the list (<c>WorkGivers_Forage.xml</c>) is after the field's harvest and sowing, so ripe berries
    /// no longer keep a field bare. A wild tree is never foraged: it is wood, felled by
    /// <see cref="WorkGiver_ConstructChopWood"/> when construction is short of it.
    /// </summary>
    public sealed class WorkGiver_ForageWild : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            // Only what MayBeForageWork keeps, for the reason WorkGiver_GrowerHarvest gives: the tests HasJobOnThing
            // starts with, so the scan takes the same plant without sorting the map's whole undergrowth first; and
            // only over the plants that yield anything (HarvestablePlant, Plant's order), the rest never being
            // HarvestableNow.
            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant);
            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i] is Plant plant && MayBeForageWork(plant)) yield return plant;
            }
        }

        /// <summary>A spawned, harvestable-now plant that is no tree and stands in no field, fully grown or one
        /// <see cref="WorkGiver_GrowerHarvest.EarlyHarvestWanted"/> could still want (short of its once-a-tick food
        /// figure, which the scan must read only when it reaches such a plant).</summary>
        private static bool MayBeForageWork(Plant plant)
        {
            if (!plant.Spawned || !plant.HarvestableNow) return false;
            if (plant.def.plant!.IsTree || plant.Map!.zoneManager.ZoneAt(plant.Position) is Zone_Growing) return false;
            return plant.FullyGrown || WorkGiver_GrowerHarvest.MayBeWantedEarly(plant);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Plant plant) || !MayBeForageWork(plant)) return false;
            if (!plant.FullyGrown && !WorkGiver_GrowerHarvest.EarlyHarvestWanted(plant)) return false;
            if (!Reachability.CanReach(pawn, plant, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, plant);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(ForageJobDefOf.Forage, thing);
    }
}
