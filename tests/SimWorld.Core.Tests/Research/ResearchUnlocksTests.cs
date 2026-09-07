using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using IResearchUnlockable = global::SimWorld.Research.IResearchUnlockable;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// The mechanism that answers "what does finishing this project unlock" existed long before anything
    /// implemented it, so every project in the authored tree unlocked nothing and research had no consequence
    /// at all (docs/research/tech-reachability.md §5.3). These tests exist so that cannot silently recur.
    /// </summary>
    public class ResearchUnlocksTests : ContentTestBase
    {
        public ResearchUnlocksTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Something_in_the_content_actually_implements_the_unlock_interface()
        {
            // The regression this whole file guards: an interface with no implementors is a no-op.
            List<Def> gated = DefDatabase.Global.AllDefs
                .Where(d => d is IResearchUnlockable u && u.ResearchPrerequisites is { Count: > 0 })
                .ToList();

            Assert.NotEmpty(gated);
        }

        [Fact]
        public void Projects_report_the_defs_they_unlock()
        {
            ResearchProjectDef masonry = DefDatabase<ResearchProjectDef>.GetNamed("Masonry");

            IReadOnlyList<IResearchUnlockable> unlocked = masonry.UnlockedDefs;
            var names = unlocked.OfType<Def>().Select(d => d.defName).ToList();

            Assert.Contains("TableStonecutter", names);
            Assert.Contains("Make_Blocks_Sandstone", names);
        }

        [Fact]
        public void A_gated_thing_is_unavailable_until_its_research_is_finished()
        {
            ThingDef stove = DefDatabase<ThingDef>.GetNamed("FueledStove");
            ResearchProjectDef cooking = DefDatabase<ResearchProjectDef>.GetNamed("Cooking");

            Assert.False(stove.IsResearchFinished);

            Find.ResearchManager.FinishProject(cooking);

            Assert.True(stove.IsResearchFinished);
        }

        [Fact]
        public void A_gated_recipe_is_unavailable_until_its_research_is_finished()
        {
            RecipeDef pemmican = DefDatabase<RecipeDef>.GetNamed("MakePemmican");
            ResearchProjectDef preservation = DefDatabase<ResearchProjectDef>.GetNamed("FoodPreservation");

            Assert.False(pemmican.AvailableNow);

            Find.ResearchManager.FinishProject(preservation);

            Assert.True(pemmican.AvailableNow);
        }

        [Fact]
        public void An_ungated_def_needs_no_research()
        {
            // A club is a stick. Gating it would be a statement that a civilization has to invent hitting.
            ThingDef club = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club");

            Assert.True(club.IsResearchFinished);
            Assert.Null(club.researchPrerequisites);
        }

        [Fact]
        public void Every_gated_def_names_a_project_that_is_reachable_from_the_start()
        {
            // A def gated on a project nothing can reach is content that can never appear. The Def loader
            // already rejects a prerequisite that does not exist; this catches the subtler case.
            foreach (Def def in DefDatabase.Global.AllDefs)
            {
                if (def is not IResearchUnlockable unlockable) continue;
                IReadOnlyList<ResearchProjectDef>? prerequisites = unlockable.ResearchPrerequisites;
                if (prerequisites == null) continue;

                foreach (ResearchProjectDef project in prerequisites)
                {
                    Assert.True(Reachable(project),
                        $"{def.defName} is gated on {project.defName}, which has an unsatisfiable prerequisite chain.");
                }
            }
        }

        /// <summary>
        /// Walks a project's prerequisite chain to the roots. The two sets are not interchangeable: <paramref
        /// name="path"/> is the current descent and catches a genuine cycle, while <paramref name="proven"/>
        /// memoises projects already shown reachable. Using one set for both jobs reports the second arm of
        /// every diamond as a cycle — the authored tree has plenty (FoodScience needs both FoodRefrigeration
        /// and Biochemistry), so that mistake fails on real, perfectly reachable content.
        /// </summary>
        private static bool Reachable(
            ResearchProjectDef project,
            HashSet<ResearchProjectDef>? path = null,
            HashSet<ResearchProjectDef>? proven = null)
        {
            path ??= new HashSet<ResearchProjectDef>();
            proven ??= new HashSet<ResearchProjectDef>();
            if (proven.Contains(project)) return true;
            if (!path.Add(project)) return false; // back-edge: a real cycle

            bool reachable = true;
            if (project.prerequisites != null)
            {
                foreach (ResearchProjectDef parent in project.prerequisites)
                {
                    if (!Reachable(parent, path, proven)) { reachable = false; break; }
                }
            }

            path.Remove(project);
            if (reachable) proven.Add(project);
            return reachable;
        }
    }
}
