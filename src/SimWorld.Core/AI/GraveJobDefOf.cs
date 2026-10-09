using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>Jobs that put the dead away. Its own <c>[DefOf]</c> class rather than another field on the shared
    /// <see cref="JobDefOf"/>, per <c>CLAUDE.md</c>: <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c>
    /// type, so a new binding never touches a file another lane is editing.</summary>
    [DefOf]
    public static class GraveJobDefOf
    {
        /// <summary>Carries a Thing into a building that stores it (RimWorld: <c>JobDefOf.HaulToContainer</c>;
        /// here the storage half only — see <see cref="JobDriver_HaulToContainer"/>).</summary>
        public static JobDef HaulToContainer = null!;
    }
}
