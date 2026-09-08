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

            // The ladder's first era is where a civilization starts, so completing it is not yet a transition:
            // CurrentEra is "the furthest era reached", and that is already the first era from tick zero.
            CompleteEra(OrderedEras()[0]);
            Assert.Empty(reached);

            CompleteEra(OrderedEras()[1]);
            Assert.Single(reached);
            Assert.Equal(OrderedEras()[1], reached[0]);

            // Finishing more projects inside an era already reached is not a second transition.
            CompleteEra(OrderedEras()[1]);
            Assert.Single(reached);
        }

        [Fact]
        public void The_event_carries_the_era_left_behind()
        {
            EraDef? from = null;
            manager.EraReached += (f, t) => from = f;

            CompleteEra(OrderedEras()[0]);
            CompleteEra(OrderedEras()[1]);

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

            // Completing the first era now lets the ladder run all the way to the third in one project finish.
            CompleteEra(OrderedEras()[0]);

            Assert.Equal(new[] { OrderedEras()[1], OrderedEras()[2] }, reached);
        }

        [Fact]
        public void Reaching_an_era_records_a_chronicle_line_and_a_letter()
        {
            CompleteEra(OrderedEras()[0]);
            CompleteEra(OrderedEras()[1]);

            EraDef reached = OrderedEras()[1];
            string headline = EraTransitionUtility.ChronicleLine(OrderedEras()[0], reached);

            Assert.Contains(headline, ChronicleHeadlines());
            Assert.Contains(reached.label!, headline);

            List<Letter> letters = EraLetters();
            Assert.Single(letters);
            Assert.Contains(reached.label!, letters[0].text);
        }

        [Fact]
        public void Loading_a_save_does_not_re_announce_the_eras_already_reached()
        {
            CompleteEra(OrderedEras()[0]);
            CompleteEra(OrderedEras()[1]);
            int chronicleEntriesBefore = Find.Storyteller.Chronicle.Count;
            int lettersBefore = EraLetters().Count;
            Assert.True(lettersBefore > 0, "the setup should have announced at least one era.");

            string xml = Scribe.SaveToString(manager, "research");
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);
            Find.ResearchManager = loaded;

            Assert.Empty(errors);
            Assert.Equal(OrderedEras()[1], loaded.CurrentEra);
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

            // …but the seeding is real: the ladder has moved, and the civilization researches at its level.
            Assert.Equal(Era("Agrarian"), manager.CurrentEra);
            Assert.Equal(Era("Agrarian").techLevel, manager.ResearcherTechLevel);
        }

        // ---- threat scaling ----

        [Fact]
        public void A_later_era_never_threatens_less_than_an_earlier_one()
        {
            List<EraDef> ordered = OrderedEras();
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].threatPointsFactor >= ordered[i - 1].threatPointsFactor,
                    $"{ordered[i].defName} threatens less than {ordered[i - 1].defName}.");
            }

            Assert.True(ordered[ordered.Count - 1].threatPointsFactor > ordered[0].threatPointsFactor,
                "the ladder should end up threatening more than it starts.");
        }

        [Fact]
        public void Reaching_a_later_era_raises_the_points_the_director_spends()
        {
            var target = new CivilizationTarget(Find.Storyteller) { PlayerWealthForStoryteller = 400000f };
            target.pawns.Add(NewHuman("Citizen"));
            Find.TickManager.DebugSetTicksGame(0);

            float pointsAtStart = StorytellerUtility.DefaultThreatPointsNow(target);

            CompleteLadderThrough(Era("Industrial"));
            Assert.Equal(Era("Industrial"), manager.CurrentEra);

            float pointsIndustrial = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.True(pointsIndustrial > pointsAtStart,
                $"industrial threat points {pointsIndustrial} did not exceed the starting {pointsAtStart}.");
        }

        [Fact]
        public void The_first_era_leaves_the_ported_threat_formula_untouched()
        {
            // The era factor is an addition to RimWorld's formula, so at the bottom of the ladder — where a
            // game starts — it must be exactly neutral, or every ported threat number would be off from the
            // first tick.
            Assert.Equal(1f, OrderedEras()[0].threatPointsFactor);
            Assert.Equal(1f, EraTransitionUtility.CurrentEraThreatPointsFactor());
        }

        // ---- content gating ----

        [Fact]
        public void An_incident_bound_to_a_later_era_cannot_fire_yet()
        {
            IncidentDef fallout = DefDatabase<IncidentDef>.GetNamed("ToxicFallout");
            Assert.NotNull(fallout.minEra);

            var parms = new IncidentParms { target = new CivilizationTarget(Find.Storyteller) };
            Assert.False(fallout.Worker.CanFireNow(parms));

            CompleteLadderThrough(fallout.minEra!);

            Assert.True(fallout.Worker.CanFireNow(parms));
        }

        [Fact]
        public void An_incident_with_no_era_bounds_fires_in_any_era()
        {
            IncidentDef heatWave = DefDatabase<IncidentDef>.GetNamed("HeatWave");
            Assert.Null(heatWave.minEra);
            Assert.Null(heatWave.maxEra);

            var parms = new IncidentParms { target = new CivilizationTarget(Find.Storyteller) };
            Assert.True(heatWave.Worker.CanFireNow(parms));

            CompleteLadderThrough(Era("Industrial"));

            Assert.True(heatWave.Worker.CanFireNow(parms));
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

            CompleteLadderThrough(Era("Bronze"));
            Assert.True(earlyOnly.Worker.CanFireNow(parms));

            CompleteLadderThrough(Era("Classical"));
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
