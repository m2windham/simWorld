using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a wild, reachable animal to attempt taming (RimWorld: <c>RimWorld.WorkGiver_InteractAnimal</c>
    /// as content wires it for the "Tame" interaction) — wires the <c>TameAnimals</c> WorkGiverDef the same
    /// way <see cref="WorkGiver_Miner"/> wires <c>Mine</c>.
    /// </summary>
    public sealed class WorkGiver_TameAnimals : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (pawnsOnMap[i] is Pawn animal && IsWildAnimal(animal)) yield return animal;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn animal) || !IsWildAnimal(animal) || !thing.Spawned) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Tame, thing);

        private static bool IsWildAnimal(Pawn animal) => animal.RaceProps.Animal && animal.faction == null && !animal.Dead;
    }

    /// <summary>Finds a tamed animal with an outstanding <see cref="TrainableDef"/> to work on (RimWorld:
    /// <c>RimWorld.WorkGiver_Train</c>).</summary>
    public sealed class WorkGiver_TrainAnimals : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (pawnsOnMap[i] is Pawn animal && IsOwnTrainableAnimal(pawn, animal)) yield return animal;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn animal) || !IsOwnTrainableAnimal(pawn, animal) || !thing.Spawned) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Train, thing);

        private static bool IsOwnTrainableAnimal(Pawn trainer, Pawn animal) =>
            animal.RaceProps.Animal && !animal.Dead && animal.faction == trainer.faction && animal.faction != null
            && animal.training.NextToTrain() != null;
    }
}
