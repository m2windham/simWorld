using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Construction for a settlement with no interior map (tracker item <c>building.abstractconstruction</c>):
    /// the one need among food, medicine and shelter that had no off-map answer at all.
    ///
    /// <para/><b>The defect this closes.</b> <c>Economy.SettlementSubsistence</c> already grows food into a
    /// mapless settlement's ledger, and <c>Health.AbstractDiseaseResolver</c> already resolves disease for one
    /// — both work with nothing generated. <see cref="SettlementConstructionInitiative.TickSettlement(World.Settlement)"/>
    /// returns immediately whenever <see cref="World.Settlement.InteriorMap"/> is null: the honest boundary
    /// for a class whose whole mechanism is placing a <c>Blueprint</c> on a map, but the practical effect was
    /// that a settlement nobody ever opened built nothing, forever, however many citizens it had or however
    /// long the game ran.
    ///
    /// <para/><b>What these assert.</b> That the real class, entirely unmodified by this lane, really does
    /// nothing for a settlement it never gets a map for (the fixture the headline used to fail against); that
    /// the new abstract path fixes exactly that; and that the abstract path gets out of the way completely the
    /// instant a map exists, so the two producers can never both credit the same building.
    /// </summary>
    public class AbstractSettlementConstructionTests : ContentTestBase
    {
        public AbstractSettlementConstructionTests(CoreContentFixture content) : base(content)
        {
        }

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "AbstractBuild" + tile, 0);

        private static void AddCitizens(Settlement settlement, int count)
        {
            for (int i = 0; i < count; i++) settlement.AddCitizen(NewHuman("Hand" + i));
        }

        private static int RunPasses(Settlement settlement, int passes, int firstPass = 1)
        {
            int credited = 0;
            for (int i = 0; i < passes; i++)
            {
                Find.TickManager.DebugSetTicksGame((firstPass + i) * AbstractConstructionTuning.IntervalTicks);
                credited += AbstractSettlementConstruction.Run(settlement);
            }
            return credited;
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        // -------------------------------------------------------------------------------------------
        // The defect, proven against the real, unmodified class first.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_real_construction_class_alone_never_builds_anything_for_a_settlement_with_no_map()
        {
            // SettlementConstructionInitiative is untouched by this lane: this drives it, by itself, across
            // forty gated intervals against a settlement that is never entered, exactly the fixture the module
            // was scouted against. If this ever stops failing, either the class changed or the fixture did.
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 10);

            for (int pass = 1; pass <= 40; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement);
            }

            Assert.Null(settlement.InteriorMap);
            Assert.Equal(0, settlement.StructureCount(ConstructionThingDefOf.Wall));
            Assert.Equal(0, settlement.StructureCount(ConstructionThingDefOf.Bed));
        }

        // -------------------------------------------------------------------------------------------
        // The headline.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlement_with_no_map_builds_over_time()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 10);

            Assert.Equal(0, settlement.StructureCount(ConstructionThingDefOf.Wall));
            Assert.Equal(0, RunPasses(settlement, 0)); // sanity: zero passes credits nothing

            RunPasses(settlement, 20);

            Assert.True(settlement.StructureCount(ConstructionThingDefOf.Wall) > 0,
                "a settlement with citizens and no map built no walls at all over twenty gated passes");
            Assert.True(settlement.StructureCount(ConstructionThingDefOf.Bed) > 0,
                "a settlement with citizens and no map built no beds at all over twenty gated passes");
        }

        [Fact]
        public void A_settlement_with_no_citizens_builds_nothing()
        {
            // No hands, nothing wanted for anyone to shelter — the same "no citizens, no needs" boundary
            // SettlementConstructionInitiative itself draws.
            Settlement settlement = PlainSettlement();
            Assert.Equal(0, RunPasses(settlement, 10));
        }

        // -------------------------------------------------------------------------------------------
        // No double count: the instant a map exists, this producer is a no-op.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlement_with_a_map_is_a_no_op_for_the_abstract_producer()
        {
            Game game = NewSoloGame("abstract-build-watched");
            Settlement settlement = game.CivilizationTarget.Settlements.First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            Assert.NotNull(settlement.InteriorMap);

            int wallsBefore = settlement.StructureCount(ConstructionThingDefOf.Wall);
            int bedsBefore = settlement.StructureCount(ConstructionThingDefOf.Bed);

            Assert.Equal(0, RunPasses(settlement, 20));

            Assert.Equal(wallsBefore, settlement.StructureCount(ConstructionThingDefOf.Wall));
            Assert.Equal(bedsBefore, settlement.StructureCount(ConstructionThingDefOf.Bed));
        }

        // -------------------------------------------------------------------------------------------
        // Determinism.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Abstract_construction_takes_no_roll_at_all()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 6);

            uint before = Rand.Current.Iterations;
            Assert.True(RunPasses(settlement, 10) > 0);
            Assert.Equal(before, Rand.Current.Iterations);
        }
    }
}
