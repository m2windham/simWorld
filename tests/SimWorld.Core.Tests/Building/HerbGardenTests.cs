using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types (CLAUDE.md).
using SettlementMedicine = global::SimWorld.Economy.SettlementMedicine;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A settlement growing its own medicine (<see cref="FarmingInitiative.RunHerbGarden"/>).
    ///
    /// <para/><b>The hole.</b> Healroot, the plant herbal medicine comes from, now exists
    /// (<see cref="HealrootTests"/>), but <see cref="FarmingInitiative"/> chooses its crop by nutrition per
    /// cell per day and herbal medicine has none -- so with nobody to drag out a zone, healroot was never in
    /// the ground. The rule these pin: when food is provided for, the settlement is short of medicine and
    /// somebody can sow it, a modest separate patch of it goes in; food wins whenever food is short.
    ///
    /// <para/>The size is derived from content and the stock the settlement wants, so what is pinned is the
    /// derivation (more people want more garden; it is a modest share of the food field) and the three
    /// conditions as on/off behaviour, not a cell count.
    /// </summary>
    public class HerbGardenTests : ContentTestBase
    {
        public HerbGardenTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 60) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Healroot => Def("Plant_Healroot");

        private static int MinSkill => Healroot.plant!.sowMinSkill;

        /// <summary>A settlement of <paramref name="citizens"/> standing on the map, the first
        /// <paramref name="skilled"/> of them skilled enough to sow healroot and the rest not. Food is
        /// provided for unless <paramref name="fed"/> is false.
        ///
        /// <para/>Stock is kept as <b>items on the map</b>, not in the ledger: <c>FarmingInitiative.Run</c>
        /// finds its settlement through the world (<c>HuntingInitiative.SettlementFor</c>), a bare test map has
        /// none, and with no settlement a pass reads the map alone. The ledger path is pinned by
        /// <c>SettlementMedicineTests</c> and by the game-driven test at the bottom of this file.</summary>
        private static Settlement SettlementWith(CoreMap map, int citizens, int skilled = 1, bool fed = true)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Herbtown", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = new Pawn(Def("Human"), "Grower" + i);
                p.skills!.GetSkill(SkillDefOf.Plants)!.Level = i < skilled ? MinSkill : MinSkill - 1;
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(i % map.Size.x, 0, 0), map);
            }
            if (fed) Feed(map, settlement);
            return settlement;
        }

        /// <summary>Lays food on the map in the far corner until it holds several times the larder the
        /// settlement wants in hand. Returns the stacks, so a test can eat them.</summary>
        private static List<Thing> Feed(CoreMap map, Settlement settlement, IntVec3? origin = null)
        {
            IntVec3 corner = origin ?? new IntVec3(map.Size.x - 1, 0, map.Size.z - 1);
            var stacks = new List<Thing>();
            ThingDef food = Def("Pemmican");
            float wanted = HuntingInitiative.NutritionWanted(settlement, map);
            for (int i = 0; HuntingInitiative.NutritionAvailable(null, map) < wanted * 3f; i++)
            {
                Thing stack = ThingMaker.MakeThing(food);
                stack.stackCount = food.stackLimit;
                GenSpawn.Spawn(stack, new IntVec3(corner.x - i % 10, 0, corner.z - i / 10), map);
                stacks.Add(stack);
            }
            return stacks;
        }

        /// <summary>Medicine lying on the map: enough to cover what the settlement wants, unless told
        /// otherwise.</summary>
        private static Thing Medicate(CoreMap map, int count, IntVec3 cell)
        {
            Thing stack = ThingMaker.MakeThing(MedicineDefOf.MedicineHerbal);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, cell, map);
            return stack;
        }

        /// <summary>Passes until the food field stops growing, then a few more: the herb garden is only
        /// considered once it has.</summary>
        private static void RunPasses(CoreMap map, int passes = 40)
        {
            for (int i = 0; i < passes; i++) FarmingInitiative.Run(map);
        }

        private static int GardenCells(CoreMap map) => FarmingInitiative.HerbGardenOf(map)?.CellCount ?? 0;

        // ---- the rule ----

        [Fact]
        public void Food_provided_for_and_medicine_short_and_a_capable_sower_means_a_herb_garden_of_healroot()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 5);
            Assert.True(SettlementMedicine.IsShort(settlement, map));
            Assert.False(FarmingInitiative.FoodIsShort(settlement, map));

            RunPasses(map);

            Zone_Growing? garden = FarmingInitiative.HerbGardenOf(map);
            Assert.NotNull(garden);
            Assert.True(garden!.CellCount > 0, "the settlement registered a herb garden with no cells in it");
            Assert.Same(Healroot, garden.plantDefToGrow);
            Assert.True(garden.allowSow);

            // And the food field is untouched: still a food crop, still the settlement's "Fields".
            Zone_Growing field = FarmingInitiative.FieldOf(map)!;
            Assert.NotSame(garden, field);
            Assert.True(FarmingInitiative.NutritionPerCellPerDay(field.plantDefToGrow!) > 0f);
        }

        [Fact]
        public void A_settlement_with_medicine_in_hand_plants_none()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 5);
            Medicate(map, SettlementMedicine.Wanted(settlement, map), new IntVec3(20, 0, 20));
            Assert.False(SettlementMedicine.IsShort(settlement, map));

            RunPasses(map);

            Assert.Null(FarmingInitiative.HerbGardenOf(map));
            Assert.NotNull(FarmingInitiative.FieldOf(map));
        }

        [Fact]
        public void When_food_is_short_food_wins_and_no_herb_garden_is_started()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 5, fed: false);
            Assert.True(FarmingInitiative.FoodIsShort(settlement, map));
            Assert.True(SettlementMedicine.IsShort(settlement, map));

            RunPasses(map);

            Assert.Null(FarmingInitiative.HerbGardenOf(map));
            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount > 0, "food short must not stop the food field");
        }

        [Fact]
        public void A_herb_garden_stops_being_sown_while_food_is_short_and_resumes_when_it_is_not()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 5);
            List<Thing> larder = map.listerThings.ThingsOfDef(Def("Pemmican")).ToList();
            RunPasses(map);
            Zone_Growing garden = FarmingInitiative.HerbGardenOf(map)!;
            int cells = garden.CellCount;
            Assert.True(garden.allowSow);

            // The larder is eaten: now food is short.
            foreach (Thing stack in larder) stack.Destroy();
            Assert.True(FarmingInitiative.FoodIsShort(settlement, map));
            RunPasses(map, 3);

            Assert.False(garden.allowSow, "the sowers' hands should be on the food field while food is short");
            Assert.Equal(cells, garden.CellCount);
            Assert.True(FarmingInitiative.FieldOf(map)!.allowSow);

            Feed(map, settlement);
            RunPasses(map, 3);

            Assert.True(garden.allowSow);
        }

        [Fact]
        public void With_nobody_able_to_sow_it_nothing_is_planted()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5, skilled: 0);

            RunPasses(map);

            Assert.Null(FarmingInitiative.HerbGardenOf(map));
        }

        [Fact]
        public void One_capable_citizen_is_enough()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 6, skilled: 1);

            Assert.True(FarmingInitiative.AnyoneCanSow(settlement, map, Healroot));
            RunPasses(map);

            Assert.NotNull(FarmingInitiative.HerbGardenOf(map));
        }

        [Fact]
        public void A_citizen_who_has_growing_work_switched_off_or_is_down_does_not_count()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 3, skilled: 1);
            Pawn grower = settlement.Citizens[0];
            Assert.True(FarmingInitiative.AnyoneCanSow(settlement, map, Healroot));

            grower.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Growing"), 0);
            Assert.False(FarmingInitiative.AnyoneCanSow(settlement, map, Healroot));

            grower.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Growing"), 3);
            Assert.True(FarmingInitiative.AnyoneCanSow(settlement, map, Healroot));

            grower.health.Kill(null, null);
            Assert.False(FarmingInitiative.AnyoneCanSow(settlement, map, Healroot));
        }

        [Fact]
        public void The_dead_do_not_count_even_if_they_could_sow()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 4, skilled: 1);
            settlement.Citizens[0].health.Kill(null, null);

            RunPasses(map);

            Assert.Null(FarmingInitiative.HerbGardenOf(map));
        }

        [Fact]
        public void The_herb_garden_waits_for_the_food_field_to_be_as_big_as_it_will_get()
        {
            CoreMap map = NewMap(80);
            Settlement settlement = SettlementWith(map, 40);
            int wanted = FarmingInitiative.FieldCellsWantedFor(settlement, map);
            Assert.True(wanted > FarmingTuning.MaxCellsPerPass * 2, "needs a field that takes several passes");

            FarmingInitiative.Run(map);
            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount < wanted);
            Assert.Null(FarmingInitiative.HerbGardenOf(map));

            RunPasses(map, 80);

            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount >= wanted
                || FarmingInitiative.HerbGardenOf(map) != null);
            Assert.NotNull(FarmingInitiative.HerbGardenOf(map));
        }

        [Fact]
        public void A_food_field_with_nowhere_left_to_grow_does_not_hold_the_medicine_back()
        {
            // Ground fit for healroot but too little of it for the field to reach the size the mouths want:
            // a 12x12 patch of soil in a sea of unsowable water, for forty mouths.
            var map = new CoreMap(40, 40, TerrainDefOf.WaterShallow);
            for (int x = 14; x < 26; x++)
            {
                for (int z = 14; z < 26; z++) map.terrainGrid.SetTerrain(new IntVec3(x, 0, z), TerrainDefOf.Soil);
            }
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Island", 0);
            for (int i = 0; i < 40; i++)
            {
                Pawn p = new Pawn(Def("Human"), "Islander" + i);
                p.skills!.GetSkill(SkillDefOf.Plants)!.Level = i == 0 ? MinSkill : 0;
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(14 + i % 12, 0, 14 + i / 12 % 12), map);
            }
            Feed(map, settlement, new IntVec3(25, 0, 25));
            int wanted = FarmingInitiative.FieldCellsWantedFor(settlement, map);

            RunPasses(map, 40);

            Zone_Growing field = FarmingInitiative.FieldOf(map)!;
            Assert.True(field.CellCount < wanted, "the test needs a field that cannot reach its size");
            Assert.NotNull(FarmingInitiative.HerbGardenOf(map));
        }

        // ---- size ----

        [Fact]
        public void More_people_want_more_garden()
        {
            CoreMap smallMap = NewMap();
            Settlement few = SettlementWith(smallMap, 4);
            CoreMap largeMap = NewMap();
            Settlement many = SettlementWith(largeMap, 30);

            int forFew = FarmingInitiative.HerbGardenCellsWantedFor(few, smallMap, Healroot);
            int forMany = FarmingInitiative.HerbGardenCellsWantedFor(many, largeMap, Healroot);

            Assert.True(forFew > 0);
            Assert.True(forMany > forFew);
        }

        [Fact]
        public void A_nobody_settlement_wants_no_garden()
        {
            CoreMap map = NewMap();
            var empty = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Ghosttown", 0);

            Assert.Equal(0, FarmingInitiative.HerbGardenCellsWantedFor(empty, map, Healroot));
        }

        [Fact]
        public void The_garden_is_a_modest_share_of_the_growing_area_not_a_second_field()
        {
            CoreMap map = NewMap(80);
            Settlement settlement = SettlementWith(map, 25);

            int garden = FarmingInitiative.HerbGardenCellsWantedFor(settlement, map, Healroot);
            int field = FarmingInitiative.FieldCellsWantedFor(settlement, map);

            Assert.True(garden > 0);
            Assert.True(garden * 4 < garden + field,
                "the herb garden (" + garden + " cells) is not a modest share beside the food field (" + field + ")");
        }

        [Fact]
        public void The_garden_stops_at_the_size_it_wants()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 4);
            int wanted = FarmingInitiative.HerbGardenCellsWantedFor(settlement, map, Healroot);

            RunPasses(map, 60);

            Assert.True(GardenCells(map) <= wanted, "the herb garden grew past what the settlement wanted");
        }

        [Fact]
        public void A_second_pass_grows_the_same_garden_rather_than_starting_another()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);

            RunPasses(map);
            RunPasses(map, 5);

            Assert.Single(map.zoneManager.AllZones.OfType<Zone_Growing>(), z => z.label == FarmingInitiative.HerbGardenLabel);
            Assert.Single(map.zoneManager.AllZones.OfType<Zone_Growing>(), z => z.label == FarmingInitiative.FieldLabel);
        }

        [Fact]
        public void The_garden_only_grows_while_the_settlement_is_short_and_follows_the_need_back_up()
        {
            // Big enough that the garden takes more than one pass to reach the size it wants.
            CoreMap map = NewMap(100);
            Settlement settlement = SettlementWith(map, 50);
            for (int pass = 0; pass < 200 && GardenCells(map) == 0; pass++) FarmingInitiative.Run(map);
            int partial = GardenCells(map);
            Assert.True(partial > 0);
            Assert.True(partial < FarmingInitiative.HerbGardenCellsWantedFor(settlement, map, Healroot),
                "the test needs a garden that is still growing");

            // Medicine arrives (a harvest, a trader): the settlement is no longer short, so the garden stops
            // where it is -- it is neither cleared nor extended.
            Thing chest = Medicate(map, SettlementMedicine.Wanted(settlement, map), new IntVec3(30, 0, 30));
            RunPasses(map, 60);
            Assert.Equal(partial, GardenCells(map));
            Assert.NotNull(FarmingInitiative.HerbGardenOf(map));

            // A bad week spends it all; the need is back and the garden grows again.
            chest.Destroy();
            RunPasses(map, 60);
            Assert.True(GardenCells(map) > partial);
        }

        // ---- the garden is worked ----

        [Fact]
        public void The_capable_citizen_is_offered_the_gardens_cells_and_the_clumsy_one_is_not()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 3, skilled: 1);
            RunPasses(map);
            Zone_Growing garden = FarmingInitiative.HerbGardenOf(map)!;

            Pawn capable = settlement.Citizens[0];
            Pawn clumsy = settlement.Citizens[1];
            var giver = new WorkGiver_GrowerSow();
            IntVec3 cell = garden.Cells[0];

            Assert.True(giver.HasJobOnCell(capable, cell));
            Assert.False(giver.HasJobOnCell(clumsy, cell));
        }

        [Fact]
        public void The_gardens_cells_are_ground_healroot_can_actually_be_sown_in()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);
            RunPasses(map);
            Zone_Growing garden = FarmingInitiative.HerbGardenOf(map)!;

            Assert.All(garden.Cells, c =>
                Assert.True(map.terrainGrid.TerrainAt(c).fertility >= Healroot.plant!.sowMinFertility));
        }

        [Fact]
        public void On_ground_too_poor_for_healroot_no_garden_is_painted()
        {
            var map = new CoreMap(40, 40, TerrainDefOf.Sand);
            Settlement settlement = SettlementWith(map, 5);
            Assert.True(TerrainDefOf.Sand.fertility < Healroot.plant!.sowMinFertility);

            RunPasses(map);

            Assert.Null(FarmingInitiative.MedicineCropFor(map));
            Assert.Null(FarmingInitiative.HerbGardenOf(map));
            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount > 0, "the food field is for poorer ground and should still be painted");
        }

        // ---- the crop ----

        [Fact]
        public void The_medicine_crop_is_one_that_makes_medicine_and_the_food_crop_is_still_one_that_feeds()
        {
            CoreMap map = NewMap();

            ThingDef? medicineCrop = FarmingInitiative.MedicineCropFor(map);
            ThingDef? foodCrop = FarmingInitiative.CropFor(map);

            Assert.NotNull(medicineCrop);
            Assert.NotNull(foodCrop);
            Assert.True(medicineCrop!.plant!.harvestedThingDef!.IsMedicine);
            Assert.False(foodCrop!.plant!.harvestedThingDef!.IsMedicine);
            Assert.NotSame(medicineCrop, foodCrop);
            Assert.True(FarmingInitiative.MedicinePerCellPerDay(foodCrop) == 0f);
            Assert.True(FarmingInitiative.MedicinePerCellPerDay(medicineCrop) > 0f);
        }

        // ---- save and load ----

        [Fact]
        public void The_herb_garden_survives_a_save_and_load_and_is_not_duplicated()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);
            RunPasses(map);
            Zone_Growing before = FarmingInitiative.HerbGardenOf(map)!;
            int cells = before.CellCount;
            bool allowSow = before.allowSow;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Zone_Growing? after = FarmingInitiative.HerbGardenOf(loaded);
            Assert.NotNull(after);
            Assert.Equal(cells, after!.CellCount);
            Assert.Same(Healroot, after.plantDefToGrow);
            Assert.Equal(allowSow, after.allowSow);
            Assert.Equal(FarmingInitiative.HerbGardenLabel, after.label);

            // A loaded map resumes mid-garden with no catch-up step: whatever the next pass decides, it
            // neither duplicates nor removes the garden it was saved with.
            FarmingInitiative.Run(loaded);
            Assert.Single(loaded.zoneManager.AllZones.OfType<Zone_Growing>(), z => z.label == FarmingInitiative.HerbGardenLabel);
        }
    }
}
