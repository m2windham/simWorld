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
    /// Until the map exists the target is the Def's base level.
    /// </summary>
    public class Need_Environment : Need_Seeker
    {
        public Need_Environment(Pawn pawn) : base(pawn)
        {
        }

        public override float CurInstantLevel => pawn.environment?.InstantLevelFor(def, pawn) ?? def.baseLevel;
    }
}
