using SimWorld.Defs;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// The memory a death leaves on people who were not there. Its own <c>[DefOf]</c> class beside
    /// <see cref="DeathThoughtDefOf"/> rather than a field added to it, per <c>CLAUDE.md</c>.
    /// </summary>
    [DefOf]
    public static class BereavementThoughtDefOf
    {
        /// <summary>One of ours is dead and you heard about it (<see cref="BereavementUtility"/>).</summary>
        public static ThoughtDef KnowColonistDied = null!;
    }
}
