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
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// Every era's armoury tier can be made, and is (era-arc §13): each recipe's ingredients come from something on
    /// a map, the armoury's own bills drive the smelting chain that feeds them, a lesser tier is made while the
    /// best waits on its metal, buried ore is tunnelled to, and on real generated maps a medieval town makes plate
    /// and crossbows and an industrial one muskets or rifles.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ArmouryTierTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public ArmouryTierTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreRecipeDef Recipe(string name) => DefDatabase<CoreRecipeDef>.GetNamed(name);

        private static void Know(params string[] projects)
        {
            foreach (string p in projects) Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(p));
        }

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Thing Spawn(CoreMap map, string def, int x, int z, int count = 1)
        {
            Thing t = ThingMaker.MakeThing(Def(def), Def(def).MadeFromStuff ? Def("WoodLog") : null);
            t.stackCount = count;
            return GenSpawn.Spawn(t, new IntVec3(x, 0, z), map);
        }

        private static ThingWithComps Bench(CoreMap map, string def, int x, int z) => (ThingWithComps)Spawn(map, def, x, z);

        private static Bill_Production? BillOn(Thing bench, string recipe) =>
            ((ThingWithComps)bench).GetComp<CompBillGiver>()!.BillStack.Bills.OfType<Bill_Production>().SingleOrDefault(b => b.recipe.defName == recipe);

        private static IEnumerable<string> MetallurgyBills(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Building).OfType<ThingWithComps>()
                .Select(b => b.GetComp<CompBillGiver>()).Where(c => c != null)
                .SelectMany(c => c!.BillStack.Bills).Where(b => MaterialLadder.IsMetallurgy(b.recipe)).Select(b => b.recipe.defName);

        private static bool Planned(CoreMap map, string def) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().Any(b => b.EntityToBuild.defName == def);

        private static Pawn Citizen(CoreMap map, int x, int z, string? weapon, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map);
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon)));
            p.skills!.GetSkill(SkillDefOf.Melee)!.Level = 3;
            p.skills.GetSkill(SkillDefOf.Shooting)!.Level = 8;
            return p;
        }

        // ---- real generated maps ----

        /// <summary>TribalStart with its starting era moved to <paramref name="era"/>: what a town founded in that
        /// age starts with (every earlier era's research seeded), the rest of the scenario as shipped.</summary>
        private static global::SimWorld.Scenario.Scenario StartingIn(string era)
        {
            global::SimWorld.Scenario.Scenario tribal = ScenarioDefOf.TribalStart.scenario;
            var scenario = new global::SimWorld.Scenario.Scenario { name = era + " start", summary = era, description = era };
            foreach (ScenPart part in tribal.parts)
            {
                scenario.parts.Add(part is ScenPart_StartingEra starting
                    ? new ScenPart_StartingEra { def = starting.def, era = DefDatabase<EraDef>.GetNamed(era) }
                    : part);
            }
            return scenario;
        }

        private static CraftingTown Open(global::SimWorld.Scenario.Scenario scenario, string seed, int band)
        {
            Game game = Game.NewGame(scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: band);
            global::SimWorld.World.Settlement settlement = game.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();
            Assert.Equal(global::SimWorld.God.View.GodCommandOutcome.Done, global::SimWorld.God.View.GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            Pawn[] citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToArray();
            foreach (Pawn citizen in citizens) Find.TickManager.RegisterAllTickabilityFor(citizen);
            return new CraftingTown
            {
                Game = game,
                Settlement = settlement,
                Map = map,
                Citizens = citizens,
                Band = SettlementReach.BandCell(map)!.Value,
            };
        }

        /// <summary>Finishes <paramref name="project"/> and, first, everything it rests on.</summary>
        private static void KnowWithPrerequisites(string project)
        {
            var order = new List<ResearchProjectDef>();
            void Visit(ResearchProjectDef p)
            {
                if (p.IsFinished || order.Contains(p)) return;
                foreach (ResearchProjectDef pre in p.prerequisites ?? new List<ResearchProjectDef>()) Visit(pre);
                order.Add(p);
            }
            Visit(DefDatabase<ResearchProjectDef>.GetNamed(project));
            foreach (ResearchProjectDef p in order) Find.ResearchManager.FinishProject(p);
        }

        private string Reading(CraftingTown town)
        {
            CoreMap map = town.Map;
            string Count(string def) => def + " " + MaterialLadder.OnMap(map, Def(def));
            IEnumerable<string> bills = map.listerThings.ThingsInGroup(ThingRequestGroup.Building).OfType<ThingWithComps>()
                .Select(b => b.GetComp<CompBillGiver>()).Where(c => c != null).SelectMany(c => c!.BillStack.Bills).OfType<Bill_Production>()
                .Select(b => b.recipe.defName + " " + b.targetCount);
            return "day " + (Find.TickManager.TicksGame / (float)GenDate.TicksPerDay).ToString("0.0") + ": "
                + string.Join(", ", new[] { "IronOre", "Iron", "Steel", "WoodLog" }.Select(Count))
                + "; bloomery " + (Planned(map, "Bloomery") ? "planned" : map.listerThings.ThingsOfDef(Def("Bloomery")).Count > 0 ? "built" : "-")
                + "; mine marks [" + string.Join(",", map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine)
                    .GroupBy(d => WorkGiver_Miner.FirstMineable(d.target.Cell, map)?.def.defName ?? "-").Select(g => g.Key + " " + g.Count())) + "]"
                + "; iron faces " + map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count(t => t.def.mineableThing == Def("IronOre") && WorkGiver_Miner.MayBeAccessible(t.Position, map))
                + "/" + map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count(t => t.def.mineableThing == Def("IronOre"))
                + "; bills [" + string.Join(", ", bills) + "]";
        }

        /// <summary>
        /// A town founded in the medieval age, with the crossbow and knighthood learned, on a generated map with iron
        /// in it: nothing is called by hand after the setup but raising the smithy. The armoury plans plate and
        /// crossbows, its bills ask for iron and steel, a bloomery is raised and worked, the ore is dug, and
        /// within <see cref="MedievalDays"/> days a plate coat and a crossbow exist.
        /// </summary>
        [Fact]
        public void A_medieval_town_makes_plate_and_crossbows_from_its_own_iron()
        {
            CraftingTown town = Open(StartingIn("Medieval"), MedievalSeed, band: 20);
            Assert.True(MaterialLadder.HasReachableVein(town.Map, Def("IronOre")), "the test map has no iron to work");
            KnowWithPrerequisites("Crossbows");
            KnowWithPrerequisites("Knighthood");
            Assert.False(DefDatabase<ResearchProjectDef>.GetNamed("Gunpowder").IsFinished, "with gunpowder the musket outranks the crossbow");
            town.Bench("Smithy");

            int ticks = town.RunUntil(() => town.Made("Apparel_PlateArmor") > 0 && town.Made("Crossbow") > 0, MedievalDays * GenDate.TicksPerDay,
                () => { if (Find.TickManager.TicksGame % GenDate.TicksPerDay == 0) output.WriteLine(Reading(town)); });
            output.WriteLine(Reading(town) + "; plate " + town.Made("Apparel_PlateArmor") + ", crossbows " + town.Made("Crossbow") + " after " + ticks + " ticks");

            Assert.True(town.Made("Crossbow") > 0, "no crossbow in " + MedievalDays + " days");
            Assert.True(town.Made("Apparel_PlateArmor") > 0, "no plate armour in " + MedievalDays + " days");
        }

        /// <summary>The industrial twin: a town founded in the industrial age (the shipped Landfall scenario) makes
        /// muskets, or rifles once it has firearms, from the iron on its map.</summary>
        [Fact]
        public void An_industrial_town_makes_muskets_or_rifles_from_its_own_iron()
        {
            CraftingTown town = Open(DefDatabase<ScenarioDef>.GetNamed("Landfall").scenario, IndustrialSeed, band: 20);
            Assert.True(MaterialLadder.HasReachableVein(town.Map, Def("IronOre")), "the test map has no iron to work");
            town.Bench("Smithy");

            int Guns() => town.Made("Gun_Musket") + town.Made("Gun_BoltActionRifle");
            int ticks = town.RunUntil(() => Guns() > 0, IndustrialDays * GenDate.TicksPerDay,
                () => { if (Find.TickManager.TicksGame % GenDate.TicksPerDay == 0) output.WriteLine(Reading(town)); });
            output.WriteLine(Reading(town) + "; muskets " + town.Made("Gun_Musket") + ", rifles " + town.Made("Gun_BoltActionRifle") + " after " + ticks + " ticks");

            Assert.True(Guns() > 0, "no musket or rifle in " + IndustrialDays + " days");
        }

        /// <summary>
        /// The founders' kit fits the age they start in (era-arc §12.4): a sticks-and-stones band carries no gun,
        /// a medieval one is capped at medieval gear, and an industrial landing may carry revolvers.
        /// </summary>
        [Theory]
        [InlineData("SticksAndStones", "founders-a")]
        [InlineData("SticksAndStones", "founders-b")]
        [InlineData("Medieval", "founders-a")]
        public void Founders_carry_nothing_past_their_starting_era(string era, string seed)
        {
            global::SimWorld.Scenario.Scenario scenario = StartingIn(era);
            Game game = Game.NewGame(scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 40);
            global::SimWorld.World.Settlement settlement = game.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();
            global::SimWorld.Factions.Faction player = settlement.faction!;
            TechLevel cap = DefDatabase<EraDef>.GetNamed(era).techLevel;

            Assert.Equal(cap, global::SimWorld.Factions.FactionAdvancement.TechLevelOf(player));
            var over = new List<string>();
            foreach (Pawn p in settlement.Citizens)
            {
                ThingWithComps? weapon = p.equipment?.Primary;
                if (weapon != null && weapon.def.techLevel > cap) over.Add(p.Label + ": " + weapon.def.defName);
            }
            Assert.True(over.Count == 0, "founders armed past " + era + ": " + string.Join(", ", over));
            Assert.DoesNotContain(settlement.Citizens, p => p.equipment?.Primary?.def.defName.StartsWith("Gun_", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void An_industrial_landing_is_capped_at_industrial()
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("Landfall").scenario, "founders-a", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            global::SimWorld.World.Settlement settlement = game.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();
            Assert.Equal(TechLevel.Industrial, global::SimWorld.Factions.FactionAdvancement.TechLevelOf(settlement.faction!));
        }

        private const string MedievalSeed = "armoury-medieval";
        private const string IndustrialSeed = "armoury-industrial";
        private const int MedievalDays = 15;
        private const int IndustrialDays = 15;

        // ---- content: every tier has a production path ----

        public static IEnumerable<object[]> ArmouryRecipes =>
            DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .Where(r => Armoury.IsRangedRecipe(r) || Armoury.IsArmourRecipe(r))
                .Select(r => new object[] { r.defName });

        /// <summary>
        /// Every ingredient of every armoury recipe has a source the game can produce on a map: a vein map
        /// generation places, a plant's harvest, an animal's leather or meat, or a researched recipe at a bench that
        /// can itself be built, whose own ingredients and whose bench's cost have a source in turn, without needing
        /// the thing being sourced. The last clause is the electric smelter's: it costs steel and is the best steel
        /// recipe, so it is not a source of steel on its own.
        /// </summary>
        [Fact]
        public void Every_armoury_recipe_has_ingredients_with_a_source()
        {
            List<CoreRecipeDef> recipes = DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .Where(r => Armoury.IsRangedRecipe(r) || Armoury.IsArmourRecipe(r)).ToList();
            Assert.NotEmpty(recipes);
            var missing = new List<string>();
            foreach (CoreRecipeDef recipe in recipes)
            {
                Assert.NotEmpty(recipe.researchPrerequisites ?? new List<ResearchProjectDef>());
                if (!RecipeHasPath(recipe, new HashSet<ThingDef>())) missing.Add(recipe.defName);
            }
            Assert.True(missing.Count == 0, "no production path: " + string.Join(", ", missing));
        }

        /// <summary>The guard of the source rule: a recipe whose only path runs through a bench built of its own
        /// product has none.</summary>
        [Fact]
        public void Steel_has_a_source_without_the_smelter_that_is_built_of_it_and_none_through_it_alone()
        {
            Assert.True(HasSource(Def("Steel"), new HashSet<ThingDef>()));
            Assert.False(RecipeHasPath(Recipe("Smelt_Steel"), new HashSet<ThingDef> { Def("Steel") }),
                "the electric smelter costs steel, so it cannot be the first source of it");
        }

        private static bool HasSource(ThingDef def, HashSet<ThingDef> needing)
        {
            if (needing.Contains(def)) return false;
            IReadOnlyList<ThingDef> things = DefDatabase<ThingDef>.AllDefsListForReading;
            if (things.Any(t => t.mineable && t.mineableThing == def && t.mineableScatterCommonality > 0f)) return true;
            if (things.Any(t => t.plant?.harvestedThingDef == def)) return true;
            if (things.Any(t => t.race != null && (t.race.leatherDef == def || t.race.meatDef == def))) return true;
            var deeper = new HashSet<ThingDef>(needing) { def };
            return DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .Where(r => r.products != null && r.products.Any(p => p.thingDef == def))
                .Any(r => RecipeHasPath(r, deeper));
        }

        private static bool RecipeHasPath(CoreRecipeDef recipe, HashSet<ThingDef> needing)
        {
            if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) return false;
            bool bench = recipe.recipeUsers.Any(user =>
                GenConstruct.BlueprintDefFor(user) != null
                && (user.costList ?? new List<ThingDefCountClass>()).All(c => HasSource(c.thingDef, needing)));
            if (!bench) return false;
            return (recipe.ingredients ?? new List<IngredientCount>())
                .All(i => i.filter.AllowedThingDefs.Any(d => HasSource(d, needing)));
        }

        // ---- the armoury's bills drive smelting ----

        [Fact]
        public void A_plate_bill_raises_a_bloomery_and_works_back_to_the_iron_ore()
        {
            Know("StoneTools", "Copper", "Bronze", "Iron", "Steel", "SteelWeapons", "Knighthood");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            Spawn(map, "MineableSteel", 33, 33);
            for (int i = 0; i < 3; i++) Citizen(map, 2 + i, 2, "Bow_Short", "Archer" + i);

            OutfittingInitiative.Run(map);
            Assert.True(BillOn(smithy, "Make_Apparel_PlateArmor")?.targetCount > 0, "no plate bill");
            Assert.True(MaterialLadder.MetalWanted(map).TryGetValue(Def("Steel"), out int steel) && steel >= 3 * 50,
                "the plate bill wants no steel smelted");

            SmeltingInitiative.Run(map);
            Assert.True(Planned(map, "Bloomery"), "no bloomery planned for the plate's steel");

            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).ToList().ForEach(b => b.Destroy());
            Bench(map, "Bloomery", 22, 20);
            SmeltingInitiative.Run(map);
            Assert.Equal(new[] { "Make_Steel_Cementation", "Smelt_Iron" }, MetallurgyBills(map).OrderBy(s => s, StringComparer.Ordinal));
        }

        [Fact]
        public void A_crossbow_bill_asks_for_the_cheaper_iron_and_mail_for_the_better_steel()
        {
            Know("StoneTools", "Copper", "Bronze", "Iron", "Steel", "SteelWeapons", "Crossbows", "StandingArmies");
            CoreMap map = NewMap();
            Bench(map, "Smithy", 20, 20);
            Bench(map, "Bloomery", 22, 20);
            Spawn(map, "MineableSteel", 33, 33);
            Spawn(map, "WoodLog", 10, 10, 75);
            var metals = new List<ThingDef> { Def("Iron"), Def("Steel") };

            Assert.Same(Def("Iron"), Armoury.GoalMetal(map, Recipe("Make_Crossbow"), metals));
            Assert.Same(Def("Steel"), Armoury.GoalMetal(map, Recipe("Make_Apparel_ChainShirt"), metals));
        }

        // ---- a lesser tier while the best waits ----

        [Fact]
        public void Hide_coats_and_great_bows_are_made_while_plate_and_rifles_wait_on_steel()
        {
            Know("StoneTools", "Bows", "Tanning", "Copper", "Bronze", "BronzeWeapons", "Iron", "Steel", "SteelWeapons",
                "Knighthood", "StandingArmies", "Firearms");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            ThingWithComps tailor = Bench(map, "TableTailor", 24, 20);
            Spawn(map, "MineableSteel", 33, 33);
            Spawn(map, "Leather_Plain", 10, 10, 75);
            Spawn(map, "WoodLog", 10, 12, 75);
            for (int i = 0; i < 3; i++) Citizen(map, 2 + i, 2, "Bow_Short", "Archer" + i);

            OutfittingInitiative.Run(map);
            Assert.True(BillOn(smithy, "Make_Apparel_PlateArmor")?.targetCount > 0, "the best armour in reach is planned");
            Assert.True(BillOn(tailor, "Make_Apparel_HideArmor")?.targetCount > 0, "the hide coat stops while plate waits on steel");
            Assert.True(BillOn(smithy, "Make_Gun_BoltActionRifle")?.targetCount > 0, "the best gun in reach is planned");
            Assert.Null(BillOn(smithy, "Make_Bow_Short"));
            Assert.True(BillOn(smithy, "Make_Bow_Great")?.targetCount > 0, "no great bow is made while the rifle waits on steel");

            // Steel for a plate and a rifle on hand: the stop-gaps end.
            Spawn(map, "Steel", 12, 12, 75);
            Spawn(map, "Steel", 12, 13, 75);
            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(tailor, "Make_Apparel_HideArmor"));
            Assert.Null(BillOn(smithy, "Make_Bow_Great"));
            Assert.True(BillOn(smithy, "Make_Apparel_PlateArmor")?.targetCount > 0);
        }

        /// <summary>
        /// While plate is short, the metal the plate is made from is kept for it: mail, which would eat the iron the
        /// steel comes from, is no stop-gap; a helmet goes only to a citizen already in plate, and is made of
        /// iron, not the plate's steel.
        /// </summary>
        [Fact]
        public void While_plate_is_short_mail_waits_and_helmets_go_in_iron_to_those_in_plate()
        {
            Know("StoneTools", "Copper", "Bronze", "BronzeWeapons", "Phalanx", "Iron", "StandingArmies", "Steel", "SteelWeapons", "Knighthood");
            CoreMap map = NewMap();
            ThingWithComps smithy = Bench(map, "Smithy", 20, 20);
            Spawn(map, "MineableSteel", 33, 33);
            Spawn(map, "Iron", 12, 12, 75);
            Pawn knight = Citizen(map, 2, 2, "Bow_Short", "Knight");
            for (int i = 0; i < 3; i++) Citizen(map, 3 + i, 2, "Bow_Short", "Archer" + i);
            knight.apparel!.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_PlateArmor"), Def("Steel")));

            OutfittingInitiative.Run(map);

            Assert.Equal(3, BillOn(smithy, "Make_Apparel_PlateArmor")?.targetCount);
            Assert.Null(BillOn(smithy, "Make_Apparel_ChainShirt"));
            Bill_Production? helmet = BillOn(smithy, "Make_Apparel_SimpleHelmet");
            Assert.Equal(1, helmet?.targetCount);
            Assert.True(helmet!.ingredientFilter.Allows(Def("Iron")));
            Assert.False(helmet.ingredientFilter.Allows(Def("Steel")));
            Assert.Contains(Def("Steel"), Armoury.HeldMetals(map));
        }

        // ---- buried ore ----

        /// <summary>A face across deep water from the band is no face: the town cannot get a miner to it.</summary>
        [Fact]
        public void A_face_across_water_from_the_band_is_not_one_the_town_can_work()
        {
            CoreMap map = NewMap();
            for (int z = 0; z < 40; z++)
            {
                for (int x = 18; x <= 21; x++) map.terrainGrid.SetTerrain(new IntVec3(x, 0, z), TerrainDefOf.WaterDeep);
            }
            Thing far = Spawn(map, "MineableSteel", 30, 30);
            Thing near = Spawn(map, "MineableSteel", 10, 30);
            var band = new IntVec3(5, 0, 5);

            Assert.False(MiningInitiative.FaceReachable(map, far.Position, band));
            Assert.True(MiningInitiative.FaceReachable(map, near.Position, band));
            Assert.True(MiningInitiative.FaceReachable(map, far.Position, null));
        }

        [Fact]
        public void Iron_behind_rock_is_reachable_and_the_settlement_tunnels_to_it()
        {
            Know("StoneTools", "Iron");
            CoreMap map = NewMap();
            // A granite block from x 20..30, z 20..30, with iron only at its heart.
            for (int x = 20; x <= 30; x++)
            {
                for (int z = 20; z <= 30; z++) Spawn(map, Math.Abs(x - 25) <= 1 && Math.Abs(z - 25) <= 1 ? "MineableSteel" : "Granite", x, z);
            }
            Assert.True(MaterialLadder.HasReachableVein(map, Def("IronOre")));

            Thing? face = MiningInitiative.TunnelFace(map, Def("IronOre"));
            Assert.NotNull(face);
            Assert.True(WorkGiver_Miner.MayBeAccessible(face!.Position, map));
            Assert.Equal("Granite", face.def.defName);
            // The nearest face to the vein through rock: three cells of granite between the vein's edge and the
            // block's edge, on any side.
            int toVein = Math.Max(Math.Abs(face.Position.x - 25), Math.Abs(face.Position.z - 25));
            Assert.Equal(5, toVein);

            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(face.Position), DesignationDefOf.Mine));
            Assert.Null(MiningInitiative.TunnelFace(map, Def("IronOre"))); // under way

            // Sealed off entirely (no open ground beside any rock): not reachable.
            CoreMap sealedMap = NewMap(5);
            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++) Spawn(sealedMap, x == 2 && z == 2 ? "MineableSteel" : "Granite", x, z);
            }
            Assert.False(MaterialLadder.HasReachableVein(sealedMap, Def("IronOre")));
        }

        [Fact]
        public void A_smelting_bill_for_buried_ore_marks_the_tunnel()
        {
            Know("StoneTools", "Iron");
            CoreMap map = NewMap();
            for (int x = 20; x <= 30; x++)
            {
                for (int z = 20; z <= 30; z++) Spawn(map, Math.Abs(x - 25) <= 1 && Math.Abs(z - 25) <= 1 ? "MineableSteel" : "Granite", x, z);
            }
            ThingWithComps bloomery = Bench(map, "Bloomery", 10, 10);
            bloomery.GetComp<CompBillGiver>()!.BillStack.AddBill(new Bill_Production(Recipe("Smelt_Iron")) { repeatMode = BillRepeatMode.TargetCount, targetCount = 30 });
            Citizen(map, 2, 2, null, "Miner");
            var settlement = new global::SimWorld.World.Settlement(global::SimWorld.World.WorldObjectDefOf.Settlement, 0, null, "Pit", 0);

            MiningInitiative.Run(settlement, map);

            // One mark, on the face of the tunnel (the next pass finds it under way and marks no second one).
            Designation mark = Assert.Single(map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine));
            IntVec3 cell = mark.target.Cell;
            Assert.Equal("Granite", WorkGiver_Miner.FirstMineable(cell, map)!.def.defName);
            Assert.Equal(5, Math.Max(Math.Abs(cell.x - 25), Math.Abs(cell.z - 25)));
            Assert.Null(MiningInitiative.TunnelFace(map, Def("IronOre")));
            MiningInitiative.Run(settlement, map);
            Assert.Single(map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine));
        }
    }
}
