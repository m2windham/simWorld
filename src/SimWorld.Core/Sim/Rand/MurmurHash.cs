namespace SimWorld.Sim
{
    /// <summary>
    /// 32-bit MurmurHash3 finaliser over one 4-byte block (RimWorld: <c>Verse.MurmurHash</c>).
    /// Every random number in the sim is <c>GetInt(seed, iteration)</c>: stateless, so a stream can be
    /// re-entered at any iteration and replayed exactly on any machine.
    /// </summary>
    public static class MurmurHash
    {
        private const uint Const1 = 3432918353u;
        private const uint Const2 = 461845907u;
        private const uint Const3 = 3864292196u;
        private const uint Const4Mix = 2246822507u;
        private const uint Const5Mix = 3266489909u;
        private const uint Const6StreamPosition = 2834544218u;

        public static int GetInt(uint seed, uint input)
        {
            uint k = input;
            k *= Const1;
            k = (k << 15) | (k >> 17);
            k *= Const2;

            uint h = seed;
            h ^= k;
            h = (h << 13) | (h >> 19);
            h = h * 5 + Const3;

            h ^= Const6StreamPosition;
            h ^= h >> 16;
            h *= Const4Mix;
            h ^= h >> 13;
            h *= Const5Mix;
            h ^= h >> 16;
            return (int)h;
        }

        /// <summary>Hashes several ints into one seed, order-sensitive.</summary>
        public static int Combine(int a, int b) => GetInt((uint)a, (uint)b);

        public static int Combine(int a, int b, int c) => GetInt((uint)GetInt((uint)a, (uint)b), (uint)c);
    }
}
