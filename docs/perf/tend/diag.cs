// Lane-private diagnostic (lane/tend). Replays the world `--suite storyteller --solo false` builds for a seed (the
// recipe lane/nonsolo matched to the bench and lane/downed reused) and follows every downing of a citizen with one
// question: from the moment they went down, what stood between them and a tend, and what killed the ones who died?
//
// Observation only reads: no Rand draws, no reservations, no jobs, no work-giver calls. The only calls that do more
// than read a field are capacity/stat getters, which cache but draw nothing.
//
//   run  <seed> <days> <dataDir> [saveTicks comma-separated] [saveDir]
//   load <save.xml> <dataDir> <endDay> <contSeed>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

internal sealed class Episode
{
    public Pawn P = null!;
    public int Downed;
    public float BleedAtDown;
    public float BloodLossAtDown;
    public int TtdAtDown;               // RimWorld's HealthUtility.TicksUntilDeathDueToBloodLoss at the moment of downing
    public int HostilesAtDown;
    public int AbleAtDown;
    public int MinAble = int.MaxValue;
    public int FirstTendJob = -1;
    public string Tender = "";
    public int TenderMedicine = -1;
    public string TenderPrevJob = "";
    public int FirstTended = -1;
    public int FirstBleedFree = -1;      // first observation with no bleeding at all
    public int ThreatClear = -1;         // first observation after downing with no hostile standing on the map
    public int End = -1;
    public string Outcome = "";
    public string DeathLook = "";
    public string Culprit = "";
    public int WaitSamplesHostile;       // 300-tick samples bleeding+untended while a hostile stood on the map
    public int WaitSamplesClear;         // ... and with none standing
    public Job? lastTendJob;
    public int TendJobs;
}

internal sealed class Raid
{
    public int Fired;
    public float Points;
    public int PeakHostiles;
    public int Clear = -1;
    public int CitizensAbleAtClear = -1;
    public int CitizensDownedAtClear = -1;
    public int HostilesLeftMap;
    public int HostilesDown;
    public int HostilesDead;
    public HashSet<Pawn> Members = new HashSet<Pawn>();
}

internal static class Program
{
    private static Faction player = null!;
    private static SimWorld.Map.Map map = null!;
    private static readonly Dictionary<Pawn, Episode> open = new Dictionary<Pawn, Episode>();
    private static readonly List<Episode> closed = new List<Episode>();
    private static readonly Dictionary<Pawn, string> lastLook = new Dictionary<Pawn, string>();
    private static readonly Dictionary<Pawn, string> curJobName = new Dictionary<Pawn, string>();
    private static readonly Dictionary<Pawn, string> prevJobName = new Dictionary<Pawn, string>();
    private static readonly List<Raid> raids = new List<Raid>();
    private static readonly HashSet<Pawn> hostilesSeen = new HashSet<Pawn>();
    // What able citizens were doing while at least one citizen lay bleeding and untended, split by threat state.
    private static readonly Dictionary<string, int> doingHostile = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> doingClear = new Dictionary<string, int>();
    private static int samplesHostile, samplesClear;
    private static float restSumAsleepClear; private static int restNAsleepClear;

    private static int Main(string[] args)
    {
        if (args[0] == "run") return Run(args);
        if (args[0] == "load") return LoadAndRun(args);
        throw new ArgumentException(args[0]);
    }

    private static DefDatabase LoadContent(string data)
    {
        var database = new DefDatabase();
        DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true }, data);
        if (!result.Success) throw new InvalidOperationException("content: " + string.Join("; ", result.Errors));
        DefDatabase.Global = database;
        return database;
    }

    private static int Run(string[] args)
    {
        int seed = int.Parse(args[1], CultureInfo.InvariantCulture);
        int days = int.Parse(args[2], CultureInfo.InvariantCulture);
        string data = args[3];
        var saveTicks = new HashSet<int>(args.Length > 4 && args[4] != "-" ? args[4].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)) : Enumerable.Empty<int>());
        string saveDir = args.Length > 5 ? args[5] : ".";

        LoadContent(data);
        Find.Reset();
        Find.TickManager = new TickManager();
        Rand.Current = new RandomStream(seed);
        Pawn.ResetThingIdCounter();
        SimWorld.Map.Map.ResetMapIdCounter();
        Ablation.Clear();

        Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed.ToString(CultureInfo.InvariantCulture),
            subdivisionOverride: 3, soloStart: false, bandSize: 25);
        SimWorld.World.Settlement home = game.CivilizationTarget.Seat ?? throw new InvalidOperationException("no seat");
        GodCommands.OpenSettlement(home.tile);
        player = Find.FactionManager.OfPlayer!;
        map = home.InteriorMap!;
        Hook(game);
        Header();

        int end = days * GenDate.TicksPerDay;
        while (Find.TickManager.TicksGame < end)
        {
            game.TickManager.DoSingleTick();
            int now = Find.TickManager.TicksGame;
            if (now % 30 == 0) Observe(now);
            if (now % GenDate.TicksPerDay == 0) DayLine(now);
            if (saveTicks.Contains(now))
            {
                string path = Path.Combine(saveDir, "s" + seed + "-t" + now + ".xml");
                Scribe.SaveToFile(game, "game", path);
                Console.WriteLine("  SAVED " + path);
            }
        }
        Report();
        return 0;
    }

    private static int LoadAndRun(string[] args)
    {
        string inXml = args[1];
        DefDatabase db = LoadContent(args[2]);
        int endDay = int.Parse(args[3], CultureInfo.InvariantCulture);
        int contSeed = int.Parse(args[4], CultureInfo.InvariantCulture);
        Find.Reset();
        Pawn.ResetThingIdCounter();
        SimWorld.Map.Map.ResetMapIdCounter();
        Ablation.Clear();
        Game game = Scribe.Load<Game>(File.ReadAllText(inXml), "game", out IReadOnlyList<string> errors, db);
        if (errors.Count > 0) Console.WriteLine("LOAD ERRORS (" + errors.Count + "): " + string.Join(" | ", errors.Take(5)));
        Rand.Current = new RandomStream(contSeed);
        for (int i = 0; i < 5 * Storyteller.IncidentCycleLengthTicks && game.CivilizationTarget.Seat == null; i++) game.TickManager.DoSingleTick();
        SimWorld.World.Settlement home = game.CivilizationTarget.Seat ?? throw new InvalidOperationException("no seat");
        player = Find.FactionManager.OfPlayer!;
        map = home.InteriorMap!;
        Hook(game);
        Console.WriteLine("loaded " + inXml + " at tick " + Find.TickManager.TicksGame + ", continuation seed " + contSeed);
        Header();
        int end = endDay * GenDate.TicksPerDay;
        while (Find.TickManager.TicksGame < end)
        {
            game.TickManager.DoSingleTick();
            int now = Find.TickManager.TicksGame;
            if (now % 30 == 0) Observe(now);
            if (now % GenDate.TicksPerDay == 0) DayLine(now);
        }
        Report();
        return 0;
    }

    private static void Hook(Game game)
    {
        Find.LetterStack.LetterReceived += l =>
        {
            if (l.label.StartsWith("Death", StringComparison.Ordinal))
                Console.WriteLine("  [" + Stamp(Find.TickManager.TicksGame) + "] DEATH " + l.text);
        };
        game.Storyteller.IncidentFired += fi =>
        {
            Console.WriteLine("  [" + Stamp(Find.TickManager.TicksGame) + "] FIRED " + fi.def.defName + " points=" + F(fi.parms.points));
            if (fi.def.defName == "RaidEnemy") raids.Add(new Raid { Fired = Find.TickManager.TicksGame, Points = fi.parms.points });
        };
    }

    private static void Header()
    {
        List<Pawn> humans = Citizens().ToList();
        Console.WriteLine("start: citizens=" + humans.Count + " doctorActive=" + humans.Count(p => p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
            + " beds=" + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count);
        Pawn? sample = humans.FirstOrDefault(p => p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor));
        if (sample != null)
        {
            Console.WriteLine("  sample " + sample.Label + " emergency givers: " + string.Join(", ", sample.workSettings.WorkGiversInOrderEmergency.Select(g => g.defName)));
            Console.WriteLine("  sample normal givers (first 14): " + string.Join(", ", sample.workSettings.WorkGiversInOrderNormal.Take(14).Select(g => g.defName + "(" + sample.workSettings.GetPriority(g.workType) + ")")));
        }
        Console.WriteLine("  doctor priority histogram: " + string.Join(", ", humans.Where(p => p.workSettings != null).GroupBy(p => p.workSettings.GetPriority(WorkTypeDefOf.Doctor)).OrderBy(g => g.Key).Select(g => "p" + g.Key + "=" + g.Count())));
        Console.WriteLine("  medicine skill histogram: " + string.Join(", ", humans.GroupBy(p => Med(p)).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine("  armed citizens: " + humans.Count(p => AttackVerbUtility.HasEquippedWeapon(p)));
    }

    private static int Med(Pawn p) => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? -1;

    private static IEnumerable<Pawn> Citizens()
    {
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
        for (int i = 0; i < all.Count; i++)
        {
            Pawn p = all[i];
            if (ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike && !p.Dead) yield return p;
        }
    }

    private static bool IsHostileStanding(Pawn p) =>
        p.Spawned && !AttackTargetsUtility.ThreatDisabled(p)
        && ((p.faction != null && p.faction.HostileTo(player)) || AttackTargetsUtility.IsManhunting(p));

    private static int HostilesStanding()
    {
        int n = 0;
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
        for (int i = 0; i < all.Count; i++)
        {
            if (IsHostileStanding(all[i])) { n++; hostilesSeen.Add(all[i]); }
        }
        return n;
    }

    private static bool Able(Pawn p) => p.Spawned && !p.Dead && !p.Downed;

    private static bool BleedingUntended(Pawn p)
    {
        foreach (Hediff h in p.health.hediffSet.hediffs)
            if (h.BleedRate > 0f && h.TendableNow()) return true;
        return false;
    }

    private static int Ttd(Pawn p)
    {
        float bleed = p.health.hediffSet.BleedRateTotal;
        if (bleed < 0.0001f) return int.MaxValue;
        float bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
        return (int)((1f - bl) / bleed * GenDate.TicksPerDay);
    }

    private static string Look(Pawn p)
    {
        var c = p.health.capacities;
        float bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
        int injuries = p.health.hediffSet.hediffs.Count(h => h is Hediff_Injury);
        int tended = p.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.IsTended);
        return "cons=" + F(c.GetLevel(PawnCapacityDefOf.Consciousness)) + " breath=" + F(c.GetLevel(PawnCapacityDefOf.Breathing))
            + " pump=" + F(c.GetLevel(PawnCapacityDefOf.BloodPumping)) + " filt=" + F(c.GetLevel(PawnCapacityDefOf.BloodFiltration))
            + " pain=" + F(p.health.hediffSet.PainTotal) + " bloodLoss=" + F(bl) + " bleed=" + F(p.health.hediffSet.BleedRateTotal)
            + " injuries=" + injuries + " tended=" + tended + " infections=" + p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection);
    }

    private static void DayLine(int now)
    {
        List<Pawn> cs = Citizens().ToList();
        Console.WriteLine("day " + (now / GenDate.TicksPerDay) + " | citizens " + cs.Count + " | downed " + cs.Count(p => p.Downed)
            + " | hostiles standing " + HostilesStanding() + " | beds " + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count);
    }

    private static void Observe(int now)
    {
        List<Pawn> cs = Citizens().ToList();
        int hostiles = HostilesStanding();

        // Raids: membership, peak, and when the map was clear of standing hostiles again.
        if (raids.Count > 0)
        {
            Raid r = raids[raids.Count - 1];
            if (r.Clear < 0)
            {
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    if (IsHostileStanding(p) && now - r.Fired < 5000) r.Members.Add(p);
                if (hostiles > r.PeakHostiles) r.PeakHostiles = hostiles;
                if (hostiles == 0 && r.PeakHostiles > 0)
                {
                    r.Clear = now;
                    r.CitizensAbleAtClear = cs.Count(Able);
                    r.CitizensDownedAtClear = cs.Count(p => p.Downed);
                    r.HostilesDead = r.Members.Count(p => p.Dead);
                    r.HostilesDown = r.Members.Count(p => !p.Dead && p.Downed);
                    r.HostilesLeftMap = r.Members.Count(p => !p.Dead && !p.Spawned);
                    Console.WriteLine("  [" + Stamp(now) + "] RAID CLEAR fired " + Stamp(r.Fired) + ": +" + (now - r.Fired) + " ticks, peak " + r.PeakHostiles
                        + " hostiles; raiders dead " + r.HostilesDead + ", down " + r.HostilesDown + ", left map " + r.HostilesLeftMap
                        + "; citizens able " + r.CitizensAbleAtClear + ", downed " + r.CitizensDownedAtClear);
                }
            }
        }

        // Jobs: keep each able citizen's previous job so a tender's interrupted job can be named.
        foreach (Pawn q in cs)
        {
            string name = q.jobs?.curJob?.def.defName ?? "(none)";
            if (curJobName.TryGetValue(q, out string? was) && was != name) prevJobName[q] = was;
            curJobName[q] = name;
        }

        // Close episodes.
        foreach (Pawn p in open.Keys.ToList())
        {
            Episode e = open[p];
            if (p.Dead)
            {
                e.End = now;
                e.Outcome = "died";
                e.Culprit = (p.health.DeathCauseHediff?.defName ?? p.health.CauseOfDeath.ToString());
                e.DeathLook = lastLook.TryGetValue(p, out string? l) ? l : "";
                closed.Add(e); open.Remove(p);
            }
            else if (!p.Downed)
            {
                e.End = now; e.Outcome = "recovered";
                closed.Add(e); open.Remove(p);
            }
        }
        // Open new ones.
        foreach (Pawn p in cs)
        {
            if (!p.Downed || open.ContainsKey(p)) continue;
            open[p] = new Episode
            {
                P = p, Downed = now, BleedAtDown = p.health.hediffSet.BleedRateTotal,
                BloodLossAtDown = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f,
                TtdAtDown = Ttd(p), HostilesAtDown = hostiles, AbleAtDown = cs.Count(Able),
            };
        }
        if (open.Count == 0) return;

        int able = cs.Count(Able);
        foreach (Episode e in open.Values)
        {
            Pawn p = e.P;
            lastLook[p] = Look(p);
            if (able < e.MinAble) e.MinAble = able;
            if (e.ThreatClear < 0 && hostiles == 0) e.ThreatClear = now;
            if (e.FirstTended < 0 && p.health.hediffSet.hediffs.Any(h => h.IsTended)) e.FirstTended = now;
            if (e.FirstBleedFree < 0 && p.health.hediffSet.BleedRateTotal <= 0f) e.FirstBleedFree = now;
        }

        // Tend jobs aimed at the downed.
        foreach (Pawn q in cs)
        {
            Job? job = q.jobs?.curJob;
            if (job == null || job.def != JobDefOf.TendPatient) continue;
            if (!(job.targetA.Thing is Pawn target) || !open.TryGetValue(target, out Episode? e)) continue;
            if (e.FirstTendJob < 0)
            {
                e.FirstTendJob = now; e.Tender = q.Label + "#" + q.thingIDNumber; e.TenderMedicine = Med(q);
                e.TenderPrevJob = prevJobName.TryGetValue(q, out string? pj) ? pj : "?";
            }
            if (!ReferenceEquals(e.lastTendJob, job)) { e.TendJobs++; e.lastTendJob = job; }
        }

        if (now % 300 != 0) return;
        bool anyWaiting = false;
        foreach (Episode e in open.Values)
        {
            if (!BleedingUntended(e.P)) continue;
            anyWaiting = true;
            if (hostiles > 0) e.WaitSamplesHostile++; else e.WaitSamplesClear++;
        }
        if (!anyWaiting) return;
        Dictionary<string, int> into = hostiles > 0 ? doingHostile : doingClear;
        if (hostiles > 0) samplesHostile++; else samplesClear++;
        foreach (Pawn q in cs)
        {
            if (!Able(q)) continue;
            string doing = q.jobs?.curJob?.def.defName ?? "(none)";
            if (q.Asleep) doing += "(asleep)";
            if (q.InMentalState) doing += "(mental)";
            into[doing] = into.TryGetValue(doing, out int n) ? n + 1 : 1;
            if (hostiles == 0 && q.Asleep && q.needs.rest != null) { restSumAsleepClear += q.needs.rest.CurLevel; restNAsleepClear++; }
        }
    }

    private static void Report()
    {
        foreach (Episode e in open.Values) { e.Outcome = "still down"; closed.Add(e); }
        Console.WriteLine();
        Console.WriteLine("RAIDS");
        foreach (Raid r in raids)
            Console.WriteLine("  fired " + Stamp(r.Fired) + " points " + F(r.Points) + " peak hostiles " + r.PeakHostiles + " clear " + (r.Clear < 0 ? "never" : "+" + (r.Clear - r.Fired))
                + " raiders dead/down/left " + r.HostilesDead + "/" + r.HostilesDown + "/" + r.HostilesLeftMap + " citizens able/downed at clear " + r.CitizensAbleAtClear + "/" + r.CitizensDownedAtClear);

        Console.WriteLine();
        Console.WriteLine("EPISODES (one row per downing of a citizen)");
        Console.WriteLine("pawn | downed | bleed@down | bl@down | ttd@down | hostiles@down | able@down | minAble | threatClear | firstTendJob | tender(med,prev) | firstTended | waitH/waitC(x300) | outcome | +ticks | culprit | last look");
        foreach (Episode e in closed.OrderBy(e => e.Downed))
        {
            Console.WriteLine(e.P.Label + "#" + e.P.thingIDNumber + " | " + Stamp(e.Downed) + " | " + F(e.BleedAtDown) + " | " + F(e.BloodLossAtDown)
                + " | " + (e.TtdAtDown == int.MaxValue ? "-" : e.TtdAtDown.ToString(CultureInfo.InvariantCulture))
                + " | " + e.HostilesAtDown + " | " + e.AbleAtDown + " | " + e.MinAble
                + " | " + (e.ThreatClear < 0 ? "-" : "+" + (e.ThreatClear - e.Downed))
                + " | " + (e.FirstTendJob < 0 ? "-" : "+" + (e.FirstTendJob - e.Downed))
                + " | " + (e.FirstTendJob < 0 ? "-" : e.Tender + "(" + e.TenderMedicine + "," + e.TenderPrevJob + ")")
                + " | " + (e.FirstTended < 0 ? "-" : "+" + (e.FirstTended - e.Downed))
                + " | " + e.WaitSamplesHostile + "/" + e.WaitSamplesClear
                + " | " + e.Outcome + " | " + (e.End < 0 ? "-" : (e.End - e.Downed).ToString(CultureInfo.InvariantCulture))
                + " | " + (e.Outcome == "died" ? e.Culprit : "-") + " | " + (e.Outcome == "died" ? e.DeathLook : ""));
        }

        Console.WriteLine();
        List<int> firstTend = closed.Where(e => e.FirstTendJob >= 0).Select(e => e.FirstTendJob - e.Downed).OrderBy(x => x).ToList();
        Console.WriteLine("TIME TO FIRST TEND JOB (ticks), n=" + firstTend.Count + " of " + closed.Count + " downings: " + Quantiles(firstTend));
        Console.WriteLine("  sorted: " + string.Join(" ", firstTend));
        List<int> bleedingEps = closed.Where(e => e.BleedAtDown > 0f).Select(e => e.FirstTendJob < 0 ? -1 : e.FirstTendJob - e.Downed).ToList();
        Console.WriteLine("  bleeding at downing: " + bleedingEps.Count + ", never tended: " + bleedingEps.Count(x => x < 0));
        var afterClear = closed.Where(e => e.FirstTendJob >= 0 && e.ThreatClear >= 0).Select(e => Math.Max(0, e.FirstTendJob - Math.Max(e.ThreatClear, e.Downed))).OrderBy(x => x).ToList();
        Console.WriteLine("TIME FROM MAP CLEAR (or downing, if later) TO FIRST TEND JOB, n=" + afterClear.Count + ": " + Quantiles(afterClear));
        Console.WriteLine("  sorted: " + string.Join(" ", afterClear));
        Console.WriteLine("DEATHS among downed: " + string.Join(", ", closed.Where(e => e.Outcome == "died").GroupBy(e => e.Culprit).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine("  of which never had a tend job: " + closed.Count(e => e.Outcome == "died" && e.FirstTendJob < 0)
            + "; never tended: " + closed.Count(e => e.Outcome == "died" && e.FirstTended < 0));

        Console.WriteLine();
        Console.WriteLine("WHAT ABLE CITIZENS DID while a citizen lay bleeding and untended (one count per able citizen per 300-tick sample)");
        Console.WriteLine("  hostile standing on map (" + samplesHostile + " samples): " + string.Join(", ", doingHostile.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine("  map clear (" + samplesClear + " samples): " + string.Join(", ", doingClear.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine("  mean rest level of the asleep, map clear: " + (restNAsleepClear == 0 ? "-" : F(restSumAsleepClear / restNAsleepClear)));
    }

    private static string Quantiles(List<int> xs)
    {
        if (xs.Count == 0) return "-";
        int Q(double q) => xs[Math.Min(xs.Count - 1, (int)Math.Floor(q * (xs.Count - 1) + 0.5))];
        return "min " + xs[0] + " p25 " + Q(0.25) + " median " + Q(0.5) + " p75 " + Q(0.75) + " p90 " + Q(0.9) + " max " + xs[xs.Count - 1];
    }

    private static string F(float f) => f.ToString("F2", CultureInfo.InvariantCulture);
    private static string Stamp(int tick) => (tick / GenDate.TicksPerDay) + "." + (tick % GenDate.TicksPerDay).ToString("D5", CultureInfo.InvariantCulture);
}
