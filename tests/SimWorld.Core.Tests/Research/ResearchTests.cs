using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

// The production namespace SimWorld.Research shares its leaf segment with this test namespace
// (SimWorld.Tests.Research); alias every production type explicitly to sidestep that shadowing,
// matching the convention already used for SimWorld.Needs / SimWorld.MindState elsewhere in this suite.
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using EndlessResearch = global::SimWorld.Research.EndlessResearch;
using EraDef = global::SimWorld.Research.EraDef;
using IResearchUnlockable = global::SimWorld.Research.IResearchUnlockable;
using ResearchProjectDefOf = global::SimWorld.Research.ResearchProjectDefOf;
using ResearchTabDefOf = global::SimWorld.Research.ResearchTabDefOf;

namespace SimWorld.Tests.Research
{
    /// <summary>A minimal Def implementing <see cref="IResearchUnlockable"/>, for exercising <see cref="ResearchProjectDef.UnlockedDefs"/>.</summary>
    internal sealed class TestUnlockableDef : Def, IResearchUnlockable
    {
        public IReadOnlyList<ResearchProjectDef>? ResearchPrerequisites { get; set; }
    }

    public class ResearchTests : ContentTestBase
    {
        private readonly ResearchManager manager;

        public ResearchTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
        }

        [Fact]
        public void Core_research_content_loads_with_no_errors_and_expected_counts()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(1, DefDatabase<global::SimWorld.Research.ResearchTabDef>.DefCount);
            Assert.Equal(8, DefDatabase<EraDef>.DefCount);
            Assert.True(DefDatabase<ResearchProjectDef>.DefCount >= 30, "expected at least 30 seed research projects.");
        }

        [Fact]
        public void DefOfs_are_bound()
        {
            Assert.NotNull(ResearchProjectDefOf.Fire);
            Assert.Equal("Fire", ResearchProjectDefOf.Fire.defName);
            Assert.NotNull(ResearchProjectDefOf.StoneTools);
            Assert.NotNull(ResearchProjectDefOf.Agriculture);
            Assert.NotNull(ResearchProjectDefOf.Writing);
            Assert.NotNull(ResearchTabDefOf.Main);
            Assert.Equal("Main", ResearchTabDefOf.Main.defName);
        }

        [Fact]
        public void Every_projects_prerequisites_are_in_an_earlier_or_same_era()
        {
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                // Endless tech is minted into this database at run time and deliberately carries no era —
                // it comes after the ladder rather than inside it (research.endless).
                if (EndlessResearch.IsGenerated(project)) continue;
                Assert.True(project.era != null, project.defName + " has no era.");
                int order = project.era!.order;
                if (project.prerequisites == null) continue;
                foreach (ResearchProjectDef prereq in project.prerequisites)
                {
                    Assert.True(prereq.era != null, prereq.defName + " has no era.");
                    Assert.True(prereq.era!.order <= order,
                        project.defName + "'s prerequisite " + prereq.defName + " is from a later era.");
                }
            }
        }

        [Fact]
        public void Hand_built_prerequisite_cycle_reports_a_config_error()
        {
            var a = new ResearchProjectDef { defName = "CycleA", baseCost = 100f, tab = ResearchTabDefOf.Main };
            var b = new ResearchProjectDef { defName = "CycleB", baseCost = 100f, tab = ResearchTabDefOf.Main };
            a.prerequisites = new List<ResearchProjectDef> { b };
            b.prerequisites = new List<ResearchProjectDef> { a };

            Assert.Contains(a.ConfigErrors(), e => e.Contains("cycle"));
            Assert.Contains(b.ConfigErrors(), e => e.Contains("cycle"));
        }

        [Fact]
        public void Self_prerequisite_reports_a_config_error()
        {
            var a = new ResearchProjectDef { defName = "SelfRef", baseCost = 100f, tab = ResearchTabDefOf.Main };
            a.prerequisites = new List<ResearchProjectDef> { a };

            Assert.Contains(a.ConfigErrors(), e => e.Contains("own prerequisite"));
        }

        [Fact]
        public void Missing_tab_or_nonpositive_cost_reports_config_errors()
        {
            var noTab = new ResearchProjectDef { defName = "NoTab", baseCost = 100f };
            Assert.Contains(noTab.ConfigErrors(), e => e.Contains("tab"));

            var zeroCost = new ResearchProjectDef { defName = "ZeroCost", baseCost = 0f, tab = ResearchTabDefOf.Main };
            Assert.Contains(zeroCost.ConfigErrors(), e => e.Contains("baseCost"));
        }

        [Fact]
        public void CostFactor_is_1_at_same_tech_level()
        {
            var p = new ResearchProjectDef { techLevel = TechLevel.Medieval };
            Assert.Equal(1f, p.CostFactor(TechLevel.Medieval));
        }

        [Fact]
        public void CostFactor_is_1_5_one_level_above()
        {
            var p = new ResearchProjectDef { techLevel = TechLevel.Industrial };
            Assert.Equal(1.5f, p.CostFactor(TechLevel.Medieval));
        }

        [Fact]
        public void CostFactor_is_2_two_levels_above()
        {
            var p = new ResearchProjectDef { techLevel = TechLevel.Spacer };
            Assert.Equal(2f, p.CostFactor(TechLevel.Medieval));
        }

        [Fact]
        public void CostFactor_is_1_when_researcher_is_more_advanced()
        {
            var p = new ResearchProjectDef { techLevel = TechLevel.Neolithic };
            Assert.Equal(1f, p.CostFactor(TechLevel.Industrial));
        }

        [Fact]
        public void ResearchPerformed_accumulates_progress_and_finishes_at_cost()
        {
            var p = new ResearchProjectDef { defName = "AccumTest", baseCost = 100f, techLevel = TechLevel.Neolithic, tab = ResearchTabDefOf.Main };
            manager.ResearcherTechLevel = TechLevel.Neolithic;
            manager.CurrentProj = p;

            manager.ResearchPerformed(40f, null);
            Assert.Equal(40f, manager.GetProgress(p));
            Assert.Equal(0.4f, p.ProgressPercent);
            Assert.False(manager.IsFinished(p));
            Assert.Equal(p, manager.CurrentProj);

            manager.ResearchPerformed(40f, null);
            Assert.Equal(80f, manager.GetProgress(p));
            Assert.False(manager.IsFinished(p));

            manager.ResearchPerformed(20f, null);
            Assert.Equal(100f, manager.GetProgress(p));
            Assert.True(manager.IsFinished(p));
            Assert.True(p.IsFinished);
            Assert.Equal(1f, p.ProgressPercent);
            Assert.Null(manager.CurrentProj);
        }

        [Fact]
        public void ResearchPerformed_applies_cost_factor_for_a_higher_tech_project()
        {
            var p = new ResearchProjectDef { defName = "HardProj", baseCost = 100f, techLevel = TechLevel.Industrial, tab = ResearchTabDefOf.Main };
            manager.ResearcherTechLevel = TechLevel.Medieval; // one level below -> CostFactor 1.5
            manager.CurrentProj = p;

            manager.ResearchPerformed(30f, null);

            Assert.Equal(20f, manager.GetProgress(p)); // 30 / 1.5
        }

        [Fact]
        public void Finishing_a_project_fires_the_event_and_clears_current_proj()
        {
            var p = new ResearchProjectDef { defName = "EventTest", baseCost = 50f, tab = ResearchTabDefOf.Main };
            manager.CurrentProj = p;
            ResearchProjectDef? fired = null;
            manager.ProjectFinished += def => fired = def;

            manager.FinishProject(p);

            Assert.Equal(p, fired);
            Assert.Null(manager.CurrentProj);
            Assert.True(manager.IsFinished(p));
        }

        [Fact]
        public void CanStartNow_and_CanBeResearchedNow_are_gated_by_visible_prerequisites()
        {
            ResearchProjectDef cooking = DefDatabase<ResearchProjectDef>.GetNamed("Cooking");
            Assert.False(cooking.CanStartNow);
            Assert.False(manager.CanBeResearchedNow(cooking));

            manager.FinishProject(ResearchProjectDefOf.Fire);

            Assert.True(cooking.CanStartNow);
            Assert.True(manager.CanBeResearchedNow(cooking));
        }

        [Fact]
        public void CanStartNow_is_gated_by_hidden_prerequisites_too()
        {
            var hiddenReq = new ResearchProjectDef { defName = "HiddenReq", baseCost = 50f, tab = ResearchTabDefOf.Main };
            var gated = new ResearchProjectDef
            {
                defName = "GatedProj",
                baseCost = 50f,
                tab = ResearchTabDefOf.Main,
                hiddenPrerequisites = new List<ResearchProjectDef> { hiddenReq },
            };

            Assert.False(gated.CanStartNow);

            manager.FinishProject(hiddenReq);

            Assert.True(gated.CanStartNow);
        }

        [Fact]
        public void UnlockedDefs_finds_every_IResearchUnlockable_naming_this_project()
        {
            ResearchProjectDef fire = ResearchProjectDefOf.Fire;
            var unlockable = new TestUnlockableDef
            {
                defName = "Test_UnlockedByFire",
                ResearchPrerequisites = new List<ResearchProjectDef> { fire },
            };
            DefDatabase.Global.Add(unlockable);
            try
            {
                Assert.Contains(unlockable, fire.UnlockedDefs);
            }
            finally
            {
                DefDatabase.Global.Remove(unlockable);
                fire.ClearCachedData();
            }
        }

        [Fact]
        public void Era_progress_tracks_finished_projects_and_reaches_1_when_the_era_is_done()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            Assert.True(sticksAndStones.Projects.Count > 0);
            Assert.True(sticksAndStones.Progress < 1f);
            Assert.False(sticksAndStones.IsComplete);

            foreach (ResearchProjectDef project in sticksAndStones.Projects)
            {
                manager.FinishProject(project);
            }

            Assert.Equal(1f, sticksAndStones.Progress);
            Assert.True(sticksAndStones.IsComplete);
        }

        [Fact]
        public void CurrentEra_and_NextEra_advance_as_earlier_eras_complete()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");

            // Nothing finished yet: CurrentEra falls back to the earliest era.
            Assert.Equal(sticksAndStones, manager.CurrentEra);

            foreach (ResearchProjectDef project in sticksAndStones.Projects) manager.FinishProject(project);

            Assert.Equal(sticksAndStones, manager.CurrentEra);
            Assert.Equal(agrarian, manager.NextEra);

            foreach (ResearchProjectDef project in agrarian.Projects) manager.FinishProject(project);

            Assert.Equal(agrarian, manager.CurrentEra);
        }

        [Fact]
        public void An_era_turns_on_its_spine_without_every_dead_end_being_researched()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            IReadOnlyList<ResearchProjectDef> spine = sticksAndStones.SpineProjects;

            // The rule is only interesting if the era actually has leaves; the shipped tree does.
            Assert.True(spine.Count > 0, "the first era has no spine.");
            Assert.True(spine.Count < sticksAndStones.Projects.Count,
                "the first era is all spine, so this test would prove nothing.");

            foreach (ResearchProjectDef project in spine) manager.FinishProject(project);

            Assert.True(sticksAndStones.IsComplete);
            Assert.True(sticksAndStones.Progress < 1f, "the leaves should still be unresearched.");
        }

        [Fact]
        public void A_project_nothing_depends_on_is_not_on_the_spine()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            var dependedUpon = new HashSet<ResearchProjectDef>();
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (project.prerequisites != null) dependedUpon.UnionWith(project.prerequisites);
                if (project.hiddenPrerequisites != null) dependedUpon.UnionWith(project.hiddenPrerequisites);
            }

            foreach (ResearchProjectDef project in sticksAndStones.Projects)
            {
                Assert.Equal(dependedUpon.Contains(project), sticksAndStones.SpineProjects.Contains(project));
            }
        }

        [Fact]
        public void The_researcher_tech_level_rises_with_the_era_and_never_falls()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            manager.ResearcherTechLevel = TechLevel.Neolithic;

            foreach (ResearchProjectDef project in sticksAndStones.SpineProjects) manager.FinishProject(project);
            Assert.Equal(sticksAndStones, manager.CurrentEra);

            foreach (ResearchProjectDef project in agrarian.SpineProjects) manager.FinishProject(project);
            Assert.Equal(agrarian, manager.CurrentEra);
            Assert.True(manager.ResearcherTechLevel >= agrarian.techLevel,
                $"tech level {manager.ResearcherTechLevel} did not follow the era to {agrarian.techLevel}.");

            // A scenario that starts a civilization above its era keeps that floor: knowledge is not forgotten.
            manager.ResearcherTechLevel = TechLevel.Spacer;
            manager.AdvanceTechLevelToEra();
            Assert.Equal(TechLevel.Spacer, manager.ResearcherTechLevel);
        }

        [Fact]
        public void DebugSetAllProjectsFinished_completes_every_era()
        {
            manager.DebugSetAllProjectsFinished();

            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading)
            {
                Assert.True(era.IsComplete, era.defName + " is not complete.");
            }
            Assert.Null(manager.NextEra);
        }

        [Fact]
        public void Scribe_round_trip_preserves_progress_current_project_and_tech_level()
        {
            manager.ResearcherTechLevel = TechLevel.Medieval;
            manager.CurrentProj = ResearchProjectDefOf.Agriculture;
            manager.ResearchPerformed(120f, null);
            manager.FinishProject(ResearchProjectDefOf.Fire);

            string xml = Scribe.SaveToString(manager, "research");
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(TechLevel.Medieval, loaded.ResearcherTechLevel);
            Assert.Equal(ResearchProjectDefOf.Agriculture, loaded.CurrentProj);
            Assert.Equal(manager.GetProgress(ResearchProjectDefOf.Agriculture), loaded.GetProgress(ResearchProjectDefOf.Agriculture));
            Assert.True(loaded.IsFinished(ResearchProjectDefOf.Fire));
        }
    }
}
