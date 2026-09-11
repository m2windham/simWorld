namespace SimWorld.Sim
{
    /// <summary>
    /// The one place a periodic job is spread across its interval by the identity of the thing doing it
    /// (RimWorld: <c>Verse.Gen.HashOffset</c> / <c>Gen.IsHashIntervalTick</c>).
    ///
    /// <para/><b>Why this exists as one helper rather than a line at each site.</b> The idiom was written out
    /// three times — on <see cref="Pawns.Pawn"/>, on <c>Building.CompTurretGun</c> and on <c>Things.Fire</c> —
    /// and all three used <c>thingIDNumber * 3</c>. Ids are handed out consecutively, so that offset is an
    /// arithmetic progression with step 3: against any interval divisible by 3 only <c>interval / 3</c>
    /// phases are reachable at all and the things pile three deep onto each of them, which is three times the
    /// intended peak-tick cost. Every busy interval in this codebase is divisible by 3 — the constant think
    /// tree's 30, needs and mental-break checks at 150, the turret scan at 15, fire's complex calcs at 150.
    ///
    /// <para/>Pawn was fixed first and measured (<c>docs/perf/hash-phasing.md</c>: at 500 pawns the peak tick
    /// fell from 41% above the mean to 10% above, with the mean unmoved — phasing moves work between ticks
    /// and never removes any, so a moved mean would have meant the measurement was wrong). The other two were
    /// recorded as a follow-up rather than fixed in place, precisely so the fix would be one helper and not a
    /// third copy of the same arithmetic. This is that helper.
    ///
    /// <para/><b>The hash.</b> RimWorld's exact constants could not be sourced here, so this uses this
    /// codebase's own <see cref="Rand.HashInt"/> — MurmurHash's finaliser, a bijection on the id. The
    /// behaviour that matters is full coverage of the modulus, an even share per phase, and the same answer
    /// for the same id every time; that is what the tests pin, not the constants.
    ///
    /// <para/>The result is masked rather than <c>Math.Abs</c>'d because the hash really can return
    /// <see cref="int.MinValue"/>, and <c>Math.Abs</c> of that throws.
    ///
    /// <para/>Nothing here draws from the random stream: <see cref="Rand.HashInt"/> is a pure function of the
    /// id, not a draw, so a thing's phase is fixed for its whole life and spreading work costs no determinism.
    /// </summary>
    public static class HashInterval
    {
        /// <summary>This id's fixed phase within any interval.</summary>
        public static int OffsetTicks(int thingIDNumber) => Rand.HashInt(thingIDNumber) & int.MaxValue;

        /// <summary>Whether the current tick is this id's turn within <paramref name="interval"/>.</summary>
        public static bool IsHashIntervalTick(int thingIDNumber, int interval) =>
            GenMath.PositiveMod(Find.TickManager.TicksGame + OffsetTicks(thingIDNumber), interval) == 0;
    }
}
