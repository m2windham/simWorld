using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// A settlement never banks into its ledger the material a blueprint or frame on its map still needs
    /// (<see cref="SettlementStockInitiative.BankStoredGoods"/>). The ledger cannot hand goods back to a map,
    /// so banked logs were lost to the house waiting on them; on the <c>delta</c> layout seed under Mono the
    /// band banked 48 logs while its houses waited on felling. In RimWorld a stockpiled log stays where a
    /// builder can fetch it.
    /// </summary>
    public class ConstructionMaterialBankingTests : ContentTestBase
    {
        public ConstructionMaterialBankingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wood => Def("WoodLog");

        private static readonly IntVec3 GranaryCell = new IntVec3(5, 0, 5);

        /// <summary>A settlement with <paramref name="logs"/> logs resting in a stockpile on its map.</summary>
        private static (Settlement Settlement, CoreMap Map) WoodInTheGranary(int logs)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Woodhome", 0);
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, GranaryCell);
            zone.filter.SetAllowAll(null);
            Thing stack = ThingMaker.MakeThing(Wood);
            stack.stackCount = logs;
            GenSpawn.Spawn(stack, GranaryCell, map);
            return (settlement, map);
        }

        private static int WoodOnMap(CoreMap map) =>
            System.Linq.Enumerable.Sum(map.listerThings.ThingsOfDef(Wood), t => t.Spawned ? t.stackCount : 0);

        [Fact]
        public void Wood_a_house_blueprint_needs_stays_on_the_map()
        {
            (Settlement settlement, CoreMap map) = WoodInTheGranary(50);
            Assert.Contains(Def("House_Paleo").costList!, e => ReferenceEquals(e.thingDef, Wood)); // fixture: a house costs wood
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_House_Paleo")), new IntVec3(12, 0, 12), map);

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(0, settlement.StoreCountOf(Wood));
            Assert.Equal(50, WoodOnMap(map));
        }

        [Fact]
        public void Wood_a_frame_still_lacks_stays_on_the_map_and_banks_once_it_has_all_it_needs()
        {
            (Settlement settlement, CoreMap map) = WoodInTheGranary(50);
            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_House_Paleo"));
            GenSpawn.Spawn(frame, new IntVec3(12, 0, 12), map);

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(50, WoodOnMap(map));

            frame.AddMaterial(Wood, frame.MaterialStillNeeded(Wood));
            Assert.Equal(50, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(50, settlement.StoreCountOf(Wood));
        }

        [Fact]
        public void With_nothing_planned_the_wood_is_banked_as_before()
        {
            (Settlement settlement, CoreMap map) = WoodInTheGranary(50);

            Assert.Equal(50, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(50, settlement.StoreCountOf(Wood));
        }
    }
}
