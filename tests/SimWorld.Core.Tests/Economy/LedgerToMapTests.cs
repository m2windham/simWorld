using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

// SimWorld.Economy shares its leaf segment with this test namespace; alias rather than rely on which one a
// bare name resolves to (CLAUDE.md).
using SettlementLarder = global::SimWorld.Economy.SettlementLarder;
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>The seam between the two halves of the game, in the ledger-to-map direction</b> — the twin of
    /// <see cref="SettlementStockTests"/>, which pins the same crossing going the other way.
    ///
    /// <para/><b>Only half the rule had been written.</b> <c>SettlementStockInitiative</c>'s own doc states the
    /// invariant — <i>a unit of goods is either a Thing on a settlement's interior map or a count in that
    /// settlement's Stores, never both</i> — and says "all that was missing was the rule for crossing". What
    /// existed crossed one way: <c>BankStoredGoods</c> takes a settlement's work off its map and credits the
    /// civilization. Nothing ever went back.
    ///
    /// <para/><b>What that cost, measured.</b> <c>SettlementLarder.WouldEatFromStores</c> refuses any
    /// <c>Spawned</c> citizen, on the correct principle that somebody standing on a map eats things on that
    /// map. But <c>ProvisionFoundingBand</c> sizes a new settlement's rations counting every non-dead citizen,
    /// spawned ones included, and puts all of it in the ledger — and no map-generation step places food. So
    /// watching a settlement locked its people out of rations stocked on their behalf. <c>--suite probe</c>
    /// on day one of a 25-strong TribalStart: watched citizens at 0.20 food against unwatched at 0.73, with
    /// the watched ledger <i>fuller</i> by exactly the nutrition the unwatched ones had eaten.
    ///
    /// <para/>These pin the conservation law rather than a headline number: what the map gains the ledger
    /// loses, to the unit.
    /// </summary>
    public class LedgerToMapTests : ContentTestBase
    {
        public LedgerToMapTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Pemmican => Def("Pemmican");

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        /// <summary>A settlement whose citizens are standing on <paramref name="map"/> — the state that makes
        /// them ineligible for the abstract larder and therefore dependent on this crossing.</summary>
        private static Settlement SettlementOn(CoreMap map, int citizens = 4)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Larderhome", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = NewHuman("Eater" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(2 + i, 0, 2), map);
            }
            return settlement;
        }

        private static float MapNutrition(CoreMap map) => HuntingInitiative.NutritionAvailable(null, map);

        private static float LedgerNutrition(Settlement settlement)
        {
            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                total += (kv.Key.ingestible?.nutrition ?? 0f) * kv.Value;
            }
            return total;
        }

        // ---- the crossing itself ----

        [Fact]
        public void Rations_in_the_ledger_reach_the_map_the_citizens_are_standing_on()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            settlement.AddStore(Pemmican, 500);

            Assert.Equal(0f, MapNutrition(map));

            int issued = SettlementStockInitiative.IssueFromStores(settlement, map);

            Assert.True(issued > 0, "a hungry settlement with a full ledger and an empty map must issue something");
            Assert.True(MapNutrition(map) > 0f, "the food has to actually exist where people can reach it");
            Assert.True(settlement.StoreCountOf(Pemmican) < 500, "and has to have left the ledger");
        }

        [Fact]
        public void What_the_map_gains_the_ledger_loses_to_the_unit()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            settlement.AddStore(Pemmican, 500);

            float before = LedgerNutrition(settlement) + MapNutrition(map);
            SettlementStockInitiative.IssueFromStores(settlement, map);
            float after = LedgerNutrition(settlement) + MapNutrition(map);

            // The invariant the class states, asserted as conservation: issuing moves, it never mints.
            Assert.Equal(before, after, 3);
        }

        [Fact]
        public void A_century_old_ledger_does_not_empty_itself_onto_the_map()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            settlement.AddStore(Pemmican, 100000);   // what opening a long-running settlement looks like

            SettlementStockInitiative.IssueFromStores(settlement, map);

            // Bounded by the shortfall, which is a few days of food for the mouths present — not by what the
            // ledger happens to hold. Asserted as a band against the settlement's own stated appetite rather
            // than a literal, because the appetite is tuned and the bound is the claim.
            float wanted = HuntingInitiative.NutritionWanted(settlement, map);
            Assert.True(MapNutrition(map) <= wanted * 1.5f,
                $"issued {MapNutrition(map):F1} nutrition against an appetite of {wanted:F1}");
            Assert.True(settlement.StoreCountOf(Pemmican) > 90000, "the rest must stay in the ledger");
        }

        [Fact]
        public void A_map_that_already_has_enough_is_left_alone()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            settlement.AddStore(Pemmican, 500);

            // Put plenty on the map first, by hand.
            Thing plenty = ThingMaker.MakeThing(Pemmican);
            plenty.stackCount = 200;
            GenSpawn.Spawn(plenty, new IntVec3(10, 0, 10), map);

            int before = settlement.StoreCountOf(Pemmican);
            int issued = SettlementStockInitiative.IssueFromStores(settlement, map);

            Assert.Equal(0, issued);
            Assert.Equal(before, settlement.StoreCountOf(Pemmican));
        }

        [Fact]
        public void An_empty_ledger_is_an_answer_rather_than_an_error()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);

            Assert.Equal(0, SettlementStockInitiative.IssueFromStores(settlement, map));
            Assert.Equal(0f, MapNutrition(map));
        }

        [Fact]
        public void A_ledger_holding_nothing_edible_is_not_raided_for_it()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            ThingDef stone = Def("BlocksSandstone");
            settlement.AddStore(stone, 400);

            Assert.Equal(0, SettlementStockInitiative.IssueFromStores(settlement, map));
            Assert.Equal(400, settlement.StoreCountOf(stone));
        }

        // ---- the two halves together ----

        [Fact]
        public void Issuing_then_banking_does_not_churn_the_same_food_back_and_forth()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOn(map);
            settlement.AddStore(Pemmican, 500);

            SettlementStockInitiative.IssueFromStores(settlement, map);
            float afterIssue = MapNutrition(map);
            Assert.True(afterIssue > 0f);

            // Banking keeps NutritionWanted on the map and takes only the surplus, so what was just issued
            // against that same threshold must survive it. If these two ever disagree about the line, a
            // settlement would shuttle its own larder in and out every rare tick forever.
            SettlementStockInitiative.EnsureGranary(settlement, map);
            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.True(MapNutrition(map) > 0f, "banking must not take back the larder issuing just placed");
        }

        // ---- determinism, which the probe depends on ----

        [Fact]
        public void Two_identical_settlements_issue_identically()
        {
            static string Run()
            {
                CoreMap map = NewMap();
                Settlement settlement = SettlementOn(map);
                settlement.AddStore(Pemmican, 500);
                int issued = SettlementStockInitiative.IssueFromStores(settlement, map);

                var cells = new List<string>();
                IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(Pemmican);
                for (int i = 0; i < stacks.Count; i++)
                {
                    cells.Add(stacks[i].Position + "x" + stacks[i].stackCount);
                }
                cells.Sort(System.StringComparer.Ordinal);
                return issued + "|" + string.Join(",", cells);
            }

            // Same units, same stacks, same cells. BestStoredFood is a total order with no draw in it and the
            // cells are walked in GenRadial's fixed pattern, so nothing here should reach the Rand stream.
            Assert.Equal(Run(), Run());
        }
    }
}
