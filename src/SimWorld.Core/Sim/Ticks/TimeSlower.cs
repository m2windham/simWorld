namespace SimWorld.Sim
{
    /// <summary>
    /// Temporarily forces normal speed after a danger signal so the player sees it
    /// (RimWorld: <c>Verse.TimeSlower</c>).
    /// </summary>
    public sealed class TimeSlower
    {
        public const int ForceNormalSpeedTicks = 800;
        public const int ForceNormalSpeedShortTicks = 240;

        private int forceNormalSpeedUntil;

        public bool ForcedNormalSpeed(int ticksGame) => ticksGame < forceNormalSpeedUntil;

        public void SignalForceNormalSpeed(int ticksGame)
        {
            int until = ticksGame + ForceNormalSpeedTicks;
            if (until > forceNormalSpeedUntil) forceNormalSpeedUntil = until;
        }

        public void SignalForceNormalSpeedShort(int ticksGame)
        {
            int until = ticksGame + ForceNormalSpeedShortTicks;
            if (until > forceNormalSpeedUntil) forceNormalSpeedUntil = until;
        }

        public void Reset() => forceNormalSpeedUntil = 0;
    }
}
