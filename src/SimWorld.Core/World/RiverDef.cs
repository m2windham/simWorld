using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>
    /// A river size class (RimWorld: <c>RimWorld.RiverDef</c>). <c>Gen.WorldGenStep_Rivers</c> picks the
    /// largest <see cref="RiverDef"/> whose <see cref="spawnFlowThreshold"/> the accumulated flow clears.
    /// </summary>
    public class RiverDef : Def
    {
        /// <summary>Below this accumulated flow the river visually tapers to the next smaller class (rendering hint; not enforced by this port's generator).</summary>
        public float degradeThreshold;

        /// <summary>Rendered width, in tile-relative units.</summary>
        public float widthOnWorld = 1f;

        /// <summary>Minimum accumulated flow (merged upstream source paths) for this class to spawn.</summary>
        public float spawnFlowThreshold;
    }
}
