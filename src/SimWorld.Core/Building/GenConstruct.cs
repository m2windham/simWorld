using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>Placement validity for a would-be Blueprint (RimWorld: <c>Verse.GenConstruct</c>, trimmed to
    /// the checks this pass's single-cell content actually needs).</summary>
    public static class GenConstruct
    {
        /// <summary>
        /// True when <paramref name="entityToBuild"/> could be placed at <paramref name="cell"/>: in bounds,
        /// the terrain offers the affordance it needs, and no Blueprint/Frame/impassable edifice already
        /// occupies the cell. <paramref name="failReason"/> names the first check that failed.
        /// </summary>
        public static bool CanPlaceBlueprintAt(ThingDef entityToBuild, IntVec3 cell, Map.Map map, out string? failReason)
        {
            if (!GenGrid.InBounds(cell, map))
            {
                failReason = "out of bounds";
                return false;
            }

            TerrainAffordanceDef? needed = entityToBuild.terrainAffordanceNeeded;
            if (needed != null)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain.affordances == null || !terrain.affordances.Contains(needed))
                {
                    failReason = "terrain lacks the " + needed.defName + " affordance";
                    return false;
                }
            }

            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                Thing t = here[i];
                if (t.def.category == ThingCategory.Blueprint || t.def.category == ThingCategory.Frame)
                {
                    failReason = "something is already planned here";
                    return false;
                }
            }

            Thing? edifice = map.edificeGrid[cell];
            if (edifice != null)
            {
                failReason = "a building already occupies this cell";
                return false;
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// The hand-authored <c>Blueprint_&lt;X&gt;</c>/<c>Frame_&lt;X&gt;</c> ThingDef for a buildable Def —
        /// see <see cref="Defs.ThingDef.entityToBuild"/>'s remarks for why these are content rather than
        /// generated. A linear scan over the (small, single-digit) set of Blueprint/Frame Defs this pass
        /// ships; not worth caching against a DefDatabase that a test's <c>ContentTestBase</c> swaps out
        /// from under a static cache every test.
        /// </summary>
        public static ThingDef? BlueprintDefFor(ThingDef entityToBuild) => FindBy(ThingCategory.Blueprint, entityToBuild);

        public static ThingDef? FrameDefFor(ThingDef entityToBuild) => FindBy(ThingCategory.Frame, entityToBuild);

        private static ThingDef? FindBy(ThingCategory category, ThingDef entityToBuild)
        {
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef d = all[i];
                if (d.category == category && ReferenceEquals(d.entityToBuild, entityToBuild)) return d;
            }
            return null;
        }
    }
}
