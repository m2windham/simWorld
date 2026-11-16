using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Farming;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Healroot, the crop herbal medicine comes from (RimWorld: <c>Plant_Healroot</c>), and the settlement
    /// planting it when its medicine runs low (<c>HealrootForMedicine</c>, <see cref="CropDemandWorker_Medicine"/>).
    /// <para/>
    /// Found on a threat-free founding band (<c>docs/perf/storyteller-pressure</c>): the scenario's herbal
    /// medicine ran down and nothing made more, because the only thing that ever chose a crop chose by
    /// nutrition and medicine has none.
    /// </summary>
    public class HealrootTests : ContentTestBase
    {
        public HealrootTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement SettlementWith(CoreMap map, int citizens, int plantsSkill)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Herbtown", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = new Pawn(Human, "Grower" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(i % map.Size.x, 0, 0), map);
                p.skills!.GetSkill(SkillDefOf.Plants)!.Level = plantsSkill;
            }
            return settlement;
        }

        private static CropDemandDef Healroot => DefDatabase<CropDemandDef>.GetNamed("HealrootForMedicine");

        private static Zone_Growing? Field(CoreMap map) => CropDemandFields.FieldOf(map, Healroot.fieldLabel);

        private static void PutMedicine(CoreMap map, int count)
        {
            Thing meds = ThingMaker.MakeThing(Def("MedicineHerbal"));
            meds.stackCount = count;
            GenSpawn.Spawn(meds, new IntVec3(20, 0, 20), map);
        }

        [Fact]
        public void Healroot_ships_and_harvests_into_herbal_medicine_with_RimWorlds_sowing_gate()
        {
            ThingDef healroot = Def("Plant_Healroot");
            Assert.Same(Def("MedicineHerbal"), healroot.plant!.harvestedThingDef);
            Assert.True(healroot.plant.sowMinSkill > 0);
            Assert.Equal(0f, FarmingInitiative.NutritionPerCellPerDay(healroot));
        }

        [Fact]
        public void The_food_field_never_chooses_healroot_and_the_medicine_field_does()
        {
            Assert.Empty(Content.Result.Errors);
            CoreMap map = NewMap();
            Assert.NotSame(Def("Plant_Healroot"), FarmingInitiative.CropFor(map));
            Assert.Same(Def("Plant_Healroot"), Healroot.crop);
            Assert.IsType<CropDemandWorker_Medicine>(Healroot.Worker);
        }

        [Fact]
        public void The_field_is_one_cell_per_dose_short_and_shrinks_as_stock_rises()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 5, plantsSkill: 10);
            PutMedicine(map, 1);
            Thing stock = map.listerThings.ThingsOfDef(Def("MedicineHerbal"))[0];
            CropDemandWorker worker = Healroot.Worker;

            int wanted = CropDemandWorker_Medicine.MedicineWanted(town);
            Assert.Equal(wanted - 1, worker.CellsWanted(map, town));
            stock.stackCount = 3;
            Assert.Equal(wanted - 3, worker.CellsWanted(map, town));
            stock.stackCount = wanted;
            Assert.Equal(0, worker.CellsWanted(map, town));
            Assert.Equal(0, worker.CellsWanted(map, null));
        }

        [Fact]
        public void A_settlement_short_of_medicine_paints_a_healroot_field_it_can_sow()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 5, plantsSkill: 10);
            PutMedicine(map, 2);

            CropDemandFields.Run(map, town);

            Zone_Growing? field = Field(map);
            Assert.NotNull(field);
            Assert.Same(Def("Plant_Healroot"), field!.plantDefToGrow);
            Assert.True(field.allowSow);
            Assert.True(field.CellCount > 0);
        }

        [Fact]
        public void A_well_stocked_settlement_does_not_sow_healroot()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 5, plantsSkill: 10);
            PutMedicine(map, 20);

            CropDemandFields.Run(map, town);
            Assert.Null(Field(map));
        }

        [Fact]
        public void Running_low_again_after_restocking_turns_sowing_back_on()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 5, plantsSkill: 10);
            PutMedicine(map, 20);
            Thing stock = map.listerThings.ThingsOfDef(Def("MedicineHerbal"))[0];

            stock.stackCount = 1;
            CropDemandFields.Run(map, town);
            Assert.True(Field(map)!.allowSow);

            stock.stackCount = 20;
            CropDemandFields.Run(map, town);
            Assert.False(Field(map)!.allowSow);
        }

        [Fact]
        public void Nobody_skilled_enough_means_no_field_and_no_sowing()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 5, plantsSkill: 2);
            PutMedicine(map, 1);

            CropDemandFields.Run(map, town);
            Assert.Null(Field(map));

            Pawn novice = town.Citizens[0];
            Assert.False(WorkGiver_GrowerSow.CanSow(novice, Def("Plant_Healroot")));
            Assert.True(WorkGiver_GrowerSow.CanSow(novice, Def("Plant_Potato")));
        }

        [Fact]
        public void An_unskilled_grower_finds_no_sowing_work_in_a_healroot_field()
        {
            CoreMap map = NewMap();
            Settlement town = SettlementWith(map, 1, plantsSkill: 2);
            var field = new Zone_Growing { label = "test", plantDefToGrow = Def("Plant_Healroot"), allowSow = true };
            map.zoneManager.RegisterZone(field);
            var cell = new IntVec3(10, 0, 10);
            map.zoneManager.AddCell(field, cell);

            var giver = new WorkGiver_GrowerSow();
            Pawn grower = town.Citizens[0];
            Assert.False(giver.HasJobOnCell(grower, cell));

            grower.skills!.GetSkill(SkillDefOf.Plants)!.Level = 8;
            Assert.True(giver.HasJobOnCell(grower, cell));
        }
    }
}
