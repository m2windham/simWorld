using SimWorld.Sim;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// One stage of world generation (RimWorld: <c>Verse.WorldGenStep</c>). <see cref="GenerateFresh"/> is
    /// called once per step, in <see cref="WorldGenStepDef.order"/>, with the *world* seed — never the
    /// process clock, so any two runs with the same seed produce the same world. Each step derives its own
    /// independent <see cref="RandomStream"/> via <see cref="SeededStream"/> so a change to one step's random
    /// draws never perturbs another step's sequence.
    /// </summary>
    public abstract class WorldGenStep
    {
        public WorldGenStepDef def = null!;

        public abstract void GenerateFresh(string seed, World world);

        /// <summary>A stream seeded from <c>seed + "_" + def.defName</c>, hashed via <see cref="GenText.StableStringHash"/>.</summary>
        protected RandomStream SeededStream(string seed) => new RandomStream(GenText.StableStringHash(seed + "_" + def.defName));
    }
}
