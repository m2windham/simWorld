using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// One stage of local map generation (RimWorld: <c>Verse.GenStep</c>), mirroring the shape of
    /// <see cref="global::SimWorld.World.Gen.WorldGenStep"/>. <see cref="Generate"/> runs once per step, in
    /// <see cref="GenStepDef.order"/>, against a seed derived from the map's own seed string — never the
    /// process clock — so the same world tile generated twice always produces the same map.
    /// </summary>
    public abstract class GenStep
    {
        public GenStepDef def = null!;

        public abstract void Generate(MapGenContext ctx);

        /// <summary>A stream seeded from <c>ctx.seedString + "_" + def.defName</c>, hashed via <see cref="GenText.StableStringHash"/>.</summary>
        protected RandomStream SeededStream(MapGenContext ctx) => new RandomStream(GenText.StableStringHash(ctx.seedString + "_" + def.defName));
    }
}
