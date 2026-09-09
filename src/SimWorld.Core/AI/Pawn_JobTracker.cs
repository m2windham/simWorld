using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// A pawn's current job, its driver, and its queue of directed orders (RimWorld: <c>Verse.AI.Pawn_JobTracker</c>).
    /// <see cref="JobTrackerTick"/> is called from <see cref="Pawn.Tick"/> after every other tracker has
    /// updated the pawn's state for this tick (needs, health, mind state, skills, age) — a job's think-tree
    /// choice and its toils' FailOn checks should see this tick's fresh numbers, not last tick's, and nothing
    /// else needs to react to a job started or ended this same tick, so jobs run last.
    /// </summary>
    public sealed class Pawn_JobTracker : IExposable
    {
        private readonly Pawn pawn;

        public Job? curJob;
        public JobDriver? curDriver;

        private readonly List<Job> jobQueue = new List<Job>();

        public Pawn_JobTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        /// <summary>Adds a job to the back of the directed-order queue (RimWorld: <c>Pawn_JobTracker.jobQueue</c>, trimmed).</summary>
        public void QueueJob(Job job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            jobQueue.Add(job);
        }

        /// <summary>Pops the first queued player-forced job, if any (RimWorld: consumed ahead of the think tree there;
        /// here <see cref="JobGiver_DirectedOrder"/> pulls it as one tier of the priority tree — see this module's report).</summary>
        internal Job? DequeueDirectedOrder()
        {
            for (int i = 0; i < jobQueue.Count; i++)
            {
                if (jobQueue[i].playerForced)
                {
                    Job job = jobQueue[i];
                    jobQueue.RemoveAt(i);
                    return job;
                }
            }
            return null;
        }

        /// <summary>Starts <paramref name="newJob"/> immediately, ending whatever was running first.</summary>
        public void StartJob(Job newJob, JobCondition lastJobEndCondition = JobCondition.InterruptForced)
        {
            if (newJob == null) throw new ArgumentNullException(nameof(newJob));
            if (curJob != null)
            {
                EndCurrentJob(lastJobEndCondition, startNewJob: false);
            }

            curJob = newJob;
            curJob.startTick = Find.TickManager.TicksGame;
            curDriver = newJob.def.MakeDriver(pawn, newJob);

            if (!curDriver.TryMakePreToilReservations())
            {
                EndCurrentJob(JobCondition.Incompletable);
                return;
            }
            curDriver.Notify_Starting();
        }

        /// <summary>Ends the current job (a no-op if there is none), releases its reservations, and — unless
        /// <paramref name="startNewJob"/> is false — immediately looks for a new one.</summary>
        public void EndCurrentJob(JobCondition condition, bool startNewJob = true)
        {
            if (curJob == null) return;
            curDriver?.Notify_Ending();
            curJob = null;
            curDriver = null;
            pawn.Map?.reservationManager.ReleaseAllClaimedBy(pawn);
            pawn.pather?.StopDead();
            if (startNewJob) TryFindAndStartJob();
        }

        /// <summary>Runs the pawn's think tree once and starts whatever job it hands back, if any.</summary>
        public void TryFindAndStartJob()
        {
            if (curJob != null || pawn.Dead || pawn.Downed) return;
            ThinkTreeDef? tree = ThinkTreeFor(pawn);
            if (tree?.thinkRoot == null) return;
            ThinkResult result = tree.thinkRoot.TryIssueJobPackage(pawn);
            if (result.IsValid) StartJob(result.Job!);
        }

        /// <summary>Which think tree governs this pawn, chosen by race (system: ai.animals) — a Humanlike
        /// pawn gets the humanlike tree, an Animal pawn its own separate tree (see <c>ThinkTrees_Animal.xml</c>:
        /// a real second tree, not the humanlike one with branches skipped), and anything else (a
        /// <see cref="SimWorld.Pawns.Intelligence.ToolUser"/>, should one ever ship) none at all.</summary>
        private static ThinkTreeDef? ThinkTreeFor(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike) return ThinkTreeDefOf.Humanlike;
            if (pawn.RaceProps.Animal) return ThinkTreeDefOf.Animal;
            return null;
        }

        public void JobTrackerTick()
        {
            pawn.pather?.PatherTick();

            if (curJob == null)
            {
                TryFindAndStartJob();
                return;
            }

            // A job loaded from a save arrives with no driver (see the ExposeData comment below): build one
            // the moment this pawn is actually ticking again, by which point it is properly re-spawned on
            // its map — reconstructing it any earlier (during PostLoadInit) would run before Map's own
            // PostLoadInit re-spawns anything, so a toil reserving its target would find no map at all yet.
            if (curDriver == null)
            {
                curDriver = curJob.def.MakeDriver(pawn, curJob);
                curDriver.Notify_Starting();
                if (curJob == null) return; // Notify_Starting may itself have ended the job.
            }

            if (curJob.Expired(Find.TickManager.TicksGame))
            {
                EndCurrentJob(JobCondition.Incompletable);
                return;
            }
            curDriver!.DriverTick();
        }

        public void ExposeData()
        {
            Job? job = curJob;
            Scribe_Deep.Look(ref job, "curJob");
            curJob = job;

            List<Job>? queue = jobQueue;
            Scribe_Collections.Look(ref queue, "jobQueue", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                jobQueue.Clear();
                if (queue != null) jobQueue.AddRange(queue);
            }

            // curDriver is deliberately left null here — see JobTrackerTick, which builds one lazily on this
            // pawn's first tick after loading rather than during PostLoadInit (too early: the pawn is not
            // yet re-spawned on its map at that point).
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                curDriver = null;
            }
        }
    }
}
