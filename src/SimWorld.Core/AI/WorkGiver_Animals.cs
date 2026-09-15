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
    /// <para/>
    /// <b>The predicate that was missing entirely, and what it cost.</b> RimWorld draws this giver's work
    /// from <c>designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Tame)</c> — the player marks one
    /// animal. This port has no Designation system at all; hunting's copy of that problem was translated into
    /// <see cref="HuntingInitiative"/> and <b>this giver's was translated into nothing</b>, so every wild
    /// animal on the map was taming work with no gate but reachability and reservation. <c>Handling</c> is
    /// naturalPriority 950 against <c>Hunting</c>'s 850, so that unconditional work outranked the hunt on
    /// every job search and the hunt giver was never consulted: twelve in-game days on a founded settlement
    /// produced sixty-six tamings and zero hunts. <see cref="TamingInitiative"/> is the missing half — see
    /// that class for the whole argument, including why a Designation layer is not what was missing.
    /// </summary>
    public sealed class WorkGiver_TameAnimals : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>The civilization-scale gate, and this giver's only cheap refusal: a settlement tames out
        /// of surplus and never while it is short of food (<see cref="TamingInitiative.WantsLivestock"/>).
        /// Kept as <see cref="ShouldSkip"/> rather than folded into <see cref="HasJobOnThing"/> so it is paid
        /// once per job search instead of once per animal, exactly as <see cref="WorkGiver_Hunt"/> pays
        /// its own.</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            return map == null || !TamingInitiative.WantsLivestock(map);
        }

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
