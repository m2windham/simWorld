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

        public double GuardMs => GuardSeconds * 1000.0;
    }
}
