using System;
using SimWorld.Combat;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The toil an attack job is made of (RimWorld: <c>Verse.AI.Toils_Combat</c> — <c>GotoCastPosition</c>
    /// plus <c>CastVerb</c>). One toil rather than two, for the reason <see cref="JobDriver_Hunt"/> already
    /// gives for its own hunt toil: the target moves, so "am I in range?" has to be re-asked every tick, and
    /// a separate goto toil would have to finish before anyone could ask.
    /// <para/>
    /// Both attack drivers share this, so an armed raider closing with a rifle and a cornered farmer swinging
    /// their fists run the same loop with a different <see cref="Verb"/> and a different reach — which is the
    /// whole difference between <c>AttackStatic</c> and <c>AttackMelee</c> here.
    /// </summary>
    public static class Toils_Combat
    {

        /// <summary>
        /// Close on the target named by <paramref name="ind"/> until <paramref name="verbSource"/>'s verb can
        /// reach, then cast it every tick until the target stops being a threat (dies, goes down, or leaves).
        /// <para/>
        /// <paramref name="verbSource"/> is asked once and the answer kept for the life of the job: a
        /// <see cref="Verb"/> is a per-tick state machine carrying warmup/burst/cooldown, so rebuilding it
        /// each tick would mean a shot that never lands (<see cref="JobDriver_Hunt"/> learned this first).
        /// It is asked lazily rather than up front so a pawn disarmed mid-fight ends the job instead of
        /// swinging a weapon it no longer holds.
        /// <para/>
        /// Ends the job through <see cref="Pawn_JobTracker.EndCurrentJob"/> rather than
        /// <c>JobDriver.EndJobWith</c>, which is protected and so out of reach from a static toil factory —
        /// the two are the same call, <c>EndJobWith</c> being a one-line forward to it.
        /// </summary>
        public static Toil AttackTarget(TargetIndex ind, Func<Verb?> verbSource)
        {
            if (verbSource == null) throw new ArgumentNullException(nameof(verbSource));

            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            toil.FailOnDespawnedOrNull(ind);

            // Deliberately no FailOn(pather.Failed) — the exact trap JobDriver_Hunt documents. Toils_Goto
            // .GotoCell is safe with that condition because its initAction calls StartPath (which clears
            // Failed) before any tick evaluates it; this toil starts paths from its tickAction instead, and
            // Pawn_PathFollower.Failed survives both StopDead and the end of a job — so a stale true left by
            // a previous job would fail this one on its very first tick, the giver would re-offer the same
            // enemy, and a pawn would stand there flickering between the two forever. Approach failure is
            // handled below, after StartPath has had its say.
            Verb? verb = null;
            toil.tickAction = () =>
            {
                Pawn attacker = toil.Pawn;
                if (!(toil.Job.GetTarget(ind).Thing is Pawn target))
                {
                    attacker.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (AttackTargetsUtility.ThreatDisabled(target))
                {
                    // Down, dead, or gone. Succeeded rather than Incompletable: the job did what it was for,
                    // and the pawn re-thinks immediately — picking the next enemy if there is one, or going
                    // back to work if there is not. This is also the only thing stopping an attacker from
                    // emptying itself into a body, since a downed or dead pawn here stays spawned and stays a
                    // perfectly valid target (see AttackTargetsUtility.ThreatDisabled).
                    attacker.pather.StopDead();
                    attacker.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                verb ??= verbSource();
                if (verb == null)
                {
                    // Disarmed mid-fight and with no natural weapon to fall back on.
                    attacker.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                float distance = (target.Position - attacker.Position).LengthHorizontal;
                if (distance > AttackVerbUtility.EffectiveRange(verb))
                {
                    // Re-path only once the previous path has run out: StartPath keeps an in-flight path to
                    // the same target rather than recomputing it, so an attacker walks to where the enemy
                    // was and re-aims from there. It also clears a failure left by the last path, so a route
                    // blocked mid-approach is simply retried from where the attacker now stands; only an
                    // enemy it genuinely cannot get near ends the job.
                    attacker.pather.StartPath(target, PathEndMode.Touch);
                    if (!attacker.pather.Moving) attacker.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (attacker.pather.Moving) attacker.pather.StopDead();

                if (verb.Available()) verb.TryStartCastOn(target, distance);
                verb.VerbTick();
            };
            return toil;
        }
    }
}
