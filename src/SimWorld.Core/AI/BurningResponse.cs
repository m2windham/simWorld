using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.AI
{
    // ------------------------------------------------------------------------------------------------------
    // What a pawn does about being on fire (RimWorld's BurningResponse think tree). Every class here is a
    // port of one decompile file, named after it; the tree that wires them sits first in
    // ThinkTrees_Humanlike.xml, where RimWorld puts it — straight after Downed, which this port has no node
    // for because a downed pawn never thinks here at all (Pawn_JobTracker.TryFindAndStartJob).
    //
    // Before this file a citizen who caught fire had no response of their own. The only thing that could put
    // them out was WorkGiver_FightFires, which took the pawn's own fire as ordinary emergency work — and only
    // inside the home area. A citizen alight in the fields kept working in the fire until they burned to
    // death. RimWorld never asked the firefighting work giver to do this (it refuses the pawn's own fire in
    // its first line); a burning pawn runs, rolls, or jumps in water, and it does so before anything else it
    // could think of.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// True while a fire is riding this pawn (RimWorld: <c>RimWorld.ThinkNode_ConditionalBurning</c>,
    /// <c>pawn.HasAttachment(ThingDefOf.Fire)</c>). The gate of the burning tier, which is the first node of
    /// the humanlike tree: pre-empts mental states, the fight tier and everything below.
    /// </summary>
    public sealed class ThinkNode_ConditionalBurning : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.GetAttachedFire() != null;
    }

    /// <summary>
    /// Makes for water within reach, which puts out a fire riding a pawn (RimWorld:
    /// <c>RimWorld.JobGiver_JumpInWater</c>). Tried first in the burning tier, every think, as RimWorld's own
    /// <c>ActivateChance</c> of 1 means.
    /// <para/>
    /// <b>Deviations, both forced by this port's terrain and pathing.</b>
    /// <list type="bullet">
    /// <item>The cell must also be <b>standable and reachable</b>. RimWorld's validator reads only
    /// <c>TerrainDef.extinguishesFire</c>, which it sets on water a pawn can wade. This port's only marker is
    /// <see cref="TerrainDef.IsWater"/>, which also covers deep and chest-deep water that is impassable here;
    /// and this port's <see cref="Toils_Goto.GotoCell"/> treats "no path" as an instant arrival rather than a
    /// failure, so an unreachable pond would be a job that ends the tick it starts, every think, for ever —
    /// a pawn stuck on that loop never reaches the extinguish roll below it.</item>
    /// <item>The leg is <see cref="BurningJobDefOf.Goto_OnCell"/>, not this port's <c>Goto</c>: see that def.</item>
    /// </list>
    /// </summary>
    public sealed class JobGiver_JumpInWater : ThinkNode_JobGiver
    {
        /// <summary>RimWorld: <c>JobGiver_JumpInWater.ActivateChance</c>.</summary>
        public const float ActivateChance = 1f;

        /// <summary>How far out the search may grow (RimWorld: <c>JobGiver_JumpInWater.MaxDistance</c>).</summary>
        public static readonly IntRange MaxDistance = new IntRange(10, 16);

        /// <summary>Where the search starts (RimWorld: <c>RCellFinder.TryFindRandomCellNearWith</c>'s
        /// <c>startingSearchRadius</c> as this giver calls it).</summary>
        public const int StartingSearchRadius = 5;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;

            // RimWorld rolls this even at a chance of 1, and the roll is kept so the stream stays RimWorld's.
            if (!(Rand.Value < ActivateChance)) return null;

            bool Validator(IntVec3 c) =>
                GenGrid.GetTerrain(c, map).IsWater
                && GenGrid.Standable(c, map)
                && Reachability.CanReach(pawn, c, PathEndMode.OnCell);

            int maxRadius = Rand.Range(MaxDistance);
            if (!TryFindRandomCellNearWith(pawn.Position, Validator, map, out IntVec3 water, StartingSearchRadius, maxRadius))
            {
                return null;
            }
            return new Job(BurningJobDefOf.Goto_OnCell, water);
        }

        /// <summary>
        /// A random cell near <paramref name="near"/> passing <paramref name="validator"/>, the search square
        /// growing by half each time thirty draws in it come up empty (RimWorld:
        /// <c>RimWorld.RCellFinder.TryFindRandomCellNearWith</c>, restated line for line). Kept private to this
        /// giver rather than starting an <c>RCellFinder</c> class that one caller would own.
        /// </summary>
        internal static bool TryFindRandomCellNearWith(IntVec3 near, Func<IntVec3, bool> validator, Map.Map map, out IntVec3 result, int startingSearchRadius, int maxSearchRadius)
        {
            int radius = startingSearchRadius;
            CellRect rect = CellRect.CenteredOn(near, radius).ClipInsideMap(map);
            int tries = 0;
            while (true)
            {
                tries++;
                if (tries > 30)
                {
                    if (radius >= maxSearchRadius || (radius > map.Size.x * 2 && radius > map.Size.z * 2)) break;
                    radius = Math.Min((int)(radius * 1.5f), maxSearchRadius);
                    rect = CellRect.CenteredOn(near, radius).ClipInsideMap(map);
                    tries = 0;
                }
                if (rect.IsEmpty) break;
                // RimWorld: CellRect.RandomCell, both bounds inclusive.
                var cell = new IntVec3(Rand.RangeInclusive(rect.minX, rect.maxX), 0, Rand.RangeInclusive(rect.minZ, rect.maxZ));
                if (validator(cell))
                {
                    result = cell;
                    return true;
                }
            }
            result = near;
            return false;
        }
    }

    /// <summary>
    /// One chance in ten, per think, to stop and put the fire out (RimWorld:
    /// <c>RimWorld.JobGiver_ExtinguishSelf</c>). The other nine fall through to
    /// <see cref="JobGiver_RunRandom"/>, so a burning pawn runs about between rolls, and each run leg or pause
    /// that ends is another think and another roll — that is RimWorld's whole self-extinguish model, and why a
    /// pawn sometimes burns for a while and sometimes for an instant.
    /// </summary>
    public sealed class JobGiver_ExtinguishSelf : ThinkNode_JobGiver
    {
        /// <summary>RimWorld: <c>JobGiver_ExtinguishSelf.ActivateChance</c>.</summary>
        public const float ActivateChance = 0.1f;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (Rand.Value < ActivateChance)
            {
                Fire? fire = pawn.GetAttachedFire();
                if (fire != null) return new Job(BurningJobDefOf.ExtinguishSelf, fire);
            }
            return null;
        }
    }

    /// <summary>
    /// Runs about at random: a short pause, then a dash to a random cell nearby, then a pause again
    /// (RimWorld: <c>Verse.AI.JobGiver_RunRandom</c>, a <c>JobGiver_Wander</c> with radius 7, 5~10 ticks
    /// between legs and its root on the pawn itself). <see cref="TryGiveJob"/> is RimWorld's
    /// <c>JobGiver_Wander.TryGiveJob</c> restated: <see cref="MindState.Pawn_MindState.nextMoveOrderIsWait"/>
    /// alternates the two halves, and a pawn asked again while still mid-leg is given a fresh leg rather than
    /// a pause.
    /// <para/>
    /// <b>Deviations.</b>
    /// <list type="bullet">
    /// <item><b>No Sprint.</b> RimWorld runs the leg at <c>LocomotionUrgency.Sprint</c>; this port has no
    /// locomotion urgency at all, so the burning pawn moves at its ordinary speed.</item>
    /// <item><b>The destination is a bounded number of random offsets</b> inside the radius, standable and
    /// reachable — the same draw <see cref="JobGiver_WanderAnywhere"/> makes — not RimWorld's
    /// <c>RCellFinder.RandomWanderDestFor</c>, which scores region-walked candidates by danger and room and
    /// has no port here.</item>
    /// <item><b>The leg is <see cref="BurningJobDefOf.GotoWander_Run"/>, not this port's <c>GotoWander</c></b>,
    /// whose driver idles a full second on arrival: RimWorld's pause between legs is the
    /// <c>Wait_Wander</c> job, five to ten ticks, and a burning pawn standing still for sixty is a pawn
    /// burning with no chance to roll.</item>
    /// </list>
    /// </summary>
    public sealed class JobGiver_RunRandom : ThinkNode_JobGiver
    {
        /// <summary>RimWorld: <c>JobGiver_RunRandom</c>'s <c>wanderRadius</c>.</summary>
        public const float WanderRadius = 7f;

        /// <summary>Ticks of pause between legs (RimWorld: <c>JobGiver_RunRandom</c>'s
        /// <c>ticksBetweenWandersRange</c>).</summary>
        public static readonly IntRange TicksBetweenWandersRange = new IntRange(5, 10);

        /// <summary>Random offsets tried for a destination before giving up this think; the same bound
        /// <see cref="JobGiver_WanderAnywhere"/> uses, not a RimWorld number.</summary>
        private const int MaxDestTries = 8;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            bool alreadyRunning = pawn.jobs?.curJob != null && pawn.jobs.curJob.def == BurningJobDefOf.GotoWander_Run;
            bool nextMoveOrderIsWait = pawn.mindState.nextMoveOrderIsWait;
            if (!alreadyRunning)
            {
                pawn.mindState.nextMoveOrderIsWait = !pawn.mindState.nextMoveOrderIsWait;
            }
            if (nextMoveOrderIsWait && !alreadyRunning)
            {
                return new Job(BurningJobDefOf.Wait_Wander) { expiryInterval = Rand.Range(TicksBetweenWandersRange) };
            }

            IntVec3 dest = GetExactWanderDest(pawn);
            if (!dest.IsValid)
            {
                pawn.mindState.nextMoveOrderIsWait = false;
                return null;
            }
            return new Job(BurningJobDefOf.GotoWander_Run, dest);
        }

        /// <summary>A standable, reachable cell within <see cref="WanderRadius"/> of the pawn, or
        /// <see cref="IntVec3.Invalid"/> (RimWorld: <c>JobGiver_Wander.GetExactWanderDest</c> with
        /// <c>JobGiver_RunRandom.GetWanderRoot</c> = the pawn's own cell; see the class doc for the deviation).</summary>
        private static IntVec3 GetExactWanderDest(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return IntVec3.Invalid;
            int count = GenRadial.NumCellsInRadius(WanderRadius);
            if (count <= 1) return IntVec3.Invalid;

            for (int i = 0; i < MaxDestTries; i++)
            {
                IntVec3 candidate = pawn.Position + GenRadial.RadialPattern[Rand.Range(1, count)];
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                return candidate;
            }
            return IntVec3.Invalid;
        }
    }

    /// <summary>
    /// Stands still for a moment, then puts out the fire riding the pawn (RimWorld:
    /// <c>RimWorld.JobDriver_ExtinguishSelf</c>: a 150-tick wait, then the fire is destroyed). Its JobDef
    /// refuses a casual interrupt (<c>JobDefs_Burning.xml</c>), and the fire's own damage cannot re-ask the
    /// think tree mid-roll either (<c>Flame</c>'s <c>canInterruptJobs</c>), so once begun it finishes unless
    /// something else actually hurts the pawn.
    /// <para/>Not ported: RimWorld's <c>records.Increment(RecordDefOf.FiresExtinguished)</c>; this port keeps
    /// no per-pawn records.
    /// </summary>
    public sealed class JobDriver_ExtinguishSelf : JobDriver
    {
        /// <summary>RimWorld: the first toil's <c>defaultDuration</c>.</summary>
        public const int ExtinguishTicks = 150;

        private Fire? TargetFire => job.targetA.Thing as Fire;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_General.Wait(ExtinguishTicks);

            var killFire = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            killFire.initAction = () =>
            {
                // RimWorld guards this toil with FailOnDestroyedOrNull; this port's driver runs an Instant
                // toil's initAction before any fail check could see it, so the guard is the null test here.
                Fire? fire = TargetFire;
                if (fire != null && !fire.Destroyed) fire.Destroy();
            };
            killFire.FailOnDespawnedOrNull(TargetIndex.A);
            yield return killFire;
        }
    }

    /// <summary>
    /// Stands where it is until the job expires (RimWorld: <c>Verse.AI.JobDriver_Wait</c>, the driver behind
    /// <c>Wait_Wander</c>). Its only toil never completes by itself: <see cref="Job.expiryInterval"/>, which
    /// <see cref="JobGiver_RunRandom"/> sets, is what ends it.
    /// <para/>Not ported: RimWorld's <c>CheckForAutoAttack</c>, which has a waiting pawn punch an adjacent
    /// enemy or beat out an adjacent fire. This port has no natives tracker to beat a fire with outside a job.
    /// </summary>
    public sealed class JobDriver_Wait : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            var wait = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            wait.initAction = () => wait.Pawn.pather.StopDead();
            yield return wait;
        }
    }

    /// <summary>
    /// Walks onto a cell and ends there, with no pause (RimWorld: <c>Verse.AI.JobDriver_Goto</c>, whose goto
    /// toil is <see cref="PathEndMode.OnCell"/>). This port's own <see cref="JobDriver_Goto"/> stops on
    /// <i>touch</i> — right for a raid closing on a pawn, wrong for a burning pawn making for water, which has
    /// to stand <i>in</i> it — and <see cref="JobDriver_GotoWander"/> idles a full second on arrival. The two
    /// burning legs (<see cref="BurningJobDefOf.Goto_OnCell"/>, <see cref="BurningJobDefOf.GotoWander_Run"/>)
    /// share this driver.
    /// </summary>
    public sealed class JobDriver_GotoOnCell : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
        }
    }
}
