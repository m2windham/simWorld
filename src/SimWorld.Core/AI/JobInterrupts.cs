using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// When taking damage makes a pawn re-ask whether its current job is still the right thing to do
    /// (RimWorld: <c>Verse.AI.CheckJobOverrideOnDamageMode</c>, read off <see cref="JobDef.checkOverrideOnDamage"/>
    /// by <see cref="Pawn_JobTracker.Notify_DamageTaken"/>).
    /// </summary>
    public enum CheckJobOverrideOnDamageMode : byte
    {
        /// <summary>Being hurt never makes this job reconsider itself.</summary>
        Never,

        /// <summary>Reconsider, unless the thing that hurt us is already one of this job's own targets — a
        /// pawn punching a raider should not re-decide every time that raider punches back. RimWorld's
        /// default, and this port's.</summary>
        OnlyIfInstigatorNotJobTarget,

        /// <summary>Reconsider on every hit, whoever landed it.</summary>
        Always,
    }

    /// <summary>
    /// How often a pawn re-examines the world while a job is already running (system 9: AI — interrupts).
    ///
    /// <para/><b>Both numbers are RimWorld's own</b>, not this port's inventions:
    /// <c>Pawn_JobTracker.JobTrackerTick</c> gates its constant-think-tree evaluation behind
    /// <c>pawn.IsHashIntervalTick(30)</c>, and <c>Pawn_JobTracker.Notify_DamageTaken</c> refuses to re-run the
    /// override check more often than every 180 ticks. Neither could be read out of a decompiler from here,
    /// so — per CLAUDE.md — they are pinned by behaviour rather than by literal (<c>JobInterruptTests</c>
    /// asserts that an interrupt arrives within a bounded number of ticks and that a burst of hits does not
    /// cost a think-tree pass each, never that the constants are 30 and 180).
    ///
    /// <para/>What the interval buys, measured, is in <c>docs/perf/constant-think-tree.md</c>: at the Full-tier
    /// budget the every-tick evaluation the brief called "the 1:1 answer" is not in fact what RimWorld does,
    /// and would cost roughly a third of the entire pawn-tick budget; at 30 it is about 1%.
    /// </summary>
    public static class ConstantThinkTreeTuning
    {
        /// <summary>Ticks between constant-think-tree evaluations, staggered across pawns by
        /// <see cref="Pawn.IsHashIntervalTick"/> so the cost spreads evenly over every tick rather than
        /// landing on one in thirty.</summary>
        public const int IntervalTicks = 30;

        /// <summary>Shortest gap between two damage-driven override checks on the same pawn. A burst weapon
        /// lands three hits in six ticks and none of them should buy its own think-tree pass.</summary>
        public const int DamageCheckMinIntervalTicks = 180;

        /// <summary>
        /// Bench-only escape hatch for A/B-measuring what the constant tree costs — the same role
        /// <see cref="PathFinder.DisableRegionCorridor"/> plays for path sharing, and the same rule: nothing
        /// in the simulation core ever assigns it. <c>0</c> disables the constant tree outright, <c>1</c>
        /// runs it every tick; the default is <see cref="IntervalTicks"/>, which is what ships.
        /// See <c>tools/bench/SimWorld.Bench/Suites/ConstantThinkTreeSuite.cs</c>.
        /// </summary>
        public static int MeasuredIntervalTicks = IntervalTicks;
    }

    /// <summary>
    /// The two constant think trees (RimWorld: <c>RaceProperties.thinkTreeConstant</c>, resolved per pawn by
    /// <c>Pawn_Thinker</c>). Its own <c>[DefOf]</c> class rather than two more fields on
    /// <see cref="ThinkTreeDefOf"/>: <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a new
    /// binding never needs an edit to a shared file (CLAUDE.md).
    /// </summary>
    [DefOf]
    public static class ConstantThinkTreeDefOf
    {
        /// <summary>Evaluated every <see cref="ConstantThinkTreeTuning.IntervalTicks"/> for a Humanlike pawn,
        /// whether or not a job is running.</summary>
        public static ThinkTreeDef HumanlikeConstant = null!;

        /// <summary>The animal counterpart — a real second tree, the same way
        /// <see cref="ThinkTreeDefOf.Animal"/> is.</summary>
        public static ThinkTreeDef AnimalConstant = null!;
    }

    /// <summary>
    /// The gate every constant think tree opens with (RimWorld:
    /// <c>Verse.AI.ThinkNode_ConditionalCanDoConstantThinkTreeJobNow</c>): may whatever this pawn is doing
    /// right now be thrown away for something the constant tree noticed?
    ///
    /// <para/><b>This is the whole of "what may interrupt what", and it is deliberately small.</b> The brief
    /// asked for a scheme and pointed at <see cref="Job.playerForced"/>, job priorities and
    /// <see cref="JobCondition"/>; what this port already had is the first and the third, plus think-tree
    /// priority expressed as list order, and that turns out to be enough without inventing anything:
    /// <list type="number">
    /// <item><b>Nothing but danger is in the constant tree at all.</b> The trees in
    /// <c>ThinkTrees_Constant.xml</c> hold the combat tier and nothing else, so "a pawn should not abandon
    /// surgery because someone walked past" needs no rule — someone walking past produces no job, and a tier
    /// that produces no job interrupts nothing. Every non-threat is handled by absence.</item>
    /// <item><b>A job may refuse to be dropped even for danger</b> — <see cref="JobDef.casualInterruptible"/>,
    /// RimWorld's own field, false only where the content says so.</item>
    /// <item><b>A player-forced job is never casually dropped.</b> An order the player gave outranks
    /// something the pawn noticed on its own; the pawn may still abandon it when actually
    /// <i>hurt</i>, because that path (<see cref="Pawn_JobTracker.Notify_DamageTaken"/>) is not this one.</item>
    /// </list>
    /// The second and third are what <see cref="Satisfied"/> tests. Note what is <i>not</i> here: no priority
    /// number, no per-job-def combat exception list. A job that must survive a raid says so once, in content.
    /// </summary>
    public sealed class ThinkNode_ConditionalCanDoConstantThinkTreeJobNow : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            Job? current = pawn.jobs?.curJob;
            if (current == null) return true;
            if (!current.def.casualInterruptible) return false;
            return !current.playerForced;
        }
    }
}
