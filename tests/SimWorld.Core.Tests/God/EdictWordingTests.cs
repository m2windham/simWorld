using System.Linq;

using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Research;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The edict drawer's sentences, as the host lane read them: "constructing come before other work." began
    /// in lower case with the wrong verb, and "Needs the Bronze age era." doubled the word "age".
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictWordingTests : ContentTestBase
    {
        public EdictWordingTests(CoreContentFixture content) : base(content)
        {
        }

        private static EdictDef Edict(string name) => DefDatabase<EdictDef>.GetNamed(name);

        [Theory]
        [InlineData("RaiseTheRoofs", "Construction comes before other work.")]
        [InlineData("GreatWorksMandate", "Construction and mining come before other work.")]
        public void A_work_first_edict_says_so_in_a_sentence(string edict, string line)
        {
            Assert.Contains(line, EdictOption.EffectsOf(Edict(edict)));
        }

        [Fact]
        public void An_exclusive_edict_still_names_the_work_as_a_doing()
        {
            Assert.Contains("Every able hand turns to farming before anything else.", EdictOption.EffectsOf(Edict("BreakTheSod")));
        }

        [Theory]
        [InlineData("Bronze", "Bronze age")]
        [InlineData("Agrarian", "Agrarian era")]
        [InlineData("SticksAndStones", "Sticks and stones era")]
        public void An_era_is_named_once(string era, string name)
        {
            Assert.Equal(name, EdictOption.EraName(DefDatabase<EraDef>.GetNamed(era)));
        }

        [Fact]
        public void Every_era_condition_says_what_it_counts()
        {
            var housed = new EraCondition_Housed { share = 1f };
            Assert.Equal(EraConditionUnit.Share, housed.Unit);
            Assert.Equal(EraConditionUnit.Days, new EraCondition_FoodStores().Unit);
            Assert.Equal(EraConditionUnit.People, new EraCondition_Population().Unit);
            Assert.Equal(EraConditionUnit.Buildings, new EraCondition_Building().Unit);
            Assert.All(DefDatabase<EraDef>.AllDefsListForReading.SelectMany(e => e.conditions), c => Assert.True(System.Enum.IsDefined(c.Unit)));
        }
    }
}
