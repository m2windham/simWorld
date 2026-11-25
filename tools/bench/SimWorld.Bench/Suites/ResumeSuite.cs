using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>A few days of a long run, replayed from a save.</b> <c>--suite arc --save-at D --save-path P</c> writes the
    /// whole game at the end of day D; this loads P and plays <c>--days</c> more with the arc's
    /// <see cref="ExposureWatch"/> on, printing every three hours what the cold and the builders are doing: the air,
    /// how many houses stand open, how many of their parts are waiting to be rebuilt, the worst hypothermia among
    /// children, babies and adults, and what the adults are busy with. A loaded game has a fresh random stream
    /// (<see cref="Rand.Current"/> is not saved), so it does not replay the original run; <c>--quake-at</c> forces an
    /// earthquake on that day if the storyteller has not sent one by then. Written for the exposure4 lane: the arc's
    /// day-520 quake in a hard frost, in minutes instead of ninety.
    /// </summary>
    internal static class ResumeSuite
    {
        public static void Run(BenchOptions opt)
        {
            if (opt.SavePath == null) throw new ArgumentException("--suite resume needs --save-path");
            Bootstrap.LoadContentOnce();
            Bootstrap.ResetSim(opt.Seed);
            // The arc's threat-free difficulty is made in code, not content: put it where the save's reference finds it.
            if (DefDatabase<DifficultyDef>.GetNamedSilentFail("MediumWithoutThreats") == null) DefDatabase<DifficultyDef>.Add(ArcSuite.MediumWithoutThreats());
            Game game = Scribe.Load<Game>(File.ReadAllText(opt.SavePath), "game", out IReadOnlyList<string> errors, DefDatabase.Global);
            foreach (string e in errors.Take(10)) Console.WriteLine("load error: " + e);
            SimWorld.World.Settlement home = game.CivilizationTarget.Seat
                ?? game.World?.worldObjects.OfType<SimWorld.World.Settlement>().FirstOrDefault(s => s.InteriorMap != null)
                ?? throw new InvalidOperationException("no settlement with a map");
            Map.Map map = home.InteriorMap ?? throw new InvalidOperationException("the seat has no map");
            Console.WriteLine("resumed day " + Day(Find.TickManager.TicksGame) + ", pop " + home.Citizens.Count(p => !p.Dead));

            var watch = new ExposureWatch();
            int quakesBefore = Fired(game, "Earthquake");
            bool forced = false;
            int end = Find.TickManager.TicksGame + opt.Days * GenDate.TicksPerDay;
            while (Find.TickManager.TicksGame < end)
            {
                game.TickManager.DoSingleTick();
                int now = Find.TickManager.TicksGame;
                if (!forced && opt.QuakeAt >= 0 && now >= opt.QuakeAt * GenDate.TicksPerDay)
                {
                    forced = true;
                    if (Fired(game, "Earthquake") == quakesBefore)
                    {
                        bool ok = DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = game.CivilizationTarget });
                        Console.WriteLine("forced earthquake day " + Day(now) + ": " + ok);
                    }
                }
                if (now % ExposureWatch.Every == 0) watch.Sample(home, now);
                if (now % (3 * GenDate.TicksPerHour) == 0) Console.WriteLine(Line(home, map, now));
            }
        }

        private static int Fired(Game game, string defName) => game.Storyteller.resourceImpact.StructuresDestroyedBy(defName);

        private static string Line(SimWorld.World.Settlement home, Map.Map map, int now)
        {
            List<Dwelling> houses = DwellingUtility.DwellingsOn(map).Where(d => d.householdId != Pawn_RelationsTracker.None && !d.beingDemolished).ToList();
            int open = houses.Count(d => !d.IsComplete(map));
            int waiting = 0, frames = 0;
            foreach (Dwelling d in houses)
            {
                foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in d.Parts)
                {
                    if (kind == DwellingPartKind.Bed) continue;
                    foreach (Thing t in map.thingGrid.ThingsListAt(cell))
                    {
                        if (t is Blueprint) waiting++;
                        else if (t is Frame) frames++;
                    }
                }
            }
            List<Pawn> alive = home.Citizens.Where(p => !p.Dead).ToList();
            string Worst(Func<Pawn, bool> f) => alive.Where(f)
                .Select(p => p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f)
                .DefaultIfEmpty(0f).Max().ToString("0.00", CultureInfo.InvariantCulture);
            bool Baby(Pawn p) => p.ageTracker.CurLifeStage?.developmentalStage == DevelopmentalStage.Baby;
            bool Child(Pawn p) => p.ageTracker.CurLifeStage?.developmentalStage == DevelopmentalStage.Child;
            bool Adult(Pawn p) => p.ageTracker.CurLifeStage?.developmentalStage == DevelopmentalStage.Adult;
            string jobs = string.Join(",", alive.Where(p => Adult(p) && p.Spawned)
                .GroupBy(p => p.jobs?.curJob?.def.defName ?? "-").OrderByDescending(g => g.Count()).Take(6)
                .Select(g => g.Key + " " + g.Count()));
            string worn = string.Join(",", alive.Where(Child).GroupBy(p => p.apparel?.WornApparel.Count ?? 0).OrderBy(g => g.Key)
                .Select(g => g.Key + "x" + g.Count()));
            return "  d" + Day(now) + " out " + map.outdoorTemperature.ToString("0.0", CultureInfo.InvariantCulture)
                + " | houses open " + open + "/" + houses.Count + " parts waiting " + waiting + " framed " + frames
                + " | worst hypo child " + Worst(Child) + " baby " + Worst(Baby) + " adult " + Worst(Adult)
                + " | alive c" + alive.Count(Child) + " b" + alive.Count(Baby) + " a" + alive.Count(Adult)
                + " | children worn " + worn + " | adults: " + jobs;
        }

        private static string Day(int tick) => (tick / (double)GenDate.TicksPerDay).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
