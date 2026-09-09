using SimWorld.Defs;

namespace SimWorld.Things
{
    /// <summary>
    /// One layer of the "what a pawn can wear at once" stack — skin, mid-layer, shell, belt, headgear
    /// (RimWorld: <c>Verse.ApparelLayerDef</c>). Two apparel pieces conflict only when they share both a
    /// layer and a covered <see cref="Health.BodyPartGroupDef"/>; content authors pick <see cref="listOrder"/>
    /// the same way <see cref="Health.BodyPartGroupDef.listOrder"/> orders parts, purely for display.
    /// </summary>
    public class ApparelLayerDef : Def
    {
        public int listOrder;
    }
}
