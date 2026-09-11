using SimWorld.Defs;

namespace SimWorld.Letters
{
    /// <summary>
    /// The letter a death arrives as. Its own <c>[DefOf]</c> class rather than a line added to
    /// <see cref="LetterDefOf"/>, per <c>CLAUDE.md</c>'s "add a file rather than edit a shared one" —
    /// <c>DefOfHelper</c> binds every <c>[DefOf]</c> type it can find, so the binding works the same and
    /// cannot collide with another lane's edit.
    ///
    /// <para/>The <c>Death</c> LetterDef itself is not new: it has shipped in <c>Letters.xml</c> since the
    /// letters module landed, with nothing bound to it and nothing raising it.
    /// </summary>
    [DefOf]
    public static class DeathLetterDefOf
    {
        /// <summary>Someone has died (<see cref="Director.StorytellerDeathEvents"/>).</summary>
        public static LetterDef Death = null!;
    }
}
