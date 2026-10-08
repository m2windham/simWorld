using System;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Toils and helpers for a job that picks a Thing up, walks, and puts it down (RimWorld:
    /// <c>Verse.AI.Toils_Haul</c>), all of them going through the pawn's <see cref="Pawn_CarryTracker"/> so that
    /// whatever has been picked up is a Thing somebody holds, never a number on a job.
    /// <para/>
    /// <b>Why these are shared.</b> <see cref="JobDriver_HaulToCell"/> and <see cref="JobDriver_FoodDeliver"/>
    /// both used to take goods off the map into a field of their own, and lost the field's contents whenever
    /// the job was cut short while only part of a stack was in hand. They now pick up, wait for arrival and put
    /// down the same way, and that way lives here once rather than twice. <see cref="Building.JobDriver_HaulToBuildingSite"/>
    /// carries through the same tracker with its own inline copy of the first two toils; it predates this class.
    /// <para/>
    /// <b>What is ported, and what is not.</b> RimWorld's <c>StartCarryThing</c> sizes the load from
    /// <c>job.count</c> and the hands' free <c>AvailableStackSpace</c>, optionally queues the remainder for the
    /// next pickup, and releases the claim on a stack it only partly emptied; here the caller says how many it
    /// wants (a stockpile cell's headroom, one meal), the remainder simply stays on the map, and the claim on
    /// the source stack is kept until the job ends, exactly as it was before the carry tracker — releasing it
    /// early is a behaviour change for the hauling AI that this port has not taken. RimWorld's
    /// <c>PlaceHauledThingInCell</c> falls back to another store cell or a haul-aside job when the drop fails;
    /// <see cref="TryPlaceCarriedThing"/> leaves the load in hand instead and lets the job's end put it down at
    /// the pawn's feet, which is where a failed placement ends up in every case this port can reach.
    /// </summary>
    public static class Toils_Haul
    {
        /// <summary>True when target <paramref name="ind"/> of <paramref name="job"/> is the Thing in
        /// <paramref name="pawn"/>'s hands — after pickup, and after a load that caught the pawn mid-carry.
        /// A <see cref="JobDriver"/> is not saved: a loaded job rebuilds its driver and starts from the first
        /// toil, so every toil before the walk to the destination asks this and skips itself when the pawn
        /// already holds the goods (RimWorld opens its drivers with a jump past the pickup,
        /// <c>Toils_Jump.JumpIf(..., pawn.IsCarryingThing(...))</c>; this port has no jump toil).</summary>
        public static bool IsCarryingTarget(Pawn pawn, Job job, TargetIndex ind)
        {
            Thing? carried = pawn.carryTracker.CarriedThing;
            return carried != null && ReferenceEquals(carried, job.GetTarget(ind).Thing);
        }

        /// <summary>
        /// Walks to the Thing named by <paramref name="ind"/> to pick it up (RimWorld: the
        /// <c>Toils_Goto.GotoThing</c> that opens <c>JobDriver_HaulToCell</c>), and does nothing at all when
        /// that Thing is already in the pawn's hands. <see cref="Toils_Goto.GotoThing"/>'s own shape, less its
        /// fail-on-despawned: a stack in hand is off the map by definition, and is exactly the case where there
        /// is nothing to walk to.
        /// </summary>
        public static Toil GotoThingToCarry(TargetIndex ind, PathEndMode peMode)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            toil.initAction = () =>
            {
                if (IsCarryingTarget(toil.Pawn, toil.Job, ind)) return;
                toil.Pawn.pather.StartPath(toil.Job.GetTarget(ind), peMode);
            };
            toil.FailOn(() =>
            {
                if (IsCarryingTarget(toil.Pawn, toil.Job, ind)) return false;
                Thing? thing = toil.Job.GetTarget(ind).Thing;
                return thing == null || thing.Destroyed || !thing.Spawned || toil.Pawn.pather.Failed;
            });
            return toil;
        }

        /// <summary>
        /// Picks up the Thing named by <paramref name="haulableInd"/> into the pawn's hands (RimWorld:
        /// <c>Toils_Haul.StartCarryThing</c>, and <c>Toils_Ingest.PickupIngestible</c> for food).
        /// <paramref name="howMany"/> is asked of the stack at the moment of pickup — a destination's headroom
        /// can change between the job being given and the pawn arriving — and the toil takes that many, or the
        /// whole stack if it has fewer. The whole stack travels as the very Thing it is, so an item with state of
        /// its own (a masterwork, a damaged one, a <see cref="Corpse"/>) arrives as itself; part of a stack is
        /// split off into a Thing of its own, which is only ever asked of a stackable good.
        /// <para/>
        /// Afterwards the job's target is re-pointed at what is now in hand, as RimWorld does
        /// (<c>curJob.SetTarget(haulableInd, carryTracker.CarriedThing)</c>), and <see cref="Job.count"/> is the
        /// number taken. The job ends <see cref="JobCondition.Incompletable"/> — with nothing picked up — when
        /// the Thing is gone, nothing is wanted, or the hands are not free (<see cref="Pawn_CarryTracker.TryStartCarry"/>
        /// refuses a downed pawn and one already holding something).
        /// </summary>
        public static Toil StartCarryThing(TargetIndex haulableInd, Func<Thing, int> howMany)
        {
            if (howMany == null) throw new ArgumentNullException(nameof(howMany));
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            toil.initAction = () =>
            {
                Pawn pawn = toil.Pawn;
                Job job = toil.Job;
                if (IsCarryingTarget(pawn, job, haulableInd)) return; // loaded mid-carry: already in hand

                Thing? thing = job.GetTarget(haulableInd).Thing;
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int wanted = howMany(thing);
                int taken = wanted > 0 ? pawn.carryTracker.TryStartCarry(thing, wanted) : 0;
                if (taken <= 0)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                job.SetTarget(haulableInd, pawn.carryTracker.CarriedThing);
                job.count = taken;
            };
            return toil;
        }

        /// <summary>
        /// Puts what the pawn is carrying down on <paramref name="cell"/> (RimWorld:
        /// <c>Toils_Haul.PlaceHauledThingInCell</c>, which goes through <c>Pawn_CarryTracker.TryDropCarriedThing</c>
        /// and <c>Thing.TryAbsorbStack</c>). Onto a stack of the same def already there it merges, <b>up to that
        /// def's stack limit and no further</b>: the stack may have grown since the pawn picked up, and a pile
        /// that passes its limit is a stack that cannot be hauled again. Anything that did not fit stays in hand.
        /// Anywhere else the carried Thing itself is set down on the cell. True when nothing is left in hand.
        /// <para/>
        /// Whatever stays in hand is not lost: the job this runs in ends straight after, and
        /// <see cref="Pawn_JobTracker.EndCurrentJob"/> puts it down at the pawn's feet.
        /// </summary>
        public static bool TryPlaceCarriedThing(Pawn pawn, IntVec3 cell)
        {
            Thing? carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return false;

            Map.Map? map = pawn.Map;
            if (map == null || !GenGrid.InBounds(cell, map)) return false;

            Thing? existing = HaulAIUtility.ExistingStackAt(map, cell);
            if (existing != null && existing.def == carried.def && !ReferenceEquals(existing, carried))
            {
                int moved = Math.Min(carried.stackCount, Math.Max(0, existing.def.stackLimit - existing.stackCount));
                existing.stackCount += moved;
                carried.stackCount -= moved;
                if (carried.stackCount > 0) return false;
                pawn.carryTracker.DestroyCarriedThing(); // the merged-into stack is the survivor
                return true;
            }

            return pawn.carryTracker.TryDropCarriedThing(cell, out _);
        }
    }
}
