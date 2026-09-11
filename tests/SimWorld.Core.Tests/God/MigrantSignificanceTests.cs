using System.Linq;

using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Migrants are ordinary citizens, and the Full-tier budget is not quietly spent on them.
    ///
    /// <para/><see cref="MigrationManager"/> used to flag every arrival with
    /// <see cref="Pawn_TierTracker.Notify_RoleChanged"/>, reading "founds a household" as the founder case.
    /// That flag is never cleared, and <see cref="global::SimWorld.God.AttentionBudget"/> ranks <c>hasRole</c>
    /// above every other reason — so arrivals accumulated permanently at the top of the ordering, unbounded,
    /// and would eventually have filled the budget with people whose only distinction was having arrived.
    /// </summary>
    public class MigrantSignificanceTests : ContentTestBase
    {
        public MigrantSignificanceTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        [Fact]
        public void AnArrivingMigrantHoldsNoRole()
        {
            Game game = NewSoloGame("migrant-no-role");
            Settlement settlement = PlayerSettlement(game);
            int before = settlement.Citizens.Count;

            // Force an arrival rather than waiting on the roll, so the test measures the flag and not the odds.
            var population = settlement.Citizens.ToList();
            while (settlement.Citizens.Count == before)
            {
                MigrationManager.ProcessArrivals(population, PawnKindDefOf.Tribesperson);
                foreach (Pawn p in population.Where(p => !settlement.Citizens.Contains(p)).ToList())
                {
                    settlement.AddCitizen(p);
                }
            }

            Pawn migrant = settlement.Citizens.Last();
            Assert.False(migrant.tier.HasRole,
                "an arrival was flagged as holding a role, which is never cleared and outranks every other "
                + "reason in the Full-tier ordering");
        }

        [Fact]
        public void MigrantsDoNotAccumulateAsPermanentlySignificantCitizens()
        {
            // The consequence, stated as the property that matters: with attention withdrawn, nobody in an
            // unwatched settlement stays Full. A role flag would have pinned every past arrival there forever.
            Game game = NewSoloGame("migrant-accumulate");
            Settlement settlement = PlayerSettlement(game);

            var population = settlement.Citizens.ToList();
            for (int i = 0; i < 40; i++)
            {
                MigrationManager.ProcessArrivals(population, PawnKindDefOf.Tribesperson);
            }
            foreach (Pawn p in population.Where(p => !settlement.Citizens.Contains(p)).ToList())
            {
                settlement.AddCitizen(p);
            }

            Assert.DoesNotContain(settlement.Citizens, p => p.tier.HasRole);
        }
    }
}
