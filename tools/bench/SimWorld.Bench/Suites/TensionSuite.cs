using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>The measurement <c>docs/design/goal-renewal.md</c> §8 step 1 asks for, and nothing else.</b> It runs
    /// one real seeded game for a long span, samples <see cref="StandingReader"/> once per in-game year, and
    /// reports the series. No prospect, no offer, no feature is built on top of the reading here or anywhere
    /// else in this lane; the point is to find out whether building one would be building on sand.
    ///
    /// <para/>§2 of that document shows that <c>MomentCurator</c>'s three rules are all bounded and two decay
    /// by construction, so a renewal layer pointed at them would go quieter the longer you played. §3 proposes
    /// tensions instead. <b>That replacement is a claim, not a result</b> — a tension can flatten too, and the
    /// obvious way for this one to flatten is arithmetic: every population in this simulation grows at a
    /// compound rate, and a <i>ratio</i> between two things growing at the same rate is constant. So the four
    /// questions this suite exists to answer, in the order they matter:
    /// <list type="number">
    /// <item><b>Does it move?</b> Range and standard deviation over the run.</item>
    /// <item><b>Does its rate of change decay?</b> The run split into thirds, with the mean year-on-year
    /// movement in each. <b>This is the load-bearing question</b> and the exact test §7 names. A last third
    /// much quieter than the first means the tension has §2's defect under a new name.</item>
    /// <item><b>Does it survive the world settling?</b> How many samples had no rival left to compare
    /// against, and what the rival count did over the run.</item>
    /// <item><b>Is it deterministic?</b> Same seed, same series.</item>
    /// </list>
    ///
    /// <para/><b>Three gap series per subject, not one.</b> The headline <see cref="StandingReading.Gap"/> is a
    /// max-statistic over rivals, and a max can move for reasons the underlying quantity did not — a new
    /// civilization emerging at twenty people changes the answer to "who are we furthest from parity with"
    /// without anything about the existing rivals changing at all. Reporting
    /// <see cref="StandingReading.GapToWeakest"/> and <see cref="StandingReading.GapToStrongest"/> beside it is
    /// what makes that distinguishable instead of flattering.
    ///
    /// <para/><b>Two subjects, and the second one is not a convenience.</b> The reading the design document
    /// cares about is the player's. But a long unwatched run at founding scale <i>loses the player's
    /// civilization</i> — see below — and a series that goes undefined at year 3 cannot answer question 2
    /// about anything. So every run also tracks a <b>reference civilization</b>: the strongest non-player
    /// civilization alive at year 0, followed by identity for the whole run. Its series answers "does this
    /// tension decay" for a subject that survives long enough to be asked, and the player's series answers
    /// "can the player's own standing be read at all over a long run". Both are reported. Neither substitutes
    /// for the other, and the hand-back says which number came from which.
    ///
    /// <para/><b>Unwatched.</b> The founding settlement's focus is cleared so no interior map is generated:
    /// the probe suite measures the watched arm at about 1,700 ticks a second against roughly 375,000
    /// unwatched, and a century is 360 million ticks, so a watched run of this length is not affordable on any
    /// machine. Nothing relative standing reads happens on a map — growth, emergence and expansion are all
    /// world-scale and year-gated — so the unwatched arm is the right arm rather than a concession.
    ///
    /// <para/><b>The founding band does not survive, and that is a finding rather than an obstacle.</b>
    /// Measured, not inferred, every run unwatched:
    /// <list type="bullet">
    /// <item><c>--solo false --band 25 --seed 12345</c>: own population 25, 15, 2, 0 over four years, deaths
    /// ledger <c>injury 24</c>.</item>
    /// <item><c>--solo true --band 25 --seed 12345</c> — alone on the planet, no rival civilization in
    /// existence to raid it: 25, 10, 5, 1, 0, deaths <c>injury 25</c>.</item>
    /// <item><c>--solo true --band 40 --seed 12345</c> — the largest band spec §5b.3 allows: 40, 27, 23, 9, 2,
    /// 0 over five years, deaths <c>injury 46</c>.</item>
    /// </list>
    /// The die-off is therefore not about rivals and not about starting small within the permitted range: an
    /// unattended settlement at founding scale does not survive its own first decade, and the abstract path
    /// resolves those threats arithmetically through <c>Director.SettlementRaidResolver</c>.
    /// <b>Nor can the band simply be made bigger:</b> <c>World.SettlementFounder.Found</c> throws outside
    /// <c>SettlementTuning.FoundingBandRange</c>, which spec §5b.3 fixes at 20-40 people, so 40 is the whole
    /// budget and there is no "start large enough to survive" configuration to retreat to. <c>--band</c> and
    /// <c>--solo</c> exist to make all of this reproducible, not to escape it — and the reference
    /// civilization exists because of it.
    /// </summary>
    internal static class TensionSuite
    {
        /// <summary>One sample per in-game year. Not a tunable: growth
        /// (<c>DemographyTuning.DemographyIntervalTicks</c>), emergence and expansion
        /// (<c>EmergenceTuning.CheckIntervalTicks</c>) are all gated to exactly this cadence, so a faster
        /// sample rate would report the same number several times and make the series look calmer than the
        /// world is.</summary>
        private const int SampleIntervalTicks = GenDate.TicksPerYear;

        /// <summary>Span, in years, of the second run whose series is compared against the first for question
        /// 4. Short on purpose: a determinism check is a prefix comparison — the two runs either agree from
        /// the first sample or they do not — and paying for a second full-length run to learn that would
        /// double the suite's cost for no extra information. Every report of it says it was a prefix.</summary>
        private const int DeterminismYears = 15;

        /// <summary>Fewest samples a thirds comparison will be attempted on. Below this the suite says the run
        /// was too short rather than printing a ratio computed from two or three steps.</summary>
        private const int MinSamplesForThirds = 30;

        public static void Run(BenchOptions opt)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            int years = opt.Days / GenDate.DaysPerYear;

            Report.Heading("Tension probe — relative standing over " + Report.Int(years) + " in-game years");
            Report.Note(
                "`docs/design/goal-renewal.md` §8 step 1: one tension, read honestly, with no offer attached. "
                + "This reports whether the reading moves and whether its rate of change decays. It is an "
                + "instrument; nothing acts on it. World: "
                + (opt.Solo ? "solo start, rivals arriving through emergence over the run" : "crowded start")
                + ", founding band " + Report.Int(opt.Band) + ", seed "
                + opt.Seed.ToString(CultureInfo.InvariantCulture) + ", unwatched. **Read the `own` and `dead` "
                + "columns first.** A run whose subject civilization dies out is not a measurement of this "
                + "tension, whatever the tables below say about the years before it did.");

            ArmResult arm = RunArm(opt, years, opt.Solo);

            foreach (Series series in arm.AllSeries)
            {
                EmitSeries(series);
                DoesItMove(series);
                DoesItDecay(series);
                DoesItSurviveSettling(series);
            }

            IsItDeterministic(opt, arm);
            Throughput(arm, years);
        }

        // ---- one run ----

        private static ArmResult RunArm(BenchOptions opt, int years, bool solo)
        {
            Bootstrap.ResetSim(opt.Seed);

            var sw = Stopwatch.StartNew();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: solo,
                bandSize: opt.Band);

            // Unwatched: no interior map, no Full-tier crowd. See the class doc for why that is the right
            // arm here rather than a concession.
            game.God.Attention.ClearFocus();

            Faction? player = Find.FactionManager.OfPlayer;
            Faction? reference = PickReference(game);

            var playerSamples = new List<StandingSample>();
            var referenceSamples = new List<StandingSample>();
            Record(0, player, reference, playerSamples, referenceSamples);

            long ticks = 0;
            for (int year = 1; year <= years; year++)
            {
                for (int i = 0; i < SampleIntervalTicks; i++)
                {
                    game.TickManager.DoSingleTick();
                    ticks++;
                }

                Record(year, player, reference, playerSamples, referenceSamples);
            }

            sw.Stop();

            var playerSeries = new Series("player" + (player != null ? " (" + player.name + ")" : ""), playerSamples);
            Series? referenceSeries = reference != null
                ? new Series("reference civilization (" + reference.name + ")", referenceSamples)
                : null;

            return new ArmResult(playerSeries, referenceSeries, ticks, sw.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// The strongest non-player civilization in existence at year 0, or null when there is none (a solo
        /// start has exactly this property for its first decades). Strongest rather than first-on-the-roster
        /// so the reference subject is the one most likely to still be there at the end of the run, and ties
        /// break on <c>loadID</c> ordinal so the choice is a function of the seed alone.
        /// </summary>
        private static Faction? PickReference(Game game)
        {
            CoreWorld? world = game.World;
            if (world == null) return null;

            Faction? best = null;
            int bestStrength = 0;
            foreach (Faction faction in Find.FactionManager.GetFactions())
            {
                if (faction.def.isPlayer) continue;

                int strength = DiplomacyAI.StrengthOf(faction, world);
                if (strength <= 0) continue;

                if (best == null || strength > bestStrength ||
                    (strength == bestStrength && string.CompareOrdinal(faction.loadID, best.loadID) < 0))
                {
                    best = faction;
                    bestStrength = strength;
                }
            }

            return best;
        }

        private static void Record(
            int year, Faction? player, Faction? reference,
            List<StandingSample> playerSamples, List<StandingSample> referenceSamples)
        {
            DeathLedger deaths = Find.Storyteller.deaths;
            int deathTotal = deaths.Total;
            string breakdown = DeathBreakdown(deaths);
            int civilizations = Find.World?.factions.Count ?? 0;

            playerSamples.Add(new StandingSample(year, ReadFor(player), deathTotal, breakdown, civilizations));
            if (reference != null)
            {
                referenceSamples.Add(new StandingSample(year, ReadFor(reference), deathTotal, breakdown, civilizations));
            }
        }

        private static StandingReading ReadFor(Faction? subject)
        {
            CoreWorld? world = Find.World;
            if (subject == null || world == null) return StandingReading.Undefined;
            return StandingReader.ReadFor(subject, Find.FactionManager, world);
        }

        private static string DeathBreakdown(DeathLedger deaths)
        {
            var sb = new StringBuilder();
            foreach (DeathCause cause in Enum.GetValues(typeof(DeathCause)))
            {
                int n = deaths[cause];
                if (n == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(cause.ToString().ToLowerInvariant()).Append(' ').Append(n.ToString(CultureInfo.InvariantCulture));
            }

            return sb.Length == 0 ? "-" : sb.ToString();
        }

        // ---- the series itself ----

        private static void EmitSeries(Series series)
        {
            Report.SubHeading("the series — " + series.Name);

            var rows = new List<string[]>();
            foreach (StandingSample s in series.Samples)
            {
                StandingReading r = s.Reading;
                rows.Add(new[]
                {
                    Report.Int(s.Year),
                    Report.Int(r.OwnStrength),
                    Report.Int(s.Civilizations),
                    Report.Int(r.RivalCount),
                    Report.Int(r.WeakestRivalStrength),
                    Report.Int(r.StrongestRivalStrength),
                    r.IsDefined ? Report.Num(r.GapToWeakest) : "-",
                    r.IsDefined ? Report.Num(r.GapToStrongest) : "-",
                    r.IsDefined ? Signed(r.Gap) : "-",
                    r.IsDefined ? (r.Ahead ? "ahead" : "behind") : "undefined",
                    Report.Int(s.Deaths),
                    s.DeathBreakdown,
                });
            }

            Report.Table(
                new[]
                {
                    "year", "own", "civs", "rivals", "weakest", "strongest",
                    "gap wk", "gap st", "gap", "side", "dead", "of what",
                },
                rows);
            Report.Note(
                "Every gap is log2 of a strength ratio: +1 is twice their strength, -1 is half, 0 is parity. "
                + "`gap` is whichever of the two bracketing gaps is further from parity — the headline reading. "
                + "`civs` counts every civilization on the world's roster including this one, so a rival "
                + "arriving and a rival becoming comparable are visibly different events. `dead` is "
                + "`Storyteller.deaths`, which by that ledger's own doc counts **the player civilization's** "
                + "people only — so on the reference-civilization table it is context about the world, not "
                + "about that subject, and no conclusion about the reference civilization should be drawn "
                + "from it. Strength is "
                + "population, inherited from `DiplomacyAI.StrengthOf` and deliberately not fixed here: no "
                + "aggregate military ledger exists at civilization scale, so a large poor civilization reads "
                + "as stronger than a small well-armed one.");

            Console.WriteLine();
            Console.WriteLine("digest: " + series.Digest);
        }

        // ---- question 1: does it move ----

        private static void DoesItMove(Series series)
        {
            Report.SubHeading("1. does it move — " + series.Name);

            Report.Table(
                new[] { "series", "n", "min", "max", "range", "mean", "std dev" },
                new List<string[]>
                {
                    StatRow("gap (headline)", series.Defined, x => x.Gap),
                    StatRow("gap to weakest", series.Defined, x => x.GapToWeakest),
                    StatRow("gap to strongest", series.Defined, x => x.GapToStrongest),
                    StatRow("own strength", series.Defined, x => x.OwnStrength),
                });

            Report.Note(
                "Over the defined samples only — a sample with nothing to compare against is not a gap of "
                + "zero. A tension that sits flat is not a tension. Read the range and the standard deviation "
                + "in log2 units: 1.0 is a doubling. `own strength` is the control: if it moves and the gaps "
                + "do not, the ratio is cancelling growth that really happened, which is the specific way this "
                + "tension could flatten while the world underneath it did not.");
        }

        private static string[] StatRow(string name, IReadOnlyList<StandingReading> readings, Func<StandingReading, double> read)
        {
            if (readings.Count == 0)
            {
                return new[] { name, "0", "-", "-", "-", "-", "-" };
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            double sum = 0;
            for (int i = 0; i < readings.Count; i++)
            {
                double v = read(readings[i]);
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }

            double mean = sum / readings.Count;
            double sq = 0;
            for (int i = 0; i < readings.Count; i++)
            {
                double d = read(readings[i]) - mean;
                sq += d * d;
            }

            double sd = Math.Sqrt(sq / readings.Count);

            return new[]
            {
                name,
                Report.Int(readings.Count),
                Report.Num(min, 3),
                Report.Num(max, 3),
                Report.Num(max - min, 3),
                Report.Num(mean, 3),
                Report.Num(sd, 3),
            };
        }

        // ---- question 2: does its rate of change decay ----

        /// <summary>
        /// <b>The load-bearing question, and the exact test §7 of the design document names.</b> Movement is
        /// the mean absolute year-on-year change over a window — the rate of change, which is what "how much
        /// did this move" means for a sampled series and what a standard deviation cannot say (a series that
        /// swings once and then sits still has the same spread as one that swings every year, and only one of
        /// them is a live tension).
        ///
        /// <para/>Read the <c>last/first</c> column. Much below 1 means the tension goes quiet as the run goes
        /// on, which is §2's defect wearing a new name and would mean §8 should not proceed as written.
        /// </summary>
        private static void DoesItDecay(Series series)
        {
            Report.SubHeading("2. does its rate of change decay — " + series.Name);

            IReadOnlyList<StandingSample> samples = series.Samples;
            if (samples.Count < MinSamplesForThirds)
            {
                Report.Note(
                    "**The run was too short to answer this.** " + Report.Int(samples.Count) + " sample(s), "
                    + "and this suite will not compute a thirds ratio below " + Report.Int(MinSamplesForThirds)
                    + ". Widen `--days` and re-run; do not read a trend off the tables above.");
                return;
            }

            var rows = new List<string[]>
            {
                ThirdsRow("gap (headline)", samples, x => x.Gap),
                ThirdsRow("gap to weakest", samples, x => x.GapToWeakest),
                ThirdsRow("gap to strongest", samples, x => x.GapToStrongest),
            };

            Report.Table(
                new[] { "series", "steps 1/2/3", "move/yr 1st", "move/yr 2nd", "move/yr 3rd", "last/first" },
                rows);

            Report.Note(
                "Movement is the mean absolute year-on-year change within each third, in log2 units. Averaged "
                + "rather than summed so an uneven split cannot flatter or flatten a third by giving it more "
                + "steps than its neighbours — the run's length rarely divides by three, and a step is skipped "
                + "when either of its two readings is undefined, which is why the steps column is printed. "
                + "A third with no steps reads `-` and its ratio is not computed. `last/first` near or above 1 "
                + "means the tension is as live at the end of the run as at the start. Well below 1 means it "
                + "flattens, and `docs/design/goal-renewal.md` §2 applies to it exactly as it applies to "
                + "`MomentCurator`.");
        }

        private static string[] ThirdsRow(string name, IReadOnlyList<StandingSample> samples, Func<StandingReading, double> read)
        {
            int n = samples.Count;
            int per = n / 3;

            Window first = Movement(samples, read, 1, per);
            Window second = Movement(samples, read, per, 2 * per);
            Window third = Movement(samples, read, 2 * per, n);

            string ratio = first.Steps > 0 && third.Steps > 0 && first.MeanPerStep > 0
                ? Report.Num(third.MeanPerStep / first.MeanPerStep, 3)
                : "n/a";

            return new[]
            {
                name,
                first.Steps + "/" + second.Steps + "/" + third.Steps,
                first.Format(),
                second.Format(),
                third.Format(),
                ratio,
            };
        }

        /// <summary>
        /// Absolute year-on-year change over the steps landing in <c>[from, to)</c>, as a mean per step.
        ///
        /// <para/><b>A step whose two readings are not both defined is not a step.</b> Differencing across a
        /// year in which there was nothing to compare would either invent movement (a gap appearing from
        /// nowhere as the reading becomes defined) or hide it, and both would be the instrument reporting more
        /// than it can prove. Those years are dropped and counted.
        /// </summary>
        private static Window Movement(IReadOnlyList<StandingSample> samples, Func<StandingReading, double> read, int from, int to)
        {
            double total = 0;
            int steps = 0;
            for (int i = Math.Max(from, 1); i < to; i++)
            {
                if (!samples[i].Reading.IsDefined || !samples[i - 1].Reading.IsDefined) continue;
                total += Math.Abs(read(samples[i].Reading) - read(samples[i - 1].Reading));
                steps++;
            }

            return new Window(total, steps);
        }

        private readonly struct Window
        {
            public Window(double total, int steps)
            {
                Total = total;
                Steps = steps;
            }

            public double Total { get; }

            public int Steps { get; }

            public double MeanPerStep => Steps > 0 ? Total / Steps : 0;

            public string Format() => Steps > 0 ? Report.Num(MeanPerStep, 4) : "-";
        }

        // ---- question 3: does it survive the world settling ----

        private static void DoesItSurviveSettling(Series series)
        {
            Report.SubHeading("3. does it survive the world settling — " + series.Name);

            int undefined = 0;
            int minRivals = int.MaxValue;
            int maxRivals = 0;
            int noRivalLeft = 0;
            int subjectGone = 0;

            foreach (StandingSample s in series.Samples)
            {
                StandingReading r = s.Reading;
                if (!r.IsDefined) undefined++;
                if (r.RivalCount == 0) noRivalLeft++;
                if (r.OwnStrength == 0) subjectGone++;
                if (r.RivalCount < minRivals) minRivals = r.RivalCount;
                if (r.RivalCount > maxRivals) maxRivals = r.RivalCount;
            }

            if (minRivals == int.MaxValue) minRivals = 0;

            Report.Table(
                new[] { "samples", "undefined", "no rival left", "subject gone", "rivals min", "rivals max" },
                new List<string[]>
                {
                    new[]
                    {
                        Report.Int(series.Samples.Count),
                        Report.Int(undefined),
                        Report.Int(noRivalLeft),
                        Report.Int(subjectGone),
                        Report.Int(minRivals),
                        Report.Int(maxRivals),
                    },
                });

            Report.Note(
                "`undefined` is the honest answer rather than a gap in the instrument: relative standing is a "
                + "comparison, and with one side missing there is no comparison to make. The reading says so "
                + "instead of reporting a very large number a caller could mistake for a very one-sided world. "
                + "The two columns beside it separate the two ways that happens, and they mean opposite "
                + "things: `no rival left` is the world settling into one civilization, `subject gone` is this "
                + "civilization ceasing to exist. A high `subject gone` count means the run never measured the "
                + "tension at all.");
        }

        // ---- question 4: is it deterministic ----

        private static void IsItDeterministic(BenchOptions opt, ArmResult arm)
        {
            Report.SubHeading("4. is it deterministic");

            ArmResult repeat = RunArm(opt, DeterminismYears, opt.Solo);

            var rows = new List<string[]>();
            bool allSame = true;

            for (int i = 0; i < arm.AllSeries.Count; i++)
            {
                Series a = arm.AllSeries[i];
                Series? b = i < repeat.AllSeries.Count ? repeat.AllSeries[i] : null;
                if (b == null)
                {
                    rows.Add(new[] { a.Name, "0", "NO (second run had no such subject)" });
                    allSame = false;
                    continue;
                }

                int n = Math.Min(a.Samples.Count, b.Samples.Count);
                string da = a.DigestOf(n);
                string db = b.DigestOf(n);
                bool same = string.Equals(da, db, StringComparison.Ordinal);
                allSame &= same;

                rows.Add(new[] { a.Name, Report.Int(Math.Max(n - 1, 0)), same ? "yes" : "NO" });

                if (!same)
                {
                    Console.WriteLine();
                    Console.WriteLine(a.Name + " run 1: " + da);
                    Console.WriteLine(a.Name + " run 2: " + db);
                }
            }

            Report.Table(new[] { "subject", "years compared", "identical" }, rows);

            Report.Note(
                "A **prefix** comparison: the second run is " + Report.Int(DeterminismYears) + " years, not the "
                + "full span, because two runs of one seed either agree from the first sample or they do not, "
                + "and a second full-length run would cost the same again to learn nothing more. The reader "
                + "itself rolls nothing at all — it is a pure function of world state — so what this actually "
                + "checks is that the *world* replays identically under the same seed, which is the part that "
                + "could break." + (allSame ? "" : " **A `NO` above is a determinism failure, not a rounding "
                + "artifact: the digest is formatted to four decimals precisely so it cannot be one.**"));
        }

        private static void Throughput(ArmResult arm, int years)
        {
            Report.SubHeading("throughput");

            Report.Table(
                new[] { "years", "ticks", "seconds", "ticks/s", "seconds/year" },
                new List<string[]>
                {
                    new[]
                    {
                        Report.Int(years),
                        Report.Int(arm.Ticks),
                        Report.Num(arm.Seconds, 1),
                        Report.Num(arm.TicksPerSecond, 0),
                        Report.Num(years > 0 ? arm.Seconds / years : 0, 2),
                    },
                });

            Report.Note(
                "Measured rather than assumed, and it moves with population, so re-read it before widening the "
                + "span. `--days` is in in-game days and " + Report.Int(GenDate.DaysPerYear) + " of them make a "
                + "year, which is the sample interval. The determinism re-run above is not counted here.");
        }

        private static string Signed(float v)
        {
            string s = Report.Num(v);
            return v > 0 ? "+" + s : s;
        }

        // ---- types ----

        /// <summary>One subject's whole series, plus the derived views every question above reads.</summary>
        private sealed class Series
        {
            public Series(string name, List<StandingSample> samples)
            {
                Name = name;
                Samples = samples;

                var defined = new List<StandingReading>();
                foreach (StandingSample s in samples)
                {
                    if (s.Reading.IsDefined) defined.Add(s.Reading);
                }

                Defined = defined;
            }

            public string Name { get; }

            public List<StandingSample> Samples { get; }

            /// <summary>Only the samples that were a reading at all — see <c>StandingReading.IsDefined</c>.
            /// Statistics over undefined readings would be statistics over zeroes that mean "no comparison",
            /// not zeroes that mean parity.</summary>
            public IReadOnlyList<StandingReading> Defined { get; }

            public string Digest => DigestOf(Samples.Count);

            public string DigestOf(int count) => StandingSample.DigestOf(Samples, count);
        }

        private sealed class ArmResult
        {
            public ArmResult(Series player, Series? reference, long ticks, double seconds)
            {
                Ticks = ticks;
                Seconds = seconds;

                var all = new List<Series> { player };
                if (reference != null) all.Add(reference);
                AllSeries = all;
            }

            public IReadOnlyList<Series> AllSeries { get; }

            public long Ticks { get; }

            public double Seconds { get; }

            public double TicksPerSecond => Seconds > 0 ? Ticks / Seconds : 0;
        }
    }

    /// <summary>One year's reading, kept whole so the table and the digest can never drift apart.</summary>
    internal readonly struct StandingSample
    {
        public StandingSample(int year, StandingReading reading, int deaths, string deathBreakdown, int civilizations)
        {
            Year = year;
            Reading = reading;
            Deaths = deaths;
            DeathBreakdown = deathBreakdown;
            Civilizations = civilizations;
        }

        public int Year { get; }

        public StandingReading Reading { get; }

        /// <summary>Running total from <c>Storyteller.deaths</c>, which counts the <b>player</b>
        /// civilization's people only (that ledger's own doc: "citizens only... the civilization the player is
        /// responsible for"). So it explains a fall in the player's own strength and is only ambient context
        /// beside any other subject's.</summary>
        public int Deaths { get; }

        public string DeathBreakdown { get; }

        /// <summary>Every civilization on the world's roster, including hidden, defeated and the subject —
        /// deliberately a broader count than <c>StandingReading.RivalCount</c>, so a rival appearing in the
        /// world and a rival becoming comparable are two visibly different events.</summary>
        public int Civilizations { get; }

        public string Line() => string.Join("|", new[]
        {
            Year.ToString(CultureInfo.InvariantCulture),
            Reading.OwnStrength.ToString(CultureInfo.InvariantCulture),
            Reading.RivalCount.ToString(CultureInfo.InvariantCulture),
            Reading.WeakestRivalStrength.ToString(CultureInfo.InvariantCulture),
            Reading.StrongestRivalStrength.ToString(CultureInfo.InvariantCulture),
            Reading.Gap.ToString("F4", CultureInfo.InvariantCulture),
        });

        public static string DigestOf(IReadOnlyList<StandingSample> samples, int count)
        {
            var sb = new StringBuilder();
            int n = Math.Min(count, samples.Count);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append("//");
                sb.Append(samples[i].Line());
            }

            return sb.ToString();
        }
    }
}
