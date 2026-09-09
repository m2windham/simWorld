using System;

namespace SimWorld.Map
{
    /// <summary>
    /// The boundary between two neighbouring <see cref="Region"/>s (RimWorld: <c>Verse.RegionLink</c>) —
    /// the edge <see cref="RegionTraverser"/> crosses during its BFS.
    ///
    /// <b>Simplification</b>: RimWorld keys a link by the exact straight run of shared border cells (an
    /// <c>EdgeSpan</c>), and also creates a link whose far side is <c>null</c> against the map edge or a
    /// solid neighbour (used for caravan-exit-at-edge reasoning and some debug tooling). This port creates
    /// exactly one link per unordered pair of regions that actually touch — a shared border that zigzags
    /// still gets a single link, and a boundary against impassable terrain or the map edge gets none at all.
    /// Both trims are inert for reachability: <see cref="RegionTraverser"/> only ever needs to know *whether*
    /// two regions touch, not the precise geometry of where, and nothing in this port travels off the map
    /// edge yet (a MapGen/World concern, out of this pass's scope).
    /// </summary>
    public sealed class RegionLink
    {
        public readonly Region RegionA;
        public readonly Region RegionB;

        public RegionLink(Region regionA, Region regionB)
        {
            RegionA = regionA ?? throw new ArgumentNullException(nameof(regionA));
            RegionB = regionB ?? throw new ArgumentNullException(nameof(regionB));
        }

        /// <summary>The region on the far side of this link from <paramref name="region"/>, or <c>null</c>
        /// if <paramref name="region"/> is neither side of it.</summary>
        public Region? GetOtherRegion(Region region)
        {
            if (ReferenceEquals(region, RegionA)) return RegionB;
            if (ReferenceEquals(region, RegionB)) return RegionA;
            return null;
        }
    }
}
