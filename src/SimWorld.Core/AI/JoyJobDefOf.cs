using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>
    /// The recreation JobDefs, bound from content. Its own class rather than three more fields on
    /// <see cref="JobDefOf"/>, per CLAUDE.md's rule about shared files — <c>DefOfHelper</c> binds by scanning
    /// every <c>[DefOf]</c> type, so a binding in a new file works exactly as well as one appended to an old
    /// one and cannot collide with another lane.
    /// </summary>
    [DefOf]
    public static class JoyJobDefOf
    {
        /// <summary>Look at the sky (RimWorld: <c>JobDefOf.Skygaze</c>).</summary>
        public static JobDef Skygaze = null!;

        /// <summary>Walk for the pleasure of it (RimWorld: <c>JobDefOf.GoForWalk</c>).</summary>
        public static JobDef GoForWalk = null!;

        /// <summary>Sit with somebody (RimWorld: <c>JobDefOf.SocialRelax</c>).</summary>
        public static JobDef SocialRelax = null!;
    }
}
