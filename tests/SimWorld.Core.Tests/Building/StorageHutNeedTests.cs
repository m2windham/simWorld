using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// How many storage huts a settlement wants (<see cref="StorageHutNeed"/>): enough to hold what it has
    /// in store, counted in stacks, and no more. Asserted as huts against stock, never as a literal count;
    /// the stacks a hut holds is a figure nobody could source.
    /// <para/>
    /// There is nothing here to save: the need is read off <see cref="Settlement.Stores"/> on every pass
    /// and keeps no state of its own. <c>SettlementTests</c> owns the ledger's Scribe round trip.
    /// </summary>
    [Collection("GlobalDefs")]
    public class StorageHutNeedTests : ContentTestBase
    {
        public StorageHutNeedTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Hut => ConstructionThingDefOf.StorageHut;

        /// <summary>A settlement with nobody in it, so storage is all it ever plans.</summary>
        private static Settlement Hoard() => new Settlement(WorldObjectDefOf.Settlement, 0, null, "Hoard", 0);

        /// <summary>Huts standing or planned on <paramref name="map"/>.</summary>
        private static int Huts(CoreMap map) =>
            map.listerThings.ThingsOfDef(Hut).Count
            + map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().Count(bp => ReferenceEquals(bp.EntityToBuild, Hut));

        /// <summary>Gated passes from <paramref name="from"/> on, until one places nothing. Returns the next
        /// pass to run.</summary>
        private static int RunUntilSettled(Settlement settlement, CoreMap map, int from = 0)
        {
            for (int pass = from; pass < from + 400; pass++)
            {
                int before = Huts(map);
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
                if (Huts(map) == before) return pass + 1;
            }
            Assert.Fail("the settlement was still planning storage after 400 passes");
            return 0;
        }

        [Fact]
        public void A_settlement_with_nothing_in_store_wants_no_storage()
        {
            Settlement settlement = Hoard();
            var map = new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);
            RunUntilSettled(settlement, map);

            Assert.Equal(0, StorageHutNeed.Target(settlement));
            Assert.Equal(0, Huts(map));
        }

        [Fact]
        public void The_need_stops_when_the_huts_hold_the_stock_and_starts_again_when_the_stock_outgrows_them()
        {
            ThingDef wood = Def("WoodLog");
            Settlement settlement = Hoard();
            var map = new CoreMap(60, 60, SimWorld.Map.TerrainDefOf.Soil);

            // A good many stacks of wood, the last of them part full.
            settlement.AddStore(wood, 10 * wood.stackLimit + 1);
            int next = RunUntilSettled(settlement, map);
            int first = Huts(map);

            // Enough to hold it, and not one hut more than that takes.
            int stacks = StorageHutNeed.StacksHeld(settlement);
            Assert.True(first > 1, "eleven stacks should not fit in one hut");
            Assert.True(first * StorageHutNeed.StacksPerStorageHut >= stacks, first + " huts do not hold " + stacks + " stacks");
            Assert.True((first - 1) * StorageHutNeed.StacksPerStorageHut < stacks, first + " huts is more than " + stacks + " stacks need");

            // Covered: more passes plan nothing more, and neither does topping up the stack that had room.
            settlement.AddStore(wood, wood.stackLimit - 1);
            next = RunUntilSettled(settlement, map, next);
            Assert.Equal(first, Huts(map));

            // Outgrown: the stock doubles and the storage follows it.
            settlement.AddStore(wood, 11 * wood.stackLimit);
            next = RunUntilSettled(settlement, map, next);
            int grown = Huts(map);
            Assert.True(grown > first, "twice the stock is held in the same " + first + " huts");
            Assert.True(grown * StorageHutNeed.StacksPerStorageHut >= StorageHutNeed.StacksHeld(settlement));

            // Run down: nothing is torn down, and nothing more is asked for until the stock passes what
            // stands again.
            settlement.AddStore(wood, -20 * wood.stackLimit);
            Assert.True(StorageHutNeed.Target(settlement) < grown);
            RunUntilSettled(settlement, map, next);
            Assert.Equal(grown, Huts(map));
        }

        [Fact]
        public void Goods_are_counted_in_stacks_so_a_bands_rations_do_not_ask_for_a_hut_a_head()
        {
            // The defect: one hut per fifty units, whatever the unit. Pemmican stacks far deeper than wood;
            // the same number of units of each is not the same amount to store.
            ThingDef wood = Def("WoodLog");
            ThingDef pemmican = SimWorld.Economy.SettlementLarder.ProvisionDef()!;
            Assert.True(pemmican.stackLimit > wood.stackLimit, "this test needs a ration that stacks deeper than wood");
            int units = 12 * pemmican.stackLimit;

            Settlement rations = Hoard();
            rations.AddStore(pemmican, units);
            Settlement timber = Hoard();
            timber.AddStore(wood, units);

            Assert.True(StorageHutNeed.Target(rations) < StorageHutNeed.Target(timber),
                units + " pemmican wants as much storage as " + units + " logs");

            // What a founding band is given to eat is a few huts' worth, not a hut for every citizen.
            int band = SettlementTuning.FoundingBandRange.max;
            Settlement founders = Hoard();
            for (int i = 0; i < band; i++) founders.AddCitizen(NewHuman("Citizen" + i));
            SimWorld.Economy.SettlementLarder.ProvisionFoundingBand(founders);
            Assert.True(StorageHutNeed.StacksHeld(founders) > 0, "the band was given nothing, so this pins nothing");
            Assert.True(StorageHutNeed.Target(founders) < band / 2,
                "a band of " + band + " wants " + StorageHutNeed.Target(founders) + " huts for its rations alone");
        }

        [Fact]
        public void A_settlement_with_no_map_wants_the_same_storage_as_one_with_a_map()
        {
            ThingDef wood = Def("WoodLog");
            int stock = 7 * wood.stackLimit;

            // A few hands each, since the path with no map builds at a rate its citizens set; too few
            // for a compound, so the map's huts are simply counted.
            Settlement mapped = Hoard();
            for (int i = 0; i < 3; i++) mapped.AddCitizen(NewHuman("Mapped" + i));
            mapped.AddStore(wood, stock);
            var map = new CoreMap(60, 60, SimWorld.Map.TerrainDefOf.Soil);
            for (int i = 0; i < 400; i++)
            {
                // Not RunUntilSettled: beds and walls come first, so the first passes plan no hut at all.
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(mapped, map);
            }

            Settlement abstractOne = Hoard();
            for (int i = 0; i < 3; i++) abstractOne.AddCitizen(NewHuman("Abstract" + i));
            abstractOne.AddStore(wood, stock);
            for (int i = 0; i < 400; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                AbstractSettlementConstruction.Run(abstractOne);
            }

            Assert.True(Huts(map) > 0);
            Assert.Equal(Huts(map), abstractOne.StructureCount(Hut));
        }
    }
}
