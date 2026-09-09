using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Crafting
{
    /// <summary>Butchering and the produce-cycle comps (system: crafting.animals).</summary>
    public class AnimalHusbandryProduceTests : ContentTestBase
    {
        public AnimalHusbandryProduceTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Muffalo => DefDatabase<ThingDef>.GetNamed("Muffalo");
        private static ThingDef Chicken => DefDatabase<ThingDef>.GetNamed("Chicken");
        private static ThingDef LeatherPlain => DefDatabase<ThingDef>.GetNamed("Leather_Plain");
        private static ThingDef WoolAnimal => DefDatabase<ThingDef>.GetNamed("WoolAnimal");
        private static ThingDef Milk => DefDatabase<ThingDef>.GetNamed("Milk");
        private static ThingDef Egg => DefDatabase<ThingDef>.GetNamed("EggAnimalUnfertilized");
        private static ThingDef MeatGeneric => DefDatabase<ThingDef>.GetNamed("Meat_Generic");
        private static RecipeDef ButcherAnimal => DefDatabase<RecipeDef>.GetNamed("ButcherAnimal");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, Gender gender = Gender.Female, string name = "Animal")
        {
            var p = new Pawn(race, name) { gender = gender };
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>
        /// Advances only the comp's own periodic check, not a full pawn Tick — the produce cycle is what
        /// these tests are pinning, and a real Tick would also run needs (a several-day fullness window
        /// would starve an animal with nothing to eat on an empty test map long before it ever came due).
        /// </summary>
        private static void AdvanceComp(CompHasGatherableBodyResource comp, int ticks)
        {
            int start = Find.TickManager.TicksGame;
            for (int i = 1; i <= ticks; i++)
            {
                Find.TickManager.DebugSetTicksGame(start + i);
                comp.CompTick();
            }
        }

        private static IEnumerable<Thing> ThingsAt(CoreMap map, IntVec3 cell)
        {
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item))
            {
                if (t.Position == cell) yield return t;
            }
        }

        // ---- content ----

        [Fact]
        public void Husbandry_content_loads_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ThingDefOf.Meat_Generic);
            Assert.NotNull(ThingCategoryDefOf.Leathers);
            Assert.NotNull(ThingCategoryDefOf.Wool);
            Assert.NotNull(ThingCategoryDefOf.AnimalProductRaw);
            Assert.True(ButcherAnimal.isButchery);
            Assert.IsType<Recipe_ButcherAnimal>(ButcherAnimal.Worker);
            Assert.Contains(DefDatabase<ThingDef>.GetNamed("TableButcher"), ButcherAnimal.recipeUsers!);
        }

        [Fact]
        public void RecipeDef_isButchery_requires_a_Recipe_ButcherAnimal_worker()
        {
            var bad = new RecipeDef
            {
                defName = "Test_BadButchery",
                isButchery = true,
                workerClass = typeof(RecipeWorker), // not a Recipe_ButcherAnimal
            };
            Assert.Contains(bad.ConfigErrors(), e => e.Contains("Recipe_ButcherAnimal"));
        }

        // ---- Recipe_ButcherAnimal / ButcherUtility ----

        [Fact]
        public void ButcherUtility_refuses_a_living_animal()
        {
            CoreMap map = NewMap(5, 5);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2));
            Assert.False(ButcherUtility.TryButcher(husky, null, ButcherAnimal));
            Assert.True(husky.Spawned);
        }

        [Fact]
        public void Butchering_a_dead_animal_spawns_meat_and_leather_and_removes_the_carcass()
        {
            CoreMap map = NewMap(5, 5);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2));
            husky.health.Kill(null, null);
            Assert.True(husky.Dead);

            bool didButcher = ButcherUtility.TryButcher(husky, null, ButcherAnimal);

            Assert.True(didButcher);
            Assert.True(husky.Destroyed);
            Assert.False(husky.Spawned);

            var products = new List<Thing>(ThingsAt(map, new IntVec3(2, 0, 2)));
            Thing meat = Assert.Single(products, t => t.def == MeatGeneric);
            Thing leather = Assert.Single(products, t => t.def == LeatherPlain);
            Assert.True(meat.stackCount > 0);
            Assert.True(leather.stackCount > 0);
        }

        [Fact]
        public void Butcher_yield_trend_a_bigger_bodied_animal_yields_more_meat_and_leather()
        {
            CoreMap mapSmall = NewMap(5, 5);
            CoreMap mapBig = NewMap(5, 5);
            Pawn husky = SpawnAnimal(mapSmall, Husky, new IntVec3(2, 0, 2)); // bodySize 0.86
            Pawn muffalo = SpawnAnimal(mapBig, Muffalo, new IntVec3(2, 0, 2)); // bodySize 1.5
            husky.health.Kill(null, null);
            muffalo.health.Kill(null, null);

            ButcherUtility.TryButcher(husky, null, ButcherAnimal);
            ButcherUtility.TryButcher(muffalo, null, ButcherAnimal);

            Thing huskyMeat = Assert.Single(ThingsAt(mapSmall, new IntVec3(2, 0, 2)), t => t.def == MeatGeneric);
            Thing muffaloMeat = Assert.Single(ThingsAt(mapBig, new IntVec3(2, 0, 2)), t => t.def == MeatGeneric);
            Thing huskyLeather = Assert.Single(ThingsAt(mapSmall, new IntVec3(2, 0, 2)), t => t.def == LeatherPlain);
            Thing muffaloLeather = Assert.Single(ThingsAt(mapBig, new IntVec3(2, 0, 2)), t => t.def == LeatherPlain);

            Assert.True(muffaloMeat.stackCount > huskyMeat.stackCount);
            Assert.True(muffaloLeather.stackCount > huskyLeather.stackCount);
        }

        [Fact]
        public void An_animal_with_no_leatherDef_yields_meat_but_no_leather()
        {
            CoreMap map = NewMap(5, 5);
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(2, 0, 2));
            Assert.Null(chicken.RaceProps.leatherDef);
            chicken.health.Kill(null, null);

            ButcherUtility.TryButcher(chicken, null, ButcherAnimal);

            var products = new List<Thing>(ThingsAt(map, new IntVec3(2, 0, 2)));
            Assert.Contains(products, t => t.def == MeatGeneric);
            Assert.DoesNotContain(products, t => t.def == LeatherPlain);
        }

        // ---- produce comps ----

        [Fact]
        public void CompMilkable_fills_over_time_then_Gather_spawns_milk_and_resets()
        {
            CoreMap map = NewMap(5, 5);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(2, 0, 2), Gender.Female);
            CompMilkable comp = muffalo.GetComp<CompMilkable>()!;
            Assert.NotNull(comp);
            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);

            AdvanceComp(comp, fullTicks / 3);
            Assert.False(comp.Active);
            Assert.True(comp.fullness > 0f && comp.fullness < 1f);

            AdvanceComp(comp, fullTicks); // comfortably past the remainder, whatever the hash offset
            Assert.True(comp.Active);

            Thing? produced = comp.Gather(NewHuman("Milker"));
            Assert.NotNull(produced);
            Assert.Equal(Milk, produced!.def);
            Assert.Equal(comp.Properties.resourceAmount, produced.stackCount);
            Assert.Equal(0f, comp.fullness);
            Assert.False(comp.Active);
        }

        [Fact]
        public void CompMilkable_never_fills_for_a_male()
        {
            CoreMap map = NewMap(5, 5);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(2, 0, 2), Gender.Male);
            CompMilkable comp = muffalo.GetComp<CompMilkable>()!;
            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);

            AdvanceComp(comp, fullTicks * 2);

            // Fullness itself is gender-blind (RimWorld: the comp doesn't know until Active is asked), but a
            // male never actually yields.
            Assert.False(comp.Active);
            Assert.Null(comp.Gather(null));
        }

        [Fact]
        public void CompShearable_is_not_gated_by_gender()
        {
            CoreMap map = NewMap(5, 5);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(2, 0, 2), Gender.Male);
            CompShearable comp = muffalo.GetComp<CompShearable>()!;
            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);

            // A small buffer past the exact interval: fullness accumulates in many small floating-point
            // additions (one per check), so landing on "at least 1" right at the exact tick count is not
            // guaranteed bit-for-bit.
            AdvanceComp(comp, fullTicks + HusbandryTuning.ProduceCheckIntervalTicks);
            Assert.True(comp.Active);

            Thing? produced = comp.Gather(null);
            Assert.NotNull(produced);
            Assert.Equal(WoolAnimal, produced!.def);
        }

        [Fact]
        public void CompEggLayer_fills_and_lays_an_unfertilized_egg()
        {
            CoreMap map = NewMap(5, 5);
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(2, 0, 2), Gender.Female);
            CompEggLayer comp = chicken.GetComp<CompEggLayer>()!;
            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);

            AdvanceComp(comp, fullTicks + HusbandryTuning.ProduceCheckIntervalTicks);
            Assert.True(comp.Active);

            Thing? produced = comp.Gather(null);
            Assert.NotNull(produced);
            Assert.Equal(Egg, produced!.def);
        }

        [Fact]
        public void Gather_before_full_is_a_no_op()
        {
            CoreMap map = NewMap(5, 5);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(2, 0, 2));
            CompMilkable comp = muffalo.GetComp<CompMilkable>()!;
            Assert.Null(comp.Gather(null));
        }

        // ---- Scribe round-trip ----

        private class AnimalHolder : IExposable
        {
            public Pawn? animal;
            public void ExposeData() => Scribe_Deep.Look(ref animal, "animal");
        }

        [Fact]
        public void Produce_comp_fullness_round_trips_through_Scribe()
        {
            CoreMap map = NewMap(5, 5);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(2, 0, 2), Gender.Female);
            CompMilkable comp = muffalo.GetComp<CompMilkable>()!;
            comp.fullness = 0.6f;

            var holder = new AnimalHolder { animal = muffalo };
            string xml = Scribe.SaveToString(holder, "game");
            AnimalHolder loaded = Scribe.Load<AnimalHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            CompMilkable loadedComp = loaded.animal!.GetComp<CompMilkable>()!;
            Assert.NotNull(loadedComp);
            Assert.Equal(0.6f, loadedComp.fullness);
        }
    }
}
