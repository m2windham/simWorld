using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Needs
{
    /// <summary>
    /// The stat beauty is summed from (RimWorld: <c>RimWorld.StatDefOf.Beauty</c>, which every Thing in its
    /// content carries a <c>statBases</c> entry for). Its own <c>[DefOf]</c> class in its own file rather than
    /// new fields on the shared <c>StatDefOf</c>: binding scans every <c>[DefOf]</c> type, so a new class
    /// costs nothing and cannot collide with a lane mid-edit on the shared one (CLAUDE.md). The StatDef itself
    /// ships in this module's own <c>Stats_Beauty.xml</c>, for the same reason.
    /// </summary>
    [DefOf]
    public static class BeautyStatDefOf
    {
        /// <summary>How much a Thing improves or spoils the look of the cell it stands in. Negative for
        /// filth, positive for art. Zero for everything that has not said otherwise — which is what lets this
        /// stat land without touching a single existing content file.</summary>
        public static StatDef Beauty = null!;
    }

    /// <summary>
    /// The environment-driven <see cref="NeedDef"/>s (RimWorld: the <c>Beauty</c>/<c>Comfort</c>/
    /// <c>Outdoors</c>/<c>RoomSize</c> members of <c>RimWorld.NeedDefOf</c>). A separate class from
    /// <see cref="NeedDefOf"/> rather than four more fields on it, for the reason above; only the one this
    /// module actually drives is bound, since a <c>[DefOf]</c> field that nothing reads is just a load-time
    /// assertion.
    /// </summary>
    [DefOf]
    public static class EnvironmentNeedDefOf
    {
        /// <summary>How pleasant the citizen's surroundings look; driven by
        /// <see cref="MapEnvironmentSampler"/> through <see cref="BeautyUtility"/>.</summary>
        public static NeedDef Beauty = null!;
    }
}
