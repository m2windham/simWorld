using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Everything world generation needs to reproduce a planet deterministically (RimWorld:
    /// <c>RimWorld.Planet.WorldInfo</c>). Saved in full so <see cref="World.RegenerateGrid"/> can rebuild
    /// an identical <see cref="WorldGrid"/> and tile data on load without ever storing the tile arrays
    /// themselves (see <see cref="World.ExposeData"/>).
    /// </summary>
    public class WorldInfo : IExposable
    {
        public string name = "World";

        /// <summary>User-facing seed text; hashed by <see cref="GenText.StableStringHash"/> into <see cref="seed"/>.</summary>
        public string seedString = "";

        public int seed;

        /// <summary>0..1 fraction of the planet generated. Maps to <see cref="subdivisionLevel"/> at generation time.</summary>
        public float planetCoverage = 0.3f;

        public OverallRainfall overallRainfall = OverallRainfall.Normal;
        public OverallTemperature overallTemperature = OverallTemperature.Normal;
        public OverallPopulation overallPopulation = OverallPopulation.Normal;

        /// <summary>The icosphere subdivision level actually used; saved verbatim so a reload reproduces the exact same grid regardless of any later change to the coverage→subdivision mapping.</summary>
        public int subdivisionLevel = 6;

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "World");
            Scribe_Values.Look(ref seedString, "seedString", "");
            Scribe_Values.Look(ref seed, "seed", 0);
            Scribe_Values.Look(ref planetCoverage, "planetCoverage", 0.3f);
            Scribe_Values.Look(ref overallRainfall, "overallRainfall", OverallRainfall.Normal);
            Scribe_Values.Look(ref overallTemperature, "overallTemperature", OverallTemperature.Normal);
            Scribe_Values.Look(ref overallPopulation, "overallPopulation", OverallPopulation.Normal);
            Scribe_Values.Look(ref subdivisionLevel, "subdivisionLevel", 6);
        }
    }
}
