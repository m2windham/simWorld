using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;
using SimWorld.World.Gen;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// The founding band of <c>SettlementCompoundTests.A_founding_band_raises_its_own_compound_and_sleeps_inside_the_ring</c>
    /// (world <c>wood-supply-founding</c>, the first wooded land tile, band seed 4343, the test fixture's ambient
    /// seed <c>--seed</c>, 12345 by default), ticked exactly as that test ticks it for <c>--layout-ticks</c>.
    /// Prints ticks per second for every 5,000-tick window, what the citizens are doing, and at the end a
    /// fingerprint of the map (things by def, citizens' positions) so two builds can be compared for behaviour.
    /// </summary>
    internal static class MiningSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("Mining: the wood-supply founding band, " + opt.LayoutTicks + " ticks");
            Bootstrap.ResetSim(opt.Seed);
            SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "wood-supply-founding", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i =>
                !world.grid.Tiles[i].WaterCovered && SimWorld.MapGen.TreeTuning.TimberMagnitude(world.grid.Tiles[i]) >= 0.4f);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(4343));
            Find.World = world;
            CoreMap map = settlement.EnterMap(world);

            var total = Stopwatch.StartNew();
            var window = Stopwatch.StartNew();
            const int Window = 5000;
            for (int tick = 0; tick < opt.LayoutTicks; tick++)
            {
                SettlementConstructionInitiative.Tick();
                Find.TickManager.DoSingleTick();
                if ((tick + 1) % Window != 0) continue;

                double secs = window.Elapsed.TotalSeconds;
                window.Restart();
                var jobs = settlement.Citizens.Where(p => p.Spawned && !p.Dead)
                    .GroupBy(p => p.jobs?.curJob?.def.defName ?? "-").OrderByDescending(g => g.Count())
                    .Select(g => g.Key + ":" + g.Count());
                Console.WriteLine("  ticks " + (tick + 1 - Window).ToString(CultureInfo.InvariantCulture).PadLeft(7) + ".." + (tick + 1).ToString(CultureInfo.InvariantCulture).PadRight(7)
                    + (Window / secs).ToString("0", CultureInfo.InvariantCulture).PadLeft(7) + " ticks/s   " + string.Join(", ", jobs));
            }
            Console.WriteLine("  total " + total.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, "
                + (opt.LayoutTicks / total.Elapsed.TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + " ticks/s");

            // Fingerprint: what is on the map and where the citizens are, at the last tick.
            var defs = map.listerThings.AllThings.GroupBy(t => t.def.defName).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + "=" + g.Count());
            Console.WriteLine("  fingerprint things: " + string.Join(" ", defs));
            Console.WriteLine("  fingerprint citizens: " + string.Join(" ", settlement.Citizens.OrderBy(p => p.thingIDNumber)
                .Select(p => p.Position.x + "," + p.Position.z)));
        }
    }
}
