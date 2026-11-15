using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using Zone_Stockpile = global::SimWorld.Building.Zone_Stockpile;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// A settlement's medicine is on its map, where a doctor looks for it.
    ///
    /// <para/>Found by <c>tools/bench --suite storyteller --threats false</c>: the founding band's twenty
    /// herbal medicine sat in the settlement ledger for all twenty traced days ("medicine on map 0, medicine
    /// in ledger 20") while thirteen people died of infections tended without it
    /// (<c>docs/perf/storyteller-pressure</c>).
    /// </summary>
    public class SettlementMedicineIssueTests : ContentTestBase
    {
        public SettlementMedicineIssueTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private static ThingDef Herbal => DefDatabase<ThingDef>.GetNamed("MedicineHerbal");

        private static CoreMap NewMap() => new CoreMap(20, 20, TerrainDefOf.Soil);

        private static Settlement PeopledSettlement(int citizens = 3)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Stockhome", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
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

        [Fact]
        public void Stored_medicine_is_issued_to_the_map_and_leaves_the_ledger()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement();
            settlement.AddStore(Herbal, 20);

            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(20, OnMap(map, Herbal));
            Assert.Equal(0, settlement.StoreCountOf(Herbal));
        }

        [Fact]
        public void A_doctor_finds_the_issued_medicine()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement();
            settlement.AddStore(Herbal, 5);
            Pawn doctor = NewHuman("Doctor");
            Pawn patient = NewHuman("Patient");
            GenSpawn.Spawn(doctor, new IntVec3(3, 0, 3), map);
            GenSpawn.Spawn(patient, new IntVec3(5, 0, 3), map);

            Assert.Null(MedicineUtility.FindBestMedicine(doctor, patient));

            SettlementStockInitiative.Run(settlement, map);

            Assert.Same(Herbal, MedicineUtility.FindBestMedicine(doctor, patient)?.def);
        }

        [Fact]
        public void Medicine_resting_in_storage_is_not_banked_back()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement();
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(8, 0, 8));
            zone.filter.SetAllowAll(null);
            Thing stack = ThingMaker.MakeThing(Herbal);
            stack.stackCount = 7;
            GenSpawn.Spawn(stack, new IntVec3(8, 0, 8), map);

            SettlementStockInitiative.Run(settlement, map);
            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(7, OnMap(map, Herbal));
            Assert.Equal(0, settlement.StoreCountOf(Herbal));
        }

        [Fact]
        public void A_settlement_with_nobody_alive_keeps_its_medicine_in_the_ledger()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement(citizens: 0);
            settlement.AddStore(Herbal, 20);

            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(0, OnMap(map, Herbal));
            Assert.Equal(20, settlement.StoreCountOf(Herbal));
        }

        [Fact]
        public void Issuing_never_creates_or_loses_a_unit()
        {
            CoreMap map = NewMap();
            Settlement settlement = PeopledSettlement();
            settlement.AddStore(Herbal, 137);

            for (int pass = 0; pass < 3; pass++) SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(137, OnMap(map, Herbal) + settlement.StoreCountOf(Herbal));
        }
    }
}
