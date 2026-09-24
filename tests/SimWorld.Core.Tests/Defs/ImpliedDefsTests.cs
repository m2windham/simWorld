using System.Collections.Generic;
using System.Linq;

using SimWorld.Content;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Things;

using Xunit;

namespace SimWorld.Tests.Defs
{
    /// <summary>
    /// <b>The Def set a game sees must be a pure function of the content, decided before play starts.</b>
    ///
    /// <para/>It was not. <see cref="CorpseDefGenerator"/> minted one <c>Corpse_&lt;race&gt;</c>
    /// <see cref="ThingDef"/> per race <i>lazily</i>, on the first death, straight into the process-wide
    /// <see cref="DefDatabase.Global"/> — which outlives a game. Two things downstream take a permanent
    /// snapshot of the Def set: <see cref="ThingFilter.SetAllowAll"/> copies
    /// <c>DefDatabase&lt;ThingDef&gt;.AllDefsListForReading</c> as it stands that instant, and
    /// <see cref="ThingCategoryDef.ChildThingDefs"/> caches its members the first time anybody asks. So which
    /// of "the snapshot" and "the minting" happened first decided the answer for ever, and that order
    /// depended on whether an <i>earlier game in the same process</i> had already killed something.
    ///
    /// <para/>The measured consequence, in <c>tools/bench --suite storyteller --days 8 --seed 12345</c>: a
    /// settlement paints its granary on day 0 and the first animal dies later, so in the first run of a
    /// process that granary's filter allowed no corpse at all and in the second run it allowed every corpse.
    /// One settlement therefore hauled its dead and the other left them lying — with <b>no difference in any
    /// observable state</b> at the tick the two chose differently (same positions, same needs, same jobs,
    /// same reservations, same region graph, same RNG position). The divergence surfaced hours of simulated
    /// time later as wealth, mood and food.
    ///
    /// <para/>These tests load the shipped content into their own database rather than reading the shared
    /// fixture's, because the defect is precisely that the shared database's contents depend on what ran
    /// before — a test that asked <see cref="DefDatabase.Global"/> would pass or fail on test ordering, which
    /// is the failure mode <c>Wiring.CoreUnderAudit</c> already warns about in as many words.
    /// </summary>
    public class ImpliedDefsTests
    {
        /// <summary>
        /// A fresh load of the shipped content, with DefOf binding off so this never re-points the static
        /// DefOf fields the rest of the suite shares at another database's Def objects.
        /// </summary>
        private static DefDatabase LoadShippedContent()
        {
            var database = new DefDatabase();
            DefLoadResult result = CoreContent.Load(
                database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = false });
            Assert.Empty(result.Errors);
            return database;
        }

        private static List<ThingDef> RacesIn(DefDatabase database) =>
            database.For<ThingDef>().AllDefsListForReading
                .Where(d => d.category == ThingCategory.Pawn && d.race != null)
                .ToList();

        /// <summary>
        /// The invariant, stated where it can be checked without reference to anything that ran earlier:
        /// a load that has just finished already holds every implied corpse def, with nobody having died and
        /// nobody having called the generator by hand.
        /// </summary>
        [Fact]
        public void A_finished_load_already_holds_a_corpse_def_for_every_race()
        {
            DefDatabase database = LoadShippedContent();

            List<ThingDef> races = RacesIn(database);
            Assert.NotEmpty(races);

            var missing = new List<string>();
            foreach (ThingDef race in races)
            {
                string name = CorpseDefGenerator.CorpseDefNameFor(race);
                ThingDef? corpse = database.GetNamedSilentFail<ThingDef>(name);
                if (corpse == null) missing.Add(name);
                else Assert.True(CorpseDefGenerator.IsCorpseDef(corpse), name + " is not a corpse def.");
            }

            Assert.True(
                missing.Count == 0,
                "The load left " + missing.Count + " corpse def(s) unminted, so the Def set is still growing "
                + "during play: " + string.Join(", ", missing));
        }

        /// <summary>
        /// The same invariant said as behaviour rather than as bookkeeping, and the exact shape the
        /// storyteller bench caught: a stockpile that accepts everything has to accept a body, on a map where
        /// nothing has died yet. Before the fix this answered "no" in the first game of a process and "yes"
        /// in every game after it.
        /// </summary>
        [Fact]
        public void A_stockpile_that_allows_everything_allows_a_corpse_before_anything_has_died()
        {
            DefDatabase database = LoadShippedContent();
            DefDatabase previous = DefDatabase.Global;
            try
            {
                DefDatabase.Global = database;

                var filter = new ThingFilter();
                filter.SetAllowAll(null);

                foreach (ThingDef race in RacesIn(database))
                {
                    ThingDef? corpse = database.GetNamedSilentFail<ThingDef>(CorpseDefGenerator.CorpseDefNameFor(race));
                    Assert.True(
                        corpse != null && filter.Allows(corpse),
                        "A filter that allows everything does not allow " + race.defName + "'s corpse, so a "
                        + "settlement painting its granary before the first death can never haul its dead.");
                }
            }
            finally
            {
                DefDatabase.Global = previous;
            }
        }

        /// <summary>
        /// Two loads in one process produce the same ThingDefs in the same order — the implied ones included.
        /// Order matters and is not decoration: <c>AllDefsListForReading</c> is walked to choose a crop
        /// (<c>Building.FarmingInitiative.CropFor</c>), a subsistence food
        /// (<c>Economy.SettlementSubsistence.SubsistenceFoodDef</c>) and the contents of every "allow all"
        /// filter, so a generator that minted in a different order would move those answers.
        /// </summary>
        [Fact]
        public void Two_loads_in_one_process_produce_the_same_thing_defs_in_the_same_order()
        {
            List<string> first = LoadShippedContent().For<ThingDef>().AllDefsListForReading.Select(d => d.defName).ToList();
            List<string> second = LoadShippedContent().For<ThingDef>().AllDefsListForReading.Select(d => d.defName).ToList();

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Generation stays idempotent now that the load runs it: a caller that asks again — the save loader
        /// does, against the database it is loading into — must add nothing.
        /// </summary>
        [Fact]
        public void Running_the_generator_again_after_a_load_adds_nothing()
        {
            DefDatabase database = LoadShippedContent();
            Assert.Equal(0, CorpseDefGenerator.EnsureGenerated(database));
        }
    }
}
