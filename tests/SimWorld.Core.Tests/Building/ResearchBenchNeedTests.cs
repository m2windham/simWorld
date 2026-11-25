using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

// SimWorld.Building shares its leaf segment with this test namespace; alias the production types (CLAUDE.md).
using Blueprint = global::SimWorld.Building.Blueprint;
using EraDef = global::SimWorld.Research.EraDef;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using ResearchWorkDefOf = global::SimWorld.Research.ResearchWorkDefOf;
using SettlementWorksInitiative = global::SimWorld.Building.SettlementWorksInitiative;
using WorksInitiativeTuning = global::SimWorld.Building.WorksInitiativeTuning;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Research benches a settlement wants (system: research, era pacing). They were a flat two, so a band of
    /// 40 researched about 8% faster than a band of 20 and nothing the settlement became changed how fast it
    /// climbed (<c>docs/research/era-arc.md</c> §2.1). Now they grow with the population and the era. The
    /// ratios in <c>Eras.xml</c> are not sourced, so these pin the behaviour, not the numbers.
    /// </summary>
    public class ResearchBenchNeedTests : ContentTestBase
    {
        private readonly ResearchManager manager;

        public ResearchBenchNeedTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
        }

        private static List<EraDef> OrderedEras() =>
            DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();

        private static Settlement PeopledSettlement(int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Scholars", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
        }

        private static int PlannedBenches(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Count(bp => ReferenceEquals(bp.EntityToBuild, ResearchWorkDefOf.ResearchBench));

        private void CompleteLadderThrough(EraDef through)
        {
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (project.era != null && project.era.order <= through.order) manager.SetProjectFinishedForSetup(project);
            }
        }

        [Fact]
        public void Nobody_wants_no_bench_and_anybody_wants_one()
        {
            foreach (EraDef era in OrderedEras())
            {
                Assert.Equal(0, WorksInitiativeTuning.ResearchBenchesWanted(0, era));
                Assert.Equal(1, WorksInitiativeTuning.ResearchBenchesWanted(1, era));
            }
        }

        [Fact]
        public void A_bigger_settlement_never_wants_fewer_benches()
        {
            foreach (EraDef era in OrderedEras())
            {
                int previous = 0;
                for (int citizens = 1; citizens <= 300; citizens++)
                {
                    int wanted = WorksInitiativeTuning.ResearchBenchesWanted(citizens, era);
                    Assert.True(wanted >= previous, $"{era.defName}: {citizens} citizens want {wanted}, {citizens - 1} wanted {previous}.");
                    previous = wanted;
                }
            }
        }

        [Fact]
        public void A_later_era_never_wants_fewer_benches_and_the_last_wants_more_than_the_first()
        {
            foreach (int citizens in new[] { 5, 20, 25, 40, 100 })
            {
                int previous = 0;
                foreach (EraDef era in OrderedEras())
                {
                    int wanted = WorksInitiativeTuning.ResearchBenchesWanted(citizens, era);
                    Assert.True(wanted >= previous, $"{citizens} citizens want {wanted} in {era.defName}, {previous} the era before.");
                    previous = wanted;
                }
            }

            List<EraDef> eras = OrderedEras();
            Assert.True(WorksInitiativeTuning.ResearchBenchesWanted(25, eras[^1]) > WorksInitiativeTuning.ResearchBenchesWanted(25, eras[0]));
        }

        [Fact]
        public void A_band_of_forty_researches_with_more_benches_than_a_band_of_twenty_in_every_era()
        {
            foreach (EraDef era in OrderedEras())
            {
                Assert.True(WorksInitiativeTuning.ResearchBenchesWanted(40, era) > WorksInitiativeTuning.ResearchBenchesWanted(20, era),
                    $"{era.defName}: 40 citizens want no more benches than 20.");
            }
        }

        [Fact]
        public void The_settlement_reads_its_era_from_the_research_it_has_done()
        {
            Settlement settlement = PeopledSettlement(25);
            List<EraDef> eras = OrderedEras();
            int atStart = WorksInitiativeTuning.ResearchBenchesWanted(settlement);
            Assert.Equal(WorksInitiativeTuning.ResearchBenchesWanted(25, eras[0]), atStart);

            // Every era before the second-last learned: the civilization is in the second-last, learning it.
            CompleteLadderThrough(eras[^3]);
            Assert.Same(eras[^2], manager.CurrentEra);
            Assert.True(WorksInitiativeTuning.ResearchBenchesWanted(settlement) > atStart);
        }

        [Fact]
        public void The_works_initiative_raises_the_benches_the_settlement_wants()
        {
            Settlement settlement = PeopledSettlement(40);
            CompleteLadderThrough(OrderedEras()[^2]);
            int wanted = WorksInitiativeTuning.ResearchBenchesWanted(settlement);
            Assert.True(wanted > 2, $"A late-era band of 40 should want more than the old flat two; wants {wanted}.");

            var map = new CoreMap(40, 40, TerrainDefOf.Soil);
            for (int i = 0; i < wanted + 5; i++) SettlementWorksInitiative.Run(settlement, map);

            Assert.Equal(wanted, PlannedBenches(map));
        }
    }
}
