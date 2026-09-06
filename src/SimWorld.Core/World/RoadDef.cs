using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>
    /// A road quality class connecting settlements (RimWorld: <c>RimWorld.RoadDef</c>).
    /// <c>Gen.WorldGenStep_Roads</c> assigns one per generated route by the settling factions' tech level.
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
    }
}
