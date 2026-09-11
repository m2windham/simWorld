using System;
using System.Collections.Generic;
using SimWorld.Health;
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

        /// <summary>When <see cref="Notify_DamageTaken"/> last spent a think-tree pass, throttling the next
        /// one (RimWorld: <c>Pawn_JobTracker.lastDamageCheckTick</c>). Far enough in the past that a pawn's
        /// very first wound always checks.</summary>
        private int lastDamageCheckTick = -99999;

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

        /// <summary>
        /// Which <i>constant</i> tree governs this pawn — the one evaluated whether or not a job is already
        /// running. Chosen by race exactly the way <see cref="ThinkTreeFor"/> chooses the main tree, and for
        /// the same reason (RimWorld reads <c>RaceProperties.thinkTreeConstant</c> off the race def; this
        /// port's two races are answered in code, see <see cref="ThinkTreeFor"/>'s own note).
        /// </summary>
        private static ThinkTreeDef? ConstantThinkTreeFor(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike) return ConstantThinkTreeDefOf.HumanlikeConstant;
            if (pawn.RaceProps.Animal) return ConstantThinkTreeDefOf.AnimalConstant;
            return null;
        }

        /// <summary>
        /// Whether a think-tree result is worth switching to, given what the pawn is already doing (RimWorld:
        /// <c>Pawn_JobTracker.ShouldStartJobFromThinkTree</c>). The same job on the same target is not a
        /// switch, it is a restart — and a restart every
        /// <see cref="ConstantThinkTreeTuning.IntervalTicks"/> would rewind a driver's toils, drop and retake
        /// its reservations, re-path it, and keep resetting <see cref="Job.startTick"/> so a job with an
        /// <see cref="Job.expiryInterval"/> could never expire. This method is the only reason the constant
        /// tree can be allowed to propose an attack job it has already proposed.
        /// </summary>
        private bool ShouldStartJobFromThinkTree(ThinkResult result)
        {
            if (!result.IsValid) return false;
            if (curJob == null) return true;
            return curJob.def != result.Job!.def || curJob.targetA != result.Job.targetA;
        }

        /// <summary>
        /// Re-runs the pawn's main think tree while a job is already running and switches to its answer if
        /// that answer differs (RimWorld: <c>Pawn_JobTracker.CheckForJobOverride</c>). The <i>main</i> tree
        /// and not the constant one on purpose: this is "is what I am doing still the right thing?", which is
        /// the whole tree's question, where the constant tree only ever asks the much narrower "has something
        /// happened that cannot wait?".
        /// </summary>
        public void CheckForJobOverride()
        {
            if (pawn.Dead || pawn.Downed) return;
            ThinkTreeDef? tree = ThinkTreeFor(pawn);
            if (tree?.thinkRoot == null) return;

            ThinkResult result = tree.thinkRoot.TryIssueJobPackage(pawn);
            if (ShouldStartJobFromThinkTree(result))
            {
                StartJob(result.Job!, JobCondition.InterruptForced);
                return;
            }

            // The directed-order tier produces its job by *popping* the queue (see JobGiver_DirectedOrder),
            // so a result this pass declines to start would otherwise be a player order silently thrown away.
            // Put it back at the front, where it was.
            if (result.IsValid && result.SourceNode is JobGiver_DirectedOrder) jobQueue.Insert(0, result.Job!);
        }

        /// <summary>
        /// This pawn was just hurt (RimWorld: <c>Pawn_JobTracker.Notify_DamageTaken</c>, reached from
        /// <see cref="Pawn_HealthTracker.PostApplyDamage"/>). Two things happen, and they are independent:
        /// <list type="number">
        /// <item><b>Being hurt wakes you.</b> Unconditional, ahead of every gate below — RimWorld's
        /// <c>RestUtility.WakeUp</c>. Whether the pawn then has anything useful to do about it is the think
        /// tree's problem; ending the sleep is not negotiable, and it is the case this whole module exists
        /// for. <see cref="JobDriver_LayDown"/> notices the flag and ends itself.</item>
        /// <item><b>Being hurt re-asks the think tree</b>, if this job's <see cref="JobDef.checkOverrideOnDamage"/>
        /// says so and the throttle allows it.</item>
        /// </list>
        /// Nothing here runs for an Interval or Statistical citizen, and the tier check is the first thing
        /// past the death check for that reason. Damage is the one path into this class that does <i>not</i>
        /// come from the tick loop — a fire, a collapsing roof or a trap reaches a citizen at any tier — and
        /// a coarse-tier citizen has no jobs at all (<c>Pawn.Tick</c> never reaches
        /// <see cref="JobTrackerTick"/> for them), so waking one or starting it a job would create state
        /// nothing would ever tick again.
        /// </summary>
        public void Notify_DamageTaken(DamageInfo dinfo)
        {
            if (dinfo == null) throw new ArgumentNullException(nameof(dinfo));
            if (pawn.Dead || pawn.Downed) return;
            if (pawn.tier != null && pawn.tier.Tier != PawnTier.Full) return;

            pawn.Asleep = false;

            if (curJob == null) return;
            CheckJobOverrideOnDamageMode mode = curJob.def.checkOverrideOnDamage;
            if (mode == CheckJobOverrideOnDamageMode.Never) return;
            if (mode == CheckJobOverrideOnDamageMode.OnlyIfInstigatorNotJobTarget && AnyTargetIs(curJob, dinfo.Instigator)) return;

            int now = Find.TickManager.TicksGame;
            if (now - lastDamageCheckTick < ConstantThinkTreeTuning.DamageCheckMinIntervalTicks) return;
            lastDamageCheckTick = now;
            CheckForJobOverride();
        }

        /// <summary>Whether <paramref name="instigator"/> is already one of this job's targets (RimWorld:
        /// <c>Job.AnyTargetIs</c>; kept here rather than on <see cref="Job"/> so an additive change needs no
        /// edit to a file three other lanes are in — CLAUDE.md). <see cref="DamageInfo.Instigator"/> is an
        /// untyped <c>object</c>, so this compares by reference and never casts.</summary>
        private static bool AnyTargetIs(Job job, object? instigator)
        {
            if (instigator == null) return false;
            return ReferenceEquals(job.targetA.Thing, instigator)
                || ReferenceEquals(job.targetB.Thing, instigator)
                || ReferenceEquals(job.targetC.Thing, instigator);
        }

        /// <summary>
        /// Evaluates the constant think tree, which — unlike the main one — runs whether or not a job is
        /// already going, and may end the one that is (RimWorld: the <c>IsHashIntervalTick(30)</c> block at
        /// the top of <c>Pawn_JobTracker.JobTrackerTick</c>).
        ///
        /// <para/><b>This is the half of the think tree that was missing.</b> Everything the AI layer knew how
        /// to decide, it could only decide at the moment a pawn had nothing to do, so a citizen hauling a rock
        /// kept hauling through a raid and a sleeping one slept through being shot. The cost of fixing that is
        /// one tree evaluation per pawn per <see cref="ConstantThinkTreeTuning.IntervalTicks"/> ticks,
        /// measured in <c>docs/perf/constant-think-tree.md</c>.
        /// </summary>
        private void ConstantThinkTreeTick()
        {
            int interval = ConstantThinkTreeTuning.IntervalTicksOverride;
            if (interval <= 0 || !pawn.IsHashIntervalTick(interval)) return;
            if (pawn.Dead || pawn.Downed) return;

            ThinkTreeDef? tree = ConstantThinkTreeFor(pawn);
            if (tree?.thinkRoot == null) return;

            ThinkResult result = tree.thinkRoot.TryIssueJobPackage(pawn);
            if (ShouldStartJobFromThinkTree(result)) StartJob(result.Job!, JobCondition.InterruptForced);
        }

        public void JobTrackerTick()
        {
            pawn.pather?.PatherTick();

            // Before the driver, and before the "have I got a job?" question below: the point of the constant
            // tree is that neither of those gates it.
            ConstantThinkTreeTick();

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

            Scribe_Values.Look(ref lastDamageCheckTick, "lastDamageCheckTick", -99999);

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
