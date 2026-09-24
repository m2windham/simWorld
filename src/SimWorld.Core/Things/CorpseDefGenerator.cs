using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// Mints one <c>Corpse_&lt;race&gt;</c> <see cref="ThingDef"/> per pawn race (RimWorld:
    /// <c>Verse.ThingDefGenerator_Corpses.ImpliedThingDefs</c>). A corpse needs a def of its own and not a
    /// single shared one, because a def is the only handle content has: a stockpile filter, a recipe
    /// ingredient and a category tree all name ThingDefs, so "allow animal corpses" has to be expressible
    /// without naming an instance. Everything that varies per individual (who it was, how big, what it
    /// yields) is read off <see cref="Corpse.InnerPawn"/> instead, which is why these defs carry no yield
    /// numbers at all.
    /// <para/>
    /// <b>Translation — when generation runs.</b> RimWorld generates implied defs inside its load pass,
    /// between reading XML and resolving cross-references. This port runs them at the <i>end</i> of the load
    /// pass instead (<see cref="ImpliedDefsAttribute"/>, called from <see cref="DefLoader.Load"/>), because a
    /// generator here needs the authored content already resolved to read a race off it — and because the
    /// database being loaded has to be passed in explicitly: <see cref="DefDatabase.Global"/> still points at
    /// whatever the *last* load produced, the same trap <see cref="Crafting.ThingFilter"/> documents and
    /// defers around. Every entry point below therefore takes a <see cref="DefDatabase"/>, and none of them
    /// reads <c>Global</c> except the convenience overloads a caller outside a load uses.
    /// <para/>
    /// <b>It used to be lazy, and that was a determinism defect.</b> Generation ran on the first caller that
    /// needed a corpse def — the first death — so until somebody died the database did not contain these
    /// defs at all. <see cref="Crafting.ThingFilter.SetAllowAll"/> and
    /// <see cref="ThingCategoryDef.ChildThingDefs"/> both <i>snapshot</i> the def set the first time they are
    /// used and keep that snapshot for ever, and <see cref="DefDatabase.Global"/> outlives a game — so a
    /// stockpile painted before the first death allowed no corpse in the first game of a process and every
    /// corpse in the second, purely because the earlier game had already minted them. Two runs of one seed
    /// diverged on that alone: one settlement hauled its dead and the other did not, with no difference in
    /// any observable state at the moment the two chose differently. Minting inside the load makes the def
    /// set a pure function of the content, which is what every snapshot downstream already assumes it is.
    /// <para/>
    /// Minting all races together rather than one at a time is deliberate for the same reason, and remains
    /// so: a <see cref="ThingFilter"/> and a <see cref="ThingCategoryDef"/> both cache the def set they see
    /// on first use, so a partial mint could let a stockpile that allows "animal corpses" allow some races
    /// and not others depending on who died first.
    /// </summary>
    [ImpliedDefs]
    public static class CorpseDefGenerator
    {
        /// <summary>RimWorld's own naming: <c>Corpse_Husky</c> for the <c>Husky</c> race.</summary>
        public const string CorpseDefNamePrefix = "Corpse_";

        /// <summary>Category every animal corpse def joins, when content defines it.</summary>
        public const string AnimalCorpseCategory = "CorpsesAnimal";

        /// <summary>Category every humanlike corpse def joins, when content defines it.</summary>
        public const string HumanlikeCorpseCategory = "CorpsesHumanlike";

        /// <summary>Extra path cost a pawn pays walking over a body (RimWorld: the same 15 on its generated
        /// corpse defs).</summary>
        public const int CorpsePathCost = 15;

        public static string CorpseDefNameFor(ThingDef raceDef) =>
            CorpseDefNamePrefix + (raceDef ?? throw new ArgumentNullException(nameof(raceDef))).defName;

        /// <summary>Generates into <see cref="DefDatabase.Global"/>; returns how many defs were added.</summary>
        public static int EnsureGenerated() => EnsureGenerated(DefDatabase.Global);

        /// <summary>
        /// Generates every missing corpse def into <paramref name="database"/> and returns how many were
        /// added (0 on a second call). Safe to call at any time and from anywhere.
        /// </summary>
        public static int EnsureGenerated(DefDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            // Snapshot first: minting adds to the very registry being walked, and a corpse def is itself a
            // ThingDef. The category test below also keeps a generated def from ever seeding a Corpse_Corpse_X.
            IReadOnlyList<ThingDef> all = database.For<ThingDef>().AllDefsListForReading;
            var races = new List<ThingDef>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def.category == ThingCategory.Pawn && def.race != null) races.Add(def);
            }

            int added = 0;
            for (int i = 0; i < races.Count; i++)
            {
                if (GenerateFor(races[i], database) != null) added++;
            }
            return added;
        }

        /// <summary>The corpse def for <paramref name="raceDef"/>, generating the whole set if it is not
        /// there yet (RimWorld: <c>ThingDef.race.corpseDef</c>, a field resolved at load).</summary>
        public static ThingDef CorpseDefFor(ThingDef raceDef) => CorpseDefFor(raceDef, DefDatabase.Global);

        public static ThingDef CorpseDefFor(ThingDef raceDef, DefDatabase database)
        {
            if (raceDef == null) throw new ArgumentNullException(nameof(raceDef));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (raceDef.race == null)
            {
                throw new ArgumentException("ThingDef " + raceDef.defName + " is not a pawn race; it has no corpse.", nameof(raceDef));
            }

            ThingDef? existing = database.GetNamedSilentFail<ThingDef>(CorpseDefNameFor(raceDef));
            if (existing != null) return existing;

            EnsureGenerated(database);
            return database.GetNamed<ThingDef>(CorpseDefNameFor(raceDef));
        }

        /// <summary>Whether <paramref name="def"/> is one of the defs this generator makes.</summary>
        public static bool IsCorpseDef(ThingDef? def) => def != null && def.thingClass == typeof(Corpse);

        private static ThingDef? GenerateFor(ThingDef raceDef, DefDatabase database)
        {
            string defName = CorpseDefNameFor(raceDef);
            if (database.GetNamedSilentFail<ThingDef>(defName) != null) return null;

            Pawns.RaceProperties race = raceDef.race!;
            var def = new ThingDef
            {
                defName = defName,
                label = "dead " + (raceDef.label ?? raceDef.defName),
                description = "The corpse of a " + (raceDef.label ?? raceDef.defName) + ".",
                thingClass = typeof(Corpse),

                // Item, like RimWorld's own generated corpse defs — which is also what makes a corpse
                // haulable here (ThingDef.EverHaulable is this port's category test) and what puts it in
                // ListerThings' Item/HaulableEver groups where the hauling giver already looks.
                category = ThingCategory.Item,
                tickerType = TickerType.Rare,
                altitudeLayer = AltitudeLayer.ItemImportant,
                selectable = true,
                destroyable = true,
                stackLimit = 1,
                pathCost = CorpsePathCost,

                // The race's own properties, shared with the race def exactly as RimWorld shares them, so a
                // corpse def can answer "what was this" without a live pawn.
                race = race,
            };

            ThingCategoryDef? category = database.GetNamedSilentFail<ThingCategoryDef>(
                race.Animal ? AnimalCorpseCategory : HumanlikeCorpseCategory);
            if (category != null) def.thingCategories = new List<ThingCategoryDef> { category };

            if (race.IsFlesh)
            {
                def.comps = new List<CompProperties> { new CompProperties_Rottable() };
            }

            // Deliberately no ingestible block. RimWorld makes a corpse desperate-only food; nothing in this
            // port can eat one (no ingestion path reads a Thing's inner pawn for nutrition), and a def-level
            // nutrition figure would make every body on the map count as larder in
            // AI.HuntingInitiative.NutritionAvailable — a settlement would stop hunting because its dead were
            // lying around. See this module's report.

            database.Add(def);
            return def;
        }
    }
}
