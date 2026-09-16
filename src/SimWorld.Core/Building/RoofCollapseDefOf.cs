using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Building
{
    /// <summary>
    /// The two Defs a roof collapse needs (RimWorld: <c>DamageDefOf.Crush</c> and the <c>CollapsedRocks</c>
    /// named by <c>RoofRockThick.collapseLeavingThingDef</c>), bound by defName in this module's own
    /// <c>[DefOf]</c> class the way <see cref="BuildingJobDefOf"/> and <see cref="ConstructionThingDefOf"/>
    /// already are, rather than appended to <c>Health.DamageDefOf</c> or <c>Things.ThingDefOf</c>.
    /// </summary>
    [DefOf]
    public static class RoofCollapseDefOf
    {
        /// <summary>Crushing force with nobody behind it. Not <c>Blunt</c>: see <c>Damages_Crush.xml</c>.</summary>
        public static DamageDef Crush = null!;

        /// <summary>What a thick rock roof leaves in the cell it fell into, and what then holds that cell's roof up.</summary>
        public static ThingDef CollapsedRocks = null!;
    }
}
