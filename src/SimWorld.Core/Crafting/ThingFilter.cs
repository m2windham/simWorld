using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Which ThingDefs (within quality/hit-point bounds) are acceptable — a bill's ingredient scope, or a
    /// stockpile's storage scope later (RimWorld: <c>Verse.ThingFilter</c>). <see cref="categories"/> and
    /// <see cref="thingDefs"/> are the XML-authored input; every read (<see cref="Allows(ThingDef)"/>,
    /// <see cref="AllowedThingDefs"/>...) and every <see cref="SetAllow(ThingDef, bool)"/> first flattens
    /// them into <see cref="AllowedThingDefs"/> — lazily, on first such use, and only once — so
    /// <see cref="Allows(ThingDef)"/> is always then a plain def-membership check, never a live category
    /// walk. <b>Deviation (deliberate):</b> flattening is lazy rather than done in
    /// <see cref="Def.ResolveReferences"/>: category membership (<see cref="ThingCategoryDef.DescendantThingDefs"/>)
    /// reads <c>DefDatabase&lt;ThingDef&gt;</c> through the static <see cref="Defs.DefDatabase{T}"/> facade,
    /// which only ever sees <see cref="Defs.DefDatabase.Global"/> — not whatever <see cref="Defs.DefDatabase"/>
    /// instance a content load happens to target (this codebase's own test fixture loads into its own,
    /// non-Global, instance) — so resolving during the load pass itself would see an empty/unrelated
    /// database. Deferring to first real use is always safe: nothing reads a filter's contents before the
    /// game or a test has finished loading and pointed <see cref="Defs.DefDatabase.Global"/> at it.
    /// </summary>
    public class ThingFilter : IExposable
    {
        /// <summary>XML-authored categories to allow; flattened into the allowed-def set on first use.</summary>
        public List<ThingCategoryDef>? categories;

        /// <summary>XML-authored individual defs to allow; flattened the same way.</summary>
        public List<ThingDef>? thingDefs;

        public FloatRange allowedHitPointsPercents = new FloatRange(0f, 1f);
        public QualityRange allowedQualities = QualityRange.All;

        // disallowedSpecialFilters intentionally omitted: no special filters (e.g. "fresh food only") exist yet.

        private readonly HashSet<ThingDef> allowedDefsInt = new HashSet<ThingDef>();
        private bool xmlFlattened;

        private void EnsureXmlFlattened()
        {
            if (xmlFlattened) return;
            xmlFlattened = true;
            if (categories != null)
            {
                foreach (ThingCategoryDef? cat in categories)
                {
                    if (cat == null) continue;
                    foreach (ThingDef def in cat.DescendantThingDefs) allowedDefsInt.Add(def);
                }
            }
            if (thingDefs != null)
            {
                foreach (ThingDef? def in thingDefs)
                {
                    if (def != null) allowedDefsInt.Add(def);
                }
            }
        }

        public IEnumerable<ThingDef> AllowedThingDefs
        {
            get
            {
                EnsureXmlFlattened();
                return allowedDefsInt;
            }
        }

        public int AllowedDefCount
        {
            get
            {
                EnsureXmlFlattened();
                return allowedDefsInt.Count;
            }
        }

        public bool Allows(ThingDef? def)
        {
            EnsureXmlFlattened();
            return def != null && allowedDefsInt.Contains(def);
        }

        /// <summary>Def membership plus the stack's quality and hit-point condition.</summary>
        public bool Allows(ItemStack stack)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));
            if (!Allows(stack.Def)) return false;
            if (stack.Quality.HasValue && !allowedQualities.Includes(stack.Quality.Value)) return false;
            if (!allowedHitPointsPercents.Includes(stack.HitPointsPercent)) return false;
            return true;
        }

        /// <summary>Allows or disallows every ThingDef currently descended from <paramref name="cat"/>.</summary>
        public void SetAllow(ThingCategoryDef cat, bool allow)
        {
            if (cat == null) throw new ArgumentNullException(nameof(cat));
            EnsureXmlFlattened();
            foreach (ThingDef def in cat.DescendantThingDefs)
            {
                SetAllowDirect(def, allow);
            }
        }

        public void SetAllow(ThingDef def, bool allow)
        {
            EnsureXmlFlattened();
            SetAllowDirect(def, allow);
        }

        private void SetAllowDirect(ThingDef def, bool allow)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (allow) allowedDefsInt.Add(def);
            else allowedDefsInt.Remove(def);
        }

        /// <summary>Allows everything <paramref name="parentFilter"/> allows, or every loaded ThingDef when null.</summary>
        public void SetAllowAll(ThingFilter? parentFilter)
        {
            xmlFlattened = true;
            allowedDefsInt.Clear();
            IEnumerable<ThingDef> source = parentFilter != null
                ? parentFilter.AllowedThingDefs
                : DefDatabase<ThingDef>.AllDefsListForReading;
            foreach (ThingDef def in source)
            {
                allowedDefsInt.Add(def);
            }
            allowedHitPointsPercents = new FloatRange(0f, 1f);
            allowedQualities = QualityRange.All;
        }

        public void SetDisallowAll()
        {
            xmlFlattened = true;
            allowedDefsInt.Clear();
        }

        public void CopyAllowancesFrom(ThingFilter other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            xmlFlattened = true;
            allowedDefsInt.Clear();
            foreach (ThingDef def in other.AllowedThingDefs)
            {
                allowedDefsInt.Add(def);
            }
            allowedHitPointsPercents = other.allowedHitPointsPercents;
            allowedQualities = other.allowedQualities;
        }

        public string Summary => AllowedDefCount == 0
            ? "(nothing)"
            : string.Join(", ", allowedDefsInt.Select(d => d.LabelCap));

        public void ExposeData()
        {
            switch (Scribe.mode)
            {
                case LoadSaveMode.Saving:
                {
                    EnsureXmlFlattened();
                    List<string>? names = allowedDefsInt.Select(d => d.defName).ToList();
                    Scribe_Collections.Look(ref names, "allowedDefs", LookMode.Value);
                    break;
                }
                case LoadSaveMode.LoadingVars:
                {
                    List<string>? names = null;
                    Scribe_Collections.Look(ref names, "allowedDefs", LookMode.Value);
                    xmlFlattened = true; // the save carries the already-flattened set; categories/thingDefs are never Scribed.
                    allowedDefsInt.Clear();
                    if (names != null)
                    {
                        foreach (string name in names)
                        {
                            ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                            if (def != null) allowedDefsInt.Add(def);
                        }
                    }
                    break;
                }
            }
            Scribe_Values.Look(ref allowedHitPointsPercents, "allowedHitPointsPercents", new FloatRange(0f, 1f));

            // QualityRange is not a ParseHelper-registered type, so its two enum halves are saved separately.
            QualityCategory minQ = allowedQualities.min;
            QualityCategory maxQ = allowedQualities.max;
            Scribe_Values.Look(ref minQ, "allowedQualitiesMin", QualityCategory.Awful);
            Scribe_Values.Look(ref maxQ, "allowedQualitiesMax", QualityCategory.Legendary);
            allowedQualities = new QualityRange(minQ, maxQ);
        }
    }
}
