using SimWorld.Defs;

namespace SimWorld.Map
{
    /// <summary>An overhead roof (RimWorld: <c>Verse.RoofDef</c>). Blocks weather and, later, collapses when unsupported.</summary>
    public class RoofDef : Def
    {
        /// <summary>Rock roof formed by an overhang rather than built (mountain roofs).</summary>
        public bool isNatural;

        /// <summary>Thick natural rock roof: takes longer to mine through, collapses more violently.</summary>
        public bool isThickRoof;
    }
}
