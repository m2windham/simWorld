using System;
using System.Collections.Generic;

using SimWorld.Needs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// What every recreation job has in common (RimWorld: the <c>JoyTickCheckEnd</c> call every
    /// <c>JobDriver_*</c> joy driver makes from its own tick action). The joy kind, the rate and the session
    /// length come from the <see cref="JoyGiverDef"/> that issued the job — see that class for why they are
    /// there and not on the <see cref="JobDef"/>.
    /// </summary>
    public abstract class JobDriver_Joy : JobDriver
    {
        private JoyGiverDef? giverInt;

        /// <summary>The giver that authored this job. Never null in play: a joy driver is only ever reached
        /// through a <see cref="JoyGiverDef"/> whose <c>jobDef</c> names it, and
        /// <see cref="JoyGiverDef.ConfigErrors"/> makes the content load fail if one has no jobDef.</summary>
        protected JoyGiverDef Giver =>
            giverInt ??= JoyUtility.GiverForJob(job.def)
                ?? throw new InvalidOperationException(
                    "JobDef " + job.def.defName + " runs a joy driver but no JoyGiverDef names it.");

        /// <summary>Credits one tick of recreation and ends the job once the need is full — RimWorld's own
        /// <c>JoyTickFullJoyAction.EndJob</c>.</summary>
        protected void JoyTick()
        {
            if (JoyUtility.JoyTickCheckEnd(pawn, Giver.joyKind, Giver.joyGainRate))
            {
                EndJobWith(JobCondition.Succeeded);
            }
        }

        /// <summary>A sit-still session of <see cref="JoyGiverDef.joyDuration"/> ticks that pays joy every
        /// tick of it.</summary>
        protected Toil JoySessionToil()
        {
            Toil toil = Toils_General.Wait(Giver.joyDuration);
            toil.tickAction = JoyTick;
            return toil;
        }
    }

    /// <summary>Walk somewhere with open sky and watch it (RimWorld: <c>RimWorld.JobDriver_Skygaze</c>).</summary>
    public sealed class JobDriver_Skygaze : JobDriver_Joy
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return JoySessionToil();
        }
    }

    /// <summary>
    /// Walk, and enjoy the walking (RimWorld: <c>RimWorld.JobDriver_GoForWalk</c>, which pays joy on every
    /// tick of the route rather than at the end of it). One leg rather than RimWorld's multi-leg route —
    /// see <see cref="JoyGiver_GoForWalk"/> for why.
    /// </summary>
    public sealed class JobDriver_GoForWalk : JobDriver_Joy
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            Toil walk = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            walk.tickAction = JoyTick;
            yield return walk;
        }
    }

    /// <summary>
    /// Sit down near somebody and talk (RimWorld: <c>RimWorld.JobDriver_SocialRelax</c>). Target A is the
    /// companion, target B the spot to sit in; the session ends early if the companion goes down, dies or
    /// leaves, which is what RimWorld's own <c>FailOnDespawnedOrNull</c> on the gather spot does.
    /// </summary>
    public sealed class JobDriver_SocialRelax : JobDriver_Joy
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil relax = JoySessionToil();
            relax.FailOnDespawnedOrNull(TargetIndex.A);
            relax.FailOn(() => job.GetTarget(TargetIndex.A).Thing is Pawn companion
                && (companion.Dead || companion.Downed));
            yield return relax;
        }
    }
}
