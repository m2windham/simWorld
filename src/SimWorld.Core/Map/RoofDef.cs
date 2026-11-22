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

        /// <summary>SimWorld: earth heaped on a built roof (a pit house's sod). It joins thick rock in the room
        /// temperature model's deep pull (<c>Building.RoomTracker</c>) and nowhere else: it is built, so it is
        /// not natural, and it falls like any built roof rather than leaving rock behind.</summary>
        public bool isEarthen;
    }
}
