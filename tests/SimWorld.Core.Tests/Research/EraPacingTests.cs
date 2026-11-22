using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

// The production namespace SimWorld.Research shares its leaf segment with this test namespace (CLAUDE.md).
using EndlessResearch = global::SimWorld.Research.EndlessResearch;
using EraDef = global::SimWorld.Research.EraDef;
using EraPacing = global::SimWorld.Research.EraPacing;
using ResearchAgenda = global::SimWorld.Research.ResearchAgenda;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using ResearchTabDefOf = global::SimWorld.Research.ResearchTabDefOf;
using WorksInitiativeTuning = global::SimWorld.Building.WorksInitiativeTuning;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// Era pacing (system: research). Played at RimWorld's prices the first four eras went by in 26 to 78
    /// days each, Medieval and Industrial took a year or more, and the cheapest-first agenda drifted into
    /// Exotic while Information was unfinished (<c>docs/research/era-arc.md</c> §2). These pin the translation:
    /// the era multiplier on cost, the nominal era lengths it produces, and an agenda that does not reach past
    /// the era it is working toward.
    /// </summary>
    public class EraPacingTests : ContentTestBase
    {
        /// <summary>The intended years with each era current, from the era pacing translation in
        /// <c>docs/status.json</c> (research). The last era has none: it is lived in from its turn onward.</summary>
        private static readonly float[] IntendedYears = { 2f, 3f, 4f, 5f, 6f, 7f, 8f };

        /// <summary>The settlement the costs were derived for (<c>Eras.xml</c>): a founding band grown a little.</summary>
        private const int NominalCitizens = 25;

        /// <summary>Research points a settlement earns per day with <paramref name="benches"/> benches, as measured
        /// (the derivation in <c>Eras.xml</c>): about 600 with one, about 390 a bench with two, and 390 a bench
        /// assumed beyond that.</summary>
        private static float PointsPerDay(int benches) => benches == 1 ? 600f : 390f * benches;

        private const float DaysPerYear = 60f;

        private readonly ResearchManager manager;

        public EraPacingTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
        }

        private static List<EraDef> OrderedEras() =>
            DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();

        private void FinishForSetup(System.Func<ResearchProjectDef, bool> which)
        {
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading.ToList())
            {
                if (which(project) && !manager.IsFinished(project)) manager.SetProjectFinishedForSetup(project);
            }
        }

        /// <summary>Effective points to research every project in <paramref name="era"/>, by a civilization
        /// whose researchers work at <paramref name="researcherLevel"/>.</summary>
        private static float EffectiveCost(EraDef era, TechLevel researcherLevel) =>
            era.Projects.Sum(p => p.baseCost * p.CostFactor(researcherLevel));

        // ---- the cost multiplier ----

        [Fact]
        public void An_era_project_costs_its_eras_factor_times_RimWorlds_tech_factor()
        {
            EraDef bronze = DefDatabase<EraDef>.GetNamed("Bronze");
            ResearchProjectDef project = bronze.Projects[0];
            Assert.Equal(TechLevel.Medieval, project.techLevel);

            Assert.Equal(bronze.researchCostFactor, project.CostFactor(TechLevel.Medieval), 4);
            Assert.Equal(1.5f * bronze.researchCostFactor, project.CostFactor(TechLevel.Neolithic), 4);
        }

        [Fact]
        public void A_project_off_the_ladder_is_not_paced()
        {
            var loose = new ResearchProjectDef { defName = "LooseProject", baseCost = 100f, techLevel = TechLevel.Neolithic, tab = ResearchTabDefOf.Main };
            Assert.Equal(1f, EraPacing.CostFactorFor(loose));
            Assert.Equal(1f, loose.CostFactor(TechLevel.Neolithic));
        }

        [Fact]
        public void Every_era_is_priced_above_RimWorlds_colony_rate()
        {
            // The finding the multiplier answers: at RimWorld's prices the arc went by in months. A factor at
            // or under 1 would hand an era straight back to that.
            foreach (EraDef era in OrderedEras())
            {
                Assert.True(era.researchCostFactor > 1f, $"{era.defName} is priced at {era.researchCostFactor}x.");
            }
        }

        [Fact]
        public void The_endless_tail_is_priced_at_the_last_eras_factor()
        {
            FinishForSetup(p => p.era != null);
            manager.EnsureSomethingToResearch();
            ResearchProjectDef generated = manager.AllProjects.First(EndlessResearch.IsGenerated);
            EraDef last = OrderedEras()[^1];

            Assert.Equal(last.researchCostFactor, EraPacing.CostFactorFor(generated));

            // So the tail still opens no cheaper than the dearest thing on the ladder, as paid for.
            float dearestLast = last.Projects.Max(p => p.baseCost * p.CostFactor(last.techLevel));
            float cheapestTail = manager.AllProjects.Where(EndlessResearch.IsGenerated)
                .Min(p => p.baseCost * p.CostFactor(last.techLevel));
            Assert.True(cheapestTail >= dearestLast, $"The tail opens at {cheapestTail}, under the last era's {dearestLast}.");
        }

        // ---- the shape it produces ----

        /// <summary>
        /// Recomputes, from the loaded content, how long a nominal settlement spends with each era current:
        /// the effective cost of the era after it, over the scholars it has and the points each earns. The
        /// first era also pays for its own content. Pinned to the intent within 10%, so a project added to an
        /// era, or a ratio changed, that moves an era off its intended length fails here rather than in a
        /// ten-hour arc run.
        /// </summary>
        [Fact]
        public void A_nominal_settlement_spends_the_intended_years_in_each_era()
        {
            List<EraDef> eras = OrderedEras();
            Assert.Equal(IntendedYears.Length + 1, eras.Count);

            float total = 0f;
            for (int i = 0; i < IntendedYears.Length; i++)
            {
                EraDef current = eras[i];
                EraDef next = eras[i + 1];
                float cost = EffectiveCost(next, current.techLevel);
                if (i == 0) cost += EffectiveCost(current, current.techLevel);
                int scholars = WorksInitiativeTuning.ResearchBenchesWanted(NominalCitizens, current);
                float years = cost / PointsPerDay(scholars) / DaysPerYear;
                total += years;

                Assert.True(years >= IntendedYears[i] * 0.9f && years <= IntendedYears[i] * 1.1f,
                    $"{current.defName} lasts {years:0.00} years for a nominal settlement; the intent is {IntendedYears[i]}.");
            }

            Assert.InRange(total, 30f, 50f);
        }

        // ---- the agenda ----

        [Fact]
        public void The_frontier_is_the_earliest_era_not_yet_complete()
        {
            List<EraDef> eras = OrderedEras();
            Assert.Same(eras[0], EraPacing.FrontierEra());

            FinishForSetup(p => p.era != null && p.era.order <= eras[2].order);
            Assert.Same(eras[3], EraPacing.FrontierEra());

            FinishForSetup(p => p.era != null);
            Assert.Null(EraPacing.FrontierEra());
        }

        [Fact]
        public void The_agenda_never_reaches_past_the_era_it_is_working_toward()
        {
            var seen = new HashSet<ResearchProjectDef>();
            int authored = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count(p => p.era != null);
            for (int step = 0; step < authored; step++)
            {
                EraDef? frontier = EraPacing.FrontierEra();
                ResearchProjectDef? chosen = ResearchAgenda.EnsureProject();
                Assert.NotNull(chosen);
                if (chosen!.era == null) break; // the ladder is done; the endless tail has opened
                Assert.True(seen.Add(chosen), chosen.defName + " was handed out twice.");
                // With no frontier the ladder is complete and its leftover leaves are fair game.
                Assert.True(frontier == null || chosen.era.order <= frontier.order,
                    $"Step {step}: chose {chosen.defName} ({chosen.era.defName}) while working toward {frontier?.defName}.");
                manager.SetProjectFinishedForSetup(chosen);
            }

            Assert.Null(EraPacing.FrontierEra());
        }

        [Fact]
        public void An_unfinished_era_holds_back_a_cheaper_project_from_the_era_after_it()
        {
            // The drift run B showed (era-arc.md §2.1): Information's spine unfinished, a cheaper Exotic project
            // on the shelf, and cheapest-first took the Exotic one. Found from content rather than named: an
            // Information spine project which, left as the only thing unfinished up to Information, still lets
            // an Exotic project cheaper than it be started.
            EraDef information = DefDatabase<EraDef>.GetNamed("Information");
            EraDef exotic = DefDatabase<EraDef>.GetNamed("Exotic");

            ResearchProjectDef? held = null;
            ResearchProjectDef? cheaperAhead = null;
            foreach (ResearchProjectDef spine in information.SpineProjects.OrderByDescending(p => p.baseCost))
            {
                Find.ResearchManager = new ResearchManager();
                foreach (ResearchProjectDef p in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (p.era != null && p.era.order <= information.order && p != spine) Find.ResearchManager.SetProjectFinishedForSetup(p);
                }
                cheaperAhead = exotic.Projects.FirstOrDefault(p => p.CanStartNow && p.baseCost < spine.baseCost);
                if (cheaperAhead != null)
                {
                    held = spine;
                    break;
                }
            }

            Assert.NotNull(held);
            Assert.Same(information, EraPacing.FrontierEra());
            Assert.Same(held, ResearchAgenda.NextProject());
        }
    }
}
