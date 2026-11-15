using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// What a pawn does while it is on fire (RimWorld: the <c>BurningResponse</c> think tree —
    /// <c>ThinkNode_ConditionalBurning</c> over <c>JobGiver_ExtinguishSelf</c> and
    /// <c>JobGiver_RunRandom</c>). It runs about, and each time it stops there is a one-in-ten chance it
    /// stops to put itself out instead of running again.
    ///
    /// <para/><b>The missing link.</b> This port set pawns alight (<c>Health.DamageWorker_Flame</c>,
    /// <see cref="FireUtility.TryAttachFire"/>) and gave them nothing to do about it. Traced on a
    /// threat-free founding band (<c>docs/perf/storyteller-pressure</c>): a citizen caught by a lightning
    /// strike went back to harvesting with the fire still on him and took twenty burns over 2,700 ticks
    /// before he fell; nobody on fire ever put it out, and every one of them burned until downed.
    /// </summary>
    public sealed class ThinkNode_ConditionalBurning : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.Spawned && pawn.GetAttachedFire() != null;
    }

    /// <summary>One time in ten, stop and put the fire out (RimWorld: <c>JobGiver_ExtinguishSelf</c>, whose
    /// <c>ActivateChance</c> is 0.1).</summary>
    public sealed class JobGiver_ExtinguishSelf : ThinkNode_JobGiver
    {
        public const float ActivateChance = 0.1f;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!Rand.Chance(ActivateChance)) return null;
            Fire? fire = pawn.GetAttachedFire();
            return fire != null ? new Job(BurningJobDefOf.ExtinguishSelf, fire) : null;
        }
    }

    /// <summary>Otherwise run somewhere nearby (RimWorld: <c>JobGiver_RunRandom</c>, a wander of radius 7).
    /// This port's wander already has that radius; RimWorld also sprints, and there is no locomotion urgency
    /// here to ask for.</summary>
    public sealed class JobGiver_RunRandom : JobGiver_WanderAnywhere
    {
    }

    /// <summary>
    /// Stands still for <see cref="ExtinguishTicks"/> and then the fire is out (RimWorld:
    /// <c>JobDriver_ExtinguishSelf</c>: a 150-tick wait, then the attached fire is destroyed). The pawn
    /// goes on burning for those 150 ticks.
    /// </summary>
    public sealed class JobDriver_ExtinguishSelf : JobDriver
    {
        public const int ExtinguishTicks = 150;

        public override bool TryMakePreToilReservations() => true;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_General.Wait(ExtinguishTicks);
            yield return Toils_General.Do(() =>
            {
                Fire? fire = job.GetTarget(TargetIndex.A).Thing as Fire;
                if (fire != null && !fire.Destroyed) fire.Destroy();
            });
        }
    }

    [DefOf]
    public static class BurningJobDefOf
    {
        public static JobDef ExtinguishSelf = null!;
    }
}
