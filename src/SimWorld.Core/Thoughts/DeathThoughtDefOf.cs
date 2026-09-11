using SimWorld.Defs;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// The memories a death leaves on the people who were there. Its own <c>[DefOf]</c> class rather than an
    /// addition to a shared one, per <c>CLAUDE.md</c>: <c>DefOfHelper</c> binds by scanning every
    /// <c>[DefOf]</c> type, so a new binding never has to touch a file another lane is editing.
    /// </summary>
    [DefOf]
    public static class DeathThoughtDefOf
    {
        /// <summary>Seeing one of your own killed in front of you (<see cref="PawnDiedThoughtsUtility"/>).</summary>
        public static ThoughtDef WitnessedDeathAlly = null!;
    }
}
