using SimWorld.Defs;
using SimWorld.Map;

namespace SimWorld.World
{
    /// <summary>
    /// A road quality class connecting settlements (RimWorld: <c>RimWorld.RoadDef</c>).
    /// <c>Gen.WorldGenStep_Roads</c> assigns one per generated route by the settling factions' tech level, and
    /// treats the whole <see cref="DefDatabase{T}"/> of these generically — never a hardcoded defName — so
    /// <c>MapGen.GenStep_Roads</c> (which paints <see cref="localTerrain"/> onto a settlement's interior map)
    /// stays just as generic over however many road classes content defines, rather than a switch statement
    /// that silently drops a class content adds later.
    /// </summary>
    public class RoadDef : Def
    {
        /// <summary>When two road classes could both occupy an edge, the higher priority wins.</summary>
        public float priority;

        /// <summary>Multiplies (reduces) travel cost across a tile this road crosses; lower is faster.</summary>
        public float movementCostMultiplier = 1f;

        /// <summary>Extra cost discount when consecutive world-tile transitions share this same road (RimWorld: rewards staying on one road instead of hopping between routes).</summary>
        public float worldTransitionSameRoadPriority;

        /// <summary>
        /// Lowest tech level a settling faction needs before <c>Gen.WorldGenStep_Roads</c> will build this
        /// class between its settlements. Not a RimWorld field (RimWorld's roads are hand-placed or
        /// ancient-ruin decoration, never tech-gated at generation) — this port's own hook for letting more
        /// advanced rival civilizations build better roads.
        /// </summary>
        public TechLevel minTechLevel = TechLevel.Animal;

        /// <summary>
        /// The local ground this road class becomes when <c>MapGen.GenStep_Roads</c> carries a world-tile
        /// road onto a settlement's interior map as a street (spec §5, §11.2's seam — "a street of the
        /// road's own terrain"). A bare defName reference, resolved after all content loads like every other
        /// Def cross-reference (CLAUDE.md's "Def references are bare defName strings"). Every shipped road
        /// class sets one; a mod that adds a road class without one gets <c>GenStep_Roads</c> drawing nothing
        /// for it rather than a fabricated terrain.
        /// </summary>
        public TerrainDef? localTerrain;
    }
}
