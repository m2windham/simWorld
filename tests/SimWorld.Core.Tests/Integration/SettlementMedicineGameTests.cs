using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using SettlementMedicine = global::SimWorld.Economy.SettlementMedicine;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>The medicine chain, watched in a real game rather than a module at a time.</b> A tribal band is
    /// founded the ordinary way, opened through the god seam and ticked, with nothing called by hand, and the
    /// questions are the ones no module test can ask: does the medicine the scenario handed the band ever
    /// reach a map where a tender can use it, and does a settlement that is short of it start growing more?
    ///
    /// <para/>Each link was invisible from inside its own module. The starting stock is credited to the
    /// settlement's ledger at game start and, before <c>Economy.SettlementMedicine</c>, nothing ever put it on
    /// a map -- a watched settlement tended without medicine from its first day, whatever its scenario said it
    /// started with. A herb garden is only worth painting if the medicine it grows would survive being hauled
    /// to the granary. And the garden gets painted only if somebody among the band can sow it.
    ///
    /// <para/>Half a game day is enough: the stock initiative and the farming initiative both act on the first
    /// rare ticks, so this asserts the links, not the growth that follows (<c>HealrootTests</c> and
    /// <c>HerbGardenTests</c> own that, with the real sow and harvest jobs). It deliberately stops there:
    /// running this seed on past a day and a half falls off a cliff in tick cost whether or not any of this
    /// code is in play -- measured with the medicine seam and the garden both switched off -- and that is not
    /// something this test can explain or should wait on.
    /// </summary>
    public class SettlementMedicineGameTests : ContentTestBase
    {
        public SettlementMedicineGameTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Hours = 12;

        [Fact]
        public void A_tribes_starting_medicine_reaches_its_map_and_a_short_settlement_paints_a_herb_garden()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-medicine",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            SimWorld.Map.Map map = settlement.InteriorMap!;
            ThingDef healroot = DefDatabase<ThingDef>.GetNamed("Plant_Healroot");

            // The state every game started in: medicine in the ledger, none on the map.
            int startingStock = settlement.StoreCountOf(MedicineDefOf.MedicineHerbal);
            Assert.True(startingStock > 0, "the tribal scenario is meant to hand the band herbal medicine");
            Assert.Equal(0, SettlementMedicine.OnMap(map));
            Assert.True(SettlementMedicine.IsShort(settlement, map),
                "the test needs a band that holds less medicine than it wants, which is the point of the rule");

            // One person who can sow healroot is all the garden asks for.
            Pawn grower = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            grower.skills!.GetSkill(SkillDefOf.Plants)!.Level = healroot.plant!.sowMinSkill;

            for (int i = 0; i < GenDate.TicksPerDay * Hours / 24; i++) game.TickManager.DoSingleTick();

            // Link 1: the stock is where a tender can reach it, and nothing was minted getting there.
            Assert.True(SettlementMedicine.OnMap(map) > 0,
                "the band's starting medicine never left the ledger for the map its doctors work on");
            Assert.True(SettlementMedicine.Stock(settlement, map) <= startingStock);

            Pawn doctor = settlement.Citizens.First(p => p.Spawned && p.Map == map && p != grower);
            Thing? found = MedicineUtility.FindBestMedicine(doctor, grower);
            Assert.NotNull(found);
            Assert.True(found!.def.IsMedicine);

            // Link 2: short of medicine, with food provided for and a sower to hand, the settlement planted some.
            Zone_Growing? garden = FarmingInitiative.HerbGardenOf(map);
            Assert.NotNull(garden);
            Assert.True(garden!.CellCount > 0);
            Assert.Same(healroot, garden.plantDefToGrow);

            // Link 3: and the one who can sow it is offered its cells, while everyone who cannot is not.
            var sow = new WorkGiver_GrowerSow();
            IntVec3 cell = garden.Cells.First(c => sow.HasJobOnCell(grower, c));
            Assert.Same(garden, map.zoneManager.ZoneAt(cell));
            Pawn clumsy = settlement.Citizens.First(p => p.Spawned && p.Map == map && p != grower
                && p.skills!.GetSkill(SkillDefOf.Plants)!.Level < healroot.plant.sowMinSkill);
            Assert.False(sow.HasJobOnCell(clumsy, cell));
        }
    }
}
