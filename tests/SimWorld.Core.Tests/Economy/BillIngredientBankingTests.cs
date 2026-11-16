using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using Zone_Stockpile = global::SimWorld.Building.Zone_Stockpile;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <see cref="SettlementStockInitiative.BankStoredGoods"/> leaves on the map what a bill standing on one
    /// of its benches would take, as it already leaves medicine: banked, the cloth a tailor's bill is waiting
    /// on would leave the map and the bill could never be worked. With no such bill it banks as before.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BillIngredientBankingTests : ContentTestBase
    {
        public BillIngredientBankingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static (Settlement, CoreMap, Thing) ClothInAStockpile()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Loomhome", 0);
            var cell = new IntVec3(5, 0, 5);
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, cell);
            zone.filter.SetAllowAll(null);
            Thing cloth = ThingMaker.MakeThing(Def("Cloth"));
            cloth.stackCount = 40;
            GenSpawn.Spawn(cloth, cell, map);
            return (settlement, map, cloth);
        }

        [Fact]
        public void Cloth_a_standing_tailoring_bill_would_take_stays_on_the_map()
        {
            (Settlement settlement, CoreMap map, Thing cloth) = ClothInAStockpile();
            var bench = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableTailor")), new IntVec3(12, 0, 12), map);
            bench.GetComp<CompBillGiver>()!.BillStack.AddBill(
                new Bill_Production(DefDatabase<RecipeDef>.GetNamed("Make_Apparel_Shirt")) { repeatMode = BillRepeatMode.TargetCount, targetCount = 1 });

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.True(cloth.Spawned);
            Assert.Equal(40, cloth.stackCount);
            Assert.Equal(0, settlement.StoreCountOf(Def("Cloth")));
        }

        [Fact]
        public void Cloth_no_bill_wants_is_banked_as_before()
        {
            (Settlement settlement, CoreMap map, Thing cloth) = ClothInAStockpile();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableTailor")), new IntVec3(12, 0, 12), map); // a bench, no bill

            Assert.Equal(40, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.True(cloth.Destroyed);
            Assert.Equal(40, settlement.StoreCountOf(Def("Cloth")));
        }
    }
}
