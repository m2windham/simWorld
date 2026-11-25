using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// The armoury (era-arc finding 5: eras buy strength, not bodies): every era's research makes a weapon or
    /// armour a citizen carries, the smithy and tailor make the best one within reach for whoever it would improve,
    /// and a citizen takes it up.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ArmouryTests : ContentTestBase
    {
        public ArmouryTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreRecipeDef Recipe(string name) => DefDatabase<CoreRecipeDef>.GetNamed(name);

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

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

        private static CompBillGiver Bench(CoreMap map, string def, int x, int z) =>
            ((ThingWithComps)Spawn(map, def, x, z)).GetComp<CompBillGiver>()!;

        private static Bill_Production? BillOn(CompBillGiver bench, string recipe) =>
            bench.BillStack.Bills.OfType<Bill_Production>().SingleOrDefault(b => b.recipe.defName == recipe);

        private static Pawn Citizen(CoreMap map, int x, int z, string? weapon = null, string name = "Citizen", int melee = 3, int shooting = 8)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map);
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon)));
            p.skills!.GetSkill(SkillDefOf.Melee)!.Level = melee;
            p.skills.GetSkill(SkillDefOf.Shooting)!.Level = shooting;
            return p;
        }

        // ---- content: every era arms its people ----

        public static IEnumerable<object[]> EraArms => new[]
        {
            new object[] { "Bow_Short", "Agrarian" },
            new object[] { "Apparel_HideArmor", "Agrarian" },
            new object[] { "Bow_Recurve", "Bronze" },
            new object[] { "Apparel_SimpleHelmet", "Bronze" },
            new object[] { "Bow_Great", "Classical" },
            new object[] { "Apparel_ChainShirt", "Classical" },
            new object[] { "Crossbow", "Medieval" },
            new object[] { "Gun_Musket", "Medieval" },
            new object[] { "Apparel_PlateArmor", "Medieval" },
            new object[] { "Gun_Revolver", "Industrial" },
            new object[] { "Gun_BoltActionRifle", "Industrial" },
            new object[] { "Apparel_FlakVest", "Industrial" },
            new object[] { "Gun_AssaultRifle", "Information" },
        };

        [Theory]
        [MemberData(nameof(EraArms))]
        public void Each_eras_arms_are_made_at_a_bench_from_that_eras_research(string thing, string era)
        {
            ThingDef def = Def(thing);
            CoreRecipeDef? recipe = DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .SingleOrDefault(r => r.products != null && r.products.Any(p => p.thingDef == def));
            Assert.NotNull(recipe);
            Assert.NotEmpty(recipe!.recipeUsers!);
            Assert.Same(DefDatabase<EraDef>.GetNamed(era), EraAvailability.EraOf(def));
            Assert.True(Armoury.IsRangedRecipe(recipe) || Armoury.IsArmourRecipe(recipe), thing + " is neither a gun nor armour to the armoury");
        }

        [Fact]
        public void Each_eras_missile_weapon_outshoots_the_last()
        {
            string[] ladder = { "Bow_Short", "Bow_Recurve", "Bow_Great", "Crossbow", "Gun_Musket", "Gun_BoltActionRifle", "Gun_AssaultRifle" };
            for (int i = 1; i < ladder.Length; i++)
            {
                Assert.True(Armoury.RangedScore(Def(ladder[i])) > Armoury.RangedScore(Def(ladder[i - 1])),
                    ladder[i] + " should outshoot " + ladder[i - 1]);
            }
        }

        [Fact]
        public void Each_eras_armour_outscores_the_last_and_clothing_is_not_armour()
        {
            string[] ladder = { "Apparel_HideArmor", "Apparel_ChainShirt", "Apparel_PlateArmor" };
            for (int i = 1; i < ladder.Length; i++)
            {
                Assert.True(Armoury.ArmourScore(Def(ladder[i])) > Armoury.ArmourScore(Def(ladder[i - 1])));
                Assert.True(Def(ladder[i]).apparel!.ConflictsWith(Def(ladder[i - 1]).apparel!), "one coat of armour at a time");
            }
            foreach (string clothing in new[] { "Apparel_Shirt", "Apparel_Pants", "Apparel_TribalRobe", "Apparel_Parka" })
            {
                Assert.False(Armoury.IsArmour(Def(clothing)), clothing);
            }
        }

        // ---- ranged bills ----

        [Fact]
        public void The_smithy_makes_the_best_bow_researched_for_the_citizens_it_would_improve()
        {
            Know("Bows");
            CoreMap map = NewMap();
            CompBillGiver smithy = Bench(map, "Smithy", 10, 10);
            Spawn(map, "WoodLog", 8, 8, 75);
            Citizen(map, 1, 1, "Bow_Short", "Archer");
            Citizen(map, 2, 1, null, "Unarmed shooter");
            Citizen(map, 3, 1, null, "Unarmed fighter", melee: 8, shooting: 3);
            Citizen(map, 4, 1, "Gun_Revolver", "Gunner");

            OutfittingInitiative.Run(map);
            Assert.Equal(1, BillOn(smithy, "Make_Bow_Short")!.targetCount); // the unarmed shooter only

            Know("BronzeWeapons");
            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(smithy, "Make_Bow_Short"));
            Assert.Equal(2, BillOn(smithy, "Make_Bow_Recurve")!.targetCount); // and the archer; never the gunner
        }

        [Fact]
        public void A_bow_whose_materials_are_out_of_reach_waits_and_the_best_one_in_reach_is_made()
        {
            Know("Bows", "Crossbows");
            CoreMap map = NewMap();
            CompBillGiver smithy = Bench(map, "Smithy", 10, 10);
            Spawn(map, "WoodLog", 8, 8, 75);
            Citizen(map, 1, 1, "Bow_Short", "Archer");
            Citizen(map, 2, 1, null, "Recruit");

            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(smithy, "Make_Crossbow"));
            Assert.Equal(1, BillOn(smithy, "Make_Bow_Short")!.targetCount);

            Spawn(map, "Steel", 8, 9, 30);
            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(smithy, "Make_Bow_Short"));
            Assert.Equal(2, BillOn(smithy, "Make_Crossbow")!.targetCount);
        }

        [Fact]
        public void A_citizen_trades_a_bow_for_a_better_one_and_never_down()
        {
            CoreMap map = NewMap();
            Thing recurve = Spawn(map, "Bow_Recurve", 10, 10);
            var giver = new JobGiver_PickUpOpportunisticWeapon { maxDistance = 30f };
            Pawn archer = Citizen(map, 1, 1, "Bow_Short", "Archer");
            Pawn veteran = Citizen(map, 2, 1, "Bow_Great", "Veteran");

            Assert.True(MaterialLadder.IsBetterWeapon(recurve, archer.equipment.Primary, archer));
            Assert.False(MaterialLadder.IsBetterWeapon(recurve, veteran.equipment.Primary, veteran));
            Assert.Same(recurve, giver.TryIssueJobPackage(archer).Job?.targetA.Thing);
            Assert.Null(giver.TryIssueJobPackage(veteran).Job);
        }

        [Fact]
        public void Wood_on_the_floor_becomes_a_recurve_bow_in_the_archers_hands()
        {
            Know("Bows", "BronzeWeapons");
            CoreMap map = NewMap();
            Bench(map, "Smithy", 10, 10);
            Spawn(map, "WoodLog", 8, 8, 40);
            Pawn archer = Citizen(map, 1, 1, "Bow_Short", "Archer");
            archer.workSettings.DisableAll();
            archer.workSettings.SetPriority(WorkTypeDefOf.Smithing, 3);

            OutfittingInitiative.Run(map);
            RunTicks(6000, archer);

            Assert.Equal("Bow_Recurve", archer.equipment.Primary?.def.defName);
        }

        // ---- armour bills ----

        [Fact]
        public void The_best_armour_in_reach_is_made_for_whoever_would_wear_it_and_a_lesser_coat_stops()
        {
            Know("Tanning");
            CoreMap map = NewMap();
            CompBillGiver tailor = Bench(map, "TableTailor", 10, 10);
            CompBillGiver smithy = Bench(map, "Smithy", 14, 10);
            Spawn(map, "Leather_Plain", 8, 8, 75);
            Pawn robed = Citizen(map, 1, 1, name: "Robed");
            robed.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_TribalRobe")));
            Citizen(map, 2, 1, name: "Bare");

            OutfittingInitiative.Run(map);
            Assert.Equal(2, BillOn(tailor, "Make_Apparel_HideArmor")!.targetCount);

            Know("StandingArmies");
            Spawn(map, "Iron", 8, 9, 75);
            Pawn mailed = Citizen(map, 3, 1, name: "Mailed");
            mailed.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_ChainShirt"), Def("Iron")));
            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(tailor, "Make_Apparel_HideArmor"));
            Assert.Equal(2, BillOn(smithy, "Make_Apparel_ChainShirt")!.targetCount); // not the one already in mail
        }

        /// <summary>
        /// The armoury's plan carried through to people in armour, on a real generated map: the shape of seed
        /// 40/201 (era-arc §12.3), where the leather lay far from the tailor and nobody ever wore a coat.
        /// Nothing here is called by hand after the setup: the outfitting planner queues the coat, a citizen
        /// fetches the leather and makes it, and the apparel optimizer puts it on.
        /// </summary>
        [Fact]
        public void The_armourys_hide_coats_are_made_from_distant_leather_and_worn_within_two_days()
        {
            CraftingTown town = CraftingTown.Open("armoury-worn");
            Know("Tanning");
            town.Clear("Leather_Plain", "Apparel_HideArmor");
            Thing tailor = town.Bench("TableTailor");
            IntVec3 far = town.FarCell(tailor.Position, 40);
            town.Stack("Leather_Plain", 75, far);
            town.Stack("Leather_Plain", 75, town.FarCell(tailor.Position, 40, c => (c - far).LengthHorizontal >= 3));
            town.Crafter(WorkTypeDefOf.Tailoring);

            int Wearing() => town.Citizens.Count(p => !p.Dead && p.apparel.WornApparel.Any(a => a.def.defName == "Apparel_HideArmor"));
            int ticks = town.RunUntil(() => Wearing() > 0, 2 * GenDate.TicksPerDay);

            Assert.True(ticks < 2 * GenDate.TicksPerDay, "ran out of time");
            Assert.True(Wearing() > 0, "nobody wears a hide coat; made " + town.Made("Apparel_HideArmor"));
        }

        // ---- save/load ----

        [Fact]
        public void A_citizen_in_era_arms_round_trips_through_a_save()
        {
            Pawn soldier = NewHuman("Soldier");
            soldier.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Crossbow")));
            soldier.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_PlateArmor"), Def("Steel")));
            float armour = soldier.apparel.WornApparel.Single().GetStatValue(ArmouryDefOf.ArmorRating_Sharp);

            string xml = Scribe.SaveToString(soldier, "pawn");
            Pawn.ResetThingIdCounter();
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal("Crossbow", loaded.equipment.Primary?.def.defName);
            ThingWithComps plate = loaded.apparel.WornApparel.Single();
            Assert.Equal("Apparel_PlateArmor", plate.def.defName);
            Assert.Equal("Steel", plate.Stuff?.defName);
            Assert.Equal(armour, plate.GetStatValue(ArmouryDefOf.ArmorRating_Sharp));
        }
    }
}
