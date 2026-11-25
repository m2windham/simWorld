using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>How a played game moves through the eras.</b> <c>CLAUDE.md</c> says a 60-day bench is not evidence
    /// about the arc; a long run through the ages is. This suite is that run: one real, watched, played
    /// settlement — citizens, research at a bench, the construction and works initiatives, the storyteller —
    /// ticked for <c>--days</c> and sampled every <c>--arc-every</c> days (a game year, 60 days, by default).
    ///
    /// <para/>Per sample it reads the era and research, population and how much of it runs as full pawns,
    /// what stands on the settlement's map counted by building def and by the era that def's research gate
    /// belongs to, deaths by cause, food on hand, the moments <c>MomentCurator</c> kept, the storyteller's
    /// threat points, and the wall clock the interval cost. Every sample is printed as soon as it is taken,
    /// so a run that is stopped partway still leaves its readings behind.
    ///
    /// <para/>It sets up the world exactly as <see cref="StorytellerSuite"/> does (TribalStart, solo by
    /// default, <c>--band</c>, <c>--threats</c>) so a reading here can be put beside one there. It tunes
    /// nothing and is not a target.
    /// </summary>
    internal static class ArcSuite
    {
        public static void Run(BenchOptions opt)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));
            int every = Math.Max(1, opt.ArcEvery);

            Report.Heading(
                "Era arc — one played settlement over " + Report.Int(opt.Days) + " days ("
                + Report.Num(opt.Days / (double)GenDate.DaysPerYear, 1) + " game years), sampled every "
                + Report.Int(every) + " days");
            Report.Note(
                "TribalStart, " + (opt.Solo ? "solo world" : "populated world") + ", band " + Report.Int(opt.Band)
                + ", seed " + opt.Seed.ToString(CultureInfo.InvariantCulture) + ", threats "
                + (opt.Threats ? "**on** (Medium as `Game.NewGame` defaults it)" : "**off** (Medium without big or intro threats)")
                + ". Watched: the settlement is opened, so its citizens run on a real map.");

            EmitCatalogue();

            Bootstrap.ResetSim(opt.Seed);
            Ablation.Clear();

            var total = Stopwatch.StartNew();
            int founded = Math.Min(opt.Band, SimWorld.World.SettlementTuning.FoundingBandRange.max);
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: opt.Solo,
                bandSize: founded,
                difficultyDef: opt.Threats ? null : MediumWithoutThreats());

            SimWorld.World.Settlement? home = game.CivilizationTarget.Seat;
            if (home == null) throw new InvalidOperationException("arc: the new game founded no settlement");
            GodCommands.OpenSettlement(home.tile);

            ResearchManager research = Find.ResearchManager;
            var finished = new List<(int Day, ResearchProjectDef Def)>();
            var eras = new List<(int Day, string From, string To)>();
            int day = 0;
            // The era turn is read two ways: the engine's own EraReached event, and the derived
            // CurrentEra read straight after every finished project (ProjectFinished is raised after the
            // progress write). The first probe run had the derived era move with no event raised, so the
            // timeline must not depend on the event alone.
            var derived = new List<(int Day, string From, string To)>();
            string lastEra = research.CurrentEra?.defName ?? "-";
            research.ProjectFinished += def =>
            {
                finished.Add((day, def));
                string now = Find.ResearchManager.CurrentEra?.defName ?? "-";
                if (now != lastEra) derived.Add((day, lastEra, now));
                lastEra = now;
            };
            research.EraReached += (from, to) => eras.Add((day, from?.defName ?? "-", to.defName));

            int finishedAtStart = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count(research.IsFinished);
            Console.WriteLine();
            Console.WriteLine(
                "start: era " + (research.CurrentEra?.defName ?? "-") + ", projects finished at setup "
                + Report.Int(finishedAtStart) + ", setup " + Report.Num(total.Elapsed.TotalSeconds, 1) + " s");

            var rollup = new GodRollup();
            var samples = new List<Sample> { Take(0, 0, home, rollup, game, 0, 0) };
            Print(samples[0], finished, eras);
            var armoury = new ArmouryReading();
            Console.WriteLine("  " + armoury.Line(home));

            int momentsSeen = game.Storyteller.Moments.Count;
            ExposureWatch? watch = opt.DeathDetail ? new ExposureWatch() : null;
            var interval = Stopwatch.StartNew();
            long ticks = 0;
            int finishedSeen = 0;
            int erasSeen = 0;
            for (day = 1; day <= opt.Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++)
                {
                    game.TickManager.DoSingleTick();
                    ticks++;
                    if (watch != null)
                    {
                        SimWorld.World.Settlement watched = game.CivilizationTarget.Seat ?? home;
                        int now = Find.TickManager.TicksGame;
                        if (now % ExposureWatch.Every == 0) watch.Sample(watched, now);
                        if (now % 2500 == 0) watch.SampleBabies(watched);
                    }
                }

                if (day % every != 0 && day != opt.Days) continue;

                SimWorld.World.Settlement current = game.CivilizationTarget.Seat ?? home;
                double seconds = interval.Elapsed.TotalSeconds;
                Sample s = Take(day, samples[samples.Count - 1].Day, current, rollup, game, seconds, ticks);
                samples.Add(s);

                Console.WriteLine();
                Print(s, finished.Skip(finishedSeen), eras.Skip(erasSeen));
                finishedSeen = finished.Count;
                erasSeen = eras.Count;

                Console.WriteLine("  " + armoury.Line(current));
                if (watch != null) Console.WriteLine(watch.CensusLine());
                IReadOnlyList<ChronicleEntry> moments = game.Storyteller.Moments;
                for (int m = momentsSeen; m < moments.Count; m++) Console.WriteLine("  moment " + Moment(moments[m]));
                momentsSeen = moments.Count;

                interval.Restart();
                if (s.Population == 0)
                {
                    Console.WriteLine("  the settlement is extinct; stopping.");
                    break;
                }
            }
            total.Stop();

            EmitEraTimeline(eras, derived, finished, research);
            EmitResearchByEra(finished);
            EmitPopulation(samples);
            EmitBuildings(samples);
            EmitMoments(game.Storyteller.Moments);
            EmitCost(samples, total.Elapsed.TotalSeconds, ticks);
            Console.WriteLine();
            Console.WriteLine(DwellingReading.Line(game.CivilizationTarget.Seat ?? home));
        }

        private static DifficultyDef MediumWithoutThreats() => new DifficultyDef
        {
            defName = "MediumWithoutThreats",
            threatScale = DifficultyDefOf.Medium.threatScale,
            allowBigThreats = false,
            allowIntroThreats = false,
        };

        // ---- one reading ----

        private sealed class Sample
        {
            public int Day;
            public string Era = "-";
            public string NextEra = "-";
            public float NextEraProgress;
            public bool ConditionsMet;
            public int ProjectsFinished;
            public string CurrentProject = "-";
            public int Population;
            public int Full;
            public int Interval;
            public int Statistical;
            public int Citizens;
            public float Mood;
            public int[] Deaths = Array.Empty<int>();
            public string Ledger = "";
            public float FoodOnMap;
            public float FoodInStores;
            public float ThreatPoints;
            public float Wealth;
            public int Moments;
            public double Seconds;
            public long Ticks;
            public Dictionary<string, int> Built = new Dictionary<string, int>();
            public Dictionary<string, int> Planned = new Dictionary<string, int>();
        }

        private static Sample Take(
            int day, int previousDay, SimWorld.World.Settlement home, GodRollup rollup, Game game, double seconds, long ticks)
        {
            rollup.Recompute(home);
            ResearchManager research = Find.ResearchManager;
            EraDef? next = research.NextEra;
            var s = new Sample
            {
                Day = day,
                Era = research.CurrentEra?.defName ?? "-",
                NextEra = next?.defName ?? "-",
                // The era the town is in is the one it is learning (docs/design/era-progression.md section 1), so
                // the progress shown beside the next era is the current era's: how far toward leaving it.
                NextEraProgress = research.CurrentEra?.Progress ?? 1f,
                ConditionsMet = research.CurrentEra == null || research.CurrentEra.ConditionsMet(EraConditionContext.Now()),
                ProjectsFinished = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count(research.IsFinished),
                CurrentProject = research.CurrentProj?.defName ?? "-",
                Population = rollup.TotalPopulation,
                Full = rollup.FullCount,
                Interval = rollup.IntervalCount,
                Statistical = rollup.StatisticalCount,
                Citizens = home.Citizens.Count(p => !p.Dead),
                Mood = rollup.MeanMood,
                Deaths = Find.Storyteller.deaths.Snapshot().ToArray(),
                Ledger = Find.Storyteller.deaths.ToString(),
                FoodInStores = StoresNutrition(home),
                ThreatPoints = StorytellerUtility.DefaultThreatPointsNow(game.CivilizationTarget),
                Wealth = game.CivilizationTarget.PlayerWealthForStoryteller,
                Moments = game.Storyteller.Moments.Count,
                Seconds = seconds,
                Ticks = ticks,
            };
            _ = previousDay;

            Map.Map? map = home.InteriorMap;
            if (map != null)
            {
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
                {
                    // A ruin's building (no faction) is not the settlement's: nobody repairs or rebuilds it,
                    // so counted with the settlement's own it read as the settlement's walls eroding.
                    Bump(s.Built, home.IsOwnStructure(t) ? t.def.defName : t.def.defName + " (ruin)");
                }
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
                {
                    Bump(s.Planned, t.def.entityToBuild?.defName ?? t.def.defName);
                }
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                {
                    Bump(s.Planned, t.def.entityToBuild?.defName ?? t.def.defName);
                }
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item))
                {
                    if (t is Corpse) continue;
                    float per = t.def.ingestible?.nutrition ?? 0f;
                    if (per > 0f) s.FoodOnMap += per * t.stackCount;
                }
            }
            return s;
        }

        private static void Bump(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

        private static float StoresNutrition(SimWorld.World.Settlement home)
        {
            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> kv in home.Stores)
            {
                float per = kv.Key.ingestible?.nutrition ?? 0f;
                if (per > 0f) total += per * kv.Value;
            }
            return total;
        }

        private static void Print(
            Sample s, IEnumerable<(int Day, ResearchProjectDef Def)> finished, IEnumerable<(int Day, string From, string To)> eras)
        {
            var sb = new StringBuilder();
            sb.Append("sample day ").Append(s.Day).Append(" (year ").Append(Report.Num(s.Day / (double)GenDate.DaysPerYear, 1))
                .Append("): era ").Append(s.Era).Append(", next ").Append(s.NextEra).Append(' ')
                .Append(Report.Num(s.NextEraProgress * 100, 0)).Append("% learned, conditions ")
                .Append(s.ConditionsMet ? "met" : "not met").Append(", projects ").Append(s.ProjectsFinished)
                .Append(", researching ").Append(s.CurrentProject)
                .Append("; pop ").Append(s.Population).Append(" (full ").Append(s.Full).Append(", interval ")
                .Append(s.Interval).Append(", statistical ").Append(s.Statistical).Append(", citizens ").Append(s.Citizens)
                .Append("), mood ").Append(Report.Num(s.Mood, 2))
                .Append("; deaths ").Append(DeathsText(s.Deaths))
                .Append("; food map ").Append(Report.Num(s.FoodOnMap, 1)).Append(" stores ").Append(Report.Num(s.FoodInStores, 1))
                .Append("; threat ").Append(Report.Num(s.ThreatPoints, 0)).Append(" wealth ").Append(Report.Num(s.Wealth, 0))
                .Append("; moments ").Append(s.Moments)
                .Append("; wall ").Append(Report.Num(s.Seconds, 1)).Append(" s");
            Console.WriteLine(sb.ToString());
            Console.WriteLine("  ledger: " + s.Ledger);
            Console.WriteLine("  built: " + CountsText(s.Built));
            Console.WriteLine("  planned: " + CountsText(s.Planned));
            foreach ((int day, string from, string to) in eras) Console.WriteLine("  era reached day " + day + ": " + from + " -> " + to);
            List<(int Day, ResearchProjectDef Def)> list = finished.ToList();
            if (list.Count > 0)
            {
                Console.WriteLine("  finished: " + string.Join(", ", list.Select(f =>
                    f.Def.defName + "@" + f.Day + "[" + (f.Def.era?.defName ?? "-") + "]")));
            }
        }

        private static string CountsText(Dictionary<string, int> counts) => counts.Count == 0
            ? "-"
            : string.Join(", ", counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + " " + kv.Value));

        private static string DeathsText(int[] d)
        {
            var parts = new List<string>();
            foreach (DeathCause c in (DeathCause[])Enum.GetValues(typeof(DeathCause)))
            {
                int n = (int)c < d.Length ? d[(int)c] : 0;
                if (n > 0) parts.Add(c + " " + n);
            }
            return parts.Count == 0 ? "0" : string.Join(", ", parts);
        }

        private static string Moment(ChronicleEntry m) =>
            "day " + Report.Int(GenDate.DaysPassedAt(m.tick)) + ": " + m.incidentDefName
            + (m.deathCause.HasValue ? " (" + m.deathCause.Value + ")" : "")
            + (string.IsNullOrEmpty(m.targetLabel) ? "" : " — " + m.targetLabel);

        // ---- the building catalogue, by era gate ----

        /// <summary>The era a building def belongs to, read from its research gate: the latest era among its
        /// <c>researchPrerequisites</c>, or "ungated" when it has none.</summary>
        private static string GateEra(ThingDef def)
        {
            EraDef? latest = null;
            if (def.researchPrerequisites != null)
            {
                foreach (ResearchProjectDef p in def.researchPrerequisites)
                {
                    if (p?.era != null && (latest == null || p.era.order > latest.order)) latest = p.era;
                }
            }
            return latest?.defName ?? "ungated";
        }

        private static string GateEra(string defName)
        {
            ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return def == null ? "?" : GateEra(def);
        }

        private static void EmitCatalogue()
        {
            Report.SubHeading("0. buildable buildings by the era of their research gate");
            var rows = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building && !d.mineable)
                .GroupBy(GateEra)
                .OrderBy(g => EraOrder(g.Key))
                .Select(g => new[]
                {
                    g.Key, Report.Int(g.Count()),
                    string.Join(", ", g.Select(d => d.defName).OrderBy(n => n, StringComparer.Ordinal)),
                });
            Report.Table(new[] { "gate era", "defs", "defs" }, rows);
        }

        private static int EraOrder(string defName)
        {
            if (defName == "ungated") return -1;
            EraDef? era = DefDatabase<EraDef>.GetNamedSilentFail(defName);
            return era?.order ?? 99;
        }

        // ---- the summary tables ----

        private static string Year(int day) => Report.Num(day / (double)GenDate.DaysPerYear, 1);

        private static void EmitEraTimeline(
            List<(int Day, string From, string To)> eras, List<(int Day, string From, string To)> derived,
            List<(int Day, ResearchProjectDef Def)> finished, ResearchManager research)
        {
            Report.SubHeading("1. era timeline");
            var rows = new List<string[]>();
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order))
            {
                int reached = eras.FindIndex(e => e.To == era.defName);
                int turned = derived.FindIndex(e => e.To == era.defName);
                List<(int Day, ResearchProjectDef Def)> own = finished.Where(f => f.Def.era == era).ToList();
                rows.Add(new[]
                {
                    era.defName,
                    turned >= 0 ? Report.Int(derived[turned].Day) : "-",
                    turned >= 0 ? Year(derived[turned].Day) : "-",
                    reached >= 0 ? Report.Int(eras[reached].Day) : "-",
                    era.IsComplete ? "yes" : "no",
                    Report.Num(era.Progress * 100, 0) + "%",
                    Report.Int(own.Count) + " / " + Report.Int(era.Projects.Count),
                    own.Count > 0 ? Report.Int(own[0].Day) : "-",
                    own.Count > 0 ? Report.Int(own[own.Count - 1].Day) : "-",
                });
            }
            Report.Table(
                new[] { "era", "became current day", "year", "EraReached day", "complete", "progress", "finished in run / projects", "first", "last" },
                rows);
            Report.Note("`became current` is the day `ResearchManager.CurrentEra` first read this era, checked after "
                + "every finished project. `EraReached` is the engine's own event for the same crossing. "
                + "Current era at the end: " + (research.CurrentEra?.defName ?? "-") + ".");
        }

        private static void EmitResearchByEra(List<(int Day, ResearchProjectDef Def)> finished)
        {
            Report.SubHeading("2. research finished in the run, by game year");
            var rows = finished
                .GroupBy(f => f.Day / GenDate.DaysPerYear)
                .OrderBy(g => g.Key)
                .Select(g => new[]
                {
                    Report.Int(g.Key + 1), Report.Int(g.Count()),
                    string.Join(", ", g.Select(f => f.Def.defName + " [" + (f.Def.era?.defName ?? "-") + "]")),
                });
            Report.Table(new[] { "year", "projects", "which" }, rows);
        }

        private static void EmitPopulation(List<Sample> samples)
        {
            Report.SubHeading("3. population, deaths and food");
            DeathCause[] causes = (DeathCause[])Enum.GetValues(typeof(DeathCause));
            var headers = new List<string> { "day", "year", "era", "pop", "full", "interval", "statistical", "mood" };
            headers.AddRange(causes.Select(c => "died " + c));
            headers.AddRange(new[] { "food map", "food stores", "threat pts", "wealth", "moments" });
            var rows = samples.Select(s =>
            {
                var r = new List<string>
                {
                    Report.Int(s.Day), Year(s.Day), s.Era, Report.Int(s.Population), Report.Int(s.Full),
                    Report.Int(s.Interval), Report.Int(s.Statistical), Report.Num(s.Mood, 2),
                };
                r.AddRange(causes.Select(c => Report.Int((int)c < s.Deaths.Length ? s.Deaths[(int)c] : 0)));
                r.AddRange(new[]
                {
                    Report.Num(s.FoodOnMap, 0), Report.Num(s.FoodInStores, 0), Report.Num(s.ThreatPoints, 0),
                    Report.Num(s.Wealth, 0), Report.Int(s.Moments),
                });
                return r.ToArray();
            });
            Report.Table(headers.ToArray(), rows);
        }

        private static void EmitBuildings(List<Sample> samples)
        {
            Report.SubHeading("4. what stands, by building def, at each sample");
            List<string> defs = samples.SelectMany(s => s.Built.Keys).Distinct()
                .OrderBy(d => EraOrder(GateEra(d))).ThenBy(d => d, StringComparer.Ordinal).ToList();
            var headers = new List<string> { "def", "gate era" };
            headers.AddRange(samples.Select(s => "y" + Year(s.Day)));
            Report.Table(headers.ToArray(), defs.Select(d =>
            {
                var r = new List<string> { d, GateEra(d) };
                r.AddRange(samples.Select(s => s.Built.TryGetValue(d, out int n) ? Report.Int(n) : "-"));
                return r.ToArray();
            }));

            Report.SubHeading("4b. what stands, by gate era, against the era the settlement is in");
            List<string> gateEras = samples.SelectMany(s => s.Built.Keys).Select(GateEra).Distinct()
                .OrderBy(EraOrder).ToList();
            var h2 = new List<string> { "day", "year", "era", "distinct defs" };
            h2.AddRange(gateEras.Select(g => "gate " + g));
            Report.Table(h2.ToArray(), samples.Select(s =>
            {
                var r = new List<string> { Report.Int(s.Day), Year(s.Day), s.Era, Report.Int(s.Built.Count) };
                r.AddRange(gateEras.Select(g => Report.Int(s.Built.Where(kv => GateEra(kv.Key) == g).Sum(kv => kv.Value))));
                return r.ToArray();
            }));
        }

        private static void EmitMoments(IReadOnlyList<ChronicleEntry> moments)
        {
            Report.SubHeading("5. moments (" + Report.Int(moments.Count) + ")");
            var byKind = moments.GroupBy(m => m.incidentDefName).OrderByDescending(g => g.Count())
                .Select(g => new[] { g.Key, Report.Int(g.Count()), Report.Int(GenDate.DaysPassedAt(g.First().tick)) });
            Report.Table(new[] { "kind", "count", "first day" }, byKind);
        }

        private static void EmitCost(List<Sample> samples, double totalSeconds, long ticks)
        {
            Report.SubHeading("6. cost");
            var rows = samples.Skip(1).Select(s => new[]
            {
                Report.Int(s.Day), Year(s.Day), Report.Int(s.Full), Report.Num(s.Seconds, 1),
                Report.Num(s.Seconds / Math.Max(1, s.Day - PreviousDay(samples, s)) * GenDate.DaysPerYear / 60.0, 1),
            });
            Report.Table(new[] { "day", "year", "full pawns", "interval s", "min per game year" }, rows);
            Report.Note("Total " + Report.Num(totalSeconds / 60.0, 1) + " min for " + Report.Int(ticks) + " ticks ("
                + Report.Int((long)(ticks / Math.Max(1e-9, totalSeconds))) + " ticks/s).");
        }

        private static int PreviousDay(List<Sample> samples, Sample s)
        {
            int i = samples.IndexOf(s);
            return i > 0 ? samples[i - 1].Day : 0;
        }
    }
}
