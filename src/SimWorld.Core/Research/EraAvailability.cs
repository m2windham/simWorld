using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// Whether a thing belongs to an era the civilization has reached yet — what a list shown to the player
    /// (a stockpile's goods, a trader's wares) should leave out so a stone-age granary does not offer bronze,
    /// iron and longswords. RimWorld has no such filter: its storage tree lists every item in the game, which
    /// is right for one colony living in one tech level and wrong for a settlement that climbs eight eras,
    /// where the list is also the player's sense of what their age can have.
    /// <para/>
    /// A thing's era is read from what makes it, never authored: its own <see cref="ThingDef.researchPrerequisites"/>,
    /// and the cheapest-era recipe that produces it (each recipe as late as its latest prerequisite's
    /// <see cref="ResearchProjectDef.era"/>). Something no recipe makes, or that is mined from the ground, has
    /// no era of its own and is always shown. Display only: nothing here stops a thing existing (a trader or a
    /// raider may still carry one), as <see cref="ThingDef.IsResearchFinished"/> does not stop spawning.
    /// </summary>
    public static class EraAvailability
    {
        /// <summary>The era <paramref name="def"/> first becomes makeable in, or null when no era gates it.</summary>
        public static EraDef? EraOf(ThingDef def)
        {
            EraDef? own = LatestEra(def.researchPrerequisites);
            EraDef? made = MadeIn(def);
            return Later(own, made);
        }

        /// <summary>
        /// True when <paramref name="def"/>'s era (<see cref="EraOf"/>) is no later than the era the
        /// civilization is working through: the first era it has not completed, so the things its current
        /// research is about to unlock are already in view.
        /// </summary>
        public static bool IsAvailableInCurrentEra(ThingDef def)
        {
            EraDef? era = EraOf(def);
            if (era == null) return true;
            EraDef? working = WorkingEra();
            return working == null || era.order <= working.order;
        }

        /// <summary>The first era not yet complete, or the last era when every one is.</summary>
        public static EraDef? WorkingEra()
        {
            ResearchManager research = Find.ResearchManager;
            EraDef? current = research.CurrentEra;
            if (current == null || !current.IsComplete) return current;
            return research.NextEra ?? current;
        }

        private static EraDef? MadeIn(ThingDef def)
        {
            IReadOnlyList<ThingDef> things = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < things.Count; i++)
            {
                if (ReferenceEquals(things[i].mineableThing, def)) return null; // dug up: no era of its own
            }

            bool anyRecipe = false;
            EraDef? earliest = null;
            IReadOnlyList<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count; i++)
            {
                List<ThingDefCountClass>? products = recipes[i].products;
                if (products == null || !products.Exists(p => ReferenceEquals(p.thingDef, def))) continue;
                EraDef? era = LatestEra(recipes[i].researchPrerequisites);
                if (era == null) return null; // a recipe anyone can run makes it
                if (!anyRecipe || era.order < earliest!.order) earliest = era;
                anyRecipe = true;
            }
            return earliest;
        }

        private static EraDef? LatestEra(List<ResearchProjectDef>? projects)
        {
            EraDef? latest = null;
            if (projects == null) return null;
            for (int i = 0; i < projects.Count; i++) latest = Later(latest, projects[i].era);
            return latest;
        }

        private static EraDef? Later(EraDef? a, EraDef? b) =>
            a == null ? b : b == null ? a : (b.order > a.order ? b : a);
    }
}
