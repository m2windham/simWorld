using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

using SettlementLarder = global::SimWorld.Economy.SettlementLarder;
using SettlementSubsistence = global::SimWorld.Economy.SettlementSubsistence;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A test that watches the game rather than a module</b>, and it watches the half of the game nobody is
    /// looking at. It founds a settlement the ordinary way — <c>Game.NewGame</c> on the tribal scenario — and
    /// then <i>never opens it</i>, so no interior map is ever generated and not one link of the map-side food
    /// economy is reachable. Then it ticks three weeks with nothing called by hand and asks the question a
    /// suite of module tests cannot: <i>does a town nobody is watching feed itself?</i>
    ///
    /// <para/><b>Why three weeks and not one.</b> A founding band walks in carrying rations sized at the
    /// slowest shipped crop's <c>growDays</c> plus the larder the food economy asks for — eleven days of food
    /// for the band that carries it. A one-week test therefore passes on a settlement with no economy at all,
    /// because it is still eating out of the crate. The failure this exists for only shows up after the crate
    /// is gone. Measured before the production half existed, twenty in-game days, nothing called by hand:
    ///
    /// <para/><code>
    /// day | alive | mean food | ledger nutrition | worst Malnutrition
    ///   0 |    25 |      0.80 |            440.0 | 0.00
    ///   5 |    24 |      0.31 |            242.4 | 0.00
    ///  11 |    22 |      0.33 |              0.0 | 0.00
    ///  15 |    22 |      0.00 |              0.0 | 0.43
    ///  20 |    18 |      0.00 |              0.0 | 1.00   (lethal at 1.00)
    /// </code>
    ///
    /// <para/><b>What this asserts, and what it deliberately does not.</b> That the ledger does not trend to
    /// zero, that it settles at the larder the food economy itself asks for rather than growing without
    /// bound, that mean nutrition is never pinned at the floor, and that nobody — living or dead — is far
    /// along <c>Malnutrition</c>, the hediff this simulation uses to mean starvation. It does <b>not</b>
    /// assert that the population is untouched, for the same reason
    /// <see cref="SettlementFoodTests"/> does not: every death in this run is a citizen beaten to death by
    /// another citizen, which <c>docs/WORK-REGISTER.md</c> §9a traces to <c>Need_Joy</c> having no source in
    /// this codebase. Asserting survival here would be asserting three other systems are healthy and would go
    /// red for reasons a food test cannot explain.
    /// </summary>
    public class SettlementProductionTests : ContentTestBase
    {
        public SettlementProductionTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Days = 21;

        private static HediffDef Malnutrition => DefDatabase<HediffDef>.GetNamed("Malnutrition");

        [Fact]
        public void An_unwatched_settlement_feeds_itself_for_three_weeks()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-production",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            var band = settlement.Citizens.ToList();

            // The unwatched case, stated before a tick runs: nobody is standing on anything, and there is no
            // interior for them to stand on. Both have to stay true for the whole run.
            Assert.Null(settlement.InteriorMap);
            Assert.All(band, p => Assert.False(p.Spawned));

            var ledger = new List<float>();
            var meanFood = new List<float>();
            for (int day = 1; day <= Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                Assert.Null(settlement.InteriorMap); // nothing may quietly generate an interior behind this
                List<Pawn> alive = settlement.Citizens.Where(p => !p.Dead).ToList();
                Assert.NotEmpty(alive);
                ledger.Add(SettlementLarder.StoredNutrition(settlement));
                meanFood.Add(alive.Average(p => p.needs.food!.CurLevelPercentage));
            }

            List<Pawn> survivors = settlement.Citizens.Where(p => !p.Dead).ToList();
            float burnedPerDay = survivors.Sum(p => HuntingTuning.NutritionPerEaterPerDay * p.HungerRate);
            Assert.True(burnedPerDay > 0f);

            // The column that read 0.0 from day eleven onward. Stated over the second half of the run — once
            // the founding crate is certainly gone — as "never below a day's food", the smallest larder that
            // is meaningfully not empty, rather than as a level this test would be pinning by hand.
            float worstLate = ledger.Skip(Days / 2).Min();
            Assert.True(worstLate > burnedPerDay,
                "after the founding rations ran out the civilization's larder fell to "
                + worstLate.ToString("F1") + " against a daily burn of " + burnedPerDay.ToString("F1")
                + " — a settlement nobody is watching is still living off a crate, not off an economy");

            // And it is a larder rather than a fountain: production stops at what the food economy itself asks
            // a settlement to keep in hand (AI.HuntingTuning.DaysOfFoodWanted), so three weeks of growing
            // cannot leave the ledger richer than the day the band walked in carrying its rations.
            float wanted = burnedPerDay * HuntingTuning.DaysOfFoodWanted;
            Assert.True(ledger.Last() <= wanted + burnedPerDay,
                "the ledger ended at " + ledger.Last().ToString("F1") + " against a target of "
                + wanted.ToString("F1") + " — abstract production is minting food, not growing it");
            Assert.True(ledger.Last() < ledger.First(),
                "three weeks of eating left the ledger fuller than the founding crate made it");

            // Nutrition is never pinned at the floor, and the trend across the second half does not point at
            // zero — the two readings that separate "eating out of a working economy" from "eating a crate".
            Assert.All(meanFood, mean => Assert.True(mean > 0f,
                "mean nutrition across the town hit zero, which is the reading this lane exists to fix"));
            Assert.True(meanFood.Max() > new Need_Food(survivors[0]).PercentageThreshHungry,
                "the town's mean nutrition never once rose back above hungry (peak "
                + meanFood.Max().ToString("F2") + ")");

            // "Does not lose citizens to hunger", stated the way the simulation states it. Living and dead
            // alike: a citizen who starved to death would otherwise leave the roster and take the evidence.
            foreach (Pawn citizen in band) AssertNotDyingOfHunger(citizen);
        }

        /// <summary>
        /// <b>The second half of the same defect.</b> The founding rations used to be credited by
        /// <c>Sim.Game.NewGame</c>, which founds exactly one settlement — the player's — so every rival
        /// civilization <c>World.EmergenceManager.Emerge</c> raises during play walked in with an empty
        /// ledger, no map to forage on and nothing producing into the only larder it had. This founds a band
        /// mid-game through the single call the emergent path makes
        /// (<c>World.SettlementFounder.Found</c>, <c>EmergenceManager.cs</c>'s own line) and then leaves the
        /// running game alone, exactly as a rival would be left alone.
        /// </summary>
        [Fact]
        public void A_rival_founded_during_play_is_provisioned_and_feeds_itself()
        {
            // Not a solo start: this needs a second civilization already on the planet for the new band to
            // belong to, which is the only thing soloStart withholds.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "settlement-production-rival",
                subdivisionOverride: 3, bandSize: 25);
            global::SimWorld.World.World world = game.World!;
            Settlement player = world.worldObjects.OfType<Settlement>().First();

            // A day into the game, a second people found a town of their own somewhere else on the planet.
            for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();

            Faction rivalFaction = world.factions.FirstOrDefault(f => !ReferenceEquals(f, player.faction))
                ?? world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => i != player.tile && !world.grid.Tiles[i].WaterCovered
                    && (world.grid.Tiles[i].biome?.canBuildBase ?? false)
                    && world.grid.Tiles[i].biome!.forageability > 0f);
            Settlement rival = SettlementFounder.Found(world, tile, rivalFaction, 25, new RandomStream(20260912), "Rivalhome");

            Assert.Equal(0f, SettlementLarder.StoredNutrition(rival));
            var rivalBand = rival.Citizens.ToList();

            for (int day = 0; day < 7; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
            }

            // It walked in carrying food — nothing in Game.NewGame ever reached it — and it is still holding
            // some a week later, which means something produced into its ledger as well.
            Assert.True(SettlementLarder.StoredNutrition(rival) > 0f,
                "a rival civilization founded during play starved in its first week with an empty ledger");
            Assert.Contains(rival.Citizens, p => !p.Dead);
            foreach (Pawn citizen in rivalBand) AssertNotDyingOfHunger(citizen);

            // And it never grew a map to do it on: the whole point is that this is the settlement nobody is
            // watching.
            Assert.Null(rival.InteriorMap);
            Assert.True(SettlementSubsistence.NutritionPerDay(rival) > 0f);
        }

        private static void AssertNotDyingOfHunger(Pawn pawn)
        {
            Hediff? malnutrition = pawn.health.hediffSet.GetFirstHediffOfDef(Malnutrition);
            if (malnutrition == null) return;

            // Well below lethalSeverity 1: a pawn briefly caught between meals picks up a trace of this and
            // sheds it again at the same pace. Anything approaching lethal is a citizen being lost to hunger.
            Assert.True(malnutrition.Severity < 0.5f,
                pawn.Label + " carries malnutrition at severity " + malnutrition.Severity.ToString("F2")
                + " (lethal at 1) in a settlement that is supposed to be feeding itself");
        }
    }
}
