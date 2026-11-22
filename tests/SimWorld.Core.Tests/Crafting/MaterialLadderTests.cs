using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// crafting.materialladder: ore becomes metal at an era's furnace through bills, every metal is building
    /// and blade stuff, and the settlement decides for itself to dig the ore, raise the furnace and queue the
    /// smelting its smithy's best weapon needs.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MaterialLadderTests : ContentTestBase
    {
        public MaterialLadderTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreRecipeDef Recipe(string name) => DefDatabase<CoreRecipeDef>.GetNamed(name);

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static void Know(params string[] projects)
        {
            foreach (string p in projects) Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(p));
        }

        private static Thing Spawn(CoreMap map, string def, int x, int z, int count = 1, string? stuff = null)
        {
            Thing t = ThingMaker.MakeThing(Def(def), stuff == null ? null : Def(stuff));
            t.stackCount = count;
            return GenSpawn.Spawn(t, new IntVec3(x, 0, z), map);
        }

        private static ThingWithComps Bench(CoreMap map, string def, int x, int z) => (ThingWithComps)Spawn(map, def, x, z);

        private static CompBillGiver Bills(Thing bench) => ((ThingWithComps)bench).GetComp<CompBillGiver>()!;

        private static Bill_Production? BillOn(Thing bench, string recipe) =>
            Bills(bench).BillStack.Bills.OfType<Bill_Production>().SingleOrDefault(b => b.recipe.defName == recipe);

        private static Pawn Citizen(CoreMap map, int x, int z, string? weapon = null, string? stuff = null, string name = "Citizen")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map);
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon), stuff == null ? null : Def(stuff)));
            return p;
        }

        private static Settlement NewSettlement() => new Settlement(WorldObjectDefOf.Settlement, 0, null, "Forgeton", 0);

        private static List<string> DesignatedVeins(CoreMap map) =>
            map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine)
                .Select(d => map.edificeGrid[d.target.Cell]!.def.defName).ToList();

        private static IEnumerable<string> MetallurgyBills(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .OfType<ThingWithComps>()
                .Where(b => b.GetComp<CompBillGiver>() != null)
                .SelectMany(b => b.GetComp<CompBillGiver>()!.BillStack.Bills.Select(bill => bill.recipe))
                .Where(MaterialLadder.IsMetallurgy)
                .Select(r => r.defName);

        // ---- content: the ladder as data ----

        [Fact]
        public void Every_metal_on_the_ladder_is_building_and_blade_stuff_and_ranks_in_era_order()
        {
            string[] blades = { "Flint", "Copper", "Bronze", "Iron", "Steel" };
            for (int i = 1; i < blades.Length; i++)
            {
                Assert.True(MaterialLadder.Sharpness(Def(blades[i - 1])) < MaterialLadder.Sharpness(Def(blades[i])),
                    blades[i - 1] + " should take a worse edge than " + blades[i] + ".");
            }

            string[] metals = { "Copper", "Bronze", "Iron", "Steel" };
            for (int i = 1; i < metals.Length; i++)
            {
                Assert.True(MaterialLadder.Toughness(Def(metals[i - 1])) < MaterialLadder.Toughness(Def(metals[i])));
            }
            foreach (string metal in metals)
            {
                foreach (string made in new[] { "Wall", "Door", "MeleeWeapon_Knife", "MeleeWeapon_Spear" })
                {
                    Assert.True(GenStuff.CanMake(Def(metal), Def(made)), metal + " should be able to make a " + made + ".");
                }
            }

            // Knapped flint is a blade and nothing else.
            Assert.True(GenStuff.CanMake(Def("Flint"), Def("MeleeWeapon_Knife")));
            Assert.False(GenStuff.CanMake(Def("Flint"), Def("Wall")));
            Assert.False(GenStuff.CanMake(Def("Flint"), Def("MeleeWeapon_Spear")));
        }

        [Fact]
        public void The_veins_give_ore_and_every_smelted_metal_bottoms_out_in_a_vein()
        {
            Assert.Equal("CopperOre", Def("MineableCopper").mineableThing!.defName);
            Assert.Equal("IronOre", Def("MineableSteel").mineableThing!.defName);

            List<CoreRecipeDef> metallurgy = DefDatabase<CoreRecipeDef>.AllDefsListForReading.Where(MaterialLadder.IsMetallurgy).ToList();
            Assert.Contains(metallurgy, r => r.defName == "Make_Bronze");
            foreach (CoreRecipeDef recipe in metallurgy)
            {
                foreach (IngredientCount ingredient in recipe.ingredients!)
                {
                    ThingDef input = ingredient.filter.AllowedThingDefs.Single();
                    bool mined = MiningInitiative.IsMinedYield(input);
                    bool smelted = metallurgy.Any(r => MaterialLadder.ProductCount(r, input) > 0);
                    Assert.True(mined || smelted, recipe.defName + " takes " + input.defName + ", which nothing mines or smelts.");
                }
                Assert.NotEmpty(recipe.recipeUsers!);
                Assert.All(recipe.recipeUsers!, u => Assert.NotNull(GenConstruct.BlueprintDefFor(u)));
            }
        }

        [Fact]
        public void Every_vein_on_the_ladder_comes_out_of_map_generation()
        {
            // GenStep_RocksAndMountains draws each vein it places from MineableUtility.RandomVeinDef.
            var rand = new RandomStream(20261121);
            var drawn = new HashSet<string>();
            for (int i = 0; i < 4000; i++) drawn.Add(MineableUtility.RandomVeinDef(rand)!.defName);
            foreach (string vein in new[] { "MineableFlint", "MineableCopper", "MineableTin", "MineableSteel" })
            {
                Assert.Contains(vein, drawn);
            }
        }

        // ---- each recipe chain, worked through the real bill giver ----

        [Theory]
        [InlineData("Smelt_Copper", "SmeltingFurnace", "Copper", "CopperOre:15", "Copper", 10)]
        [InlineData("Make_Bronze", "SmeltingFurnace", "Bronze", "Copper:9,Tin:1", "Bronze", 10)]
        [InlineData("Make_Bronze_Arsenical", "SmeltingFurnace", "Bronze", "CopperOre:25", "Bronze", 10)]
        [InlineData("Smelt_Iron", "Bloomery", "Iron", "IronOre:20", "Iron", 10)]
        [InlineData("Make_Steel_Cementation", "Bloomery", "Steel", "Iron:10", "Steel", 8)]
        [InlineData("Smelt_Steel", "ElectricSmelter", "Electricity", "IronOre:20", "Steel", 15)]
        public void A_smelting_bill_turns_its_inputs_into_its_metal(string recipe, string bench, string research, string inputs, string product, int made)
        {
            Know(research);
            CoreMap map = NewMap(12);
            ThingWithComps furnace = Bench(map, bench, 6, 6);
            int x = 4;
            foreach (string input in inputs.Split(','))
            {
                string[] kv = input.Split(':');
                Spawn(map, kv[0], x++, 4, int.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            Pawn smith = Citizen(map, 1, 1);
            smith.workSettings.DisableAll();
            smith.workSettings.SetPriority(WorkTypeDefOf.Smithing, 3);
            Bills(furnace).BillStack.AddBill(new Bill_Production(Recipe(recipe)) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 1 });

            RunTicks(8000, smith);

            Assert.Equal(made, MaterialLadder.OnMap(map, Def(product)));
            foreach (string input in inputs.Split(','))
            {
                Assert.Equal(0, MaterialLadder.OnMap(map, Def(input.Split(':')[0])));
            }
        }

        [Fact]
        public void A_knife_smithed_from_bronze_is_a_bronze_knife()
        {
            Know("StoneTools");
            CoreMap map = NewMap(12);
            ThingWithComps smithy = Bench(map, "Smithy", 6, 6);
            Spawn(map, "Bronze", 5, 4, 10);
            Pawn smith = Citizen(map, 1, 1);
            smith.workSettings.DisableAll();
            smith.workSettings.SetPriority(WorkTypeDefOf.Smithing, 3);
            Bills(smithy).BillStack.AddBill(new Bill_Production(Recipe("Make_MeleeWeapon_Knife")) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 1 });

            RunTicks(3000, smith);

            Thing? knife = map.listerThings.ThingsOfDef(Def("MeleeWeapon_Knife")).FirstOrDefault()
                ?? smith.equipment.Primary;
            Assert.NotNull(knife);
            Assert.Equal("Bronze", knife!.Stuff?.defName);
        }

        // ---- the settlement's choices ----

        [Fact]
        public void The_stone_age_smithy_knaps_flint_and_the_settlement_digs_for_it()
        {
            Know("StoneTools");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            Spawn(map, "MineableFlint", 30, 30);
            Spawn(map, "MineableSteel", 31, 33); // iron ore, which nothing at this age can use
            Citizen(map, 2, 2);

            OutfittingInitiative.Run(map);

            Bill_Production bill = BillOn(smithy, "Make_MeleeWeapon_Knife")!;
            Assert.NotNull(bill);
            Assert.Equal(new[] { "Flint" }, bill.ingredientFilter.AllowedThingDefs.Select(d => d.defName));

            MiningInitiative.Run(NewSettlement(), map);
            Assert.Equal(new[] { "MineableFlint" }, DesignatedVeins(map));
        }

        [Fact]
        public void With_no_tin_the_bronze_age_smelts_arsenical_bronze_and_with_tin_it_casts_tin_bronze()
        {
            Know("Copper", "Bronze");
            CoreMap map = NewMap();
            Spawn(map, "MineableCopper", 10, 10);

            Assert.Equal("Make_Bronze_Arsenical", MaterialLadder.BestRecipeFor(map, Def("Bronze"))!.defName);

            Spawn(map, "MineableTin", 30, 10);
            Assert.Equal("Make_Bronze", MaterialLadder.BestRecipeFor(map, Def("Bronze"))!.defName);

            // And with no copper anywhere, no bronze at all: the era is reached by what is in the ground.
            CoreMap barren = NewMap();
            Spawn(barren, "MineableTin", 30, 10);
            Assert.Null(MaterialLadder.BestRecipeFor(barren, Def("Bronze")));
        }

        [Fact]
        public void A_bronze_age_smithy_wants_bronze_spears_for_its_flint_armed_citizens_and_raises_a_furnace_beside_it()
        {
            Know("StoneTools", "Copper", "Bronze", "BronzeWeapons");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            Spawn(map, "MineableCopper", 33, 33);
            Citizen(map, 2, 2, "MeleeWeapon_Knife", "Flint");
            Citizen(map, 3, 2, "Bow_Short", name: "Hunter"); // a bow is not traded for a blade

            OutfittingInitiative.Run(map);

            Assert.Null(BillOn(smithy, "Make_MeleeWeapon_Knife")); // one weapon bill per bench: the best
            Bill_Production spear = BillOn(smithy, "Make_MeleeWeapon_Spear")!;
            Assert.NotNull(spear);
            Assert.Equal(1, spear.targetCount);
            Assert.Equal(new[] { "Bronze" }, spear.ingredientFilter.AllowedThingDefs.Select(d => d.defName));

            SmeltingInitiative.Run(map);

            Blueprint furnace = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Single(b => b.EntityToBuild.defName == "SmeltingFurnace");
            Assert.True(Math.Sqrt((furnace.Position - smithy.Position).LengthHorizontalSquared) <= WorkGiver_DoBill.IngredientSearchRadius,
                "A furnace out of the smith's reach smelts metal nobody can forge.");
        }

        [Fact]
        public void Once_the_furnace_stands_it_holds_the_bill_for_the_metal_and_the_miners_dig_its_ore()
        {
            Know("StoneTools", "Copper", "Bronze", "BronzeWeapons");
            CoreMap map = NewMap();
            Bench(map, "Smithy", 20, 20);
            ThingWithComps furnace = Bench(map, "SmeltingFurnace", 22, 20);
            Spawn(map, "MineableCopper", 33, 33);
            Spawn(map, "MineableSteel", 6, 6);
            Citizen(map, 2, 2, "MeleeWeapon_Knife", "Flint");

            OutfittingInitiative.Run(map);
            SmeltingInitiative.Run(map);

            Assert.Equal(new[] { "Make_Bronze_Arsenical" }, MetallurgyBills(map));
            Bill_Production bill = BillOn(furnace, "Make_Bronze_Arsenical")!;
            Assert.True(bill.targetCount >= Recipe("Make_MeleeWeapon_Spear").ingredients![0].count);

            MiningInitiative.Run(NewSettlement(), map);
            Assert.Equal(new[] { "MineableCopper" }, DesignatedVeins(map));

            // The need met, the bill goes: the spear is made and lying there for the citizen to take up.
            Spawn(map, "MeleeWeapon_Spear", 21, 22, stuff: "Bronze");
            OutfittingInitiative.Run(map);
            SmeltingInitiative.Run(map);
            Assert.Empty(MetallurgyBills(map));
        }

        [Fact]
        public void Copper_already_on_hand_is_not_smelted_again()
        {
            Know("StoneTools", "Copper", "Bronze", "BronzeWeapons");
            CoreMap map = NewMap();
            Bench(map, "Smithy", 20, 20);
            Bench(map, "SmeltingFurnace", 22, 20);
            Spawn(map, "MineableCopper", 33, 33);
            Spawn(map, "MineableTin", 34, 30);
            Spawn(map, "Copper", 23, 22, 75);
            // Armed already: an unarmed citizen would be given a copper spear from the pile at once, a stop-gap.
            Citizen(map, 2, 2, "MeleeWeapon_Knife", "Flint");

            OutfittingInitiative.Run(map);
            SmeltingInitiative.Run(map);

            Assert.Equal(new[] { "Make_Bronze" }, MetallurgyBills(map));
        }

        [Fact]
        public void The_electric_smelter_takes_the_steel_over_from_the_bloomery_once_it_stands()
        {
            Know("StoneTools", "Iron", "Steel", "SteelWeapons", "Electricity");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            ThingWithComps bloomery = Bench(map, "Bloomery", 22, 20);
            Spawn(map, "MineableSteel", 33, 33);
            Citizen(map, 2, 2, "MeleeWeapon_Spear", "Bronze");
            Bills(bloomery).BillStack.AddBill(new Bill_Production(Recipe("Make_Steel_Cementation")) { repeatMode = BillRepeatMode.TargetCount, targetCount = 30 });

            OutfittingInitiative.Run(map);
            Assert.Equal(new[] { "Steel" }, BillOn(smithy, "Make_MeleeWeapon_LongSword")!.ingredientFilter.AllowedThingDefs.Select(d => d.defName));

            SmeltingInitiative.Run(map);
            // The newest technique needs its own bench: one is planned, and the old bill is not kept meanwhile.
            Assert.Contains(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>(), b => b.EntityToBuild.defName == "ElectricSmelter");
            Assert.Empty(MetallurgyBills(map));

            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).ToList().ForEach(b => b.Destroy());
            ThingWithComps smelter = Bench(map, "ElectricSmelter", 18, 20);
            SmeltingInitiative.Run(map);
            Assert.Equal(new[] { "Smelt_Steel" }, MetallurgyBills(map));
            Assert.NotNull(BillOn(smelter, "Smelt_Steel"));
        }

        [Fact]
        public void A_citizen_trades_a_worse_blade_for_a_better_one_but_never_a_bow_or_an_equal()
        {
            CoreMap map = NewMap();
            Thing bronzeSpear = Spawn(map, "MeleeWeapon_Spear", 5, 5, stuff: "Bronze");
            Thing flintKnife = ThingMaker.MakeThing(Def("MeleeWeapon_Knife"), Def("Flint"));
            Thing bow = ThingMaker.MakeThing(Def("Bow_Short"));
            Thing otherSpear = ThingMaker.MakeThing(Def("MeleeWeapon_Spear"), Def("Bronze"));

            Assert.True(MaterialLadder.IsBetterWeapon(bronzeSpear, null));
            Assert.True(MaterialLadder.IsBetterWeapon(bronzeSpear, flintKnife));
            Assert.False(MaterialLadder.IsBetterWeapon(flintKnife, bronzeSpear));
            Assert.False(MaterialLadder.IsBetterWeapon(bronzeSpear, bow));
            Assert.False(MaterialLadder.IsBetterWeapon(bronzeSpear, otherSpear));

            // Through the real think node: the flint-armed citizen goes for the spear, the archer does not.
            var giver = new JobGiver_PickUpOpportunisticWeapon { maxDistance = 30f };
            Pawn knapper = Citizen(map, 2, 2, "MeleeWeapon_Knife", "Flint", "Knapper");
            Pawn archer = Citizen(map, 3, 2, "Bow_Short", name: "Archer");
            Assert.Same(bronzeSpear, giver.TryIssueJobPackage(knapper).Job?.targetA.Thing);
            Assert.Null(giver.TryIssueJobPackage(archer).Job);
        }

        [Fact]
        public void Spare_metal_goes_into_doors_bulk_metal_into_walls_and_none_that_the_smithy_is_short_of()
        {
            CoreMap map = NewMap();
            var door = (Blueprint)Spawn(map, "Blueprint_Door", 10, 10);
            door.stuffToUse = Def("WoodLog");
            var wall = (Blueprint)Spawn(map, "Blueprint_Wall", 12, 10);
            wall.stuffToUse = Def("WoodLog");
            Spawn(map, "Bronze", 20, 20, 20);

            Assert.Equal(1, MaterialLadder.UpgradeBlueprints(map));
            Assert.Equal("Bronze", door.stuffToUse!.defName);
            Assert.Equal("WoodLog", wall.stuffToUse!.defName); // bronze went into blades and gates, not walls

            // A smithy short of bronze keeps it: the next wooden door stays wooden.
            Know("StoneTools", "BronzeWeapons");
            ThingWithComps smithy = Bench(map, "Smithy", 30, 30);
            Citizen(map, 2, 2);
            Citizen(map, 3, 2);
            Bills(smithy).BillStack.AddBill(new Bill_Production(Recipe("Make_MeleeWeapon_Spear")) { repeatMode = BillRepeatMode.TargetCount, targetCount = 2 });
            MaterialLadder.RestrictBill(Bills(smithy).BillStack.Bills[0], Def("Bronze"));
            var secondDoor = (Blueprint)Spawn(map, "Blueprint_Door", 10, 14);
            secondDoor.stuffToUse = Def("WoodLog");
            Assert.Equal(0, MaterialLadder.UpgradeBlueprints(map));
            Assert.Equal("WoodLog", secondDoor.stuffToUse!.defName);

            // Iron is bulk metal: a wall may be built of it, and nobody is short of it.
            Spawn(map, "Iron", 22, 20, 20);
            MaterialLadder.UpgradeBlueprints(map);
            Assert.Equal("Iron", wall.stuffToUse!.defName);
            Assert.Equal("Iron", secondDoor.stuffToUse!.defName);
        }

        // ---- save ----

        [Fact]
        public void A_furnace_its_bills_and_bronze_goods_survive_a_save_and_reload()
        {
            Know("StoneTools", "Copper", "Bronze", "BronzeWeapons");
            CoreMap map = NewMap();
            Bench(map, "Smithy", 20, 20);
            Bench(map, "SmeltingFurnace", 22, 20);
            Spawn(map, "MineableCopper", 33, 33);
            Spawn(map, "Bronze", 24, 24, 7);
            Spawn(map, "MeleeWeapon_Knife", 25, 24, stuff: "Bronze");
            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Door"), Def("Bronze"));
            GenSpawn.Spawn(frame, new IntVec3(10, 0, 10), map);
            Citizen(map, 2, 2, "MeleeWeapon_Knife", "Flint");
            OutfittingInitiative.Run(map);
            SmeltingInitiative.Run(map);
            Bill_Production bill = BillOn(map.listerThings.ThingsOfDef(Def("SmeltingFurnace"))[0], "Make_Bronze_Arsenical")!;

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Bill_Production loadedBill = BillOn(loaded.listerThings.ThingsOfDef(Def("SmeltingFurnace"))[0], "Make_Bronze_Arsenical")!;
            Assert.Equal(bill.targetCount, loadedBill.targetCount);
            Bill_Production loadedSpear = BillOn(loaded.listerThings.ThingsOfDef(Def("Smithy"))[0], "Make_MeleeWeapon_Spear")!;
            Assert.Equal(new[] { "Bronze" }, loadedSpear.ingredientFilter.AllowedThingDefs.Select(d => d.defName));
            Assert.Equal(7, MaterialLadder.OnMap(loaded, Def("Bronze")));
            Assert.Equal("Bronze", loaded.listerThings.ThingsOfDef(Def("MeleeWeapon_Knife")).Single().Stuff?.defName);
            Assert.Equal("Bronze", loaded.listerThings.ThingsOfDef(Def("Frame_Door")).Single().Stuff?.defName);
            Assert.Equal("Flint", ((Pawn)loaded.mapPawns.AllPawns.Single()).equipment.Primary?.Stuff?.defName);

            // And the reloaded settlement decides exactly what the saved one did.
            OutfittingInitiative.Run(loaded);
            SmeltingInitiative.Run(loaded);
            Assert.Equal(new[] { "Make_Bronze_Arsenical" }, MetallurgyBills(loaded));
        }
    }
}
