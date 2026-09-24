using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

using SimWorld.Conditions;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// <b>An instrument aimed at one question: does the opening hour have a shape?</b>
    /// <c>docs/design/the-loop.md</c> §5 makes the storyteller the whole of SimWorld's pressure and says the
    /// first question is whether it is aimed and scaled correctly for a founding band of 20-40. Nobody knows,
    /// because nothing has ever read <see cref="StorytellerUtility.DefaultThreatPointsNow"/> over a run. This
    /// suite reads it, every in-game day, with every multiplier broken out as its own column, and reads what
    /// the storyteller actually did with the number beside it.
    ///
    /// <para/><b>It tunes nothing and it wants nothing.</b> No constant, curve or def is touched from here.
    /// The point is that the next change to the threat curve is argued against a reading rather than against
    /// an opinion — and <c>CLAUDE.md</c>'s warning about <c>--suite probe</c> applies here twice over: a
    /// threat curve tuned to make these tables tidy is a threat curve tuned to make the game flat.
    ///
    /// <para/><b>One arm: watched and played.</b> <see cref="ProbeSuite"/> runs two arms because it is asking
    /// whether the gaze changes a settlement's fortunes. This is asking what the player is put through, and
    /// the player is by definition watching. <c>GodCommands.OpenSettlement</c> generates the founding
    /// settlement's interior and puts its citizens at Full tier, so a raid arrives as pawns on a map and is
    /// fought, rather than being settled arithmetically by <c>SettlementRaidResolver</c>.
    ///
    /// <para/><b>Which world — read the heading before any table.</b> <c>--solo</c> chooses it, and solo is
    /// the default so a run with no flag replays every earlier one. A solo world has no civilization but the
    /// player's at time zero, so <c>RaidEnemy</c>, which picks its raider from hostile civilizations, cannot
    /// fire in it until emergence founds one. The first readings of this suite were taken with solo hardcoded
    /// and were very nearly read as findings about raids; the heading and table 0 now name the world, and
    /// table 0 lists every rival and where it stood with the player at the start and the end.
    ///
    /// <para/><b>What each table can and cannot see — read this before believing a cell.</b>
    ///
    /// <para/><i>Table 1 (the curve)</i> is exact and has no caveat. Every column is recomputed in this suite
    /// from public API in the same order <see cref="StorytellerUtility.DefaultThreatPointsNow"/> applies them,
    /// and the reconstruction is checked against that method's own return value on every sample: the
    /// <c>reconstruction</c> row under the table reports how many days disagreed, and the answer must be zero.
    /// If it is not, every decomposition column is wrong and the table says so rather than being quietly
    /// believed. "wealth" is <see cref="IIncidentTarget.PlayerWealthForStoryteller"/> — the number the
    /// storyteller reads, never a wealth figure computed here — and "pop" is the length of
    /// <see cref="IIncidentTarget.PlayerPawnsForStoryteller"/>, the same roster the curve sums over, which is
    /// the attended slice of the civilization rather than its head count. <c>GodRollup.TotalPopulation</c>
    /// (which includes the Statistical cohort) is reported separately in table 3 so the two are never confused.
    ///
    /// <para/><i>Table 2 (what fired)</i> distinguishes three things a reader would otherwise conflate:
    /// <list type="bullet">
    /// <item><b>attempted</b> — the storyteller selected this incident and raised
    /// <see cref="Storyteller.IncidentFired"/>.</item>
    /// <item><b>fired</b> — the worker returned true. Read off the chronicle's tail by object identity:
    /// <see cref="Storyteller.TryFire"/> appends exactly one <see cref="ChronicleEntry"/> on success and then
    /// raises the event, so the entry is there, is last, and has not been seen before. Nothing is inferred
    /// from a def name or a tick alone.</item>
    /// <item><b>stub</b> — the worker's own effect method contains no effect. Detected structurally, by the IL
    /// of the most derived <c>TryExecuteWorker</c> being a bare <c>ldc.i4.1; ret</c> (with
    /// <see cref="IncidentWorker_Placeholder"/> as a belt-and-braces second test), never from a list of def
    /// names this suite would have to be told to update. Three shipped incidents are stubs today — it was
    /// four until <c>WandererJoin</c> gained a real worker — and a table that showed them as pressure would
    /// be lying about what the player is under.</item>
    /// </list>
    ///
    /// <para/><i>The "what it did" column</i> is built only from quantities this port already attributes, and
    /// it under-claims by construction:
    /// <list type="bullet">
    /// <item><c>killed</c>, <c>razed</c> and <c>denied</c> come from <see cref="DeathLedger.BySource"/> and
    /// <see cref="ResourceImpactLedger"/>, which are keyed by the incident's own defName at the point the harm
    /// happens. They are settled over the whole window from this firing until that def next fires, so a raid
    /// whose casualties land over the following hours is still credited to the raid. A run ends with the last
    /// firing's window still open; whatever it had claimed by then is what it shows.</item>
    /// <item><c>quest</c>, <c>condition</c> and <c>sick</c> are measured over a tight window — the single tick
    /// the storyteller pass ran on, minus what earlier firings in the same pass already claimed. Conditions
    /// are identified by object identity against
    /// <see cref="GameConditionManager.ActiveConditions"/>; sickness by
    /// <see cref="Hediff.sourceIncident"/>, which <see cref="IncidentWorker_Disease"/> sets to its own defName.</item>
    /// <item><c>raiders</c>, <c>pack</c>, <c>repelled</c>, <c>looted</c> and <c>caravan</c> are read straight
    /// off the workers' own public readouts (<c>IncidentWorker_RaidEnemy.LastRaid*</c>,
    /// <c>IncidentWorker_ManhunterPack.LastPack</c>,
    /// <c>IncidentWorker_TraderCaravanArrival.LastArrival</c>) and only on a firing that returned true, so a
    /// failed firing can never inherit a previous one's numbers.</item>
    /// </list>
    ///
    /// <para/><b>What table 2 deliberately does not have a column for.</b> <i>Pawns downed.</i> There is no
    /// per-incident attribution for a downing in this port — <c>StorytellerPawnEvents.Notify_PawnDowned</c>
    /// forwards to adaptation and keeps nothing, and a downing is transient, so any per-firing number here
    /// would be a guess dressed as a measurement. Table 3 reports the peak number of citizens down on any one
    /// day instead, which is honest and is a run-level reading rather than a per-incident one. A firing that
    /// returned true, is not a stub, and moved nothing this instrument can see reads
    /// <c>no observable effect</c> — which means exactly that and not "it did nothing".
    ///
    /// <para/><i>Table 3 (shape)</i> computes its incident rates over <b>effective</b> firings — fired and not
    /// a stub — and reports the raw counts beside them, because the gap between the two is the single most
    /// useful number in this suite.
    ///
    /// <para/><b>Determinism</b> is checked by re-running a prefix of the same seed and comparing digests.
    /// Same shape as <see cref="TensionSuite"/>'s own question 4, and for the same reason: two runs of one
    /// seed either agree from the first sample or they do not. Four digests rather than one, because they
    /// fail apart and which one failed is the information: the threat points alone, the curve inputs that
    /// produce them, the world beside them, and the firing log. The first real run of this suite returned
    /// identical threat points and a diverging settlement — a single digest would have reported that as "the
    /// threat curve is not deterministic", which was false.
    /// </summary>
    internal static class StorytellerSuite
    {
        /// <summary>Days of the same seed re-run for the determinism check. A prefix, for
        /// <see cref="TensionSuite"/>'s reason: a second full-length run costs the same again to learn
        /// nothing more.</summary>
        private const int DeterminismDays = 8;

        /// <summary>How wide a window "incidents per 10 days" is normalised to.</summary>
        private const int RatePer = 10;

        public static void Run(BenchOptions opt)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            Report.Heading(
                "Storyteller probe — the threat curve over " + opt.Days.ToString(CultureInfo.InvariantCulture)
                + " in-game days at founding scale, " + WorldShape(opt));
            Report.Note(
                "One arm: **watched and played**. The founding settlement is opened, so its citizens run at "
                + "Full tier and a threat arrives on a real map. **World: "
                + (opt.Solo
                    ? "solo** — the player's civilization is the only one generated, and a rival exists only "
                    + "once emergence founds one, so `RaidEnemy` has nobody to raid with until then. Read every "
                    + "threat finding below as a finding about a world with no enemies in it."
                    : "populated** — rival civilizations are generated at time zero, as a player's world has "
                    + "them. Table 0 lists who they are and where they stand with the player.")
                + " Founding band " + Report.Int(opt.Band)
                + " (spec §5b.3 allows 20-40), seed " + opt.Seed.ToString(CultureInfo.InvariantCulture)
                + ", storyteller and difficulty as `Game.NewGame` defaults them. **This suite changes no "
                + "tuning constant and is not a target** — see its class doc, and `CLAUDE.md` on what happens "
                + "to a game tuned against its own instruments.");

            ArmResult arm = RunArm(opt, opt.Days);

            EmitWorld(opt, arm);
            EmitCurve(arm);
            EmitFirings(arm);
            EmitShape(opt, arm);
            IsItDeterministic(opt, arm);
            Throughput(opt, arm);
        }

        // ---- the one arm ----

        private static ArmResult RunArm(BenchOptions opt, int days)
        {
            Bootstrap.ResetSim(opt.Seed);

            // Nothing in this suite ablates anything; clearing is how a previous suite's leftover set is
            // prevented from being reported here as the baseline. Same reasoning as ProbeSuite's own call.
            Ablation.Clear();

            var sw = Stopwatch.StartNew();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: opt.Solo,
                bandSize: opt.Band);

            SimWorld.World.Settlement? home = PlayerSeat(game);
            if (home == null) throw new InvalidOperationException("storyteller: the new game founded no settlement");
            RequirePlayers(home);

            List<FactionReading> worldAtStart = ReadFactions();

            GodCommands.OpenSettlement(home.tile);

            var log = new FiringLog();
            Storyteller storyteller = game.Storyteller;
            storyteller.IncidentFired += log.OnFired;

            var rollup = new GodRollup();
            var samples = new List<CurveSample> { SampleCurve(0, game, home, rollup) };

            long ticks = 0;
            try
            {
                for (int day = 1; day <= days; day++)
                {
                    for (int i = 0; i < GenDate.TicksPerDay; i++)
                    {
                        // The storyteller runs from a post-ticker gated on TicksGame % IncidentCycleLengthTicks,
                        // and DoSingleTick increments the clock before it runs anything — so this is the tick
                        // about to carry a storyteller pass, and the only tick worth paying for a snapshot on.
                        if ((Find.TickManager.TicksGame + 1) % Storyteller.IncidentCycleLengthTicks == 0)
                        {
                            log.BeginInterval(day);
                        }

                        game.TickManager.DoSingleTick();
                        ticks++;
                    }

                    log.SettleAttributed();

                    // Re-resolve: the roster changes under us and a settlement can in principle be lost.
                    SimWorld.World.Settlement current = PlayerSeat(game) ?? home;
                    samples.Add(SampleCurve(day, game, current, rollup));
                }
            }
            finally
            {
                storyteller.IncidentFired -= log.OnFired;
            }

            log.SettleAttributed();
            sw.Stop();

            return new ArmResult(samples, log, ticks, sw.Elapsed.TotalSeconds, worldAtStart, ReadFactions());
        }

        /// <summary>
        /// The player's own founding settlement — <see cref="CivilizationTarget.Seat"/>, the oldest settlement
        /// of the civilization the storyteller is telling its story about.
        ///
        /// <para/><b>Not "the first settlement in the world", which is what this used to read.</b> In a solo
        /// world the two are the same object, because the player's founding is the only settlement at time
        /// zero and every emerged rival is appended after it. In a populated world they are not:
        /// <c>WorldGenStep_Factions</c> places every rival's settlements before <c>Game.NewGame</c> founds the
        /// player's, so the first one is a rival's, and a <c>--solo false</c> run on seed 12345 opened a
        /// tribal town called Elderwood and watched <i>it</i> while the player's band sat unattended.
        /// <see cref="RequirePlayers"/> is what caught that, and it stays so the suite refuses rather than
        /// measures the wrong town.
        /// </summary>
        private static SimWorld.World.Settlement? PlayerSeat(Game game) => game.CivilizationTarget.Seat;

        private static void RequirePlayers(SimWorld.World.Settlement home)
        {
            if (ReferenceEquals(home.faction, Find.FactionManager.OfPlayer)) return;
            throw new InvalidOperationException(
                "storyteller: the settlement this suite would open is " + home.name + " of "
                + (home.faction?.name ?? "no faction") + ", not the player's");
        }

        /// <summary>Every civilization other than the player's and where it stands with the player right now.
        /// Read, never written: this is the population <c>IncidentWorker_RaidEnemy</c> picks a raider from.</summary>
        private static List<FactionReading> ReadFactions()
        {
            var rows = new List<FactionReading>();
            SimWorld.Factions.Faction? player = Find.FactionManager.OfPlayer;
            foreach (SimWorld.Factions.Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (ReferenceEquals(f, player) || f.def.hidden) continue;
                rows.Add(new FactionReading(
                    f,
                    player == null ? "-" : f.RelationKindWith(player).ToString().ToLowerInvariant(),
                    player == null ? 0 : f.GoodwillWith(player),
                    player != null && f.HostileTo(player),
                    f.defeated));
            }
            return rows;
        }

        // ---- table 0: the world the reading was taken in ----

        private static void EmitWorld(BenchOptions opt, ArmResult arm)
        {
            Report.SubHeading("0. the world this run was taken in: " + WorldShape(opt));

            if (arm.WorldAtStart.Count == 0 && arm.WorldAtEnd.Count == 0)
            {
                Report.Note(
                    "No civilization but the player's existed at any point in this run. `RaidEnemy` picks its "
                    + "raider from hostile civilizations (`FactionManager.RandomEnemyFaction`), so over this run it "
                    + "**could not fire, by construction** — whatever table 2 says about it is a statement about "
                    + "this world, not about raids.");
                return;
            }

            var rows = new List<string[]>();
            var seen = new HashSet<SimWorld.Factions.Faction>();
            foreach (FactionReading start in arm.WorldAtStart)
            {
                seen.Add(start.Faction);
                FactionReading? end = Lookup(arm.WorldAtEnd, start.Faction);
                rows.Add(WorldRow(start.Faction, start.Describe(), end?.Describe() ?? "gone"));
            }
            foreach (FactionReading end in arm.WorldAtEnd)
            {
                if (seen.Contains(end.Faction)) continue;
                rows.Add(WorldRow(end.Faction, "not yet in existence", end.Describe()));
            }

            Report.Table(
                new[] { "civilization", "def", "tech", "day 0", "day " + Report.Int(opt.Days), "raids from day" },
                rows);

            int hostileStart = 0;
            foreach (FactionReading r in arm.WorldAtStart) if (r.Hostile && !r.Defeated) hostileStart++;
            int hostileEnd = 0;
            foreach (FactionReading r in arm.WorldAtEnd) if (r.Hostile && !r.Defeated) hostileEnd++;

            Report.Note(
                "Relations are to the player, as `goodwill` and the kind it reads as. `raids from day` is the "
                + "def's `earliestRaidDays`, which `FactionRaidRules.CanRaidYet` holds a raider to. Hostile and "
                + "undefeated: " + Report.Int(hostileStart) + " at day 0, " + Report.Int(hostileEnd) + " at day "
                + Report.Int(opt.Days) + ". `RaidEnemy` can pick a raider only from those, and only once its "
                + "`raids from day` has passed.");
        }

        private static FactionReading? Lookup(List<FactionReading> rows, SimWorld.Factions.Faction faction)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (ReferenceEquals(rows[i].Faction, faction)) return rows[i];
            }
            return null;
        }

        private static string[] WorldRow(SimWorld.Factions.Faction f, string atStart, string atEnd) => new[]
        {
            f.name,
            f.def.defName,
            f.def.techLevel.ToString(),
            atStart,
            atEnd,
            f.def.permanentEnemy
                ? Report.Int(f.def.earliestRaidDays) + " (permanent enemy)"
                : Report.Int(f.def.earliestRaidDays),
        };

        /// <summary>Which world a run was taken in, said in words — a solo reading was once read as a general
        /// one, and a label on the heading is the cheapest way to make that hard to do again.</summary>
        private static string WorldShape(BenchOptions opt) => opt.Solo
            ? "solo world (`--solo true`, the default)"
            : "populated world (`--solo false`)";

        // ---- table 1: the threat curve, decomposed ----

        /// <summary>
        /// Every term of <see cref="StorytellerUtility.DefaultThreatPointsNow"/>, recomputed here from public
        /// API in that method's own order, plus that method's own answer for the same instant. The two must
        /// agree to the bit; <see cref="CurveSample.ReconstructionMatches"/> is what says whether they did.
        /// </summary>
        private static CurveSample SampleCurve(int day, Game game, SimWorld.World.Settlement home, GodRollup rollup)
        {
            rollup.Recompute(home);

            CivilizationTarget target = game.CivilizationTarget;
            Storyteller storyteller = game.Storyteller;

            float wealth = target.PlayerWealthForStoryteller;
            float baseFromWealth = StorytellerUtility.PointsPerWealthCurve.Evaluate(wealth);
            float perColonist = StorytellerUtility.PointsPerColonistByWealthCurve.Evaluate(wealth);

            // Accumulated exactly as DefaultThreatPointsNow accumulates it — same start value, same order of
            // additions — so the reconstruction check below is a real check and not a tolerance.
            float points = baseFromWealth;
            int pop = 0;
            int downed = 0;
            foreach (Pawn pawn in target.PlayerPawnsForStoryteller)
            {
                pop++;
                if (!pawn.Dead && pawn.Downed) downed++;
                float factor = pawn.Dead ? 0f : GenMath.Lerp(0.5f, 1f, pawn.health.summaryHealth.SummaryHealthPercent);
                points += perColonist * factor;
            }
            float colonistSum = points - baseFromWealth;

            float difficultyFactor = storyteller.difficulty != null ? storyteller.difficulty.threatScale : 1f;
            float adaptationFactor = storyteller.adaptation.TotalThreatPointsFactor(storyteller.difficulty);
            float eraFactor = EraTransitionUtility.CurrentEraThreatPointsFactor();
            float daysPassedFactor = storyteller.def != null
                ? storyteller.def.pointsFactorFromDaysPassed.Evaluate(GenDate.DaysPassedAt(Find.TickManager.TicksGame))
                : 1f;

            if (storyteller.difficulty != null) points *= difficultyFactor;
            points *= adaptationFactor;
            points *= eraFactor;
            if (storyteller.def != null) points *= daysPassedFactor;

            float preClamp = points;
            float engine = StorytellerUtility.DefaultThreatPointsNow(target);

            return new CurveSample(
                day: day,
                pop: pop,
                downed: downed,
                wealth: wealth,
                baseFromWealth: baseFromWealth,
                perColonist: perColonist,
                colonistSum: colonistSum,
                difficultyFactor: difficultyFactor,
                adaptationFactor: adaptationFactor,
                eraFactor: eraFactor,
                daysPassedFactor: daysPassedFactor,
                preClamp: preClamp,
                points: engine,
                totalPopulation: rollup.TotalPopulation,
                mood: rollup.MeanMood,
                foodNeed: rollup.MeanFoodNeed,
                deaths: storyteller.deaths.Total,
                moments: storyteller.Moments.Count,
                era: Find.ResearchManager.CurrentEra?.defName ?? "-");
        }

        private static void EmitCurve(ArmResult arm)
        {
            Report.SubHeading("1. the threat curve, sampled daily");

            var rows = new List<string[]>();
            foreach (CurveSample s in arm.Samples)
            {
                rows.Add(new[]
                {
                    Report.Int(s.Day),
                    Report.Int(s.Pop),
                    Report.Num(s.Wealth, 0),
                    Report.Num(s.BaseFromWealth, 1),
                    Report.Num(s.PerColonist, 1),
                    Report.Num(s.ColonistSum, 1),
                    Report.Num(s.DifficultyFactor, 2),
                    Report.Num(s.AdaptationFactor, 3),
                    Report.Num(s.EraFactor, 2),
                    Report.Num(s.DaysPassedFactor, 2),
                    Report.Num(s.Points, 1),
                    s.ClampNote,
                });
            }

            Report.Table(
                new[]
                {
                    "day", "pop", "wealth", "base from wealth", "per-colonist", "colonist sum",
                    "x difficulty", "x adaptation", "x era", "x daysPassed", "points", "clamped?",
                },
                rows);

            int mismatches = 0;
            foreach (CurveSample s in arm.Samples)
            {
                if (!s.ReconstructionMatches) mismatches++;
            }

            Report.Note(
                "`wealth` is `IIncidentTarget.PlayerWealthForStoryteller` — what the storyteller reads, not a "
                + "figure computed here. `pop` is the roster the curve itself sums over "
                + "(`PlayerPawnsForStoryteller`), which is the attended slice, not the head count; table 3 "
                + "carries `GodRollup.TotalPopulation` beside it. `clamped?` is `min` or `max` when "
                + "`GenMath.Clamp` actually bound the result at "
                + Report.Num(StorytellerUtility.MinThreatPoints, 0) + " or "
                + Report.Num(StorytellerUtility.MaxThreatPoints, 0)
                + " — **a run sitting on a clamp is a run in which the whole curve above it is doing nothing**. "
                + "reconstruction: " + Report.Int(arm.Samples.Count - mismatches) + "/"
                + Report.Int(arm.Samples.Count) + " days where these columns multiply back to "
                + "`DefaultThreatPointsNow`'s own answer"
                + (mismatches == 0
                    ? "."
                    : ". **A mismatch means the decomposition is wrong and no column above can be trusted.**"));
        }

        // ---- table 2: what actually fired ----

        private static void EmitFirings(ArmResult arm)
        {
            Report.SubHeading("2. what actually fired");

            if (arm.Firings.Count == 0)
            {
                Report.Note(
                    "Nothing fired at all over this run. That is a reading, not an empty table: at founding "
                    + "scale the storyteller's own gates (`minDaysPassed`, `earliestDay`, `minRefireDays`) can "
                    + "outlast a short window. Widen `--days` before concluding the director is idle.");
                return;
            }

            var rows = new List<string[]>();
            foreach (Firing f in arm.Firings)
            {
                rows.Add(new[]
                {
                    Report.Int(f.Day),
                    f.DefName,
                    f.Category,
                    Report.Num(f.Points, 1),
                    f.Fired ? "yes" : "no",
                    f.Describe(),
                });
            }

            Report.Table(new[] { "day", "defName", "category", "points at fire", "fired?", "what it did" }, rows);

            Report.Note(
                "`points at fire` is the `IncidentParms.points` the worker was actually handed, which is **not** "
                + "table 1's daily sample and is not meant to be. `StorytellerUtility.DefaultParmsNow` gives "
                + "points only to the two threat categories, so every other category reads 0.0 and that means "
                + "\"this incident does not spend threat points\", not \"the storyteller had none\". A threat "
                + "incident marked `pointsScaleable` is additionally jittered by its comp's "
                + "`randomPointsFactorRange` (`StorytellerComp_RandomMain`), so it lands beside the curve "
                + "rather than on it.");

            var stubDefs = new SortedSet<string>(StringComparer.Ordinal);
            int stubFirings = 0;
            int failed = 0;
            int inert = 0;
            foreach (Firing f in arm.Firings)
            {
                if (!f.Fired)
                {
                    failed++;
                    continue;
                }
                if (f.Stub)
                {
                    stubFirings++;
                    stubDefs.Add(f.DefName + " (" + f.WorkerType + ")");
                }
                else if (!f.MovedAnything)
                {
                    inert++;
                }
            }

            Report.Note(
                "`fired?` is the worker's own return value, read off the chronicle tail by object identity — "
                + "see the class doc. **`nothing (stub worker)` is not a formatting choice**: "
                + Report.Int(stubFirings) + " of " + Report.Int(arm.Firings.Count)
                + " firings ran a worker whose effect method is a bare `return true`, detected from its IL "
                + "rather than from a list of names"
                + (stubDefs.Count == 0 ? "" : " — " + string.Join(", ", stubDefs))
                + ". " + Report.Int(failed) + " firings were refused by their worker (`fired? = no`), and "
                + Report.Int(inert) + " fired a real worker that moved nothing this instrument can see "
                + "(`no observable effect`), which is a weaker claim than \"it did nothing\" — the class doc "
                + "lists exactly what the column can and cannot see, and pawns downed is not on it.");

            if (arm.OrphanedAttribution)
            {
                Report.Note(
                    "**Orphaned attribution:** a source-keyed ledger grew for a defName with no open firing "
                    + "(killed " + Report.Int(arm.OrphanDeaths) + ", razed " + Report.Int(arm.OrphanStructures)
                    + ", denied " + Report.Num(arm.OrphanNutrition, 1) + "). That harm happened and this table "
                    + "does not show which firing owns it. It is a defect in this instrument, not in the game.");
            }
        }

        // ---- table 3: the shape of the run ----

        private static void EmitShape(BenchOptions opt, ArmResult arm)
        {
            Report.SubHeading("3. the shape of the run");

            var points = new List<float>();
            for (int i = 0; i < arm.Samples.Count; i++) points.Add(arm.Samples[i].Points);

            float mean = 0f;
            for (int i = 1; i < points.Count; i++) mean += Math.Abs(points[i] - points[i - 1]);
            mean = points.Count > 1 ? mean / (points.Count - 1) : 0f;

            int days = Math.Max(opt.Days, 1);
            int effective = 0;
            var effectiveDays = new HashSet<int>();
            var anyDays = new HashSet<int>();
            foreach (Firing f in arm.Firings)
            {
                anyDays.Add(f.Day);
                if (!f.Fired || f.Stub) continue;
                effective++;
                effectiveDays.Add(f.Day);
            }

            Report.Table(
                new[] { "reading", "value" },
                new List<string[]>
                {
                    new[] { "threat points min / median / max", Report.Num(Min(points), 1) + " / " + Report.Num(Median(points), 1) + " / " + Report.Num(Max(points), 1) },
                    new[] { "mean absolute day-on-day change in points", Report.Num(mean, 2) },
                    new[] { "effective incidents (fired, not a stub)", Report.Int(effective) },
                    new[] { "effective incidents per " + RatePer.ToString(CultureInfo.InvariantCulture) + " days", Report.Num(effective * (double)RatePer / days, 2) },
                    new[] { "days with any effective incident", Report.Int(effectiveDays.Count) + "/" + Report.Int(days) },
                    new[] { "longest quiet stretch, effective (days)", Report.Int(LongestQuiet(effectiveDays, days)) },
                    new[] { "attempted firings (all, incl. stubs and refusals)", Report.Int(arm.Firings.Count) },
                    new[] { "attempted firings per " + RatePer.ToString(CultureInfo.InvariantCulture) + " days", Report.Num(arm.Firings.Count * (double)RatePer / days, 2) },
                    new[] { "days with any attempted firing", Report.Int(anyDays.Count) + "/" + Report.Int(days) },
                    new[] { "longest quiet stretch, attempted (days)", Report.Int(LongestQuiet(anyDays, days)) },
                });

            Report.Note(
                "**The gap between the `effective` and `attempted` rows is the reading this suite exists for.** "
                + "A stub worker returns true, spends its refire timer and records a chronicle line, so it "
                + "counts as an incident everywhere except in what the player experiences. `longest quiet "
                + "stretch` counts consecutive days with nothing at all, measured over days 1.." + Report.Int(days)
                + ".");

            // Its own subheading rather than a second table under the one above: `Report.Note` writes a
            // blockquote and no trailing blank line, so a table emitted straight after one is swallowed into
            // it by every markdown renderer. Every other table in this bench is preceded by a heading for
            // exactly that reason.
            Report.SubHeading("was it interesting");

            CurveSample last = arm.Samples[arm.Samples.Count - 1];
            int eventful = 0;
            int peakDowned = 0;
            for (int i = 0; i < arm.Samples.Count; i++)
            {
                if (arm.Samples[i].Downed > peakDowned) peakDowned = arm.Samples[i].Downed;
                if (i == 0) continue;
                if (arm.Samples[i].Moments > arm.Samples[i - 1].Moments || arm.Samples[i].Deaths > arm.Samples[i - 1].Deaths) eventful++;
            }

            Report.Table(
                new[] { "moments", "eventful days", "food swing", "mood swing", "pop swing", "deaths", "peak downed", "end pop (all tiers)", "era" },
                new List<string[]>
                {
                    new[]
                    {
                        Report.Int(last.Moments),
                        Report.Int(eventful) + "/" + Report.Int(arm.Samples.Count - 1),
                        Report.Num(Swing(arm.Samples, x => x.FoodNeed)),
                        Report.Num(Swing(arm.Samples, x => x.Mood)),
                        Report.Int((long)Swing(arm.Samples, x => x.Pop)),
                        Report.Int(last.Deaths),
                        Report.Int(peakDowned),
                        Report.Int(last.TotalPopulation),
                        last.Era,
                    },
                });

            Report.Note(
                "Carried over from `--suite probe` so a storyteller reading and an outcome reading sit in the "
                + "same run. `moments` is `MomentCurator`'s own judgement of what was worth remembering and is "
                + "the better reading of the two — see `CLAUDE.md`. Flat is the failure mode; none of these is "
                + "a score. `peak downed` is the most citizens on the roster down on any one sampled day — a "
                + "run-level reading, because this port attributes a downing to no incident (class doc).");
        }

        private static int LongestQuiet(HashSet<int> busy, int days)
        {
            int longest = 0;
            int run = 0;
            for (int day = 1; day <= days; day++)
            {
                if (busy.Contains(day))
                {
                    run = 0;
                    continue;
                }
                run++;
                if (run > longest) longest = run;
            }
            return longest;
        }

        // ---- determinism ----

        private static void IsItDeterministic(BenchOptions opt, ArmResult arm)
        {
            Report.SubHeading("4. is it deterministic");

            int prefixDays = Math.Min(DeterminismDays, opt.Days);
            ArmResult repeat = RunArm(opt, prefixDays);

            int n = Math.Min(arm.Samples.Count, repeat.Samples.Count);

            // Four series rather than one, because a single "did the run replay" answer is the one answer a
            // reader must not be given here. The first run of this suite came back NO on a combined digest
            // while every threat-points value in it was identical to the last decimal — the divergence was in
            // the settlement's stores and its citizens' needs. A row labelled "threat curve" going NO on that
            // evidence says the threat curve is non-deterministic, which was not true and is exactly the kind
            // of claim this suite exists not to make. Split, each row answers only for its own columns.
            var series = new List<(string Name, Func<CurveSample, string> Line)>
            {
                ("threat points (the `points` column)", s => s.PointsLine()),
                ("curve inputs (wealth, per-colonist, the four factors)", s => s.InputsLine()),
                ("world beside it (pop, mood, food, deaths, moments)", s => s.WorldLine()),
            };

            var rows = new List<string[]>();
            var divergences = new List<(string Name, string A, string B)>();
            bool allSame = true;
            foreach ((string name, Func<CurveSample, string> line) in series)
            {
                string a = CurveSample.DigestOf(arm.Samples, n, line);
                string b = CurveSample.DigestOf(repeat.Samples, n, line);
                bool same = string.Equals(a, b, StringComparison.Ordinal);
                if (!same)
                {
                    allSame = false;
                    divergences.Add((name, a, b));
                }
                rows.Add(new[] { name, Report.Int(n - 1), same ? "yes" : "NO" });
            }

            string firingsA = Firing.DigestUpToDay(arm.Firings, prefixDays);
            string firingsB = Firing.DigestUpToDay(repeat.Firings, prefixDays);
            bool firingsSame = string.Equals(firingsA, firingsB, StringComparison.Ordinal);
            if (!firingsSame)
            {
                allSame = false;
                divergences.Add(("firing log", firingsA, firingsB));
            }
            rows.Add(new[] { "firing log", Report.Int(prefixDays), firingsSame ? "yes" : "NO" });

            Report.Table(new[] { "series", "days compared", "identical" }, rows);

            for (int i = 0; i < divergences.Count; i++)
            {
                (string name, string a, string b) = divergences[i];
                Console.WriteLine();
                Console.WriteLine(name + " — run 1: " + a);
                Console.WriteLine(name + " — run 2: " + b);
            }

            Report.Note(
                "A **prefix** comparison over " + Report.Int(prefixDays) + " days: two runs of one seed either "
                + "agree from the first sample or they do not. The run is split into four series because they "
                + "can fail apart, and which one failed is the whole of the information. The threat points are "
                + "a pure function of the roster, the stores and the clock; the curve inputs are where those "
                + "are read; the world beside them is what the settlement actually did; and the firing log is "
                + "where the seeded draws land. **Points can replay exactly while the world under them does "
                + "not** — below the wealth curve's 14,000 floor the points do not read stores at all, so a "
                + "settlement that hauled one fewer meal moves the world rows and cannot move the points row."
                + (allSame
                    ? ""
                    : " **A `NO` is a determinism failure, not a rounding artifact: the digests are formatted "
                    + "to fixed decimals precisely so they cannot be one. It is a reading about the "
                    + "simulation, not about this suite — nothing here writes to the world.**"));
        }

        private static void Throughput(BenchOptions opt, ArmResult arm)
        {
            Report.SubHeading("throughput");

            Report.Table(
                new[] { "days", "ticks", "seconds", "ticks/s", "seconds/day" },
                new List<string[]>
                {
                    new[]
                    {
                        Report.Int(opt.Days),
                        Report.Int(arm.Ticks),
                        Report.Num(arm.Seconds, 1),
                        Report.Num(arm.TicksPerSecond, 0),
                        Report.Num(opt.Days > 0 ? arm.Seconds / opt.Days : 0, 2),
                    },
                });

            Report.Note(
                "Watched and played is the expensive arm — a generated interior and citizens at Full tier — so "
                + "this is what decides the affordable window. The determinism re-run above is not counted here.");
        }

        // ---- small maths ----

        private static float Min(List<float> xs)
        {
            float lo = xs.Count > 0 ? xs[0] : 0f;
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] < lo) lo = xs[i];
            }
            return lo;
        }

        private static float Max(List<float> xs)
        {
            float hi = xs.Count > 0 ? xs[0] : 0f;
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] > hi) hi = xs[i];
            }
            return hi;
        }

        private static float Median(List<float> xs)
        {
            if (xs.Count == 0) return 0f;
            var sorted = new List<float>(xs);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2f;
        }

        private static float Swing(IReadOnlyList<CurveSample> samples, Func<CurveSample, float> read)
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

        // ---- types ----

        private sealed class ArmResult
        {
            public ArmResult(
                List<CurveSample> samples, FiringLog log, long ticks, double seconds,
                List<FactionReading> worldAtStart, List<FactionReading> worldAtEnd)
            {
                Samples = samples;
                Firings = log.Firings;
                Ticks = ticks;
                Seconds = seconds;
                OrphanDeaths = log.OrphanDeaths;
                OrphanStructures = log.OrphanStructures;
                OrphanNutrition = log.OrphanNutrition;
                WorldAtStart = worldAtStart;
                WorldAtEnd = worldAtEnd;
            }

            public List<CurveSample> Samples { get; }

            /// <summary>Every civilization but the player's, as it stood at time zero.</summary>
            public List<FactionReading> WorldAtStart { get; }

            /// <summary>The same, on the run's last day — including any civilization emergence founded since.</summary>
            public List<FactionReading> WorldAtEnd { get; }

            public List<Firing> Firings { get; }

            public long Ticks { get; }

            public double Seconds { get; }

            public double TicksPerSecond => Seconds > 0 ? Ticks / Seconds : 0;

            public int OrphanDeaths { get; }

            public int OrphanStructures { get; }

            public float OrphanNutrition { get; }

            public bool OrphanedAttribution => OrphanDeaths > 0 || OrphanStructures > 0 || OrphanNutrition > 0f;
        }

        /// <summary>One civilization's standing with the player at one instant.</summary>
        private sealed class FactionReading
        {
            public FactionReading(SimWorld.Factions.Faction faction, string kind, int goodwill, bool hostile, bool defeated)
            {
                Faction = faction;
                Kind = kind;
                Goodwill = goodwill;
                Hostile = hostile;
                Defeated = defeated;
            }

            public SimWorld.Factions.Faction Faction { get; }

            public string Kind { get; }

            public int Goodwill { get; }

            public bool Hostile { get; }

            public bool Defeated { get; }

            public string Describe() =>
                Kind + " (" + Goodwill.ToString(CultureInfo.InvariantCulture) + ")" + (Defeated ? ", defeated" : "");
        }
    }

    /// <summary>One day's reading of the threat curve, decomposed. Immutable, and digested through three
    /// separate lines — <see cref="PointsLine"/>, <see cref="InputsLine"/> and <see cref="WorldLine"/> — so
    /// the determinism check can say which part of a run failed to replay rather than only that one did.</summary>
    internal readonly struct CurveSample
    {
        public CurveSample(
            int day, int pop, int downed, float wealth, float baseFromWealth, float perColonist, float colonistSum,
            float difficultyFactor, float adaptationFactor, float eraFactor, float daysPassedFactor,
            float preClamp, float points, int totalPopulation, float mood, float foodNeed, int deaths,
            int moments, string era)
        {
            Day = day;
            Pop = pop;
            Downed = downed;
            Wealth = wealth;
            BaseFromWealth = baseFromWealth;
            PerColonist = perColonist;
            ColonistSum = colonistSum;
            DifficultyFactor = difficultyFactor;
            AdaptationFactor = adaptationFactor;
            EraFactor = eraFactor;
            DaysPassedFactor = daysPassedFactor;
            PreClamp = preClamp;
            Points = points;
            TotalPopulation = totalPopulation;
            Mood = mood;
            FoodNeed = foodNeed;
            Deaths = deaths;
            Moments = moments;
            Era = era;
        }

        public int Day { get; }

        public int Pop { get; }

        /// <summary>Citizens on the storyteller's own roster who are down right now. Not a per-incident
        /// number and deliberately not in table 2 — see <see cref="StorytellerSuite"/>'s class doc.</summary>
        public int Downed { get; }

        public float Wealth { get; }

        public float BaseFromWealth { get; }

        public float PerColonist { get; }

        public float ColonistSum { get; }

        public float DifficultyFactor { get; }

        public float AdaptationFactor { get; }

        public float EraFactor { get; }

        public float DaysPassedFactor { get; }

        /// <summary>The product of every column above, before <c>GenMath.Clamp</c> saw it.</summary>
        public float PreClamp { get; }

        /// <summary><see cref="StorytellerUtility.DefaultThreatPointsNow"/>'s own answer — never this suite's
        /// arithmetic, so the reconstruction check has something independent to check against.</summary>
        public float Points { get; }

        public int TotalPopulation { get; }

        public float Mood { get; }

        public float FoodNeed { get; }

        public int Deaths { get; }

        public int Moments { get; }

        public string Era { get; }

        /// <summary>Whether the clamp actually bound the result, and at which end. `-` when the decomposition
        /// passed through untouched, which is the answer that means the curve above is live.</summary>
        public string ClampNote
        {
            get
            {
                if (PreClamp < StorytellerUtility.MinThreatPoints) return "min";
                if (PreClamp > StorytellerUtility.MaxThreatPoints) return "max";
                return "-";
            }
        }

        /// <summary>Whether this suite's decomposition multiplies back to the engine's own number. Exact
        /// equality, not a tolerance: the reconstruction performs the identical operations in the identical
        /// order, so anything but equality means a term is wrong.</summary>
        public bool ReconstructionMatches =>
            GenMath.Clamp(PreClamp, StorytellerUtility.MinThreatPoints, StorytellerUtility.MaxThreatPoints) == Points;

        /// <summary>The answer the storyteller actually acts on, alone. Kept apart from every other column so
        /// that "the threat points replayed" is a claim this suite can make or refuse on its own evidence —
        /// see <see cref="StorytellerSuite"/>'s determinism section for why one combined digest was wrong.</summary>
        public string PointsLine() => string.Join("|", new[]
        {
            Day.ToString(CultureInfo.InvariantCulture),
            Points.ToString("F3", CultureInfo.InvariantCulture),
        });

        /// <summary>Everything table 1 multiplies together to reach <see cref="Points"/>, and nothing else.</summary>
        public string InputsLine() => string.Join("|", new[]
        {
            Day.ToString(CultureInfo.InvariantCulture),
            Pop.ToString(CultureInfo.InvariantCulture),
            Wealth.ToString("F3", CultureInfo.InvariantCulture),
            BaseFromWealth.ToString("F3", CultureInfo.InvariantCulture),
            PerColonist.ToString("F3", CultureInfo.InvariantCulture),
            ColonistSum.ToString("F3", CultureInfo.InvariantCulture),
            DifficultyFactor.ToString("F3", CultureInfo.InvariantCulture),
            AdaptationFactor.ToString("F4", CultureInfo.InvariantCulture),
            EraFactor.ToString("F3", CultureInfo.InvariantCulture),
            DaysPassedFactor.ToString("F3", CultureInfo.InvariantCulture),
        });

        /// <summary>What the settlement did, as table 3 reports it — no part of the threat curve.</summary>
        public string WorldLine() => string.Join("|", new[]
        {
            Day.ToString(CultureInfo.InvariantCulture),
            Downed.ToString(CultureInfo.InvariantCulture),
            TotalPopulation.ToString(CultureInfo.InvariantCulture),
            Mood.ToString("F4", CultureInfo.InvariantCulture),
            FoodNeed.ToString("F4", CultureInfo.InvariantCulture),
            Deaths.ToString(CultureInfo.InvariantCulture),
            Moments.ToString(CultureInfo.InvariantCulture),
            Era,
        });

        public static string DigestOf(IReadOnlyList<CurveSample> samples, int count, Func<CurveSample, string> line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            var sb = new StringBuilder();
            int n = Math.Min(count, samples.Count);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append("//");
                sb.Append(line(samples[i]));
            }
            return sb.ToString();
        }
    }

    /// <summary>One attempted firing and everything this instrument can honestly say it did.</summary>
    internal sealed class Firing
    {
        public Firing(int day, int tick, string defName, string category, float points, bool fired, bool stub, string workerType)
        {
            Day = day;
            Tick = tick;
            DefName = defName;
            Category = category;
            Points = points;
            Fired = fired;
            Stub = stub;
            WorkerType = workerType;
        }

        public int Day { get; }

        public int Tick { get; }

        public string DefName { get; }

        public string Category { get; }

        public float Points { get; }

        /// <summary>The worker's own return value. See <see cref="StorytellerSuite"/>'s class doc for how it
        /// is read without the event carrying it.</summary>
        public bool Fired { get; }

        /// <summary>The worker's effect method contains no effect, determined from its IL.</summary>
        public bool Stub { get; }

        public string WorkerType { get; }

        // Settled from source-keyed ledgers over this firing's whole window.
        public int Killed { get; set; }

        public int Razed { get; set; }

        public float Denied { get; set; }

        // Measured over the storyteller pass's own tick.
        public int Quests { get; set; }

        public int Sick { get; set; }

        public List<string> ConditionsStarted { get; } = new List<string>();

        /// <summary>Worker-specific readouts, each from that worker's own public API and only on a firing that
        /// returned true. Free-form because a raid and a caravan have nothing in common to tabulate.</summary>
        public List<string> Detail { get; } = new List<string>();

        public bool MovedAnything =>
            Killed > 0 || Razed > 0 || Denied > 0f || Quests > 0 || Sick > 0
            || ConditionsStarted.Count > 0 || Detail.Count > 0;

        public string Describe()
        {
            if (!Fired) return "refused by its worker";
            if (Stub) return "nothing (stub worker)";

            var parts = new List<string>();
            if (Killed > 0) parts.Add("killed " + Killed.ToString(CultureInfo.InvariantCulture));
            if (Razed > 0) parts.Add("razed " + Razed.ToString(CultureInfo.InvariantCulture) + " structures");
            if (Denied > 0f) parts.Add("denied " + Denied.ToString("0.#", CultureInfo.InvariantCulture) + " nutrition");
            if (Quests > 0) parts.Add(Quests == 1 ? "offered a quest" : "offered " + Quests.ToString(CultureInfo.InvariantCulture) + " quests");
            if (Sick > 0) parts.Add("made " + Sick.ToString(CultureInfo.InvariantCulture) + " sick");
            for (int i = 0; i < ConditionsStarted.Count; i++) parts.Add("began " + ConditionsStarted[i]);
            for (int i = 0; i < Detail.Count; i++) parts.Add(Detail[i]);

            return parts.Count == 0 ? "no observable effect" : string.Join("; ", parts);
        }

        public string Line() => string.Join("|", new[]
        {
            Day.ToString(CultureInfo.InvariantCulture),
            Tick.ToString(CultureInfo.InvariantCulture),
            DefName,
            Category,
            Points.ToString("F3", CultureInfo.InvariantCulture),
            Fired ? "1" : "0",
            Stub ? "1" : "0",
        });

        /// <summary>Digest of every firing up to and including <paramref name="days"/>. Deliberately excludes
        /// the effect columns: those are settled over a window that a prefix run truncates, so comparing them
        /// would report a shorter run as a determinism failure when it is only a shorter run.</summary>
        public static string DigestUpToDay(IReadOnlyList<Firing> firings, int days)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < firings.Count; i++)
            {
                if (firings[i].Day > days) break;
                if (sb.Length > 0) sb.Append("//");
                sb.Append(firings[i].Line());
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Collects every attempted firing and measures what it did, without the simulation knowing it exists.
    ///
    /// <para/><b>Two clocks, because two kinds of effect need two.</b> Quantities the port already keys by
    /// incident defName (deaths, structures, denied nutrition) are settled over a long window — from a firing
    /// until that def next fires — because harm arrives after the incident does. Quantities that are not
    /// keyed at all (quests, game conditions, sickness) are measured over the single tick the storyteller pass
    /// ran on, and against what earlier firings in the same pass already claimed, because over any longer
    /// window something else could have moved them.
    /// </summary>
    internal sealed class FiringLog
    {
        private readonly List<Firing> firings = new List<Firing>();
        private readonly Dictionary<string, Firing> openBySource = new Dictionary<string, Firing>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> lastDeaths = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> lastStructures = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastNutrition = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<Type, bool> stubCache = new Dictionary<Type, bool>();

        private int day;
        private int intervalQuests;
        private HashSet<GameCondition> intervalConditions = new HashSet<GameCondition>();
        private Dictionary<string, int> intervalSick = new Dictionary<string, int>(StringComparer.Ordinal);
        private ChronicleEntry? lastAttributedEntry;

        public List<Firing> Firings => firings;

        /// <summary>Harm a source-keyed ledger recorded for a defName no firing was open for. It happened, and
        /// this log cannot say which firing owns it — reported as a defect in the instrument rather than
        /// folded into a firing that may not have caused it.</summary>
        public int OrphanDeaths { get; private set; }

        public int OrphanStructures { get; private set; }

        public float OrphanNutrition { get; private set; }

        /// <summary>Called immediately before the tick that will carry a storyteller pass, so the tight-window
        /// baseline is as close to the pass as a caller outside the tick loop can get.</summary>
        public void BeginInterval(int currentDay)
        {
            day = currentDay;
            intervalQuests = Find.QuestManager.QuestsListForReading.Count;
            intervalConditions = SnapshotConditions();
            intervalSick = SnapshotSick();
        }

        public void OnFired(FiringIncident fi)
        {
            if (fi?.def == null) return;

            // Settle first: anything the ledgers have gained since the last settlement belongs to whichever
            // firing was open then, never to this one.
            SettleAttributed();

            int tick = Find.TickManager.TicksGame;
            bool fired = SucceededNow(fi, tick);
            IncidentWorker worker = fi.def.Worker;
            bool stub = IsEffectFree(worker);

            var record = new Firing(
                day,
                tick,
                fi.def.defName,
                fi.def.category?.defName ?? "-",
                fi.parms.points,
                fired,
                stub,
                worker.GetType().Name);

            firings.Add(record);
            openBySource[record.DefName] = record;

            if (fired && !stub)
            {
                MeasureTightWindow(record);
                MeasureWorkerReadouts(record, worker);
            }

            // Whether or not this firing is measured, the tight-window baseline advances: a later firing in
            // the same pass must not be handed the earlier one's changes as its own.
            intervalQuests = Find.QuestManager.QuestsListForReading.Count;
            intervalConditions = SnapshotConditions();
            intervalSick = SnapshotSick();
        }

        /// <summary>
        /// Credits every source-keyed ledger's growth since the last call to the firing that owns that source.
        /// Growth for a source that has never fired is counted as orphaned rather than quietly dropped.
        /// </summary>
        public void SettleAttributed()
        {
            Storyteller storyteller = Find.Storyteller;

            foreach (KeyValuePair<string, int> pair in storyteller.deaths.BySource)
            {
                lastDeaths.TryGetValue(pair.Key, out int before);
                if (pair.Value == before) continue;
                lastDeaths[pair.Key] = pair.Value;
                int delta = pair.Value - before;
                if (openBySource.TryGetValue(pair.Key, out Firing? open)) open.Killed += delta;
                else OrphanDeaths += delta;
            }

            foreach (KeyValuePair<string, int> pair in storyteller.resourceImpact.StructuresDestroyedBySource)
            {
                lastStructures.TryGetValue(pair.Key, out int before);
                if (pair.Value == before) continue;
                lastStructures[pair.Key] = pair.Value;
                int delta = pair.Value - before;
                if (openBySource.TryGetValue(pair.Key, out Firing? open)) open.Razed += delta;
                else OrphanStructures += delta;
            }

            foreach (KeyValuePair<string, float> pair in storyteller.resourceImpact.NutritionDeniedBySource)
            {
                lastNutrition.TryGetValue(pair.Key, out float before);
                if (pair.Value == before) continue;
                lastNutrition[pair.Key] = pair.Value;
                float delta = pair.Value - before;
                if (openBySource.TryGetValue(pair.Key, out Firing? open)) open.Denied += delta;
                else OrphanNutrition += delta;
            }
        }

        /// <summary>
        /// Whether the worker returned true, without the event carrying the answer.
        /// <see cref="Storyteller.TryFire"/> appends exactly one <see cref="ChronicleEntry"/> on success and
        /// raises the event immediately after, and the chronicle only ever trims from the front — so on
        /// success the tail is that entry, it carries this def's name and this tick, and this log has not
        /// already claimed it.
        /// </summary>
        private bool SucceededNow(FiringIncident fi, int tick)
        {
            System.Collections.Generic.IReadOnlyList<ChronicleEntry> chronicle = Find.Storyteller.Chronicle;
            if (chronicle.Count == 0) return false;

            ChronicleEntry tail = chronicle[chronicle.Count - 1];
            if (ReferenceEquals(tail, lastAttributedEntry)) return false;
            if (tail.tick != tick) return false;
            if (!string.Equals(tail.incidentDefName, fi.def.defName, StringComparison.Ordinal)) return false;

            lastAttributedEntry = tail;
            return true;
        }

        private void MeasureTightWindow(Firing record)
        {
            int quests = Find.QuestManager.QuestsListForReading.Count;
            if (quests > intervalQuests) record.Quests = quests - intervalQuests;

            foreach (GameCondition condition in ActiveConditions())
            {
                if (intervalConditions.Contains(condition)) continue;
                record.ConditionsStarted.Add(condition.def?.defName ?? condition.GetType().Name);
            }

            // Only this incident's own stamp counts: Hediff.sourceIncident carries the defName that caused it,
            // so a second illness arriving in the same tick belongs to whatever caused that one.
            Dictionary<string, int> sick = SnapshotSick();
            sick.TryGetValue(record.DefName, out int now);
            intervalSick.TryGetValue(record.DefName, out int before);
            if (now > before) record.Sick += now - before;
        }

        /// <summary>
        /// The three workers that keep a public record of their own last firing. Read only on a firing that
        /// returned true, so a refused firing can never inherit the previous one's numbers.
        /// </summary>
        private static void MeasureWorkerReadouts(Firing record, IncidentWorker worker)
        {
            switch (worker)
            {
                case IncidentWorker_RaidEnemy raid:
                {
                    int squad = raid.LastRaidPawns?.Count ?? 0;
                    if (squad > 0) record.Detail.Add(squad.ToString(CultureInfo.InvariantCulture) + " raiders arrived");
                    if (raid.LastRaidFaction != null) record.Detail.Add("from " + raid.LastRaidFaction.name);
                    SettlementRaidOutcome? outcome = raid.LastRaidOutcome;
                    if (outcome.HasValue && outcome.Value.Resolved)
                    {
                        SettlementRaidOutcome o = outcome.Value;
                        record.Detail.Add(o.Repelled ? "repelled" : "broke in");
                        if (o.TotalLivesLost > 0) record.Detail.Add("cost " + o.TotalLivesLost.ToString(CultureInfo.InvariantCulture) + " lives");
                        if (o.GoodsLooted > 0) record.Detail.Add("looted " + o.GoodsLooted.ToString(CultureInfo.InvariantCulture) + " items");
                    }
                    else if (squad > 0)
                    {
                        // Measured at the instant of firing, when the squad has landed and nothing has yet
                        // happened to it. "fought on the watched map" would be asserting an outcome this
                        // reading cannot have seen; whatever the fight costs arrives later, through `killed`.
                        record.Detail.Add("landed on the watched map, outcome not yet decided");
                    }
                    break;
                }

                case IncidentWorker_ManhunterPack manhunter:
                {
                    int pack = manhunter.LastPack?.Count ?? 0;
                    if (pack > 0) record.Detail.Add("a pack of " + pack.ToString(CultureInfo.InvariantCulture) + " turned manhunter");
                    break;
                }

                case IncidentWorker_TraderCaravanArrival trader:
                {
                    if (trader.LastArrival != null)
                    {
                        record.Detail.Add("a caravan landed");
                    }
                    else if (trader.LastTrader != null)
                    {
                        record.Detail.Add("a trader was generated but landed nowhere");
                    }
                    break;
                }

                default:
                    break;
            }
        }

        private static System.Collections.Generic.IReadOnlyList<GameCondition> ActiveConditions() =>
            Find.World?.gameConditionManager.ActiveConditions ?? Array.Empty<GameCondition>();

        private static HashSet<GameCondition> SnapshotConditions()
        {
            var set = new HashSet<GameCondition>();
            foreach (GameCondition condition in ActiveConditions()) set.Add(condition);
            return set;
        }

        /// <summary>
        /// How many live hediffs on the civilization's roster each incident put there.
        /// <see cref="IncidentWorker_Disease"/> stamps <see cref="Hediff.sourceIncident"/> with its own
        /// defName for exactly this kind of provenance question, so this needs no list of disease defs.
        /// </summary>
        private static Dictionary<string, int> SnapshotSick()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            Storyteller storyteller = Find.Storyteller;
            System.Collections.Generic.IReadOnlyList<IIncidentTarget> targets = storyteller.AllIncidentTargets;
            for (int t = 0; t < targets.Count; t++)
            {
                foreach (Pawn pawn in targets[t].PlayerPawnsForStoryteller)
                {
                    if (pawn.Dead || pawn.health == null) continue;
                    List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                    for (int h = 0; h < hediffs.Count; h++)
                    {
                        string? source = hediffs[h].sourceIncident;
                        if (string.IsNullOrEmpty(source)) continue;
                        counts.TryGetValue(source!, out int n);
                        counts[source!] = n + 1;
                    }
                }
            }
            return counts;
        }

        /// <summary>
        /// Whether this worker's effect method contains no effect — detected from the IL of the most derived
        /// <c>TryExecuteWorker</c>, which for a stub is a bare <c>ldc.i4.1; ret</c> and nothing else.
        /// Structural on purpose: a defName list would have to be maintained, and the day somebody adds a
        /// fifth stub is exactly the day this table would start over-claiming. The
        /// <see cref="IncidentWorker_Placeholder"/> test beside it is belt and braces — that class's own doc
        /// declares it a stand-in — and a worker whose IL cannot be read at all is reported as a real worker,
        /// because an instrument that cannot tell must not claim.
        /// </summary>
        private bool IsEffectFree(IncidentWorker worker)
        {
            Type type = worker.GetType();
            if (stubCache.TryGetValue(type, out bool cached)) return cached;

            bool stub = worker is IncidentWorker_Placeholder || HasConstantTrueBody(type);
            stubCache[type] = stub;
            return stub;
        }

        private static bool HasConstantTrueBody(Type workerType)
        {
            const byte Nop = 0x00;
            const byte LdcI4_0 = 0x16;
            const byte LdcI4_1 = 0x17;
            const byte Ret = 0x2A;

            MethodInfo? method = workerType.GetMethod(
                "TryExecuteWorker",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            byte[]? il = method?.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;

            int start = il.Length > 0 && il[0] == Nop ? 1 : 0;
            int length = il.Length - start;
            if (length != 2) return false;
            if (il[start + 1] != Ret) return false;
            return il[start] == LdcI4_1 || il[start] == LdcI4_0;
        }
    }
}
