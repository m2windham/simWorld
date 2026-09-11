using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a wild animal worth hunting and issues the <c>Hunt</c> job (RimWorld:
    /// <c>RimWorld.WorkGiver_HunterHunt</c>). Wires the <c>Hunt</c> <see cref="WorkGiverDef"/>'s
    /// <c>giverClass</c> in <c>WorkGivers.xml</c>, which until now fell back to the empty
    /// <see cref="WorkGiver_Pending"/> — the work type appeared in every pawn's priority grid and could never
    /// produce a job.
    /// <para/>
    /// <b>The one predicate that could not be ported.</b> RimWorld scans animals carrying
    /// <c>Designation.Hunt</c>. This codebase has no Designation system at all, so what replaces it is
    /// <see cref="HuntingInitiative"/>: the settlement hunts while it is short of food and stops when it is
    /// not. See that class for the whole argument, including why copying <see cref="WorkGiver_Miner"/>'s
    /// "just do all of it" translation would have been wrong here.
    /// <para/>
    /// <b>Ordering against taming.</b> <c>Handling</c> (naturalPriority 950) outranks <c>Hunting</c> (850) in
    /// content, so a pawn who could either tame or hunt the same animal tames it — which is the right way
    /// round and needed no special-casing here.
    /// </summary>
    public sealed class WorkGiver_Hunt : WorkGiver_Scanner
    {
        /// <summary>The hunter closes to weapon range itself inside <see cref="JobDriver_Hunt"/> (prey moves,
        /// so a single path computed up front would be stale on arrival); <see cref="Touch"/> is what the
        /// reachability test in <see cref="HasJobOnThing"/> asks for, and the worst case the driver may have
        /// to walk.</summary>
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// Cheap refusals before any scan, cheapest first: no map, no ranged weapon (RimWorld's own
        /// <see cref="HuntUtility.HasHuntingWeapon"/> gate), violence disabled (RimWorld checks this too;
        /// <see cref="Pawn_WorkSettings"/> already zeroes the whole Hunting work type for such a pawn, so this
        /// is belt-and-braces rather than the only guard), and finally the food gate — the map-and-ledger
        /// walk, kept last so it is only paid by a pawn who could actually hunt.
        /// </summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            if (!HuntUtility.HasHuntingWeapon(pawn)) return true;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return true;
            return !HuntingInitiative.WantsMeat(map);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (pawnsOnMap[i] is Pawn animal && HuntUtility.IsHuntableAnimal(animal)) yield return animal;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn animal) || !HuntUtility.IsHuntableAnimal(animal)) return false;
            if (ReferenceEquals(animal, pawn)) return false;
            if (!HuntUtility.IsSafeToHunt(pawn, animal)) return false;
            if (!Reachability.CanReach(pawn, animal, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, animal);
        }

        /// <summary>The hunt carries an expiry (<see cref="HuntingTuning.HuntJobExpiryTicks"/>) so a chase
        /// after fleeing prey cannot run forever; <see cref="Pawn_JobTracker.JobTrackerTick"/> already
        /// enforces <see cref="Job.expiryInterval"/> for every job, so nothing new drives it.</summary>
        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(HuntingDefOf.Hunt, thing) { expiryInterval = HuntingTuning.HuntJobExpiryTicks };
    }
}
