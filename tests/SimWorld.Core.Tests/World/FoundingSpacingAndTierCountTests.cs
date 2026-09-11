using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// Two defects the attention/tiering work surfaced rather than caused. Both were latent while every
    /// citizen stayed <see cref="PawnTier.Full"/> forever and every game was a solo start; neither is once
    /// settlements settle to Statistical and worlds carry other factions' settlements.
    ///
    /// <para/>"World" is aliased to <c>global::SimWorld.World.World</c> throughout, as the sibling tests do —
    /// this namespace's own last segment is also "World" and would otherwise shadow it.
    /// </summary>
    public class FoundingSpacingAndTierCountTests : ContentTestBase
    {
        public FoundingSpacingAndTierCountTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static global::SimWorld.World.World GenerateSoloWorld(string seed, int subdivision = 4) =>
            WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", subdivision, soloStart: true);

        private static Faction AnyFaction(global::SimWorld.World.World world) => world.factions[0];

        // ---- PopulationOf must sum to TotalPopulation across all three tiers ----

        [Fact]
        public void ThePopulationOfEveryTierSumsToTheTotal()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("tier-sum");
            Settlement settlement = SettlementFounder.Found(
                world, BestTile(world), AnyFaction(world), 24, new RandomStream(77), "Sumhome");
            settlement.AddStatisticalPeople(500);

            // The state an unattended settlement now reaches as a matter of course: live Pawns whose trackers
            // settled to Statistical, sitting alongside the bare cohort.
            foreach (Pawn p in settlement.Citizens.Take(4).ToList())
            {
                p.tier.Notify_AttentionChanged(false);
                p.tier.DemoteToStatistical();
            }

            int summed = settlement.PopulationOf(PawnTier.Full)
                + settlement.PopulationOf(PawnTier.Interval)
                + settlement.PopulationOf(PawnTier.Statistical);

            Assert.Equal(settlement.TotalPopulation, summed);
        }

        [Fact]
        public void ALivePawnSittingAtStatisticalIsCountedOnTopOfTheBareCohort()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("tier-live");
            Settlement settlement = SettlementFounder.Found(
                world, BestTile(world), AnyFaction(world), 22, new RandomStream(91), "Cohorthome");
            settlement.AddStatisticalPeople(30);

            int before = settlement.PopulationOf(PawnTier.Statistical);
            Assert.Equal(30, before);

            Pawn one = settlement.Citizens[0];
            one.tier.Notify_AttentionChanged(false);
            one.tier.DemoteToStatistical();

            // The bug: this used to still answer 30, silently losing a citizen who is really at that tier.
            Assert.Equal(before + 1, settlement.PopulationOf(PawnTier.Statistical));
            Assert.Equal(PawnTier.Statistical, one.tier.Tier);
        }

        [Fact]
        public void TheBareCohortIsStillNeverEnumerated()
        {
            // The design constraint the original one-liner existed to satisfy, kept: a settlement of tens of
            // thousands must answer this without materialising anyone. The cohort has no Pawn objects at all,
            // so a count that large returning instantly is the proof.
            global::SimWorld.World.World world = GenerateSoloWorld("tier-big");
            Settlement settlement = SettlementFounder.Found(
                world, BestTile(world), AnyFaction(world), 20, new RandomStream(13), "Bighome");
            settlement.AddStatisticalPeople(40000);

            Assert.Equal(40000, settlement.PopulationOf(PawnTier.Statistical) - LiveAt(settlement, PawnTier.Statistical));
            Assert.Equal(20, settlement.Citizens.Count);
        }

        private static int LiveAt(Settlement s, PawnTier tier) => s.Citizens.Count(p => p.tier.Tier == tier);

        private static int BestTile(global::SimWorld.World.World world)
        {
            SimWorld.World.Siting.SiteWeightDef weights =
                DefDatabase<SimWorld.World.Siting.SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < world.grid.TilesCount; i++)
            {
                if (world.grid.Tiles[i].WaterCovered) continue;
                float score = SimWorld.World.Siting.SiteScorer.Score(world.grid, i, weights);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // ---- the starting settlement obeys the same spacing rule as every other founding ----

        [Fact]
        public void AStartingSettlementIsNotFoundedOnTopOfAWorldGeneratedOne()
        {
            // A non-solo start: world generation places other factions' settlements, and the player's founding
            // used to ignore them entirely and take the single best-scored tile — which can be one of theirs.
            //
            // Stated plainly: on the seeds here this one passes with the spacing rule removed too, so it does
            // not reproduce the defect — the sibling distance test is the one with teeth. It is kept because
            // tile uniqueness is the property God.AttentionManager actually depends on to name a settlement,
            // and a guard on the property you rely on is worth having even when today's seeds only violate
            // the weaker spacing rule that protects it.
            Game game = Game.NewGame(
                SimWorld.Scenario.ScenarioDefOf.TribalStart.scenario,
                "spacing-seed", subdivisionOverride: 4, soloStart: false, settlementName: "Playerhome");

            var settlements = game.World!.worldObjects.OfType<Settlement>().ToList();
            Assert.True(settlements.Count > 1, "this test needs a world that generated other settlements");

            var tiles = settlements.Select(s => s.tile).ToList();
            Assert.Equal(tiles.Count, tiles.Distinct().Count());
        }

        [Fact]
        public void TheStartingSettlementClearsTheSameMinimumDistanceWorldGenerationUses()
        {
            Game game = Game.NewGame(
                SimWorld.Scenario.ScenarioDefOf.TribalStart.scenario,
                "spacing-distance", subdivisionOverride: 4, soloStart: false, settlementName: "Farhome");

            var settlements = game.World!.worldObjects.OfType<Settlement>().ToList();
            Settlement player = settlements.Single(s => s.name == "Farhome");
            int minDistance = WorldGenStep_Factions.MinSettlementDistance(game.World.grid.TilesCount);

            foreach (Settlement other in settlements.Where(s => s != player))
            {
                Assert.True(
                    game.World.grid.ApproxDistanceInTiles(player.tile, other.tile) >= minDistance,
                    "founded " + game.World.grid.ApproxDistanceInTiles(player.tile, other.tile)
                        + " tiles from '" + other.name + "', under the " + minDistance + "-tile rule");
            }
        }

        [Fact]
        public void AnExplicitStartTileIsStillHonouredExactly()
        {
            // The spacing rule applies to *picking* a site, never to overriding the caller. A scenario or a
            // test that names a tile gets that tile.
            global::SimWorld.World.World probe = GenerateSoloWorld("explicit-tile");
            int wanted = BestTile(probe);

            Game game = Game.NewGame(
                SimWorld.Scenario.ScenarioDefOf.TribalStart.scenario,
                "explicit-tile", subdivisionOverride: 4, soloStart: true, startTile: wanted,
                settlementName: "Exacthome");

            Settlement player = game.World!.worldObjects.OfType<Settlement>().Single(s => s.name == "Exacthome");
            Assert.Equal(wanted, player.tile);
        }
    }
}
