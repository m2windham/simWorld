namespace SimWorld.Bench
{
    /// <summary>Parsed CLI options shared by every suite.</summary>
    internal sealed class BenchOptions
    {
        public string Suite { get; set; } = "tick";
        public int Pawns { get; set; } = 1000;
        public int Days { get; set; } = 1;
        public int Seed { get; set; } = 12345;
        public int Warmup { get; set; } = 1;
        public int Runs { get; set; } = 3;
        public double GuardSeconds { get; set; } = 120.0;
        public int[] ScalingNs { get; set; } = { 100, 250, 500, 1000, 2500, 5000, 10000 };
        public int[] Subdivisions { get; set; } = { 3, 4, 5, 6 };
        public int[] PathingNs { get; set; } = { 100, 400, 1000 };

        /// <summary>Ticks per trial for the constant-think-tree A/B (--suite interrupts). A tenth of an
        /// in-game day: the A/B is a ratio between three cadences over the same span, so the span only has to
        /// be long enough to be steady and short enough that three cadences x (warmup + runs) trials fit
        /// inside the guard at the Full-tier budget.</summary>
        public int ConstantTreeTicks { get; set; } = 6000;

        /// <summary>Repetitions of the 600-tick hash cycle per trial for --suite phasing. The suite folds tick
        /// t into bucket t % 600 and averages, so this is the sample count behind every bucket: enough
        /// repetitions and the periodic signal separates from per-tick timer noise. 10 is 6,000 ticks, the
        /// same span the constant-tree A/B uses.</summary>
        public int PhasingCycles { get; set; } = 10;
        /// <summary>Population sweep for --suite targets: what finding an enemy costs as N moves. Straddles
        /// TieringTuning.FullTierBudget (500) on both sides, because the claim under test is about the shape
        /// of the curve and a single point has no shape.</summary>
        public int[] TargetNs { get; set; } = { 100, 250, 500, 1000, 2000 };

        /// <summary>Population sweep for --suite targets' whole-tick-loop A/B. A shorter list than
        /// <see cref="TargetNs"/>: a tick-loop trial costs seconds where an isolated scan costs
        /// milliseconds.</summary>
        public int[] TargetTickNs { get; set; } = { 250, 500, 1000 };

        public double GuardMs => GuardSeconds * 1000.0;
    }
}
