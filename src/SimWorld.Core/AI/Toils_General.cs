using System;

namespace SimWorld.AI
{
    /// <summary>Toils with no target of their own — waiting, running a plain callback (RimWorld: <c>Verse.AI.Toils_General</c>).</summary>
    public static class Toils_General
    {
        /// <summary>Waits <paramref name="ticks"/> ticks, then advances.</summary>
        public static Toil Wait(int ticks)
        {
            return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = ticks,
            };
        }

        /// <summary>Runs <paramref name="action"/> once, then advances immediately.</summary>
        public static Toil Do(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = action,
            };
        }
    }
}
