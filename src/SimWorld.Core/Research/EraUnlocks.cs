using System.Collections.Generic;

using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// What entering an era changes, read from content rather than authored as a list: the edicts it opens and
    /// the ones it retires (<see cref="God.EdictDef.requiredEra"/> / <see cref="God.EdictDef.obsoleteEra"/>), the
    /// buildings its research gates (a buildable thing whose latest research prerequisite belongs to the era),
    /// the house the settlement builds next (<see cref="Building.DwellingDef.era"/>), the research it now has to
    /// learn, and the raiders a rival fields once it has mastered the era (<see cref="Pawns.PawnKindDef.era"/>).
    /// The era-turn letter (<see cref="EraTransitionUtility"/>) and the god view (<c>EraOutlook</c>) both say it,
    /// so an era turn is never silent about what it did (<c>docs/research/era-arc.md</c> §7.1: "nothing in the
    /// settlement would change when they did").
    /// </summary>
    public static class EraUnlocks
    {
        /// <summary>Edicts that need <paramref name="era"/>, in content order.</summary>
        public static List<God.EdictDef> EdictsOpened(EraDef era)
        {
            var found = new List<God.EdictDef>();
            foreach (God.EdictDef def in DefDatabase<God.EdictDef>.AllDefsListForReading)
            {
                if (def.requiredEra == era) found.Add(def);
            }
            return found;
        }

        /// <summary>Edicts <paramref name="era"/> retires, in content order.</summary>
        public static List<God.EdictDef> EdictsRetired(EraDef era)
        {
            var found = new List<God.EdictDef>();
            foreach (God.EdictDef def in DefDatabase<God.EdictDef>.AllDefsListForReading)
            {
                if (def.obsoleteEra == era) found.Add(def);
            }
            return found;
        }

        /// <summary>The houses of <paramref name="era"/> (<see cref="Building.DwellingDef.era"/>), in ladder order.</summary>
        public static List<Building.DwellingDef> Dwellings(EraDef era)
        {
            var found = new List<Building.DwellingDef>();
            foreach (Building.DwellingDef def in DefDatabase<Building.DwellingDef>.AllDefsListForReading)
            {
                if (def.era == era) found.Add(def);
            }
            found.Sort((a, b) => a.rung.CompareTo(b.rung));
            return found;
        }

        /// <summary>Raider kinds a rival fields once it has mastered <paramref name="era"/>.</summary>
        public static List<Pawns.PawnKindDef> RaiderKinds(EraDef era)
        {
            var found = new List<Pawns.PawnKindDef>();
            foreach (Pawns.PawnKindDef def in DefDatabase<Pawns.PawnKindDef>.AllDefsListForReading)
            {
                if (def.era == era) found.Add(def);
            }
            return found;
        }

        /// <summary>
        /// Buildings whose research belongs to <paramref name="era"/>: every thing with a blueprint (something can
        /// be built as it) whose latest research prerequisite is in the era. Cached on the era.
        /// </summary>
        public static IReadOnlyList<ThingDef> Buildings(EraDef era) => era.UnlockedBuildings;

        internal static List<ThingDef> ComputeBuildings(EraDef era)
        {
            var buildable = new HashSet<ThingDef>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].category == ThingCategory.Blueprint && all[i].entityToBuild is ThingDef built) buildable.Add(built);
            }

            var found = new List<ThingDef>();
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (!buildable.Contains(def) || def.researchPrerequisites == null) continue;
                EraDef? latest = null;
                for (int r = 0; r < def.researchPrerequisites.Count; r++)
                {
                    EraDef? e = def.researchPrerequisites[r]?.era;
                    if (e != null && (latest == null || e.order > latest.order)) latest = e;
                }
                if (latest == era) found.Add(def);
            }
            return found;
        }

        /// <summary>
        /// The sentences an era-turn letter adds: what is new and what is gone. Empty when the era changes nothing
        /// content can name.
        /// </summary>
        public static List<string> Describe(EraDef era)
        {
            var lines = new List<string>();
            List<God.EdictDef> opened = EdictsOpened(era);
            if (opened.Count > 0) lines.Add("New edicts: " + Labels(opened) + ".");
            List<God.EdictDef> retired = EdictsRetired(era);
            if (retired.Count > 0) lines.Add("Edicts left behind: " + Labels(retired) + ".");
            List<Building.DwellingDef> houses = Dwellings(era);
            if (houses.Count > 0) lines.Add("Houses to learn: " + Labels(houses) + ".");
            IReadOnlyList<ThingDef> buildings = Buildings(era);
            if (buildings.Count > 0) lines.Add("Buildings to learn: " + Labels(buildings) + ".");
            int projects = era.Projects.Count;
            if (projects > 0) lines.Add(projects + " research projects to learn in this age.");
            if (era.conditions.Count > 0)
            {
                var asks = new List<string>(era.conditions.Count);
                for (int i = 0; i < era.conditions.Count; i++) asks.Add(era.conditions[i].Label.ToLowerInvariant());
                lines.Add("To leave it, besides its research: " + string.Join("; ", asks) + ".");
            }
            return lines;
        }

        private static string Labels<T>(IReadOnlyList<T> defs) where T : Def
        {
            var names = new List<string>(defs.Count);
            for (int i = 0; i < defs.Count; i++) names.Add(defs[i].label ?? defs[i].defName);
            return string.Join(", ", names);
        }
    }
}
