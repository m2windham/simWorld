using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Thoughts;

namespace SimWorld.Needs
{
    /// <summary>
    /// A need whose level chases a target computed from the pawn's situation
    /// (RimWorld: <c>RimWorld.Need_Seeker</c>): mood chases the thought total, beauty chases the surroundings.
    /// Rise/fall speeds are per hour on the Def; one interval is 150/2500 of an hour.
    /// </summary>
    public abstract class Need_Seeker : Need
    {
        private const float IntervalHourFraction = (float)IntervalTicks / GenDate.TicksPerHour;

        protected Need_Seeker(Pawn pawn) : base(pawn)
        {
        }

        public override void NeedInterval()
        {
            if (IsFrozen) return;
            float target = CurInstantLevel;
            if (target > CurLevel)
            {
                CurLevel = System.Math.Min(CurLevel + def.seekerRisePerHour * IntervalHourFraction, target);
            }
            else if (target < CurLevel)
            {
                CurLevel = System.Math.Max(CurLevel - def.seekerFallPerHour * IntervalHourFraction, target);
            }
        }

        /// <summary>O(1) bulk equivalent (see the base class doc): holds the current instant target constant
        /// across the whole elapsed span rather than re-sampling it every slice — exact for a target that does
        /// not move (the common case until the map/thoughts systems drive one), an approximation otherwise.</summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0 || IsFrozen) return;
            float target = CurInstantLevel;
            float elapsedHourFraction = (float)elapsedTicks / GenDate.TicksPerHour;
            if (target > CurLevel)
            {
                CurLevel = System.Math.Min(CurLevel + def.seekerRisePerHour * elapsedHourFraction, target);
            }
            else if (target < CurLevel)
            {
                CurLevel = System.Math.Max(CurLevel - def.seekerFallPerHour * elapsedHourFraction, target);
            }
        }
    }

    /// <summary>
    /// Mood (RimWorld: <c>RimWorld.Need_Mood</c>): the seeker whose target is 50% plus the summed thought
    /// offsets. Mental breaks read <see cref="Need.CurLevel"/>.
    /// </summary>
    public class Need_Mood : Need_Seeker
    {
        public ThoughtHandler thoughts;

        public Need_Mood(Pawn pawn) : base(pawn)
        {
            thoughts = new ThoughtHandler(pawn);
        }

        public override float CurInstantLevel => GenMath.Clamp01(0.5f + thoughts.TotalMoodOffset() / 100f);

        public override void NeedInterval()
        {
            base.NeedInterval();
            thoughts.ThoughtInterval();
        }

        /// <summary>O(1) bulk equivalent (see the base class doc): ages/expires memories through one
        /// <see cref="Thoughts.ThoughtHandler.ThoughtInterval"/> pass rather than one per slice, so a memory
        /// held through a long Interval-tier span expires more slowly than true per-tick simulation would — a
        /// documented imprecision (mood memories fading a bit late), not a magnitude-changing one, and mood
        /// itself is only ever sampled (not bulk-decayed) once a citizen falls all the way to Statistical.</summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            base.NeedIntervalBulk(elapsedTicks);
            if (elapsedTicks > 0) thoughts.ThoughtInterval();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ThoughtHandler? t = thoughts;
            Scribe_Deep.Look(ref t, "thoughts", pawn);
            thoughts = t ?? new ThoughtHandler(pawn);
        }
    }

    /// <summary>
    /// Beauty, comfort, outdoors, room size (RimWorld: <c>Need_Beauty</c>, <c>Need_Comfort</c>,
    /// <c>Need_Outdoors</c>, <c>Need_RoomSize</c>): seekers whose target is sampled from the surroundings.
    /// </summary>
    public class Need_Environment : Need_Seeker
    {
        public Need_Environment(Pawn pawn) : base(pawn)
        {
        }

        /// <summary>
        /// The level the surroundings ask for, in three steps.
        /// <list type="number">
        /// <item><description>A sampler somebody set on this pawn wins outright. That is the seam as it was
        /// built — a host, a test, or a future system substituting its own reading — and it keeps whatever
        /// cost policy its owner chose, tier included.</description></item>
        /// <item><description>Otherwise, below <see cref="PawnTier.Full"/> the target is the level the need is
        /// already at, so the need holds rather than drifting. <b>This is the one place tiering touches this
        /// need and it is deliberate:</b> Interval citizens advance their needs in bulk on the Long bucket
        /// (<see cref="Pawns.Pawn_TierTracker.CoarseTick"/> calling <see cref="Need.NeedIntervalBulk"/>), and
        /// an environment need advanced there would pay for a full map sample per citizen per coarse tick —
        /// the one need whose target cannot be extrapolated from its own state. Statistical citizens never ask
        /// at all (their needs are cohort-sampled outright). Holding costs nothing, says nothing false about
        /// surroundings nobody is simulating, and resumes the instant the citizen is promoted back to
        /// Full.</description></item>
        /// <item><description>Otherwise the map answers, through <see cref="MapEnvironmentSampler"/> — which
        /// returns null for a citizen who is not spawned and for the three environment needs nothing drives
        /// yet, leaving those exactly at the Def's base level as before.</description></item>
        /// </list>
        /// </summary>
        public override float CurInstantLevel
        {
            get
            {
                if (pawn.environment != null) return pawn.environment.InstantLevelFor(def, pawn) ?? def.baseLevel;
                if (pawn.tier != null && pawn.tier.Tier != PawnTier.Full) return CurLevel;
                return MapEnvironmentSampler.Instance.InstantLevelFor(def, pawn) ?? def.baseLevel;
            }
        }
    }
}
