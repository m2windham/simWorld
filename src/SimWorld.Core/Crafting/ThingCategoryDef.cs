using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;

namespace SimWorld.Crafting
{
    /// <summary>
    /// One branch of the item category tree used by <see cref="ThingFilter"/>s and (later) the trade and
    /// stockpile UI (RimWorld: <c>RimWorld.ThingCategoryDef</c>). Parent/child links and thing membership
    /// are computed lazily from the loaded Defs — rather than stored — since content across files can add
    /// either side of the relationship in any order; see <see cref="Work.WorkTypeDef.WorkGivers"/> for the
    /// same pattern already used elsewhere in this codebase.
    /// </summary>
    public class ThingCategoryDef : Def
    {
        public ThingCategoryDef? parent;

        private List<ThingCategoryDef>? childCategoriesCache;
        private List<ThingDef>? childThingDefsCache;

        /// <summary>Every category whose <see cref="parent"/> is this one.</summary>
        public IReadOnlyList<ThingCategoryDef> ChildCategories
        {
            get
            {
                if (childCategoriesCache == null)
                {
                    childCategoriesCache = DefDatabase<ThingCategoryDef>.AllDefsListForReading
                        .Where(c => c.parent == this)
                        .ToList();
                }
                return childCategoriesCache;
            }
        }

        /// <summary>Every ThingDef whose <c>thingCategories</c> lists this category directly (not only via a subcategory).</summary>
        public IReadOnlyList<ThingDef> ChildThingDefs
        {
            get
            {
                if (childThingDefsCache == null)
                {
                    childThingDefsCache = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(d => d.thingCategories != null && d.thingCategories.Contains(this))
                        .ToList();
                }
                return childThingDefsCache;
            }
        }

        /// <summary>Every ThingDef directly or transitively (through subcategories) under this category.</summary>
        public IEnumerable<ThingDef> DescendantThingDefs
        {
            get
            {
                foreach (ThingDef def in ChildThingDefs)
                {
                    yield return def;
                }
                foreach (ThingCategoryDef child in ChildCategories)
                {
                    foreach (ThingDef def in child.DescendantThingDefs)
                    {
                        yield return def;
                    }
                }
            }
        }

        /// <summary>This category and every descendant category, itself first (depth-first).</summary>
        public IEnumerable<ThingCategoryDef> ThisAndChildCategoryDefs
        {
            get
            {
                yield return this;
                foreach (ThingCategoryDef child in ChildCategories)
                {
                    foreach (ThingCategoryDef descendant in child.ThisAndChildCategoryDefs)
                    {
                        yield return descendant;
                    }
                }
            }
        }

        /// <summary>Ancestors from the immediate parent up to the root; excludes this category itself.</summary>
        public IEnumerable<ThingCategoryDef> Parents
        {
            get
            {
                for (ThingCategoryDef? p = parent; p != null; p = p.parent)
                {
                    yield return p;
                }
            }
        }
    }

    /// <summary>A material grouping (metal, wood, fabric...) a "made from stuff" ThingDef can draw from (RimWorld: <c>RimWorld.StuffCategoryDef</c>).</summary>
    public class StuffCategoryDef : Def
    {
    }

    /// <summary>What makes a ThingDef usable as a construction/crafting material (RimWorld: <c>RimWorld.StuffProperties</c>).</summary>
    public class StuffProperties
    {
        public List<StuffCategoryDef>? categories;
        public float commonality = 1f;
        public List<StatModifier>? statOffsets;
        public List<StatModifier>? statFactors;

        // color intentionally omitted: no rendering layer exists yet for stuff-tinted Things.
    }
}
