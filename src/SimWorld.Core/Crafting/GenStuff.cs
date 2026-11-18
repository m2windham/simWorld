using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Crafting
{
    /// <summary>Stuff choice for a "made from stuff" def (RimWorld: <c>RimWorld.GenStuff</c>, trimmed to what
    /// construction reads).</summary>
    public static class GenStuff
    {
        /// <summary>
        /// The stuff a stuff-built def is made of when nobody chose one (RimWorld: <c>GenStuff.DefaultStuffFor</c>):
        /// wood, then steel, then granite blocks, then cloth, whichever <paramref name="def"/> can be made of
        /// first, else the first allowed stuff in load order. Null when the def is not made from stuff.
        /// <para/>
        /// <b>Trimmed:</b> RimWorld checks melee weapons against steel/plasteel and apparel against
        /// cloth/leather before this chain, and tries plasteel and plain leather in it; this port has no
        /// plasteel or plain-leather stuff, and nothing here asks for a weapon's or garment's default.
        /// From memory of RimWorld's source, not a copy of it.
        /// </summary>
        public static ThingDef? DefaultStuffFor(ThingDef def)
        {
            if (!def.MadeFromStuff) return null;
            if (CanMake(StuffDefOf.WoodLog, def)) return StuffDefOf.WoodLog;
            if (CanMake(StuffDefOf.Steel, def)) return StuffDefOf.Steel;
            if (CanMake(StuffDefOf.BlocksGranite, def)) return StuffDefOf.BlocksGranite;
            if (CanMake(StuffDefOf.Cloth, def)) return StuffDefOf.Cloth;

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (CanMake(all[i], def)) return all[i];
            }
            return null;
        }

        /// <summary>Whether <paramref name="stuff"/> shares a stuff category with <paramref name="def"/>
        /// (RimWorld: <c>StuffProperties.CanMake</c>).</summary>
        public static bool CanMake(ThingDef stuff, ThingDef def)
        {
            if (stuff.stuffProps?.categories == null || def.stuffCategories == null) return false;
            foreach (StuffCategoryDef category in def.stuffCategories)
            {
                if (stuff.stuffProps.categories.Contains(category)) return true;
            }
            return false;
        }
    }

    /// <summary>The stuffs <see cref="GenStuff.DefaultStuffFor"/> tries in order.</summary>
    [DefOf]
    public static class StuffDefOf
    {
        public static ThingDef WoodLog = null!;
        public static ThingDef Steel = null!;
        public static ThingDef BlocksGranite = null!;
        public static ThingDef Cloth = null!;
    }
}
