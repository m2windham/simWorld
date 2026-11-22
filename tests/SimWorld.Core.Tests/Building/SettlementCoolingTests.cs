using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A settlement builds passive coolers in its dormitory when it is hot (SettlementCooling, a translation:
    /// RimWorld leaves this to the player), at the place the compound keeps for them.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SettlementCoolingTests : ContentTestBase
    {
        public SettlementCoolingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Cooler => CoolingThingDefOf.PassiveCooler;

        private static Settlement Band(int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Compound", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
        }

        private static void RunPasses(Settlement settlement, CoreMap map, int passes, int from)
        {
            for (int i = from; i < from + passes; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
        }

        private static List<IntVec3> CoolerSites(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => ReferenceEquals(bp.EntityToBuild, Cooler)).Select(bp => bp.Position).ToList();

        private static void FinishCoolerResearch() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("PassiveCooler"));

        [Fact]
        public void The_compound_keeps_one_beds_place_inside_for_two_coolers()
        {
            CoreMap map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil);
            Settlement settlement = Band(30);
            RunPasses(settlement, map, 1, 0);
            SettlementCompound compound = map.settlementCompound!;
            List<IntVec3> coolers = compound.CoolerCells.ToList();
            Assert.Equal(2, coolers.Count);
            int bedsAround = compound.Interior.Area / ConstructionThingDefOf.Bed.size.Area;
            Assert.Equal(bedsAround - 1, compound.BedCells.Count());
            var bedCells = new HashSet<IntVec3>(compound.BedCells.SelectMany(b => GenAdj.OccupiedRect(b, Rot4.North, ConstructionThingDefOf.Bed.size).Cells));
            foreach (IntVec3 c in coolers)
            {
                Assert.True(compound.Interior.Contains(c));
                Assert.DoesNotContain(c, bedCells);
            }
            Assert.Equal(compound.Interior.Area - 2, bedCells.Count);
        }

        [Fact]
        public void Only_a_settlement_that_knows_how_and_is_hot_wants_coolers()
        {
            CoreMap map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 20f };
            Settlement settlement = Band(30);
            Assert.False(SettlementCooling.WantsCooler(settlement, map)); // no compound yet
            RunPasses(settlement, map, 1, 0);

            Assert.False(SettlementCooling.WantsCooler(settlement, map)); // mild
            map.outdoorTemperature = 40f;
            Assert.False(SettlementCooling.WantsCooler(settlement, map)); // hot, but not researched
            FinishCoolerResearch();
            Assert.True(SettlementCooling.WantsCooler(settlement, map));

            map.outdoorTemperature = 20f;
            Assert.False(SettlementCooling.WantsCooler(settlement, map));
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(GameConditionDefOf.HeatWave, 60000));
            Assert.True(SettlementCooling.WantsCooler(settlement, map));
        }

        [Fact]
        public void In_a_heat_wave_the_settlement_plans_two_coolers_in_the_dormitory_and_no_more()
        {
            CoreMap map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 20f };
            Settlement settlement = Band(30);
            FinishCoolerResearch();
            RunPasses(settlement, map, 20, 0);
            Assert.Empty(CoolerSites(map)); // mild: none

            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(GameConditionDefOf.HeatWave, 600000));
            RunPasses(settlement, map, 40, 20);

            List<IntVec3> sites = CoolerSites(map);
            Assert.Equal(SettlementCooling.CoolersWanted, sites.Count);
            Assert.Equal(new HashSet<IntVec3>(map.settlementCompound!.CoolerCells), new HashSet<IntVec3>(sites));
        }
    }
}
