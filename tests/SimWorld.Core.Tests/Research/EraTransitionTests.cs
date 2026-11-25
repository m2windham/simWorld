using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Letters;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

using EraDef = global::SimWorld.Research.EraDef;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// The era ladder as an event: entering an era is a moment in the civilization's history (chronicle line
    /// and letter), it scales what the director spends on threats, and it gates content that belongs to an
    /// age (<c>docs/spec/simworld-spec.md</c> §10).
    /// </summary>
    public class EraTransitionTests : ContentTestBase
    {
        private readonly ResearchManager manager;

        public EraTransitionTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
            Find.Storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"),
                DefDatabase<DifficultyDef>.GetNamed("Medium"));
        }

        // ---- helpers ----

        private static List<EraDef> OrderedEras() =>
            DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();

        private static EraDef Era(string defName) => DefDatabase<EraDef>.GetNamed(defName);

        /// <summary>Finishes every project tagged to the era, which finishes its spine along with them.</summary>
        private void CompleteEra(EraDef era)
        {
            foreach (ResearchProjectDef project in era.Projects)
            {
                if (!manager.IsFinished(project)) manager.FinishProject(project);
            }
        }

        /// <summary>Completes every era from the bottom of the ladder up to and including <paramref name="through"/>.</summary>
        private void CompleteLadderThrough(EraDef through)
        {
            foreach (EraDef era in OrderedEras())
            {
                if (era.order > through.order) break;
                CompleteEra(era);
            }
        }

        private static List<string> ChronicleHeadlines() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName).ToList();

        private static List<Letter> EraLetters() =>
            Find.LetterStack.LettersListForReading.Where(l => l.def == LetterDefOf.EraReached).ToList();

        // ---- the transition itself ----

        [Fact]
        public void Reaching_an_era_raises_the_event_exactly_once()
        {
            var reached = new List<EraDef>();
            manager.EraReached += (from, to) => reached.Add(to);

            // A civilization starts in the first era. Learning it is the way into the second: CurrentEra is the
            // era the civilization is in, and finishing the first era's research moves it on (with no world,
            // there is no settlement for the era's conditions to read, so research alone decides).
            Assert.Equal(OrderedEras()[0], manager.CurrentEra);
            CompleteEra(OrderedEras()[0]);
            Assert.Single(reached);
            Assert.Equal(OrderedEras()[1], reached[0]);
            Assert.Equal(OrderedEras()[1], manager.CurrentEra);

            // Finishing more projects of an era already left is not a second transition.
            CompleteEra(OrderedEras()[0]);
            Assert.Single(reached);
        }

        [Fact]
        public void The_event_carries_the_era_left_behind()
        {
            EraDef? from = null;
            manager.EraReached += (f, t) => from = f;

            CompleteEra(OrderedEras()[0]);

            Assert.Equal(OrderedEras()[0], from);
        }

        [Fact]
        public void Crossing_two_eras_at_once_announces_each_of_them()
        {
            // Finish the second and third eras while the first is still open: the ladder cannot advance past
            // an incomplete era, so nothing fires yet.
            var reached = new List<EraDef>();
            manager.EraReached += (from, to) => reached.Add(to);

            CompleteEra(OrderedEras()[1]);
            CompleteEra(OrderedEras()[2]);
            Assert.Empty(reached);

            // Completing the first era now lets the ladder run on, one era at a time, into the fourth: the
            // first, second and third are all learned.
            CompleteEra(OrderedEras()[0]);

            Assert.Equal(new[] { OrderedEras()[1], OrderedEras()[2], OrderedEras()[3] }, reached);
        }

        [Fact]
        public void Research_work_that_finishes_an_era_announces_it()
        {
            // The played path: points from a research job, through ResearchPerformed, finishing the era's
            // last spine project. The other tests here call FinishProject directly, which never caught that
            // ResearchPerformed wrote the finishing progress before FinishProject read the era it was leaving.
            var reached = new List<EraDef>();
            manager.EraReached += (from, to) => reached.Add(to);

            EraDef first = OrderedEras()[0];
            EraDef second = OrderedEras()[1];
            ResearchProjectDef last = first.SpineProjects[first.SpineProjects.Count - 1];
            foreach (ResearchProjectDef project in first.Projects)
            {
                if (project != last && !manager.IsFinished(project)) manager.FinishProject(project);
            }
            Assert.Equal(first, manager.CurrentEra);

            manager.CurrentProj = last;
            float factor = last.CostFactor(manager.ResearcherTechLevel);
            manager.ResearchPerformed(last.baseCost * factor * 0.5f, null);
            Assert.Empty(reached);
            manager.ResearchPerformed(last.baseCost * factor, null);

            Assert.True(manager.IsFinished(last));
            Assert.Equal(new[] { second }, reached);
            Assert.Contains(EraTransitionUtility.ChronicleLine(first, second), ChronicleHeadlines());
            Assert.Single(EraLetters());
            Assert.Contains(Find.Storyteller.Moments,
                e => e.incidentDefName == EraTransitionUtility.ChronicleLine(first, second));
        }

        [Fact]
        public void Reaching_an_era_records_a_chronicle_line_and_a_letter()
        {
            CompleteEra(OrderedEras()[0]);

            EraDef reached = OrderedEras()[1];
            string headline = EraTransitionUtility.ChronicleLine(OrderedEras()[0], reached);

            Assert.Contains(headline, ChronicleHeadlines());
            Assert.Contains(reached.LabelCap, headline);

            List<Letter> letters = EraLetters();
            Assert.Single(letters);
            Assert.Contains(reached.LabelCap, letters[0].text);
            // The letter says what the era changed, from content: Agrarian opens edicts and asks for a surplus.
            foreach (string line in EraUnlocks.Describe(reached)) Assert.Contains(line, letters[0].text);
            Assert.Contains("New edicts:", letters[0].text);
        }

        /// <summary>The second playtest's era letter read "agrarian": content writes labels lower case for
        /// mid-sentence use, and an era is a name. The line is also a moment, so a chronicle of landmarks
        /// carries it.</summary>
        [Fact]
        public void An_era_is_named_capitalised_in_its_letter_and_its_moment()
        {
            CompleteEra(OrderedEras()[0]);
            EraDef reached = OrderedEras()[1];
            Assert.NotEqual(reached.label, reached.LabelCap); // content's label is lower case, or this proves nothing

            Letter letter = Assert.Single(EraLetters());
            Assert.Equal("A new era: " + reached.LabelCap, letter.label);
            Assert.DoesNotContain(" " + reached.label + ",", letter.text);

            var entry = Find.Storyteller.Chronicle.Single(e => e.incidentDefName.StartsWith("Era reached: " + reached.LabelCap, System.StringComparison.Ordinal));
            Assert.True(entry.isMoment);
        }

        [Fact]
        public void Loading_a_save_does_not_re_announce_the_eras_already_reached()
        {
            CompleteEra(OrderedEras()[0]);
            CompleteEra(OrderedEras()[1]);
            Assert.Equal(OrderedEras()[2], manager.CurrentEra);
            int chronicleEntriesBefore = Find.Storyteller.Chronicle.Count;
            int lettersBefore = EraLetters().Count;
            Assert.True(lettersBefore > 0, "the setup should have announced at least one era.");

            string xml = Scribe.SaveToString(manager, "research");
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);
            Find.ResearchManager = loaded;

            Assert.Empty(errors);
            Assert.Equal(OrderedEras()[2], loaded.CurrentEra);
            Assert.Equal(chronicleEntriesBefore, Find.Storyteller.Chronicle.Count);
            Assert.Equal(lettersBefore, EraLetters().Count);
        }

        [Fact]
        public void A_scenario_that_starts_in_a_later_era_announces_nothing()
        {
            var reached = new List<EraDef>();
            manager.EraReached += (from, to) => reached.Add(to);

            // What ScenPart_StartingEra does for a civilization that starts in the bronze age.
            foreach (EraDef era in OrderedEras().Where(e => e.order < Era("Bronze").order))
            {
                foreach (ResearchProjectDef project in era.Projects) manager.SetProjectFinishedForSetup(project);
            }

            Assert.Empty(reached);
            Assert.Empty(EraLetters());

            // …but the seeding is real: the civilization is in the Bronze age, and researches at the level of
            // the era it has mastered, the one before.
            Assert.Equal(Era("Bronze"), manager.CurrentEra);
            Assert.Equal(Era("Agrarian"), manager.MasteredEra);
            Assert.Equal(Era("Agrarian").techLevel, manager.ResearcherTechLevel);
        }

        // ---- threat points across the ladder ----
        //
        // An era used to multiply the threat points (EraDef.threatPointsFactor, 1.0 up to 3.4). It bought
        // raiders of unchanged strength and nothing else, and on 40/201 threats-on it sent a Medieval raid the
        // size of the town that killed 24 of 25 people. It was removed; era pressure is meant to come back as
        // strength rather than head count (docs/research/era-arc.md finding 5). These tests pin the removal:
        // the same town is priced the same on either side of an era crossing, all the way up the ladder.

        private static CivilizationTarget Town(int people, float wealth)
        {
            var target = new CivilizationTarget(Find.Storyteller) { PlayerWealthForStoryteller = wealth };
            for (int i = 0; i < people; i++) target.pawns.Add(NewHuman("Citizen" + i));
            return target;
        }

        [Theory]
        [InlineData(25, 28000f)]
        [InlineData(8, 400000f)]
        public void Crossing_an_era_does_not_change_the_threat_points_for_the_same_town(int people, float wealth)
        {
            CivilizationTarget town = Town(people, wealth);
            Find.TickManager.DebugSetTicksGame(400 * GenDate.TicksPerDay);
            float before = StorytellerUtility.DefaultThreatPointsNow(town);

            foreach (EraDef era in OrderedEras())
            {
                CompleteEra(era);
                Assert.True(manager.CurrentEra!.order >= era.order, $"{era.defName} was not reached.");
                float after = StorytellerUtility.DefaultThreatPointsNow(town);
                Assert.True(after == before,
                    $"reaching {era.defName} moved the threat for the same town from {before} to {after}.");
            }
        }

        [Fact]
        public void A_bigger_town_still_draws_a_bigger_threat_in_a_late_era()
        {
            // The removal must not flatten the curve: RimWorld's own terms (roster and wealth) still move it.
            CompleteLadderThrough(Era("Industrial"));
            Find.TickManager.DebugSetTicksGame(400 * GenDate.TicksPerDay);

            float small = StorytellerUtility.DefaultThreatPointsNow(Town(10, 28000f));
            float large = StorytellerUtility.DefaultThreatPointsNow(Town(30, 28000f));
            float richer = StorytellerUtility.DefaultThreatPointsNow(Town(10, 400000f));

            Assert.True(large > small, $"30 people drew {large}, no more than 10 people's {small}.");
            Assert.True(richer > small, $"a richer town drew {richer}, no more than {small}.");
        }

        // ---- content gating ----

        [Fact]
        public void An_incident_bound_to_a_later_era_cannot_fire_yet()
        {
            IncidentDef fallout = DefDatabase<IncidentDef>.GetNamed("ToxicFallout");
            Assert.NotNull(fallout.minEra);

            var parms = new IncidentParms { target = new CivilizationTarget(Find.Storyteller) };
            Assert.False(fallout.Worker.CanFireNow(parms));

            // It needs the civilization in its era, which is entered once every era before it is learned.
            CompleteLadderThrough(OrderedEras().Last(e => e.order < fallout.minEra!.order));
            Assert.Equal(fallout.minEra, manager.CurrentEra);

            Assert.True(fallout.Worker.CanFireNow(parms));
        }

        [Fact]
        public void An_incident_with_no_era_bounds_fires_in_any_era()
        {
            // The shipped content half: a heat wave belongs to no particular age.
            IncidentDef heatWave = DefDatabase<IncidentDef>.GetNamed("HeatWave");
            Assert.Null(heatWave.minEra);
            Assert.Null(heatWave.maxEra);

            // The gate half is exercised on an incident with nothing else to say, the way
            // An_incident_a_civilization_outgrows_stops_firing already does. HeatWave used to serve as both
            // because it was a placeholder that always succeeded; it is a real incident now
            // (Conditions.IncidentWorker_MakeGameCondition) with gating of its own, and gating stacked on
            // gating is no longer a clean reading of the era window alone.
            var unbounded = new IncidentDef
            {
                defName = "Test_AnyEra",
                category = IncidentCategoryDefOf.Misc,
            };

            var parms = new IncidentParms { target = new CivilizationTarget(Find.Storyteller) };
            Assert.True(unbounded.Worker.CanFireNow(parms));

            CompleteLadderThrough(Era("Industrial"));

            Assert.True(unbounded.Worker.CanFireNow(parms));
        }

        [Fact]
        public void An_incident_a_civilization_outgrows_stops_firing()
        {
            var earlyOnly = new IncidentDef
            {
                defName = "Test_EarlyOnly",
                category = IncidentCategoryDefOf.Misc,
                maxEra = Era("Bronze"),
            };

            var parms = new IncidentParms { target = new CivilizationTarget(Find.Storyteller) };
            Assert.True(earlyOnly.Worker.CanFireNow(parms));

            CompleteLadderThrough(Era("Agrarian")); // in the Bronze age: still inside the window
            Assert.True(earlyOnly.Worker.CanFireNow(parms));

            CompleteLadderThrough(Era("Bronze")); // in the Classical age: outgrown
            Assert.False(earlyOnly.Worker.CanFireNow(parms));
        }

        [Fact]
        public void An_era_window_the_wrong_way_round_is_a_config_error()
        {
            var backwards = new IncidentDef
            {
                defName = "Test_BackwardsEraWindow",
                category = IncidentCategoryDefOf.Misc,
                minEra = Era("Industrial"),
                maxEra = Era("Bronze"),
            };

            Assert.Contains(backwards.ConfigErrors(), e => e.Contains("minEra"));
        }
    }
}
