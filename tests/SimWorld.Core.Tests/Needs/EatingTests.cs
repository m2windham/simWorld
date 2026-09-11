using System.Linq;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Needs
{
    /// <summary>
    /// Eating a sitting rather than a mouthful (RimWorld: <c>FoodUtility.WillIngestStackCountOf</c> and the
    /// <c>job.count</c> that <c>Toils_Ingest</c> takes in one chew).
    ///
    /// <para/><b>The defect these pin closed.</b> <see cref="JobDriver_Ingest"/> ate exactly one unit of the
    /// stack per job. Every raw foodstuff this port ships is 0.05 nutrition and a human stomach is 1.0, so a
    /// citizen standing in a full larder recovered two per cent of a meal per reserve-walk-chew cycle while
    /// <see cref="Need_Food"/> took 1.6 a day off them — it could not out-eat its own hunger however much
    /// food was in front of it. Meals (0.9 nutrition, one unit) hid this completely, which is why the eating
    /// path's own unit tests were all green while a real settlement starved with food on the ground.
    ///
    /// <para/>The assertions are bands and orderings, not literals: what matters is that one sitting of raw
    /// food fills a hungry stomach, that it stops at full rather than wasting the stack, and that RimWorld's
    /// per-sitting cap is what bounds it.
    /// </summary>
    public class EatingTests : ContentTestBase
    {
        public EatingTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 8) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private Pawn HungryPawn(CoreMap map, IntVec3 cell, float foodPercentage)
        {
            Pawn pawn = NewHuman("Eater");
            GenSpawn.Spawn(pawn, cell, map);
            pawn.needs.food!.CurLevelPercentage = foodPercentage;
            return pawn;
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        [Fact]
        public void One_sitting_of_raw_food_fills_a_hungry_stomach()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(2, 0, 2);
            Pawn pawn = HungryPawn(map, cell, 0.1f);
            Thing berries = SpawnStack(map, cell, "RawBerries", 60);

            float before = pawn.needs.food!.CurLevel;
            pawn.jobs.StartJob(new Job(JobDefOf.Ingest, berries));
            RunTicks(JobDriver_Ingest.IngestDurationTicks + 10, pawn);

            float gained = pawn.needs.food.CurLevel - before;

            // The point of the whole fix: one job's worth of eating has to be a meal, not a mouthful. A
            // single unit of this def is 0.05 nutrition, so anything near that is the defect coming back.
            Assert.True(gained > berries.def.ingestible!.nutrition * 2f,
                "one sitting gained " + gained.ToString("F3") + " nutrition — barely more than a single unit");
            Assert.True(pawn.needs.food.CurCategory == HungerCategory.Fed,
                "a pawn that ate its fill from a full stack is still " + pawn.needs.food.CurCategory);
        }

        [Fact]
        public void A_sitting_stops_at_full_rather_than_eating_the_whole_stack()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(2, 0, 2);
            Pawn pawn = HungryPawn(map, cell, 0.5f);
            Thing berries = SpawnStack(map, cell, "RawBerries", 75);

            pawn.jobs.StartJob(new Job(JobDefOf.Ingest, berries));
            RunTicks(JobDriver_Ingest.IngestDurationTicks + 10, pawn);

            Assert.True(berries.stackCount > 0, "a half-full pawn ate a whole 75-unit stack in one sitting");
            Assert.True(pawn.needs.food!.CurLevel <= pawn.needs.food.MaxLevel + 0.001f,
                "eating overshot the stomach's own maximum");
        }

        [Fact]
        public void A_hungrier_pawn_takes_more_units_than_a_less_hungry_one()
        {
            // A trend, not a count: what is being pinned is that the sitting is sized by what the pawn wants.
            CoreMap map = NewMap();
            Pawn starving = HungryPawn(map, new IntVec3(1, 0, 1), 0.05f);
            Pawn peckish = HungryPawn(map, new IntVec3(6, 0, 6), 0.7f);
            ThingDef berries = Def("RawBerries");

            Assert.True(FoodUtility.WillIngestStackCountOf(starving, berries)
                > FoodUtility.WillIngestStackCountOf(peckish, berries));
        }

        [Fact]
        public void A_sitting_never_exceeds_the_per_def_cap()
        {
            CoreMap map = NewMap();
            Pawn pawn = HungryPawn(map, new IntVec3(1, 0, 1), 0f);
            ThingDef berries = Def("RawBerries");

            Assert.True(FoodUtility.WillIngestStackCountOf(pawn, berries) <= berries.ingestible!.maxNumToIngestAtOnce);
        }

        [Fact]
        public void A_meal_is_still_one_unit_because_one_meal_is_already_a_meal()
        {
            // The regression guard in the other direction: this change must not make a pawn wolf down four
            // cooked meals because the arithmetic said it could.
            CoreMap map = NewMap();
            Pawn pawn = HungryPawn(map, new IntVec3(1, 0, 1), 0.1f);

            Assert.Equal(1, FoodUtility.WillIngestStackCountOf(pawn, Def("MealSimple")));
        }

        [Fact]
        public void Every_shipped_raw_foodstuff_can_fill_a_stomach_in_one_sitting()
        {
            // Written against content, not against berries: a raw food added in XML whose per-unit nutrition
            // is so small that even a full sitting cannot feed anyone is the same defect wearing a new def
            // name, and this catches it with no test change.
            CoreMap map = NewMap();
            Pawn pawn = HungryPawn(map, new IntVec3(1, 0, 1), 0f);

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsNutritionGivingIngestible))
            {
                float perSitting = FoodUtility.WillIngestStackCountOf(pawn, def) * def.ingestible!.nutrition;
                Assert.True(perSitting >= pawn.needs.food!.MaxLevel * 0.5f,
                    def.defName + " gives only " + perSitting.ToString("F2") + " nutrition in a full sitting");
            }
        }
    }
}
