using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Defs
{
    /// <summary>How solid a Thing's footprint is for cover and construction (RimWorld: <c>Verse.FillCategory</c>).</summary>
    public enum FillCategory
    {
        None,
        Low,
        Full,
    }

    /// <summary>Building-only tunables (RimWorld: <c>Verse.BuildingProperties</c>); minimal until Building lands.</summary>
    public class BuildingProperties
    {
        /// <summary>Occupies the edifice slot of its cells (one per cell; walls, rock, most buildings).</summary>
        public bool isEdifice = true;
    }

    /// <summary>
    /// Map/Things-layer half of <see cref="ThingDef"/>: everything about how a Thing occupies and interacts
    /// with the map. Kept in its own file/partial so the Defs-layer file stays engine-of-Def concerns only.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Footprint in cells when facing North; rotated 90° for an East/West-facing instance.</summary>
        public IntVec2 size = new IntVec2(1, 1);

        public Traversability passability = Traversability.Standable;

        /// <summary>0 = no cover, 1 = fully blocks line of fire and sight.</summary>
        public float fillPercent;

        public AltitudeLayer altitudeLayer = AltitudeLayer.Item;

        /// <summary>False means indestructible by damage (hit points never apply).</summary>
        public bool useHitPoints = true;

        /// <summary>Extra path-finding cost a pawn pays entering a cell this Thing occupies.</summary>
        public int pathCost;

        public int stackLimit = 1;

        public bool destroyable = true;

        public bool selectable = true;

        /// <summary>Can be placed at a rotation other than North.</summary>
        public bool rotatable;

        /// <summary>Terrain affordance a cell must offer before this can be placed there (e.g. mining needs Diggable).</summary>
        public TerrainAffordanceDef? terrainAffordanceNeeded;

        /// <summary>Present only on Building Defs.</summary>
        public BuildingProperties? building;

        /// <summary>True for natural rock and veins that can be mined out.</summary>
        public bool mineable;

        /// <summary>Item ThingDef mining this yields; null until the Items module supplies one.</summary>
        public ThingDef? mineableThing;

        public int mineableYield;

        /// <summary>Highest hit points instances of this Def start with: <c>statBases[MaxHitPoints]</c>, else 100.</summary>
        public int BaseMaxHitPoints
        {
            get
            {
                StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail("MaxHitPoints");
                if (stat != null && StatBaseDefined(stat))
                {
                    return (int)GetStatValueAbstract(stat);
                }
                return 100;
            }
        }

        /// <summary>Items are the only category haulable to stockpiles by the (later) hauling system.</summary>
        public bool EverHaulable => category == ThingCategory.Item;

        /// <summary>Occupies the one-per-cell edifice slot (RimWorld: <c>ThingDef.IsEdifice()</c>).</summary>
        public bool IsEdifice => category == ThingCategory.Building && (building?.isEdifice ?? true);

        public FillCategory Fillage
        {
            get
            {
                if (fillPercent >= 1f) return FillCategory.Full;
                return fillPercent > 0f ? FillCategory.Low : FillCategory.None;
            }
        }
    }
}
