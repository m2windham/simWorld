using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map.View;
using SimWorld.Research;
using SimWorld.Tests.Content;
using SimWorld.Work;

using Xunit;

using DesignationDef = global::SimWorld.Designations.DesignationDef;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// Every defName the seam offers the player as a choice carries a label in content, so
    /// <see cref="DefLabels.LabelOf"/> never falls back to showing the raw defName. Each failure names the def
    /// type and defName, so the message is the to-do list.
    ///
    /// <para/>The orderable JobDefs' labels were added for this test and are derived from each def's own
    /// <c>reportString</c> ("clearing snow." becomes "clear snow"): RimWorld's JobDefs carry a report string and
    /// no label. The DesignationDefs' are RimWorld's designator labels, recalled — see the content files.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PlayerFacingLabelTests : ContentTestBase
    {
        public PlayerFacingLabelTests(CoreContentFixture content) : base(content)
        {
        }

        private static void Check(List<string> missing, string kind, Def def)
        {
            if (string.IsNullOrWhiteSpace(def.label)) missing.Add(kind + " " + def.defName);
        }

        [Fact]
        public void Every_orderable_job_and_designation_has_a_label()
        {
            var missing = new List<string>();
            foreach (OrderableJob job in LeverCatalogue.OrderableJobs())
            {
                Check(missing, "JobDef", DefDatabase<SimWorld.AI.JobDef>.GetNamed(job.DefName));
                Assert.Equal(job.Label, DefLabels.LabelOf(job.DefName));
            }
            foreach (DesignationDef d in DefDatabase<DesignationDef>.AllDefsListForReading)
            {
                Check(missing, "DesignationDef", d);
            }
            Assert.Empty(missing);
        }

        [Fact]
        public void Every_buildable_bench_recipe_research_project_work_type_and_storable_has_a_label()
        {
            var missing = new List<string>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.category == ThingCategory.Blueprint && d.entityToBuild is ThingDef built)
                {
                    Check(missing, "ThingDef(buildable)", built);
                }
            }
            foreach (BenchRecipes bench in LeverCatalogue.BillGivers())
            {
                Check(missing, "ThingDef(bench)", DefDatabase<ThingDef>.GetNamed(bench.BenchDefName));
                foreach (DefOption recipe in bench.Recipes)
                {
                    Check(missing, "RecipeDef", DefDatabase<RecipeDef>.GetNamed(recipe.DefName));
                }
            }
            foreach (ResearchProjectDef p in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                Check(missing, "ResearchProjectDef", p);
            }
            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                Check(missing, "WorkTypeDef", w);
            }
            foreach (DefOption storable in LeverCatalogue.StorableThings())
            {
                Check(missing, "ThingDef(storable)", DefDatabase<ThingDef>.GetNamed(storable.DefName));
            }
            Assert.Empty(missing);
        }
    }
}
