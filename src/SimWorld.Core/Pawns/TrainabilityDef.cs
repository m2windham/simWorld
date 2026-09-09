using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>
    /// How trainable a race is at all (RimWorld: <c>RimWorld.TrainabilityDef</c>): gates which
    /// <see cref="TrainableDef"/>s an animal of this race can ever learn. <see cref="intelligenceOrder"/> is
    /// compared against <see cref="TrainableDef.minTrainability"/>'s own — higher can learn everything a
    /// lower one can, plus more.
    /// </summary>
    public class TrainabilityDef : Def
    {
        public int intelligenceOrder;
    }

    /// <summary>The three trainability tiers content ships (RimWorld's own: None, Intermediate, Advanced).</summary>
    [DefOf]
    public static class TrainabilityDefOf
    {
        public static TrainabilityDef None = null!;
        public static TrainabilityDef Intermediate = null!;
        public static TrainabilityDef Advanced = null!;
    }
}
