// Lane-private diagnostic (lane/retreat). Replays the world `--suite storyteller --solo false` builds for a seed
// (the same recipe docs/perf/tend/diag.cs uses: Find.Reset, a fresh TickManager, RandomStream(seed), reset id
// counters, Ablation.Clear, Game.NewGame(TribalStart, seed, subdivision 3, soloStart false, band 25), then open the
// seat) and follows every raid that lands on the watched map: how long it holds the map, what became of the
// raiders, and what it cost the settlement.
//
// Observation only reads: no Rand draws, no reservations, no jobs.
//
//   run <seed> <days> <dataDir>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.AI;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;

internal sealed class Raid
{
    public int Index;
    public int Fired;
    public float Points;
    public string Faction = "";
    public bool AutoFlee;
    public List<Pawn> Members = new List<Pawn>();
    public int Clear = -1;              // first 30-tick observation with no member standing on the map
    public int FirstLetter = -1;        // "Raiders fleeing" / "Raiders leaving" (after arm only)
    public string LetterKind = "";
    public int DownedRaidersAtLetter = -1;
    public int StandingAtLetter = -1;
    public HashSet<Pawn> EverDowned = new HashSet<Pawn>();
    public HashSet<Pawn> GotUp = new HashSet<Pawn>();
    public int CitizenDownings;          // citizen downing episodes that began in [Fired, next raid)
    public int CitizenDowningsWhileHeld; // ... and in [Fired, Clear)
    public int KilledAttributed = -1;    // DeathLedger.AttributedTo("RaidEnemy") over [Fired, next raid or end)
    public int CitizenDeathsAll;         // every citizen death letter over the same window
    public int PeakStanding;
    public int DeadAtClear, DownAtClear, LeftAtClear;
    public int DeadAtEnd, DownAtEnd, LeftAtEnd, StandingAtEnd;
}

internal static class Program
{
    private static Faction player = null!;
    private static SimWorld.Map.Map map = null!;
    private static readonly List<Raid> raids = new List<Raid>();
    private static readonly HashSet<Pawn> citizenDown = new HashSet<Pawn>();
    private static IncidentWorker_RaidEnemy raidWorker = null!;
    private static IReadOnlyList<Pawn>? lastSeenSquad;
    private static int attributedAtFire;
    private static readonly List<string> events = new List<string>();

    private static int Main(string[] args)
    {
        int seed = int.Parse(args[1], CultureInfo.InvariantCulture);
        int days = int.Parse(args[2], CultureInfo.InvariantCulture);
        string data = args[3];

        var database = new DefDatabase();
        DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true }, data);
        if (!result.Success) throw new InvalidOperationException("content: " + string.Join("; ", result.Errors));
        DefDatabase.Global = database;

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
        raidWorker = (IncidentWorker_RaidEnemy)DefDatabase<IncidentDef>.GetNamed("RaidEnemy").Worker;

        Find.LetterStack.LetterReceived += l =>
        {
            int now = Find.TickManager.TicksGame;
            if (l.label.StartsWith("Death", StringComparison.Ordinal))
            {
                Raid? r = raids.LastOrDefault();
                if (r != null) r.CitizenDeathsAll++;
                events.Add("[" + Stamp(now) + "] DEATH " + l.text);
            }
            if (l.label == "Raiders fleeing" || l.label == "Raiders leaving")
            {
                events.Add("[" + Stamp(now) + "] LETTER " + l.label + ": " + l.text);
                Raid? r = raids.LastOrDefault(x => x.Clear < 0) ?? raids.LastOrDefault();
                if (r != null && r.FirstLetter < 0)
                {
                    r.FirstLetter = now;
                    r.LetterKind = l.label;
                    r.DownedRaidersAtLetter = r.Members.Count(p => !p.Dead && p.Downed) + r.Members.Count(p => p.Dead);
                    r.StandingAtLetter = r.Members.Count(Standing);
                }
            }
        };
        game.Storyteller.IncidentFired += fi =>
        {
            int now = Find.TickManager.TicksGame;
            if (fi.def.defName != "RaidEnemy") { events.Add("[" + Stamp(now) + "] fired " + fi.def.defName); return; }
            IReadOnlyList<Pawn>? squad = raidWorker.LastRaidPawns;
            if (squad == null || ReferenceEquals(squad, lastSeenSquad) || raidWorker.LastRaidOutcome != null) { events.Add("[" + Stamp(now) + "] RaidEnemy did not land on the map"); return; }
            lastSeenSquad = squad;
            CloseWindow();
            var r = new Raid
            {
                Index = raids.Count + 1, Fired = now, Points = fi.parms.points,
                Faction = raidWorker.LastRaidFaction?.name + " (" + raidWorker.LastRaidFaction?.def.defName + ")",
                AutoFlee = raidWorker.LastRaidFaction?.def.autoFlee ?? false,
            };
            r.Members.AddRange(squad.Where(p => p.Spawned && ReferenceEquals(p.Map, map)));
            raids.Add(r);
            attributedAtFire = Find.Storyteller.deaths.AttributedTo("RaidEnemy");
            events.Add("[" + Stamp(now) + "] RAID " + r.Index + " fired: " + r.Members.Count + " raiders from " + r.Faction + ", points " + F(r.Points) + ", autoFlee " + r.AutoFlee);
        };

        int end = days * GenDate.TicksPerDay;
        while (Find.TickManager.TicksGame < end)
        {
            game.TickManager.DoSingleTick();
            int now = Find.TickManager.TicksGame;
            if (now % 30 == 0) Observe(now);
        }
        CloseWindow();
        Report(end);
        return 0;
    }

    private static bool Standing(Pawn p) => p.Spawned && !p.Dead && !p.Downed && ReferenceEquals(p.Map, map);

    private static void CloseWindow()
    {
        Raid? prev = raids.LastOrDefault();
        if (prev != null && prev.KilledAttributed < 0)
            prev.KilledAttributed = Find.Storyteller.deaths.AttributedTo("RaidEnemy") - attributedAtFire;
    }

    private static IEnumerable<Pawn> Citizens()
    {
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
        for (int i = 0; i < all.Count; i++)
        {
            Pawn p = all[i];
            if (ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike && !p.Dead) yield return p;
        }
    }

    private static void Observe(int now)
    {
        Raid? cur = raids.LastOrDefault();
        foreach (Pawn c in Citizens().ToList())
        {
            if (c.Downed && citizenDown.Add(c) && cur != null)
            {
                cur.CitizenDownings++;
                if (cur.Clear < 0) cur.CitizenDowningsWhileHeld++;
            }
            else if (!c.Downed) citizenDown.Remove(c);
        }
        foreach (Raid r in raids)
        {
            foreach (Pawn p in r.Members)
            {
                if (p.Spawned && !p.Dead && p.Downed) r.EverDowned.Add(p);
                else if (Standing(p) && r.EverDowned.Contains(p) && r.GotUp.Add(p))
                    events.Add("[" + Stamp(now) + "] raid " + r.Index + ": " + p.Label + " got back up");
            }
        }
        if (cur == null || cur.Clear >= 0) return;
        int standing = cur.Members.Count(Standing);
        if (standing > cur.PeakStanding) cur.PeakStanding = standing;
        if (standing == 0)
        {
            cur.Clear = now;
            cur.DeadAtClear = cur.Members.Count(p => p.Dead);
            cur.DownAtClear = cur.Members.Count(p => !p.Dead && p.Spawned && p.Downed);
            cur.LeftAtClear = cur.Members.Count(p => !p.Dead && !p.Spawned);
            events.Add("[" + Stamp(now) + "] RAID " + cur.Index + " CLEAR after " + (now - cur.Fired) + " ticks: raiders dead " + cur.DeadAtClear + ", down " + cur.DownAtClear + ", left map " + cur.LeftAtClear);
        }
    }

    private static void Report(int end)
    {
        foreach (Raid r in raids)
        {
            r.DeadAtEnd = r.Members.Count(p => p.Dead);
            r.DownAtEnd = r.Members.Count(p => !p.Dead && p.Spawned && p.Downed);
            r.LeftAtEnd = r.Members.Count(p => !p.Dead && !p.Spawned);
            r.StandingAtEnd = r.Members.Count(Standing);
        }
        Console.WriteLine("EVENTS");
        foreach (string e in events) Console.WriteLine("  " + e);
        Console.WriteLine();
        Console.WriteLine("RAIDS ON THE WATCHED MAP (hold = ticks from arrival until no raider stands on the map)");
        Console.WriteLine("raid | fired | faction | autoFlee | squad | peak standing | letter (+ticks, kind, lost/standing then) | hold | raiders dead/down/left at clear | got up | at end dead/down/left/standing | citizen downings (while held / window) | killed by raid (window) | citizen deaths all (window)");
        foreach (Raid r in raids)
        {
            Console.WriteLine(r.Index + " | " + Stamp(r.Fired) + " | " + r.Faction + " | " + r.AutoFlee + " | " + r.Members.Count + " | " + r.PeakStanding
                + " | " + (r.FirstLetter < 0 ? "-" : "+" + (r.FirstLetter - r.Fired) + " " + r.LetterKind + " " + r.DownedRaidersAtLetter + "/" + r.StandingAtLetter)
                + " | " + (r.Clear < 0 ? "never (>" + (end - r.Fired) + ")" : (r.Clear - r.Fired).ToString(CultureInfo.InvariantCulture))
                + " | " + (r.Clear < 0 ? "-" : r.DeadAtClear + "/" + r.DownAtClear + "/" + r.LeftAtClear)
                + " | " + r.GotUp.Count
                + " | " + r.DeadAtEnd + "/" + r.DownAtEnd + "/" + r.LeftAtEnd + "/" + r.StandingAtEnd
                + " | " + r.CitizenDowningsWhileHeld + " / " + r.CitizenDownings
                + " | " + r.KilledAttributed + " | " + r.CitizenDeathsAll);
        }
        Console.WriteLine();
        Console.WriteLine("TOTALS: raids " + raids.Count + ", citizen downings " + raids.Sum(r => r.CitizenDownings) + ", killed by RaidEnemy " + Find.Storyteller.deaths.AttributedTo("RaidEnemy")
            + ", deaths " + Find.Storyteller.deaths + ", citizens alive " + Citizens().Count());
        Console.WriteLine();
        Console.WriteLine("MOMENTS (MomentCurator)");
        foreach (ChronicleEntry m in Find.Storyteller.Moments)
            Console.WriteLine("  [" + Stamp(m.tick) + "] " + m.incidentDefName + (string.IsNullOrEmpty(m.targetLabel) ? "" : " | " + m.targetLabel));
    }

    private static string F(float f) => f.ToString("F0", CultureInfo.InvariantCulture);
    private static string Stamp(int tick) => (tick / GenDate.TicksPerDay) + "." + (tick % GenDate.TicksPerDay).ToString("D5", CultureInfo.InvariantCulture);
}
