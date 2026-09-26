// Lane-private diagnostic (lane/retreat): paired continuations of one raid, with and without its Lord.
//
//   save <seed> <dataDir> <raidN> <savePath>
//       Replays the `--suite storyteller --solo false` world (docs/perf/tend/diag.cs recipe) until the Nth raid
//       lands on the watched map, and saves the game on the tick it lands, before its lord has ticked once.
//   cont <savePath> <dataDir> <contSeed> <ticks> <arm>
//       Loads that save, reseeds Rand.Current with contSeed, and runs <ticks>. arm = lord: as shipped.
//       arm = nolord: the raid's lord is dropped from its map on load, so its members keep the assault duty
//       they were saved with and nothing ever changes it — the behaviour before this lane, in the same binary
//       and from the same save, so the two arms differ in that one respect only.
//       Prints one row: what the raid did and what it cost.
//
// Observation only reads: no Rand draws, no reservations, no jobs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SimWorld.AI.Group;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args[0] == "save") return Save(args);
        if (args[0] == "cont") return Cont(args);
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

    private static int Save(string[] args)
    {
        int seed = int.Parse(args[1], CultureInfo.InvariantCulture);
        string data = args[2];
        int raidN = int.Parse(args[3], CultureInfo.InvariantCulture);
        string path = args[4];
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
        SimWorld.Map.Map map = home.InteriorMap!;
        var worker = (IncidentWorker_RaidEnemy)DefDatabase<IncidentDef>.GetNamed("RaidEnemy").Worker;
        IReadOnlyList<Pawn>? seen = null;
        int landed = 0;
        bool saveNow = false;
        game.Storyteller.IncidentFired += fi =>
        {
            if (fi.def.defName != "RaidEnemy") return;
            IReadOnlyList<Pawn>? squad = worker.LastRaidPawns;
            if (squad == null || ReferenceEquals(squad, seen) || worker.LastRaidOutcome != null) return;
            seen = squad;
            if (!squad.Any(p => p.Spawned && ReferenceEquals(p.Map, map))) return;
            landed++;
            if (landed == raidN) saveNow = true;
        };
        int limit = 120 * GenDate.TicksPerDay;
        while (Find.TickManager.TicksGame < limit)
        {
            game.TickManager.DoSingleTick();
            if (!saveNow) continue;
            Scribe.SaveToFile(game, "game", path);
            Console.WriteLine("saved raid " + raidN + " at tick " + Find.TickManager.TicksGame + " (" + Stamp(Find.TickManager.TicksGame) + "): "
                + seen!.Count + " raiders from " + worker.LastRaidFaction?.name + " (" + worker.LastRaidFaction?.def.defName + ", autoFlee " + worker.LastRaidFaction?.def.autoFlee + ")");
            return 0;
        }
        Console.WriteLine("raid " + raidN + " never landed");
        return 1;
    }

    private static int Cont(string[] args)
    {
        string path = args[1];
        DefDatabase db = LoadContent(args[2]);
        int contSeed = int.Parse(args[3], CultureInfo.InvariantCulture);
        int ticks = int.Parse(args[4], CultureInfo.InvariantCulture);
        string arm = args[5];
        Find.Reset();
        Pawn.ResetThingIdCounter();
        SimWorld.Map.Map.ResetMapIdCounter();
        Ablation.Clear();
        Game game = Scribe.Load<Game>(File.ReadAllText(path), "game", out IReadOnlyList<string> errors, db);
        if (errors.Count > 0) Console.WriteLine("LOAD ERRORS (" + errors.Count + "): " + string.Join(" | ", errors.Take(5)));
        Rand.Current = new RandomStream(contSeed);
        int start = Find.TickManager.TicksGame;

        SimWorld.Map.Map map = game.Maps.Single(m => m.lordManager.lords.Count > 0);
        Lord lord = map.lordManager.lords.Single();
        List<Pawn> members = lord.ownedPawns.ToList();
        Faction player = Find.FactionManager.OfPlayer!;
        if (arm == "nolord") map.lordManager.lords.Clear();
        else if (arm != "lord") throw new ArgumentException(arm);

        var citizenDown = new HashSet<Pawn>();
        foreach (Pawn c in Citizens(map, player)) if (c.Downed) citizenDown.Add(c);
        int downingsHeld = 0, downingsAll = 0, clear = -1, letter = -1, gotUp = 0, lastStanding = -1, restandSamples = 0, downingsAfterClear = 0;
        string letterKind = "";
        var everDown = new HashSet<Pawn>();
        var gotUpSet = new HashSet<Pawn>();
        int attributed0 = Find.Storyteller.deaths.AttributedTo("RaidEnemy");
        int deaths0 = Find.Storyteller.deaths.Total;
        int deathsAtClear = -1, attributedAtClear = -1;
        Find.LetterStack.LetterReceived += l =>
        {
            if (letter < 0 && (l.label == "Raiders fleeing" || l.label == "Raiders leaving"))
            {
                letter = Find.TickManager.TicksGame - start;
                letterKind = l.label == "Raiders fleeing" ? "fled" : "gave up";
            }
        };

        while (Find.TickManager.TicksGame < start + ticks)
        {
            game.TickManager.DoSingleTick();
            int now = Find.TickManager.TicksGame;
            if (now % 30 != 0) continue;
            foreach (Pawn c in Citizens(map, player))
            {
                if (c.Downed && citizenDown.Add(c)) { downingsAll++; if (clear < 0) downingsHeld++; else downingsAfterClear++; }
                else if (!c.Downed) citizenDown.Remove(c);
            }
            foreach (Pawn p in members)
            {
                if (p.Spawned && !p.Dead && p.Downed) everDown.Add(p);
                else if (Standing(p, map) && everDown.Contains(p) && gotUpSet.Add(p)) gotUp++;
            }
            bool anyStanding = members.Any(p => Standing(p, map));
            if (anyStanding) lastStanding = now - start;
            if (anyStanding && clear >= 0) restandSamples++;
            if (clear < 0 && !anyStanding)
            {
                clear = now - start;
                deathsAtClear = Find.Storyteller.deaths.Total - deaths0;
                attributedAtClear = Find.Storyteller.deaths.AttributedTo("RaidEnemy") - attributed0;
            }
        }

        int dead = members.Count(p => p.Dead);
        int down = members.Count(p => !p.Dead && p.Spawned && p.Downed);
        int left = members.Count(p => !p.Dead && !p.Spawned);
        int standing = members.Count(p => Standing(p, map));
        Console.WriteLine(string.Join(" | ", new[]
        {
            arm, "seed " + contSeed, "squad " + members.Count,
            "letter " + (letter < 0 ? "-" : "+" + letter + " " + letterKind),
            "hold " + (clear < 0 ? ">" + ticks : clear.ToString(CultureInfo.InvariantCulture)),
            "end dead/down/left/standing " + dead + "/" + down + "/" + left + "/" + standing,
            "got up " + gotUp,
            "last raider standing +" + lastStanding + ", standing again after clear " + (restandSamples * 30) + " ticks",
            "citizen downings after clear " + downingsAfterClear,
            "citizen downings held/all " + downingsHeld + "/" + downingsAll,
            "citizen deaths at clear " + (deathsAtClear < 0 ? "-" : deathsAtClear.ToString(CultureInfo.InvariantCulture)) + ", all " + (Find.Storyteller.deaths.Total - deaths0),
            "killed by raid at clear " + (attributedAtClear < 0 ? "-" : attributedAtClear.ToString(CultureInfo.InvariantCulture)) + ", all " + (Find.Storyteller.deaths.AttributedTo("RaidEnemy") - attributed0),
            "moments " + Find.Storyteller.Moments.Count(m => m.tick >= start) + " new: " + string.Join("; ", Find.Storyteller.Moments.Where(m => m.tick >= start).Select(m => m.incidentDefName)),
        }));
        return 0;
    }

    private static bool Standing(Pawn p, SimWorld.Map.Map map) => p.Spawned && !p.Dead && !p.Downed && ReferenceEquals(p.Map, map);

    private static IEnumerable<Pawn> Citizens(SimWorld.Map.Map map, Faction player)
    {
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
        for (int i = 0; i < all.Count; i++)
        {
            Pawn p = all[i];
            if (ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike && !p.Dead) yield return p;
        }
    }

    private static string Stamp(int tick) => (tick / GenDate.TicksPerDay) + "." + (tick % GenDate.TicksPerDay).ToString("D5", CultureInfo.InvariantCulture);
}
