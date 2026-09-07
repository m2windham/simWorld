using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.MapGen
{
    /// <summary>
    /// A named, ordered pipeline of <see cref="GenStepDef"/>s (RimWorld: <c>Verse.MapGeneratorDef</c>).
    /// Distinct settlement kinds (a base, later a ruin or a faction camp) can each name their own generator;
    /// only <c>Base</c> exists so far (spec §5, §5b).
    /// </summary>
    public class MapGeneratorDef : Def
    {
        public List<GenStepDef> genSteps = new List<GenStepDef>();
    }
}
