using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// <see cref="CoreRecipeDef.workSpeedStat"/> is read (defs.wiringaudit.untouched.gaps): a bill whose
    /// recipe names one is worked at that stat's value, as RimWorld's <c>Toils_Recipe.DoRecipeWork</c> does,
    /// and the meal recipes name <c>CookSpeed</c> as RimWorld's do.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RecipeWorkSpeedTests : ContentTestBase
    {
        public RecipeWorkSpeedTests(CoreContentFixture content) : base(content)
        {
        }

        private static StatDef CookSpeed => DefDatabase<StatDef>.GetNamed("CookSpeed");

        private static Pawn CookAt(int level)
        {
            Pawn p = NewHuman("cook" + level);
            p.skills.GetSkill(SkillDefOf.Cooking)!.Level = level;
            return p;
        }

        [Fact]
        public void Every_cooking_recipe_is_worked_at_cooking_speed()
        {
            var cooking = DefDatabase<CoreRecipeDef>.AllDefsListForReading.Where(CookingInitiative.IsCooking).ToList();
            Assert.NotEmpty(cooking);
            Assert.All(cooking, r => Assert.Same(CookSpeed, r.workSpeedStat));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(20)]
        public void A_bill_with_a_work_speed_stat_is_worked_at_that_stat(int level)
        {
            Pawn cook = CookAt(level);
            CoreRecipeDef meal = DefDatabase<CoreRecipeDef>.GetNamed("CookMealSimple");

            Assert.Equal(cook.GetStatValue(CookSpeed), JobDriver_DoBill.WorkSpeedFor(cook, meal), 4);
        }

        [Fact]
        public void A_better_cook_cooks_faster()
        {
            CoreRecipeDef meal = DefDatabase<CoreRecipeDef>.GetNamed("CookMealSimple");
            Assert.True(JobDriver_DoBill.WorkSpeedFor(CookAt(20), meal) > JobDriver_DoBill.WorkSpeedFor(CookAt(0), meal));
        }

        [Fact]
        public void A_recipe_naming_no_stat_keeps_the_skill_curve()
        {
            CoreRecipeDef knife = DefDatabase<CoreRecipeDef>.GetNamed("Make_MeleeWeapon_Knife");
            Assert.Null(knife.workSpeedStat);
            Pawn smith = NewHuman("smith");
            smith.skills.GetSkill(knife.workSkill!)!.Level = 10;

            Assert.Equal(JobDriver_DoBill.WorkSpeedFactorFromSkillLevel.Evaluate(10), JobDriver_DoBill.WorkSpeedFor(smith, knife), 4);
        }
    }
}
