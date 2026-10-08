using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using SettlementMedicine = global::SimWorld.Economy.SettlementMedicine;
using SettlementMedicineTuning = global::SimWorld.Economy.SettlementMedicineTuning;
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;
using Zone_Stockpile = global::SimWorld.Building.Zone_Stockpile;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>Medicine has to be where a tender can reach it</b> (<see cref="SettlementMedicine"/>).
    ///
    /// <para/><b>What was wrong.</b> The stock initiative banks every non-food unit resting in a stockpile
    /// into the settlement's ledger and issues only food back. So a harvest of herbal medicine -- hauled to
    /// the granary like anything else -- was destroyed off the map and credited to a ledger no tender reads,
    /// and a founding band's starting medicine (credited to the ledger at game start, before any map exists)
    /// was never put on a map at all. <c>MedicineUtility.FindBestMedicine</c> looks at spawned items only.
    /// Giving the settlement a source of medicine would have changed nothing a player could see.
    ///
    /// <para/>These pin the conservation law and the reserve, not a number: medicine within the settlement's
    /// reserve stays on the map, only the excess is banked, the shortfall is issued back, and nothing is minted
    /// or lost on the way.
    /// </summary>
    public class SettlementMedicineTests : ContentTestBase
    {
        public SettlementMedicineTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static and ContentTestBase does not reset it -- see MedicineTests.
            Find.FactionManager = new FactionManager();
        }

        private static ThingDef Herbal => MedicineDefOf.MedicineHerbal;

        private static ThingDef Industrial => MedicineDefOf.MedicineIndustrial;

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Settlement SettlementOf(int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Salvehome", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
        }

        /// <summary>A stockpile accepting everything, one cell, holding <paramref name="count"/> of
        /// <paramref name="def"/> -- the state a citizen's own hauling leaves behind.</summary>
        private static Thing Stockpiled(CoreMap map, IntVec3 cell, ThingDef def, int count)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, cell);
            zone.filter.SetAllowAll(null);

            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static int OnMap(CoreMap map, ThingDef def)
        {
            int total = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) total += stacks[i].stackCount;
            }
            return total;
        }

        private static int Everywhere(Settlement settlement, CoreMap map, ThingDef def) =>
            OnMap(map, def) + settlement.StoreCountOf(def);

        // ---- the reserve ----

        [Fact]
        public void A_settlement_wants_more_medicine_for_more_people()
        {
            CoreMap map = NewMap();

            int few = SettlementMedicine.Wanted(SettlementOf(2), map);
            int many = SettlementMedicine.Wanted(SettlementOf(20), map);

            Assert.True(few > 0);
            Assert.True(many > few);
            Assert.Equal(0, SettlementMedicine.Wanted(SettlementOf(0), map));
        }

        [Fact]
        public void The_dead_do_not_need_medicine()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementOf(4);
            int before = SettlementMedicine.Wanted(settlement, map);

            settlement.Citizens[0].health.Kill(null, null);

            Assert.True(SettlementMedicine.Wanted(settlement, map) < before);
        }

        // ---- map to ledger ----

        [Fact]
        public void Medicine_within_the_reserve_stays_on_the_map_where_a_tender_can_reach_it()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            Assert.True(wanted >= 2);
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, wanted - 1);

            int banked = SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(0, banked);
            Assert.Equal(wanted - 1, OnMap(map, Herbal));
            Assert.Equal(0, settlement.StoreCountOf(Herbal));
        }

        [Fact]
        public void Only_the_excess_over_the_reserve_is_banked_and_not_a_unit_is_lost()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            int surplus = 9;
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, wanted + surplus);

            int banked = SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(surplus, banked);
            Assert.Equal(wanted, OnMap(map, Herbal));
            Assert.Equal(surplus, settlement.StoreCountOf(Herbal));
            Assert.Equal(wanted + surplus, Everywhere(settlement, map, Herbal));
        }

        [Fact]
        public void The_reserve_is_shared_across_kinds_of_medicine_so_it_is_not_spent_twice()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, wanted);
            Stockpiled(map, new IntVec3(6, 0, 5), Industrial, 4);

            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(wanted, OnMap(map, Herbal) + OnMap(map, Industrial));
            Assert.Equal(4, settlement.StoreCountOf(Herbal) + settlement.StoreCountOf(Industrial));
        }

        [Fact]
        public void Food_is_still_banked_by_its_own_reserve_and_not_by_the_medicine_one()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            ThingDef potatoes = DefDatabase<ThingDef>.GetNamed("RawPotatoes");
            ThingDef chunk = DefDatabase<ThingDef>.GetNamed("ChunkSandstone");
            Stockpiled(map, new IntVec3(5, 0, 5), chunk, 12);
            Stockpiled(map, new IntVec3(6, 0, 5), Herbal, 3);

            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(12, settlement.StoreCountOf(chunk));
            Assert.Equal(0, OnMap(map, chunk));
            Assert.Equal(3, OnMap(map, Herbal));
            Assert.Equal(0, settlement.StoreCountOf(potatoes));
        }

        // ---- ledger to map ----

        [Fact]
        public void A_founding_bands_starting_medicine_reaches_the_map_its_tenders_work_on()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            settlement.AddStore(Herbal, 20);   // what Game.NewGame credits a scenario's starting stock as
            Assert.Equal(0, OnMap(map, Herbal));

            int issued = SettlementMedicine.IssueFromStores(settlement, map);

            Assert.Equal(wanted, issued);
            Assert.Equal(wanted, OnMap(map, Herbal));
            Assert.Equal(20 - wanted, settlement.StoreCountOf(Herbal));
            Assert.Equal(20, Everywhere(settlement, map, Herbal));
        }

        [Fact]
        public void A_century_old_ledger_does_not_empty_itself_onto_the_map()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            settlement.AddStore(Herbal, 100000);

            SettlementMedicine.IssueFromStores(settlement, map);

            Assert.Equal(SettlementMedicine.Wanted(settlement, map), OnMap(map, Herbal));
            Assert.Equal(100000, Everywhere(settlement, map, Herbal));
        }

        [Fact]
        public void Nothing_is_issued_while_the_map_already_holds_enough()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, wanted);
            settlement.AddStore(Herbal, 10);

            Assert.Equal(0, SettlementMedicine.IssueFromStores(settlement, map));
            Assert.Equal(10, settlement.StoreCountOf(Herbal));
        }

        [Fact]
        public void The_most_potent_medicine_in_the_ledger_is_issued_first()
        {
            Settlement settlement = SettlementOf(2);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            settlement.AddStore(Herbal, 50);
            settlement.AddStore(Industrial, wanted);

            SettlementMedicine.IssueFromStores(settlement, map);

            Assert.Equal(wanted, OnMap(map, Industrial));
            Assert.Equal(0, OnMap(map, Herbal));
        }

        [Fact]
        public void A_settlement_with_nothing_in_its_ledger_issues_nothing_and_does_not_throw()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();

            Assert.Equal(0, SettlementMedicine.IssueFromStores(settlement, map));
            Assert.Null(SettlementMedicine.BestStoredMedicine(settlement));
        }

        // ---- the whole pass ----

        [Fact]
        public void A_full_stock_pass_leaves_medicine_where_a_tender_can_find_it_and_moves_nothing_else_in_or_out()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, wanted + 4);     // a harvest, hauled in
            settlement.AddStore(Herbal, 7);                                // plus what the founders brought
            int before = Everywhere(settlement, map, Herbal);

            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(before, Everywhere(settlement, map, Herbal));
            Assert.Equal(wanted, OnMap(map, Herbal));

            Faction faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Tribe", "F_Tribe");
            Find.FactionManager.Add(faction);
            Pawn doctor = NewHuman("Doctor");
            doctor.faction = faction;
            GenSpawn.Spawn(doctor, new IntVec3(10, 0, 10), map);
            Pawn patient = NewHuman("Patient");
            patient.faction = faction;
            GenSpawn.Spawn(patient, new IntVec3(11, 0, 10), map);

            Thing? found = MedicineUtility.FindBestMedicine(doctor, patient);
            Assert.NotNull(found);
            Assert.True(found!.def.IsMedicine);
        }

        [Fact]
        public void The_stock_initiatives_own_pass_issues_the_starting_stock_without_anyone_calling_medicine_by_hand()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            settlement.AddStore(Herbal, 20);

            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(SettlementMedicine.Wanted(settlement, map), OnMap(map, Herbal));
        }

        [Fact]
        public void Repeated_passes_settle_and_do_not_oscillate()
        {
            Settlement settlement = SettlementOf(3);
            CoreMap map = NewMap();
            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, 40);
            settlement.AddStore(Herbal, 5);
            int total = Everywhere(settlement, map, Herbal);

            SettlementStockInitiative.Run(settlement, map);
            int mapAfterFirst = OnMap(map, Herbal);
            int ledgerAfterFirst = settlement.StoreCountOf(Herbal);
            for (int i = 0; i < 5; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(mapAfterFirst, OnMap(map, Herbal));
            Assert.Equal(ledgerAfterFirst, settlement.StoreCountOf(Herbal));
            Assert.Equal(total, Everywhere(settlement, map, Herbal));
        }

        // ---- need ----

        [Fact]
        public void A_settlement_is_short_of_medicine_when_both_books_together_hold_less_than_it_wants()
        {
            Settlement settlement = SettlementOf(4);
            CoreMap map = NewMap();
            int wanted = SettlementMedicine.Wanted(settlement, map);

            Assert.True(SettlementMedicine.IsShort(settlement, map));

            settlement.AddStore(Herbal, wanted - 1);
            Assert.True(SettlementMedicine.IsShort(settlement, map));

            Stockpiled(map, new IntVec3(5, 0, 5), Herbal, 1);
            Assert.False(SettlementMedicine.IsShort(settlement, map));
        }
    }
}
