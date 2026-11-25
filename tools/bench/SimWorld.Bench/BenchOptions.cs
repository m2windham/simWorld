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

        /// <summary>Names to switch off for --suite probe's ablated arm (see <c>SimWorld.Sim.Ablation</c>).
        /// Empty means the probe reports a single baseline instead of a subtraction.</summary>
        public string[] Without { get; set; } = System.Array.Empty<string>();

        /// <summary>Whether --suite tension starts the player alone and lets rivals emerge (the default, and
        /// the same world shape --suite probe uses), or generates a world already full of rival civilizations.
        /// It is a switch rather than a fixed choice because the two answer different halves of the same
        /// question and the crowded one has a measured, reproducible outcome worth keeping reachable — see
        /// that suite's own doc.</summary>
        public bool Solo { get; set; } = true;

        /// <summary>Founding band size for --suite tension. Separate from <see cref="Pawns"/>, which means
        /// something else in every other suite and defaults to a value no founding band should have. The
        /// default is the 25 --suite probe also founds with; that band does not survive a long unwatched run,
        /// which is itself one of that suite's findings, so the size is a knob rather than a constant.</summary>
        public int Band { get; set; } = 25;

        /// <summary>Whether --suite storyteller lets the storyteller's threats fire (the default), or runs the
        /// same world under a difficulty that is Medium in every other respect and allows neither big nor
        /// intro threats. The second is the baseline a threat reading is read against: what the settlement
        /// does to itself over the same days with nobody attacking it.</summary>
        public bool Threats { get; set; } = true;

        /// <summary>--suite layout: the new-game seed string (the host's own by default), how many ticks to
        /// run, and how often to print a brief interim line (0: never).</summary>
        public string LayoutSeed { get; set; } = Suites.LayoutSuite.HostSeed;

        public int LayoutTicks { get; set; } = 60000;

        public int LayoutEvery { get; set; }

        /// <summary>--suite layout: read the god view and the map view every this many ticks, as the host's
        /// renderer does every frame (0, never).</summary>
        public int LayoutViewEvery { get; set; }

        /// <summary>--suite layout: print <see cref="SimWorld.Sim.SimDigest"/> every this many ticks (0, never), to
        /// compare a run line by line with the host's.</summary>
        public int LayoutDigestEvery { get; set; }

        /// <summary>--suite layout: leave the map id counter as it stands, as the host does.</summary>
        public bool LayoutHostCounters { get; set; }

        /// <summary>--suite layout: run the layout this many times in one process, like pressing Play again in
        /// an Editor that does not reload its domain (default 1).</summary>
        public int LayoutPlays { get; set; } = 1;

        /// <summary>--suite arc: days between samples (default 60, one game year).</summary>
        public int ArcEvery { get; set; } = 60;

        /// <summary>--suite arc: name every death, and follow every citizen who took hypothermia
        /// (<see cref="Suites.ExposureWatch"/>). Read-only: the run is the same run without it.</summary>
        public bool DeathDetail { get; set; }

        public double GuardMs => GuardSeconds * 1000.0;
    }
}
