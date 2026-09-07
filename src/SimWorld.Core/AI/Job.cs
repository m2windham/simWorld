using System;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// One thing a pawn is doing (RimWorld: <c>Verse.AI.Job</c>): a <see cref="JobDef"/> plus up to three
    /// targets. A <see cref="JobDriver"/> (constructed from <c>def.driverClass</c>) is what actually runs it.
    /// </summary>
    public class Job : IExposable
    {
        public JobDef def = null!;

        public LocalTargetInfo targetA = LocalTargetInfo.Invalid;
        public LocalTargetInfo targetB = LocalTargetInfo.Invalid;
        public LocalTargetInfo targetC = LocalTargetInfo.Invalid;

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

            Scribe_Values.Look(ref count, "count", -1);
            Scribe_Values.Look(ref haulMode, "haulMode", HaulMode.Undefined);
            Scribe_Values.Look(ref playerForced, "playerForced");
            Scribe_Values.Look(ref expiryInterval, "expiryInterval", -1);
            Scribe_Values.Look(ref startTick, "startTick", -1);
        }

        public override string ToString() => def?.defName ?? "(no def)";
    }
}
