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

        /// <summary>
        /// The stuff a settlement's planner builds <paramref name="def"/> from on <paramref name="map"/>: its
        /// <see cref="DefaultStuffFor"/> when a whole one's worth of that lies on the map, else the first other
        /// stuff it can be made of that does (the default chain's order, then load order), else the default
        /// anyway, so a settlement short of everything still asks for wood and goes to chop it. Null when the
        /// def is not made from stuff.
        /// <para/>
        /// Not a RimWorld method: RimWorld's player picks the stuff in the architect menu, which offers only
        /// stuffs the map holds some of. A settlement here places its own blueprints, so it needs the same
        /// choice made for it. "A whole one's worth" is <see cref="ThingDef.CostListCountFor"/> of loose
        /// spawned stacks, as <see cref="Building.StoneWallMaterials"/> counts what a stone wall can afford:
        /// a blueprint in a stuff the settlement cannot finish would sit counting toward a need nobody fills.
        /// </summary>
        public static ThingDef? AffordableStuffFor(Map.Map map, ThingDef def)
        {
            ThingDef? fallback = DefaultStuffFor(def);
            if (fallback == null) return null;
            if (HasOnMap(map, def, fallback)) return fallback;

            ThingDef[] chain = { StuffDefOf.WoodLog, StuffDefOf.Steel, StuffDefOf.BlocksGranite, StuffDefOf.Cloth };
            foreach (ThingDef stuff in chain)
            {
                if (CanMake(stuff, def) && HasOnMap(map, def, stuff)) return stuff;
            }
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (CanMake(all[i], def) && HasOnMap(map, def, all[i])) return all[i];
            }
            return fallback;
        }

        /// <summary>Whether a whole <paramref name="def"/>'s worth of <paramref name="stuff"/> lies on the map
        /// that no blueprint or frame already placed is still waiting for. Without the second half one planner
        /// pass laid three steel beds on one bed's worth of steel, and two of them waited for steel forever:
        /// nothing on the map makes it, unlike wood, which a builder fells a tree for.</summary>
        private static bool HasOnMap(Map.Map map, ThingDef def, ThingDef stuff)
        {
            int need = def.CostListCountFor(stuff, stuff);
            int have = 0;
            IReadOnlyList<Things.Thing> stacks = map.listerThings.ThingsOfDef(stuff);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) have += stacks[i].stackCount;
            }
            if (have < need) return false;

            IReadOnlyList<Things.Thing> blueprints = map.listerThings.ThingsInGroup(Map.ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Building.Blueprint bp) have -= bp.ThingCountNeeded(stuff);
            }
            IReadOnlyList<Things.Thing> frames = map.listerThings.ThingsInGroup(Map.ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Building.Frame frame) have -= frame.MaterialStillNeeded(stuff);
            }
            return have >= need;
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
