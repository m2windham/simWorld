using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// An earthquake does not come in a settlement's first days.
    ///
    /// <para/>A founding band has built little worth losing and has not yet met its first threat; a quake that
    /// arrives in that window takes a share of a handful of beds off people with no buffer and nothing to recover
    /// with (on seed <c>roof-a</c>, a band of twenty-five, it fired on day 2.78). The day is a design number of
    /// this port's own — RimWorld has no earthquake — so the tests pin the <i>relation</i> that justifies it, not
    /// the literal: the quake is not allowed before the first threat of any storyteller that scripts one. Move a
    /// storyteller's first threat past the quake's day and the relation test says the quake now precedes
    /// somebody's first threat; move the quake's day and the gate tests follow it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeEarliestDayTests : ContentTestBase
    {
        public EarthquakeEarliestDayTests(CoreContentFixture content) : base(content)
        {
            Ablation.Clear();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
        }

        private static IncidentDef Earthquake => DefDatabase<IncidentDef>.GetNamed("Earthquake");

        private static IncidentParms Parms() => new IncidentParms { target = new CivilizationTarget() };

        private static void AtDay(float day) =>
            Find.TickManager.DebugSetTicksGame((int)(day * GenDate.TicksPerDay));

        /// <summary>Reaches <see cref="StorytellerComp.UsableIncidentsInCategory"/> — the list every comp picks
        /// from — which is protected; the gate that matters is the one the storyteller actually asks.</summary>
        private sealed class SelectionProbe : StorytellerComp
        {
            public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target) =>
                Enumerable.Empty<FiringIncident>();

            public static IReadOnlyList<IncidentDef> Usable(IncidentParms parms) =>
                UsableIncidentsInCategory(Earthquake.category, parms).ToList();
        }

        [Fact]
        public void The_quake_declares_an_earliest_day()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(Earthquake.earliestDay > 0, "the earthquake can fire on the first day of a settlement");
        }

        [Fact]
        public void No_earthquake_before_its_earliest_day_and_one_is_possible_from_it()
        {
            int first = Earthquake.earliestDay;

            AtDay(0);
            Assert.False(Earthquake.Worker.CanFireNow(Parms()), "an earthquake was allowed on day 0");

            AtDay(first - 0.01f);
            Assert.False(Earthquake.Worker.CanFireNow(Parms()), "an earthquake was allowed just before its earliest day");

            AtDay(first);
            Assert.True(Earthquake.Worker.CanFireNow(Parms()), "an earthquake was still refused on its earliest day");

            AtDay(first + 30);
            Assert.True(Earthquake.Worker.CanFireNow(Parms()), "an earthquake was refused long after its earliest day");
        }

        [Fact]
        public void A_forced_quake_does_not_bypass_the_first_days_either()
        {
            // IncidentParms.forced skips the narrator's refire spacing, which is its own bookkeeping; it does not
            // skip a gate that says when this incident makes sense at all.
            AtDay(Earthquake.earliestDay - 1);
            IncidentParms parms = Parms();
            parms.forced = true;

            Assert.False(Earthquake.Worker.CanFireNow(parms));
        }

        [Fact]
        public void The_storytellers_own_selection_list_leaves_the_quake_out_early_and_puts_it_in_later()
        {
            AtDay(Earthquake.earliestDay - 1);
            Assert.DoesNotContain(Earthquake, SelectionProbe.Usable(Parms()));

            AtDay(Earthquake.earliestDay + 1);
            Assert.Contains(Earthquake, SelectionProbe.Usable(Parms()));
        }

        /// <summary>
        /// The reason for the number, as a relation. A storyteller that scripts a first threat names its day
        /// (<see cref="StorytellerCompProperties_ClassicIntro.day"/>) and begins its big-threat cycle on another
        /// (<see cref="StorytellerCompProperties_OnOffCycle.minDaysPassed"/>); the quake must come after all of
        /// them, for every storyteller that has them. (Randy Random scripts no first threat — his big threats
        /// are weights in a random draw — so there is no day of his to hold the quake behind.)
        /// </summary>
        [Fact]
        public void The_quake_comes_after_the_first_threat_of_every_storyteller_that_scripts_one()
        {
            var firstThreatDays = new List<(string Storyteller, string What, float Day)>();
            foreach (StorytellerDef teller in DefDatabase<StorytellerDef>.AllDefsListForReading)
            {
                foreach (StorytellerCompProperties comp in teller.comps ?? new List<StorytellerCompProperties>())
                {
                    if (comp is StorytellerCompProperties_ClassicIntro intro)
                    {
                        firstThreatDays.Add((teller.defName, "intro " + intro.incident.defName, intro.day));
                    }
                    else if (comp is StorytellerCompProperties_OnOffCycle cycle
                        && (cycle.category == IncidentCategoryDefOf.ThreatBig || cycle.incident?.category == IncidentCategoryDefOf.ThreatBig))
                    {
                        firstThreatDays.Add((teller.defName, "big-threat cycle", cycle.minDaysPassed));
                    }
                }
            }

            // Not vacuous: at least two storytellers script a first threat, and each names both an intro and a cycle.
            Assert.True(firstThreatDays.Select(t => t.Storyteller).Distinct().Count() >= 2,
                "fewer than two storytellers script a first threat; the relation has nothing to hold the quake behind");
            Assert.Contains(firstThreatDays, t => t.What.StartsWith("intro", System.StringComparison.Ordinal));
            Assert.Contains(firstThreatDays, t => t.What == "big-threat cycle");

            foreach ((string teller, string what, float day) in firstThreatDays)
            {
                Assert.True(Earthquake.earliestDay > day,
                    "the earthquake (day " + Earthquake.earliestDay + ") can precede " + teller + "'s " + what + " (day " + day + ")");
            }
        }
    }
}
