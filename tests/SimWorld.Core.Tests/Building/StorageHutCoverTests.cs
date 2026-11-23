using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Storage follows what needs covering (<see cref="StorageHutNeed.StacksNeedingCover"/>): a hut is wanted
    /// for goods the weather would wear away, as RimWorld roofs a store against deterioration, and not for a
    /// larder of grain or a heap of stone that keeps in the open. The defect: a 40-strong band holding a month
    /// of raw rice was asked for a hut per three stacks of it, over a hundred, and built them instead of
    /// houses. Asserted as huts against goods, never as a literal count.
    /// <para/>
    /// Nothing to save: the need is read off <see cref="Settlement.Stores"/> each pass (see
    /// <c>StorageHutNeedTests</c>).
    /// </summary>
    [Collection("GlobalDefs")]
    public class StorageHutCoverTests : ContentTestBase
    {
        public StorageHutCoverTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Hut => ConstructionThingDefOf.StorageHut;

        private static Settlement Hoard() => new Settlement(WorldObjectDefOf.Settlement, 0, null, "Hoard", 0);

        private static bool Deteriorates(ThingDef def) =>
            def.CanEverDeteriorate && def.GetStatValueAbstract(DeteriorationDefOf.DeteriorationRate) > 0f;

        private static int Huts(CoreMap map) =>
            map.listerThings.ThingsOfDef(Hut).Count
            + map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().Count(bp => ReferenceEquals(bp.EntityToBuild, Hut));

        /// <summary>Goods that keep outside: a month of grain for a big band, and a quarry's worth of stone.</summary>
        private static readonly string[] KeepsOutside = { "RawRice", "ChunkGranite", "BlocksGranite" };

        private static void StockWhatKeepsOutside(Settlement settlement, int stacksEach)
        {
            foreach (string name in KeepsOutside)
            {
                ThingDef def = Def(name);
                Assert.False(Deteriorates(def), name + " wears away outside, so it cannot stand for goods that keep");
                settlement.AddStore(def, stacksEach * def.stackLimit);
            }
        }

        [Fact]
        public void Goods_that_keep_in_the_open_ask_for_no_storage_however_much_of_them_there_is()
        {
            Settlement settlement = Hoard();
            StockWhatKeepsOutside(settlement, stacksEach: 100);

            Assert.True(StorageHutNeed.StacksHeld(settlement) >= 300, "the ledger should be holding hundreds of stacks");
            Assert.Equal(0, StorageHutNeed.StacksNeedingCover(settlement));
            Assert.Equal(0, StorageHutNeed.Target(settlement));
        }

        [Fact]
        public void Only_what_the_weather_would_ruin_raises_the_need_so_a_full_granary_adds_nothing_to_the_timber()
        {
            ThingDef wood = Def("WoodLog");
            Assert.True(Deteriorates(wood), "this test needs wood to be something a roof protects");

            Settlement timber = Hoard();
            timber.AddStore(wood, 10 * wood.stackLimit);

            Settlement timberAndGrain = Hoard();
            timberAndGrain.AddStore(wood, 10 * wood.stackLimit);
            StockWhatKeepsOutside(timberAndGrain, stacksEach: 100);

            Assert.True(StorageHutNeed.Target(timber) > 0, "ten stacks of logs should want a roof");
            Assert.Equal(StorageHutNeed.Target(timber), StorageHutNeed.Target(timberAndGrain));
        }

        [Fact]
        public void A_settlement_sitting_on_a_large_larder_and_stone_plans_no_huts_but_plans_them_for_its_wood()
        {
            Settlement settlement = Hoard();
            var map = new CoreMap(60, 60, SimWorld.Map.TerrainDefOf.Soil);
            StockWhatKeepsOutside(settlement, stacksEach: 100);

            int pass = 0;
            for (; pass < 50; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
            Assert.Equal(0, Huts(map));

            // The same initiative does still roof what needs it.
            ThingDef wood = Def("WoodLog");
            settlement.AddStore(wood, 6 * wood.stackLimit);
            for (; pass < 100; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
            int huts = Huts(map);
            Assert.True(huts > 0, "six stacks of logs were left without a roof");
            Assert.True(huts * StorageHutNeed.StacksPerStorageHut >= StorageHutNeed.StacksNeedingCover(settlement));
            Assert.True((huts - 1) * StorageHutNeed.StacksPerStorageHut < StorageHutNeed.StacksNeedingCover(settlement),
                huts + " huts for " + StorageHutNeed.StacksNeedingCover(settlement) + " stacks: the grain and stone were counted too");
        }
    }
}
