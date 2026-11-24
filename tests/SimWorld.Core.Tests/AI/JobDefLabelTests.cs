using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A job is shown by its label, and a JobDef with none falls back to its defName: the second playtest read
    /// "ConstructFinishFrame" as what a colonist was doing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class JobDefLabelTests : ContentTestBase
    {
        public JobDefLabelTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Every_shipped_JobDef_has_a_label_of_its_own()
        {
            var unlabelled = DefDatabase<JobDef>.AllDefsListForReading
                .Where(d => string.IsNullOrWhiteSpace(d.label) || d.label == d.defName)
                .Select(d => d.defName)
                .ToList();
            Assert.True(unlabelled.Count == 0, "JobDefs shown by their defName: " + string.Join(", ", unlabelled));
        }
    }
}
