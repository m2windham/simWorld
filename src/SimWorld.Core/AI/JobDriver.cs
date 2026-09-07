using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The toil state machine that actually runs a <see cref="Job"/> (RimWorld: <c>Verse.AI.JobDriver</c>).
    /// A fresh driver is built from <c>job.def.driverClass</c> every time a job starts — including after a
    /// load, where <see cref="Pawn_JobTracker"/> rebuilds it from the (deep-saved) <see cref="Job"/> and
    /// calls <see cref="Notify_Starting"/> again. Toils are therefore written to be safely re-enterable
    /// (reserving something already held is a no-op; re-starting a path is harmless) rather than needing
    /// their own save data — nothing here implements <c>IExposable</c>.
    /// </summary>
    public abstract class JobDriver
    {
        public Pawn pawn = null!;
        public Job job = null!;

        private readonly List<Toil> toils = new List<Toil>();
        private int curToilIndex = -1;
        private int ticksLeftThisToil;

        /// <summary>Builds this job's toil sequence. Called once, from <see cref="Notify_Starting"/>.</summary>
        public abstract IEnumerable<Toil> MakeNewToils();

        /// <summary>
        /// Reserves whatever this job needs before its first toil runs; returning false aborts the job
        /// immediately (RimWorld: <c>JobDriver.TryMakePreToilReservations</c>). The default assumes the job
        /// reserves nothing extra beyond what its own toils reserve.
        /// </summary>
        public virtual bool TryMakePreToilReservations() => true;

        /// <summary>Cleanup hook run once, whatever the job's outcome (RimWorld: folds into <c>JobDriver.Cleanup</c>).</summary>
        public virtual void Notify_Ending()
        {
        }

        private Toil? CurToil => curToilIndex >= 0 && curToilIndex < toils.Count ? toils[curToilIndex] : null;

        public void Notify_Starting()
        {
            toils.Clear();
            foreach (Toil toil in MakeNewToils())
            {
                toil.actor = this;
                toils.Add(toil);
            }
            curToilIndex = -1;
            ReadyForNextToil();
        }

        /// <summary>Advances to the next toil (or ends the job Succeeded, past the last one), running its <c>initAction</c>.</summary>
        public void ReadyForNextToil()
        {
            curToilIndex++;
            if (curToilIndex >= toils.Count)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            Toil toil = toils[curToilIndex];
            ticksLeftThisToil = toil.defaultDuration;
            toil.initAction?.Invoke();
            // initAction may itself have ended the job (e.g. a reservation toil failing); a driver no
            // longer current for its own pawn must not keep driving itself.
            if (pawn.jobs.curDriver != this) return;
            if (toil.defaultCompleteMode == ToilCompleteMode.Instant)
            {
                ReadyForNextToil();
            }
        }

        /// <summary>Runs one tick of the current toil (RimWorld: <c>JobDriver.DriverTick</c>).</summary>
        public void DriverTick()
        {
            Toil? toil = CurToil;
            if (toil == null) return;

            if (toil.ShouldFail())
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            toil.tickAction?.Invoke();
            if (pawn.jobs.curDriver != this) return;
            if (CurToil != toil) return;

            switch (toil.defaultCompleteMode)
            {
                case ToilCompleteMode.Delay:
                    ticksLeftThisToil--;
                    if (ticksLeftThisToil <= 0) ReadyForNextToil();
                    break;
                case ToilCompleteMode.PatherArrival:
                    if (!pawn.pather.Moving) ReadyForNextToil();
                    break;
                    // Instant already advanced inside ReadyForNextToil; Never waits for an explicit EndJobWith.
            }
        }

        protected void EndJobWith(JobCondition condition) => pawn.jobs.EndCurrentJob(condition);
    }
}
