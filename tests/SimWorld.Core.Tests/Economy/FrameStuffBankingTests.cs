using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// Banking never takes the stuff a stuff-built frame was planned with and still lacks: a door framed in
    /// steel keeps the steel it waits for on the map, not just the wood a default door would have wanted
    /// (<see cref="SettlementStockInitiative.BankStoredGoods"/>). Pinned while tracing a door frame that
    /// stalled at 2 of 4 steel: the steel it lacked had gone to a bed and to construction failures, not to
    /// the ledger.
    /// </summary>
    public class FrameStuffBankingTests : ContentTestBase
    {
        public FrameStuffBankingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static readonly IntVec3 GranaryCell = new IntVec3(5, 0, 5);

        [Fact]
        public void Steel_a_steel_door_frame_still_lacks_stays_on_the_map_and_banks_once_delivered()
        {
            ThingDef steel = Def("Steel");
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Steelhome", 0);
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, GranaryCell);
            zone.filter.SetAllowAll(null);
            Thing stack = ThingMaker.MakeThing(steel);
            stack.stackCount = 30;
            GenSpawn.Spawn(stack, GranaryCell, map);

            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Door"), steel);
            GenSpawn.Spawn(frame, new IntVec3(12, 0, 12), map);
            frame.AddMaterial(steel, 2);
            Assert.True(frame.MaterialStillNeeded(steel) > 0); // fixture: the frame still waits on steel

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(30, map.listerThings.ThingsOfDef(steel).Where(t => t.Spawned).Sum(t => t.stackCount));

            frame.AddMaterial(steel, frame.MaterialStillNeeded(steel));
            Assert.Equal(30, SettlementStockInitiative.BankStoredGoods(settlement, map));
        }
    }
}
