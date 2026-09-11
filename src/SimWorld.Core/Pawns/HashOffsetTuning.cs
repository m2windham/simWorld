namespace SimWorld.Pawns
{
    /// <summary>
    /// The knob that lets the bench put <see cref="Pawn.HashOffsetTicks"/> back the way it was, so the cost of
    /// the phasing fix is a measurement rather than an assertion (<c>docs/perf/hash-phasing.md</c>).
    /// </summary>
    public static class HashOffsetTuning
    {
        /// <summary>
        /// Bench-only escape hatch for A/B-measuring the hash-interval phasing — the same role
        /// <see cref="AI.ConstantThinkTreeTuning.IntervalTicksOverride"/> and
        /// <see cref="AI.PathFinder.DisableRegionCorridor"/> play for their passes, and the same rule: nothing
        /// in the simulation core ever assigns it. <c>true</c> reinstates the <c>thingIDNumber * 3</c> offset
        /// that clustered every pawn onto a third of the phases of any interval divisible by 3; the default,
        /// <c>false</c>, is the hashed offset that ships.
        /// See <c>tools/bench/SimWorld.Bench/Suites/HashPhasingSuite.cs</c>.
        /// </summary>
        public static bool UseLegacyMultiplyOffset;

        /// <summary>The offset as it stood before the fix, kept here rather than in <see cref="Pawn"/> so the
        /// shipping path reads as one expression.</summary>
        public static int LegacyOffsetTicks(int thingIDNumber) => thingIDNumber * 3;
    }
}
