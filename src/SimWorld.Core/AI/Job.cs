using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// One thing a pawn is doing (RimWorld: <c>Verse.AI.Job</c>): a <see cref="JobDef"/> plus up to three
    /// targets, each of which may also carry a <i>queue</i> of further targets the driver works through one
    /// at a time. A <see cref="JobDriver"/> (constructed from <c>def.driverClass</c>) is what actually runs it.
    /// </summary>
    public class Job : IExposable
    {
        public JobDef def = null!;

        public LocalTargetInfo targetA = LocalTargetInfo.Invalid;
        public LocalTargetInfo targetB = LocalTargetInfo.Invalid;
        public LocalTargetInfo targetC = LocalTargetInfo.Invalid;

        /// <summary>
        /// Further targets for slot A, beyond <see cref="targetA"/> itself (RimWorld: <c>Job.targetQueueA</c>).
        /// Null until something queues into it; reach it through <see cref="GetTargetQueue"/>, which creates it
        /// on demand exactly as RimWorld's does.
        /// </summary>
        public List<LocalTargetInfo>? targetQueueA;

        /// <summary>Further targets for slot B (RimWorld: <c>Job.targetQueueB</c>). A bill's chosen ingredient
        /// stacks travel here, one entry per stack, so the driver can reserve and then spend those exact
        /// Things rather than re-deciding at the finish line.</summary>
        public List<LocalTargetInfo>? targetQueueB;

        /// <summary>How much of each entry in a target queue this job wants, parallel to it (RimWorld:
        /// <c>Job.countQueue</c>). Shorter than the queue, or null, means "no count for that entry".</summary>
        public List<int>? countQueue;

        /// <summary>Item count this job cares about (a haul/ingest amount); -1 = not applicable.</summary>
        public int count = -1;

        /// <summary>What to do with a hauled payload; unused until hauling exists (see <see cref="HaulMode"/>).</summary>
        public HaulMode haulMode = HaulMode.Undefined;

        /// <summary>True for a job the player explicitly ordered rather than one the think tree chose.</summary>
        public bool playerForced;

        /// <summary>Ticks after which this job auto-expires; -1 = never.</summary>
        public int expiryInterval = -1;

        /// <summary><see cref="Sim.TickManager.TicksGame"/> when this job started running; -1 until then.</summary>
        public int startTick = -1;

        public Job()
        {
        }

        public Job(JobDef def, LocalTargetInfo targetA = default, LocalTargetInfo targetB = default, LocalTargetInfo targetC = default)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.targetA = targetA;
            this.targetB = targetB;
            this.targetC = targetC;
        }

        public LocalTargetInfo GetTarget(TargetIndex ind)
        {
            switch (ind)
            {
                case TargetIndex.A: return targetA;
                case TargetIndex.B: return targetB;
                default: return targetC;
            }
        }

        public void SetTarget(TargetIndex ind, LocalTargetInfo target)
        {
            switch (ind)
            {
                case TargetIndex.A: targetA = target; break;
                case TargetIndex.B: targetB = target; break;
                default: targetC = target; break;
            }
        }

        /// <summary>
        /// This slot's queue of further targets, created empty on first use (RimWorld:
        /// <c>Job.GetTargetQueue</c>). Only A and B have one — RimWorld logs an error and returns null for C,
        /// which under nullable reference types would hand every caller a null to guard; this throws instead,
        /// since asking for queue C is a coding mistake and never a runtime condition.
        /// </summary>
        public List<LocalTargetInfo> GetTargetQueue(TargetIndex ind)
        {
            switch (ind)
            {
                case TargetIndex.A: return targetQueueA ??= new List<LocalTargetInfo>();
                case TargetIndex.B: return targetQueueB ??= new List<LocalTargetInfo>();
                default: throw new ArgumentException("Job target queue C doesn't exist.", nameof(ind));
            }
        }

        /// <summary>Appends to the named slot's queue (RimWorld: <c>Job.AddQueuedTarget</c>).</summary>
        public void AddQueuedTarget(TargetIndex ind, LocalTargetInfo target) => GetTargetQueue(ind).Add(target);

        /// <summary>True once <see cref="expiryInterval"/> ticks have passed since <see cref="startTick"/>.</summary>
        public bool Expired(int nowTicks) => expiryInterval >= 0 && startTick >= 0 && nowTicks - startTick >= expiryInterval;

        public void ExposeData()
        {
            JobDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;

            Scribe_Targets.Look(ref targetA, "targetA");
            Scribe_Targets.Look(ref targetB, "targetB");
            Scribe_Targets.Look(ref targetC, "targetC");

            LookTargetQueue(ref targetQueueA, "targetQueueA");
            LookTargetQueue(ref targetQueueB, "targetQueueB");
            List<int>? counts = countQueue;
            Scribe_Collections.Look(ref counts, "countQueue", LookMode.Value);
            countQueue = counts;

            Scribe_Values.Look(ref count, "count", -1);
            Scribe_Values.Look(ref haulMode, "haulMode", HaulMode.Undefined);
            Scribe_Values.Look(ref playerForced, "playerForced");
            Scribe_Values.Look(ref expiryInterval, "expiryInterval", -1);
            Scribe_Values.Look(ref startTick, "startTick", -1);
        }

        /// <summary>
        /// Saves one target queue as a count plus an indexed run of <see cref="Scribe_Targets"/> entries.
        /// <b>Deviation:</b> RimWorld hands the whole list to <c>Scribe_Collections</c> under
        /// <c>LookMode.LocalTargetInfo</c>, a look mode this port's <see cref="LookMode"/> does not have.
        /// Adding one would mean teaching <see cref="Scribe_Collections"/>, <see cref="ScribeExtractor"/> and
        /// the enum itself about <see cref="LocalTargetInfo"/> — three files at the foundation of every other
        /// system's saving — to serve one field, so the indexing lives here instead. Same document shape a
        /// per-element <see cref="Scribe_Targets"/> call always produces; the label suffix is what keeps each
        /// element's Thing reference distinct in the loader's cross-ref bank.
        /// </summary>
        private static void LookTargetQueue(ref List<LocalTargetInfo>? queue, string label)
        {
            int savedCount = queue?.Count ?? -1;
            Scribe_Values.Look(ref savedCount, label + "Count", -1);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (savedCount < 0)
                {
                    queue = null;
                    return;
                }
                queue = new List<LocalTargetInfo>(savedCount);
                for (int i = 0; i < savedCount; i++) queue.Add(LocalTargetInfo.Invalid);
            }

            if (queue == null) return;
            for (int i = 0; i < queue.Count; i++)
            {
                LocalTargetInfo target = queue[i];
                Scribe_Targets.Look(ref target, label + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                queue[i] = target;
            }
        }

        public override string ToString() => def?.defName ?? "(no def)";
    }
}
