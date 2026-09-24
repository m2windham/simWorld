using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>This suite measures outcomes, not time.</b> Every other suite in this bench answers "how long did
    /// that take"; this one answers "what happened to these people". It lives here anyway, and deliberately:
    /// the test suite takes the better part of an hour to run and cannot absorb a simulation measured in
    /// in-game years, while this harness already has content bootstrap, a seeded reset, a CLI and a markdown
    /// reporter, and is invoked on purpose rather than on every push.
    ///
    /// <para/><b>What it is for.</b> A defect is only measurable against a baseline that is trustworthy and
    /// repeatable. The intended use is a subtraction: run the probe, change one thing, run it again with the
    /// same seed, and attribute the difference. That is worthless if the two runs would have differed anyway,
    /// so determinism is the property this exists to provide and <c>ProbeDeterminismTests</c> is what
    /// defends it.
    ///
    /// <para/><b>Two arms, one seed.</b> The same world is run watched and unwatched. Citizens on an attended
    /// settlement are simulated at Full tier — they walk, work and eat — while unattended ones are carried by
    /// the abstract economy. The two should agree about how a century goes; where they disagree, one of them
    /// is wrong, and which one is a question you can only ask if you measured both from the same seed.
    ///
    /// <para/><b>It reports its own throughput</b> because the affordable window is a measurement, not an
    /// assumption. <c>GenDate.TicksPerYear</c> is 3,600,000 and there is no bulk-simulate path — the only way
    /// to advance the world is one <c>DoSingleTick</c> at a time — so what a decade costs in wall clock is a
    /// fact about this machine that the run itself is best placed to establish.
    /// </summary>
    internal static class ProbeSuite
    {
        /// <summary>Founding band size for both arms. Fixed rather than taken from <c>--pawns</c> (which means
        /// something else everywhere else in this bench) so the two arms are never accidentally unequal.</summary>
        private const int DefaultBandSize = 25;

        public static void Run(BenchOptions opt)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            Report.Heading("Simulation probe — outcomes over " + opt.Days.ToString(CultureInfo.InvariantCulture) + " in-game days");
            Report.Note(
                "Two arms from one seed. `watched` keeps the founding settlement attended and its interior "
                + "generated, so its citizens run at Full tier; `unwatched` clears focus and lets the abstract "
                + "economy carry them. Same seed, same world, same band.");

            ArmResult watched = RunArm(opt, attended: true, ablate: null);
            ArmResult unwatched = RunArm(opt, attended: false, ablate: null);

            Emit("watched", watched);
            Emit("unwatched", unwatched);
            Compare(watched, unwatched);
            WasItInteresting(watched, unwatched);

            if (opt.Without.Length > 0)
            {
                ArmResult watchedOff = RunArm(opt, attended: true, ablate: opt.Without);
                ArmResult unwatchedOff = RunArm(opt, attended: false, ablate: opt.Without);
                Ablated(opt, watched, unwatched, watchedOff, unwatchedOff);
                Throughput(opt, watched, unwatched);
                return;
            }

            Throughput(opt, watched, unwatched);
        }

        // ---- one arm ----

        private static ArmResult RunArm(BenchOptions opt, bool attended, string[]? ablate)
        {
            Bootstrap.ResetSim(opt.Seed);

            // Switched off for this arm only, and cleared by the next ResetSim — a run that leaves an
            // ablation set would silently report the disabled world as the baseline.
            Ablation.Clear();
            if (ablate != null)
            {
                for (int i = 0; i < ablate.Length; i++) Ablation.Disable(ablate[i]);
            }

            var sw = Stopwatch.StartNew();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: true,
                bandSize: DefaultBandSize);

            World.Settlement? home = FirstSettlement(game);
            if (home == null) throw new InvalidOperationException("probe: the new game founded no settlement");

            // NewGame already focuses the founding settlement, so `watched` only has to add the interior map
            // and `unwatched` is the arm that has to undo something.
            if (attended) GodCommands.OpenSettlement(home.tile);
            else game.God.Attention.ClearFocus();

            var rollup = new GodRollup();
            var samples = new List<ProbeSample> { Sample(0, home, rollup) };

            long ticks = 0;
            for (int day = 1; day <= opt.Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++)
                {
                    game.TickManager.DoSingleTick();
                    ticks++;
                }

                // Re-resolve: the roster changes under us and a settlement can in principle be lost.
                World.Settlement? current = FirstSettlement(game) ?? home;
                samples.Add(Sample(day, current, rollup));
            }

            sw.Stop();
            return new ArmResult(samples, ticks, sw.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// The player's own founding settlement. Not the first settlement in the world: in a populated world,
        /// world generation places rival civilizations before <c>Game.NewGame</c> founds the player, so the
        /// first one is a rival's. <c>StorytellerSuite</c> hit exactly this the first time it ran populated —
        /// it opened a rival tribal town and measured that. This suite still pins <c>soloStart: true</c>,
        /// where the two lookups agree, so nothing it has ever reported was affected; the fix is here so that
        /// letting it run populated later cannot quietly measure the wrong town.
        /// </summary>
        private static World.Settlement? FirstSettlement(Game game) => game.CivilizationTarget.Seat;

        // ---- the metric vector ----

        private static ProbeSample Sample(int day, World.Settlement home, GodRollup rollup)
        {
            rollup.Recompute(home);
            DeathLedger deaths = Find.Storyteller.deaths;

            return new ProbeSample(
                day: day,
                population: rollup.TotalPopulation,
                full: rollup.FullCount,
                interval: rollup.IntervalCount,
                statistical: rollup.StatisticalCount,
                mood: rollup.MeanMood,
                health: rollup.MeanHealth,
                foodNeed: rollup.MeanFoodNeed,
                industry: rollup.MeanIndustrySkill,
                age: deaths[DeathCause.Age],
                starvation: deaths[DeathCause.Starvation],
                disease: deaths[DeathCause.Disease],
                injury: deaths[DeathCause.Injury],
                unknown: deaths[DeathCause.Unknown],
                larderNutrition: LarderNutrition(home),
                researchDone: FinishedProjects(),
                moments: Find.Storyteller?.Moments.Count ?? 0,
                era: rollup.CurrentEra?.defName ?? "-",
                attributed: new Dictionary<string, int>(deaths.BySource));
        }

        /// <summary>Total edible nutrition sitting in the settlement's ledger. Summed here rather than on
        /// <c>Settlement</c> because the ledger is a count of things and only the probe cares what those
        /// things are worth as food.</summary>
        private static float LarderNutrition(World.Settlement home)
        {
            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> kv in home.Stores)
            {
                float per = kv.Key.ingestible?.nutrition ?? 0f;
                if (per > 0f) total += per * kv.Value;
            }
            return total;
        }

        private static int FinishedProjects()
        {
            ResearchManager? research = Find.ResearchManager;
            if (research == null) return 0;

            int done = 0;
            foreach (ResearchProjectDef def in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (research.IsFinished(def)) done++;
            }
            return done;
        }

        // ---- output ----

        private static void Emit(string name, ArmResult arm)
        {
            Report.SubHeading(name);

            var rows = new List<string[]>();
            foreach (ProbeSample s in arm.Samples)
            {
                rows.Add(new[]
                {
                    Report.Int(s.Day),
                    Report.Int(s.Population),
                    s.Full + "/" + s.Interval + "/" + s.Statistical,
                    Report.Num(s.Mood),
                    Report.Num(s.Health),
                    Report.Num(s.FoodNeed),
                    Report.Num(s.LarderNutrition, 1),
                    Report.Int(s.Deaths),
                    s.DeathBreakdown,
                    Report.Int(s.ResearchDone),
                    Report.Int(s.Moments),
                    s.Era,
                });
            }

            Report.Table(
                new[] { "day", "pop", "F/I/S", "mood", "health", "food", "larder", "dead", "of what", "research", "moments", "era" },
                rows);
            Console.WriteLine();
            Console.WriteLine("digest: " + arm.Digest);
        }

        /// <summary>
        /// The comparison the whole suite exists for: the same world, watched and not. A citizen should not be
        /// better off for being unobserved, and the size of any gap here is the size of the problem.
        /// </summary>
        private static void Compare(ArmResult watched, ArmResult unwatched)
        {
            Report.SubHeading("watched minus unwatched");

            var rows = new List<string[]>();
            int n = Math.Min(watched.Samples.Count, unwatched.Samples.Count);
            for (int i = 0; i < n; i++)
            {
                ProbeSample w = watched.Samples[i];
                ProbeSample u = unwatched.Samples[i];
                rows.Add(new[]
                {
                    Report.Int(w.Day),
                    Delta(w.Population - u.Population),
                    Signed(w.Mood - u.Mood),
                    Signed(w.FoodNeed - u.FoodNeed),
                    Signed(w.LarderNutrition - u.LarderNutrition, 1),
                    Delta(w.Deaths - u.Deaths),
                });
            }

            Report.Table(new[] { "day", "d pop", "d mood", "d food", "d larder", "d dead" }, rows);
            Report.Note(
                "A negative `d food` means the watched citizens are hungrier than the unwatched ones — the "
                + "gaze making a place worse off, which the tiering design should not want.");
        }

        private static void Throughput(BenchOptions opt, ArmResult watched, ArmResult unwatched)
        {
            Report.SubHeading("throughput");

            long ticks = watched.Ticks + unwatched.Ticks;
            double seconds = watched.Seconds + unwatched.Seconds;
            double perSecond = seconds > 0 ? ticks / seconds : 0;

            Report.Table(
                new[] { "arm", "ticks", "seconds", "ticks/s" },
                new List<string[]>
                {
                    new[] { "watched", Report.Int(watched.Ticks), Report.Num(watched.Seconds, 1), Report.Num(watched.TicksPerSecond, 0) },
                    new[] { "unwatched", Report.Int(unwatched.Ticks), Report.Num(unwatched.Seconds, 1), Report.Num(unwatched.TicksPerSecond, 0) },
                });

            if (perSecond > 0)
            {
                // Projected per arm, never from the combined rate: an attended arm carries a map and an
                // unattended one does not, so the two differ by more than an order of magnitude and their
                // average describes neither.
                Report.Note(
                    "A single in-game year is " + Report.Int(GenDate.TicksPerYear) + " ticks: about " +
                    Report.Num(YearMinutes(watched.TicksPerSecond), 1) + " minutes watched and " +
                    Report.Num(YearMinutes(unwatched.TicksPerSecond), 1) + " unwatched. That is what decides "
                    + "the affordable window — measured rather than assumed, and it moves with population, "
                    + "so re-read it as the band grows. `--days " +
                    opt.Days.ToString(CultureInfo.InvariantCulture) + "` produced the runs above.");
            }
        }

        /// <summary>
        /// <b>The reading this suite is most likely to get wrong by succeeding at everything else.</b>
        ///
        /// <para/>Every other number here measures survival: how many lived, how well they ate, how few died.
        /// Tuned against those alone, the optimum is a settlement that never starves, never loses anybody and
        /// never has a bad day — which is a spreadsheet, not a game. An instrument exists so a designer does
        /// not fool themselves about mechanics; the moment it becomes the target it starts doing the opposite.
        ///
        /// <para/>So this reports what the run was <i>like</i>. <b>Moments</b> are the chronicle curator's own
        /// judgement of what was worth remembering — first deaths, longevity records, the things that stood
        /// out — and a run that produced none was a run nobody would tell anybody about. <b>Swing</b> is
        /// peak-to-trough on the numbers a player actually feels; a flat line is the failure mode, not the
        /// goal, because near-misses and recoveries are what make a settlement's history worth having.
        ///
        /// <para/>Neither is a score to maximise. They are here so that "nothing went wrong" stops reading as
        /// success.
        /// </summary>
        private static void WasItInteresting(ArmResult watched, ArmResult unwatched)
        {
            Report.SubHeading("was it interesting");

            Report.Table(
                new[] { "arm", "moments", "eventful days", "food swing", "mood swing", "pop swing", "deaths" },
                new List<string[]>
                {
                    Row("watched", watched),
                    Row("unwatched", unwatched),
                });

            Report.Note(
                "Flat is the failure mode. A run with no moments and little swing is one nobody would tell a "
                + "story about, however many of its people survived — and survival is the only thing the "
                + "tables above can see.");

            WhoKilledThem(watched, unwatched);

            static string[] Row(string name, ArmResult arm)
            {
                List<ProbeSample> ss = arm.Samples;
                ProbeSample last = ss[ss.Count - 1];

                int eventful = 0;
                for (int i = 1; i < ss.Count; i++)
                {
                    if (ss[i].Moments > ss[i - 1].Moments || ss[i].Deaths > ss[i - 1].Deaths) eventful++;
                }

                return new[]
                {
                    name,
                    Report.Int(last.Moments),
                    eventful.ToString(CultureInfo.InvariantCulture) + "/" + (ss.Count - 1).ToString(CultureInfo.InvariantCulture),
                    Report.Num(Swing(ss, x => x.FoodNeed)),
                    Report.Num(Swing(ss, x => x.Mood)),
                    Report.Int((long)Swing(ss, x => x.Population)),
                    Report.Int(last.Deaths),
                };
            }
        }

        /// <summary>Peak minus trough across the run — how much the number actually moved, rather than where
        /// it ended up.</summary>
        private static float Swing(IReadOnlyList<ProbeSample> samples, Func<ProbeSample, float> read)
        {
            if (samples.Count == 0) return 0f;
            float lo = read(samples[0]);
            float hi = lo;
            for (int i = 1; i < samples.Count; i++)
            {
                float v = read(samples[i]);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            return hi - lo;
        }

        /// <summary>
        /// <b>The subtraction this whole harness exists for.</b> The same seed, run with the named things
        /// switched off and on, so the difference belongs to them.
        ///
        /// <para/>It is a subtraction rather than a comparison of two worlds because everything else is held
        /// identical by construction: an ablated incident is still selected, still fires, still spends its
        /// refire timer and still advances its own stream (see <c>SimWorld.Sim.Ablation</c>), so the
        /// storyteller's rolls and every other system's draws land exactly where they would have.
        ///
        /// <para/>Read the moments and swing rows, not only the deaths. A defect that costs lives and makes
        /// the run duller is a bad defect; one that costs lives and makes it more eventful may be the point.
        ///
        /// <para/><b>Read the `killed` column before the `d deaths` column, and believe it instead.</b> The
        /// subtraction above holds every <i>draw</i> identical but it cannot hold the <i>world</i> identical:
        /// from the tick a threat spawns, the two arms diverge — different jobs taken, different meals eaten,
        /// different people hurt — and by the end of a fortnight the difference in total deaths is mostly
        /// that divergence rather than the threat. Measured over three seeds, `d deaths` came back 0, +1, -2,
        /// 0, 0, +1: it changes sign, and a -2 would mean a manhunter pack saved two lives, which no causal
        /// story supports. The noise is larger than the effect, so the column cannot resolve what it looks
        /// like it is resolving. `killed` is read straight off the on-arm with no subtraction at all — each
        /// dead citizen credited to the incident that put their killer in the world — so it is exact in a
        /// single run and needs no control. The off-arm's `killed` is there only as a self-check: it must be
        /// zero, because an ablated threat kills nobody.
        /// </summary>
        private static void Ablated(
            BenchOptions opt, ArmResult watchedOn, ArmResult unwatchedOn, ArmResult watchedOff, ArmResult unwatchedOff)
        {
            Report.SubHeading("with minus without: " + string.Join(", ", opt.Without));

            string subject = opt.Without.Length > 0 ? opt.Without[0] : string.Empty;

            Report.Table(
                new[]
                {
                    "arm", "killed", "off-arm killed", "deaths on", "deaths off", "d deaths",
                    "moments on", "moments off", "d moments", "d mood swing",
                },
                new List<string[]>
                {
                    AblationRow("watched", watchedOn, watchedOff, subject),
                    AblationRow("unwatched", unwatchedOn, unwatchedOff, subject),
                });

            Report.Note(
                "`killed` is the answer; `d deaths` is the question it replaced. A zero in `killed` means the "
                + "thing under test genuinely killed nobody over this window — which is a real result, and "
                + "exactly what the shipped placeholder would have reported for its whole life. Widen "
                + "`--days` before concluding it is harmless; a threat gated behind a storyteller's "
                + "minDaysPassed cannot show up in a run shorter than the gate. A non-zero `off-arm killed` "
                + "is a bug in the ablation, not a finding: an ablated threat must kill nobody.");
        }

        private static string[] AblationRow(string name, ArmResult on, ArmResult off, string subject)
        {
            ProbeSample a = on.Samples[on.Samples.Count - 1];
            ProbeSample b = off.Samples[off.Samples.Count - 1];
            return new[]
            {
                name,
                Report.Int(a.AttributedTo(subject)),
                Report.Int(b.AttributedTo(subject)),
                Report.Int(a.Deaths),
                Report.Int(b.Deaths),
                Delta(a.Deaths - b.Deaths),
                Report.Int(a.Moments),
                Report.Int(b.Moments),
                Delta(a.Moments - b.Moments),
                Signed(Swing(on.Samples, x => x.Mood) - Swing(off.Samples, x => x.Mood)),
            };
        }

        /// <summary>
        /// Which threats actually killed somebody, and how many each took — read off one run, with no second
        /// arm to compare against.
        ///
        /// <para/>This is the readout the ablation table's `d deaths` column was standing in for, badly. A
        /// difference between two runs answers "did these two worlds end up different", which for anything
        /// that spawns onto a live map is mostly a question about divergence. Attribution answers "who killed
        /// this person", which is the question that was being asked all along, and a single run settles it.
        ///
        /// <para/><b>The unattributed remainder is not a gap.</b> Most deaths have no killer: age, hunger and
        /// illness are the settlement failing rather than something doing it. A row is only ever added by a
        /// threat that claims its dead, so an empty table means a fortnight in which nothing killed anybody —
        /// which for a civilization-scale game is an ordinary and usually good outcome, and a suspicious one
        /// only if a threat is known to have fired.
        /// </summary>
        private static void WhoKilledThem(ArmResult watched, ArmResult unwatched)
        {
            var rows = new List<string[]>();
            AddRows("watched", watched);
            AddRows("unwatched", unwatched);

            if (rows.Count == 0) return;

            Report.SubHeading("who killed them");
            Report.Table(new[] { "arm", "source", "killed", "of total deaths" }, rows);
            Report.Note(
                "Exact in one run: each death is credited to the incident that put its killer in the world, "
                + "at the moment it happens. Deaths with no row are the ones nothing claimed — age, hunger, "
                + "illness — which is most of them in a settlement that is merely struggling rather than "
                + "under attack.");

            void AddRows(string name, ArmResult arm)
            {
                ProbeSample last = arm.Samples[arm.Samples.Count - 1];
                foreach (KeyValuePair<string, int> pair in last.Attributed)
                {
                    if (pair.Value <= 0) continue;
                    rows.Add(new[]
                    {
                        name,
                        pair.Key,
                        Report.Int(pair.Value),
                        Report.Int(last.Deaths),
                    });
                }
            }
        }

        private static double YearMinutes(double ticksPerSecond) =>
            ticksPerSecond > 0 ? GenDate.TicksPerYear / ticksPerSecond / 60.0 : 0;

        private static string Delta(int d) => d > 0 ? "+" + d.ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture);

        private static string Signed(float d, int decimals = 2)
        {
            string s = Report.Num(d, decimals);
            return d > 0 ? "+" + s : s;
        }

        // ---- types ----

        private sealed class ArmResult
        {
            public ArmResult(List<ProbeSample> samples, long ticks, double seconds)
            {
                Samples = samples;
                Ticks = ticks;
                Seconds = seconds;
            }

            public List<ProbeSample> Samples { get; }

            public long Ticks { get; }

            public double Seconds { get; }

            public double TicksPerSecond => Seconds > 0 ? Ticks / Seconds : 0;

            /// <summary>Every sample folded to one line, so two runs are compared by eye in a second and by a
            /// test in one assertion. This is the value a determinism check pins.</summary>
            public string Digest => ProbeSample.DigestOf(Samples);
        }
    }

    /// <summary>One day's reading of the fixed metric vector. Immutable, and formatted by
    /// <see cref="Line"/> so the digest and the table can never drift apart.</summary>
    internal readonly struct ProbeSample
    {
        public ProbeSample(
            int day, int population, int full, int interval, int statistical,
            float mood, float health, float foodNeed, float industry,
            int age, int starvation, int disease, int injury, int unknown,
            float larderNutrition, int researchDone, int moments, string era,
            IReadOnlyDictionary<string, int>? attributed = null)
        {
            Day = day;
            Population = population;
            Full = full;
            Interval = interval;
            Statistical = statistical;
            Mood = mood;
            Health = health;
            FoodNeed = foodNeed;
            Industry = industry;
            Age = age;
            Starvation = starvation;
            Disease = disease;
            Injury = injury;
            Unknown = unknown;
            LarderNutrition = larderNutrition;
            ResearchDone = researchDone;
            Moments = moments;
            Era = era;
            Attributed = attributed ?? EmptyAttribution;
        }

        private static readonly IReadOnlyDictionary<string, int> EmptyAttribution =
            new Dictionary<string, int>();

        public int Day { get; }

        public int Population { get; }

        public int Full { get; }

        public int Interval { get; }

        public int Statistical { get; }

        public float Mood { get; }

        public float Health { get; }

        public float FoodNeed { get; }

        public float Industry { get; }

        public int Age { get; }

        public int Starvation { get; }

        public int Disease { get; }

        public int Injury { get; }

        public int Unknown { get; }

        public float LarderNutrition { get; }

        public int ResearchDone { get; }

        /// <summary>How many entries the chronicle's own curator has judged worth remembering. The closest
        /// thing this probe has to a reading of whether the run was <i>interesting</i> — see the class doc on
        /// why that matters more than any of the survival numbers beside it.</summary>
        public int Moments { get; }

        public string Era { get; }

        /// <summary>Deaths this run can name a killer for, keyed by the <c>defName</c> of the incident that
        /// put the killer in the world. The axis that makes a single run able to answer "what did this threat
        /// cost" — see the ablation table's doc for why differencing two runs could not.</summary>
        public IReadOnlyDictionary<string, int> Attributed { get; }

        /// <summary>What <paramref name="source"/> killed, or zero if it never killed anybody.</summary>
        public int AttributedTo(string? source) =>
            !string.IsNullOrEmpty(source) && Attributed.TryGetValue(source!, out int n) ? n : 0;

        public int Deaths => Age + Starvation + Disease + Injury + Unknown;

        /// <summary>The causes that actually happened, named. Empty rather than five zeroes, because a table
        /// of zeroes is harder to read than a blank.</summary>
        public string DeathBreakdown
        {
            get
            {
                var sb = new StringBuilder();
                Append(sb, "age", Age);
                Append(sb, "starv", Starvation);
                Append(sb, "dis", Disease);
                Append(sb, "inj", Injury);
                Append(sb, "unk", Unknown);
                return sb.Length == 0 ? "-" : sb.ToString();
            }
        }

        private static void Append(StringBuilder sb, string label, int n)
        {
            if (n == 0) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(label).Append(' ').Append(n.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Formatted to a fixed number of decimals on purpose. Two runs of the same seed produce bit-identical
        /// floats, so rounding loses nothing real; what it buys is a digest that stays readable and that a
        /// future change to float formatting cannot silently perturb.
        /// </summary>
        public string Line() => string.Join("|", new[]
        {
            Day.ToString(CultureInfo.InvariantCulture),
            Population.ToString(CultureInfo.InvariantCulture),
            Full.ToString(CultureInfo.InvariantCulture),
            Interval.ToString(CultureInfo.InvariantCulture),
            Statistical.ToString(CultureInfo.InvariantCulture),
            Mood.ToString("F4", CultureInfo.InvariantCulture),
            Health.ToString("F4", CultureInfo.InvariantCulture),
            FoodNeed.ToString("F4", CultureInfo.InvariantCulture),
            Industry.ToString("F4", CultureInfo.InvariantCulture),
            Age.ToString(CultureInfo.InvariantCulture),
            Starvation.ToString(CultureInfo.InvariantCulture),
            Disease.ToString(CultureInfo.InvariantCulture),
            Injury.ToString(CultureInfo.InvariantCulture),
            Unknown.ToString(CultureInfo.InvariantCulture),
            LarderNutrition.ToString("F3", CultureInfo.InvariantCulture),
            ResearchDone.ToString(CultureInfo.InvariantCulture),
            Moments.ToString(CultureInfo.InvariantCulture),
            Era,
        });

        /// <summary>Every sample in one string. Equality of two digests is the determinism claim.</summary>
        public static string DigestOf(IReadOnlyList<ProbeSample> samples)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0) sb.Append("//");
                sb.Append(samples[i].Line());
            }
            return sb.ToString();
        }
    }
}
