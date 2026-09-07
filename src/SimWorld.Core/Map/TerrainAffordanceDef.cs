using SimWorld.Defs;

namespace SimWorld.Map
{
    /// <summary>
    /// A capability a terrain offers building/planting on top of it (RimWorld: <c>Verse.TerrainAffordanceDef</c>).
    /// A ThingDef that needs one (e.g. mining needs Diggable) cannot be placed on terrain lacking it.
    /// </summary>
    public class TerrainAffordanceDef : Def
    {
    }
}
