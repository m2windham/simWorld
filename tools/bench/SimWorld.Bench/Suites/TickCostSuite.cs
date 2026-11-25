using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>What one tick of a watched Bronze-age town costs.</b> The host asked for Speed 5 (3,000 ticks a second)
    /// and measured about 0.37 ms a tick, which cannot fit in a frame. This is the town it was measuring, posed
    /// rather than grown: a TribalStart band of <c>--band</c>, every project of the eras before Bronze finished as
    /// setup (<see cref="ResearchManager.SetProjectFinishedForSetup"/>, the same door a scenario starting in an
    /// era uses), the settlement opened so its citizens run on a real map, then <c>--warmup</c> days ticked so the
    /// town has built itself before the clock starts, then <c>--days</c> days timed.
    ///
    /// <para/>Reaching Bronze by play takes about 300 game days (<c>docs/research/era-arc.md</c>), hours of wall
    /// clock per run; this gets a town of the right era and size in minutes, so it can be run before and after a
    /// change and under <c>dotnet-trace</c>.
    ///
    /// <para/>It prints what a performance change must leave alone — population, deaths, finished houses,
    /// moments — and <see cref="SimDigest.Line"/> (Rand draws and a hash of every thing on the map), so "no
    /// behaviour change" is a diff of two outputs rather than a claim.
    /// </summary>
    internal static class TickCostSuite
    {
        public static void Run(BenchOptions opt)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            Report.Heading("Tick cost — a watched Bronze-age town, band " + Report.Int(opt.Band) + ", seed "
                + opt.Seed.ToString(CultureInfo.InvariantCulture));
            Report.Note("TribalStart, solo, research set up to the Bronze age, settlement opened; "
                + Report.Int(opt.Warmup) + " day(s) ticked untimed, then " + Report.Int(opt.Days) + " day(s) timed.");

            Bootstrap.ResetSim(opt.Seed);
            int founded = Math.Min(opt.Band, SimWorld.World.SettlementTuning.FoundingBandRange.max);
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: true,
                bandSize: founded);

            SimWorld.World.Settlement? home = game.CivilizationTarget.Seat;
            if (home == null) throw new InvalidOperationException("tickcost: the new game founded no settlement");

            ResearchManager research = Find.ResearchManager;
            EraDef bronze = DefDatabase<EraDef>.GetNamed("Bronze");
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.Where(e => e.order < bronze.order))
            {
                foreach (ResearchProjectDef project in era.Projects) research.SetProjectFinishedForSetup(project);
            }
            GodCommands.OpenSettlement(home.tile);
            Map.Map map = home.InteriorMap ?? throw new InvalidOperationException("tickcost: the settlement has no interior");

            Console.WriteLine();
            Console.WriteLine("start: era " + (research.CurrentEra?.defName ?? "-") + ", citizens " + Living(home));

            for (int day = 0; day < opt.Warmup; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
            }
            Console.WriteLine("after warmup: " + Readings(game, home, map));

            var perDay = new List<double>();
            var watch = new Stopwatch();
            for (int day = 0; day < opt.Days; day++)
            {
                watch.Restart();
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                watch.Stop();
                double msPerTick = watch.Elapsed.TotalMilliseconds / GenDate.TicksPerDay;
                perDay.Add(msPerTick);
                Console.WriteLine("  day " + (day + 1).ToString(CultureInfo.InvariantCulture) + ": "
                    + Report.Num(msPerTick, 4) + " ms/tick, citizens " + Living(home));
            }

            perDay.Sort();
            double median = perDay.Count == 0 ? 0 : perDay[perDay.Count / 2];
            Console.WriteLine();
            Console.WriteLine("timed: median " + Report.Num(median, 4) + " ms/tick, mean "
                + Report.Num(perDay.Count == 0 ? 0 : perDay.Average(), 4) + " ms/tick over "
                + Report.Int(opt.Days) + " day(s)");
            Console.WriteLine("end: " + Readings(game, home, map));
            Console.WriteLine("digest: " + SimDigest.Line(map));
        }

        private static int Living(SimWorld.World.Settlement home) => home.Citizens.Count(p => !p.Dead);

        /// <summary>What a performance change must not move: the era, population, deaths by cause, finished
        /// houses and the moments the curator kept.</summary>
        private static string Readings(Game game, SimWorld.World.Settlement home, Map.Map map)
        {
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            int complete = dwellings.Count(d => d.IsComplete(map));
            return "tick " + Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture)
                + ", era " + (Find.ResearchManager.CurrentEra?.defName ?? "-")
                + ", pop " + Living(home)
                + ", " + Find.Storyteller.deaths
                + ", houses " + complete + "/" + dwellings.Count
                + ", moments " + game.Storyteller.Moments.Count;
        }
    }
}
