using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Sends a hauler to top up anything with a <see cref="CompRefuelable"/> that has burned low (RimWorld:
    /// <c>RimWorld.WorkGiver_Refuel</c>; WorkGiverDef <c>Refuel</c>, Hauling, priority 60, from RimWorld 1.0's
    /// <c>Core/Defs/WorkGiverDefs/WorkGivers.xml</c>). RimWorld scans <c>ThingRequestGroup.Refuelable</c>; this
    /// port has no such group, so it scans the artificial buildings and keeps those with the comp. The turret
    /// exclusion and the atomic job (<c>RefuelAtomic</c>) are left out: nothing shipped needs them.
    /// </summary>
    public sealed class WorkGiver_Refuel : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t is ThingWithComps twc && twc.GetComp<CompRefuelable>() != null) yield return t;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            RefuelWorkGiverUtility.CanRefuel(pawn, thing, forced);

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            RefuelWorkGiverUtility.RefuelJob(pawn, thing);
    }

    /// <summary>RimWorld: <c>RimWorld.RefuelWorkGiverUtility</c>, less the atomic path and the forbidden checks
    /// (nothing is forbidden in this port).</summary>
    public static class RefuelWorkGiverUtility
    {
        public static bool CanRefuel(Pawn pawn, Thing t, bool forced = false)
        {
            CompRefuelable? comp = (t as ThingWithComps)?.GetComp<CompRefuelable>();
            if (comp == null || comp.IsFull) return false;
            if (!forced && !comp.ShouldAutoRefuelNow) return false;
            Map.Map? map = pawn.Map;
            if (map == null || !t.Spawned) return false;
            if (!map.reservationManager.CanReserve(pawn, t)) return false;
            if (t.Faction != pawn.Faction) return false;
            return FindBestFuel(pawn, t) != null;
        }

        public static Job? RefuelJob(Pawn pawn, Thing t)
        {
            Thing? fuel = FindBestFuel(pawn, t);
            return fuel == null ? null : new Job(RefuelJobDefOf.Refuel, t, fuel);
        }

        /// <summary>The nearest fuel the pawn can reserve and reach (RimWorld: <c>FindBestFuel</c>, a
        /// <c>GenClosest.ClosestThingReachable</c> over the filter's best request; here a nearest-by-distance
        /// pass over each allowed def's lister, the same search <see cref="WorkGiver_ConstructDeliverResources"/>
        /// makes for materials).</summary>
        internal static Thing? FindBestFuel(Pawn pawn, Thing refuelable)
        {
            CompRefuelable comp = ((ThingWithComps)refuelable).GetComp<CompRefuelable>()!;
            Map.Map map = pawn.Map!;
            Thing? best = null;
            int bestDistSq = int.MaxValue;
            foreach (ThingDef def in comp.Props.fuelFilter.AllowedThingDefs)
            {
                IReadOnlyList<Thing> candidates = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Thing candidate = candidates[i];
                    if (!candidate.Spawned || candidate.stackCount <= 0) continue;
                    int distSq = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                    if (distSq >= bestDistSq) continue;
                    if (!map.reservationManager.CanReserve(pawn, candidate)) continue;
                    if (!Reachability.CanReach(pawn, candidate, PathEndMode.ClosestTouch)) continue;
                    best = candidate;
                    bestDistSq = distSq;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Fetches fuel and feeds it to a refuelable (RimWorld: <c>RimWorld.JobDriver_Refuel</c>). Target A is the
    /// refuelable, target B the fuel: reserve both, walk to the fuel, pick up what fills the tank, walk to the
    /// refuelable, work 240 ticks, then <c>Toils_Refuel.FinalizeRefueling</c>. RimWorld's job-wide end and fail
    /// conditions (full: succeeded; no longer wanting fuel: failed) sit on the toils, since this port's drivers
    /// have none of their own. What is left in hand is put down when the job ends, as with every carry here.
    /// </summary>
    public sealed class JobDriver_Refuel : JobDriver
    {
        /// <summary>RimWorld: <c>JobDriver_Refuel.RefuelingDuration</c>.</summary>
        public const int RefuelingDuration = 240;

        private Thing Refuelable => job.GetTarget(TargetIndex.A).Thing!;

        private CompRefuelable RefuelableComp => ((ThingWithComps)Refuelable).GetComp<CompRefuelable>()!;

        private bool CarryingFuel
        {
            get
            {
                Thing? carried = pawn.carryTracker.CarriedThing;
                return carried != null && ReferenceEquals(carried, job.GetTarget(TargetIndex.B).Thing);
            }
        }

        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        private bool RefuelableGone()
        {
            Thing? t = job.GetTarget(TargetIndex.A).Thing;
            return t == null || t.Destroyed || !t.Spawned;
        }

        private bool NoLongerWanted() => !RefuelableGone() && !RefuelableComp.ShouldAutoRefuelNowIgnoringFuelPct;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A).FailOn(RefuelableGone);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            yield return Toils_General.Do(() =>
            {
                if (RefuelableComp.IsFull) EndJobWith(JobCondition.Succeeded);
                else job.count = RefuelableComp.GetFuelCountToFullyRefuel();
            });

            var gotoFuel = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            gotoFuel.initAction = () =>
            {
                if (CarryingFuel) return;
                pawn.pather.StartPath(job.GetTarget(TargetIndex.B), PathEndMode.ClosestTouch);
            };
            gotoFuel.FailOn(() =>
            {
                if (CarryingFuel) return false;
                Thing? fuel = job.GetTarget(TargetIndex.B).Thing;
                return fuel == null || fuel.Destroyed || !fuel.Spawned || pawn.pather.Failed;
            });
            gotoFuel.FailOn(RefuelableGone).FailOn(NoLongerWanted);
            yield return gotoFuel;

            yield return Toils_General.Do(() =>
            {
                if (CarryingFuel) return;
                Thing fuel = job.GetTarget(TargetIndex.B).Thing!;
                int taken = pawn.carryTracker.TryStartCarry(fuel, job.count);
                if (taken <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                job.SetTarget(TargetIndex.B, pawn.carryTracker.CarriedThing);
            });

            Toil gotoRefuelable = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoRefuelable.FailOn(RefuelableGone).FailOn(NoLongerWanted).FailOn(() => !CarryingFuel);
            yield return gotoRefuelable;

            Toil work = Toils_General.Wait(RefuelingDuration);
            work.FailOn(RefuelableGone).FailOn(NoLongerWanted).FailOn(() => !CarryingFuel);
            yield return work;

            yield return Toils_General.Do(() =>
            {
                Thing carried = pawn.carryTracker.CarriedThing!;
                RefuelableComp.Refuel(new List<Thing> { carried });
                if (carried.stackCount <= 0) pawn.carryTracker.DestroyCarriedThing();
            });
        }
    }

    /// <summary>Binds the refuel job (RimWorld: <c>JobDefOf.Refuel</c>); its own class so no shared DefOf is edited.</summary>
    [DefOf]
    public static class RefuelJobDefOf
    {
        public static JobDef Refuel = null!;
    }
}
