// Lane-private (lane/downed). A paired before/after for the recovery of one raid's casualties.
//
//   save <seed> <saveTick> <out.xml> <dataDir>
//       Replays `--suite storyteller --solo false` for <seed> (the recipe lane/nonsolo matched to the bench) up to
//       <saveTick>, records every citizen downed after the first RaidEnemy fired, and saves the whole game.
//   load <seed> <in.xml> <dataDir> <endDay> <contSeed>
//       Loads that save under <dataDir>'s content, seeds Rand with <contSeed>, runs to <endDay>, and reports what
//       became of the recorded casualties. Built twice: against the branch base (old infection code) and against
//       lane/downed (new), each given its own content, so the two arms share every wound and differ only in the
//       infection model. Observation only reads; it draws no randomness.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;

internal static class Program
{
    private static readonly Dictionary<Pawn, int> firstInfected = new Dictionary<Pawn, int>();

    private static int Main(string[] args)
    {
        string mode = args[0];
        int seed = int.Parse(args[1], CultureInfo.InvariantCulture);
        return mode == "save" ? Save(seed, int.Parse(args[2], CultureInfo.InvariantCulture), args[3], args[4])
             : mode == "load" ? Load(seed, args[2], args[3], int.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture))
             : throw new ArgumentException(mode);
    }

    private static DefDatabase LoadContent(string data)
    {
        var database = new DefDatabase();
        DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true }, data);
        if (!result.Success) throw new InvalidOperationException("content: " + string.Join("; ", result.Errors));
        DefDatabase.Global = database;
        return database;
    }

    private static string F(float f) => f.ToString("F2", CultureInfo.InvariantCulture);

    private static bool IsCitizen(Pawn p, Faction player) => ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike;

    // ---- save ----

    private static int Save(int seed, int saveTick, string outXml, string data)
    {
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
        Faction player = Find.FactionManager.OfPlayer!;
        SimWorld.Map.Map map = home.InteriorMap!;

        int raidTick = -1;
        game.Storyteller.IncidentFired += fi =>
        {
            Console.WriteLine("  [tick " + Find.TickManager.TicksGame + "] FIRED " + fi.def.defName + " points=" + F(fi.parms.points));
            if (raidTick < 0 && fi.def.defName == "RaidEnemy") raidTick = Find.TickManager.TicksGame;
        };
        Find.LetterStack.LetterReceived += l =>
        {
            if (l.label.StartsWith("Death", StringComparison.Ordinal)) Console.WriteLine("  [tick " + Find.TickManager.TicksGame + "] " + l.text);
        };

        var casualties = new HashSet<Pawn>();
        while (Find.TickManager.TicksGame < saveTick)
        {
            game.TickManager.DoSingleTick();
            if (raidTick < 0 || Find.TickManager.TicksGame % 30 != 0) continue;
            foreach (Pawn p in map.mapPawns.AllPawns)
            {
                if (IsCitizen(p, player) && !p.Dead && p.Downed) casualties.Add(p);
            }
        }
        if (raidTick < 0) throw new InvalidOperationException("no raid before the save tick");

        int infections = map.mapPawns.AllPawns.Where(p => IsCitizen(p, player) && !p.Dead)
            .Sum(p => p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection));
        Console.WriteLine("saved at tick " + saveTick + ", raid fired at " + raidTick + "; citizens alive "
            + map.mapPawns.AllPawns.Count(p => IsCitizen(p, player) && !p.Dead)
            + ", downed by the raid and alive " + casualties.Count(p => !p.Dead)
            + ", downed now " + casualties.Count(p => !p.Dead && p.Downed)
            + ", wound infections present " + infections
            + ", hostiles standing " + map.mapPawns.AllPawns.Count(p => p.faction != null && p.faction.HostileTo(player) && !p.Dead && !p.Downed));

        Scribe.SaveToFile(game, "game", outXml);
        File.WriteAllLines(outXml + ".casualties", casualties.Where(p => !p.Dead).OrderBy(p => p.thingIDNumber)
            .Select(p => p.thingIDNumber.ToString(CultureInfo.InvariantCulture) + "\t" + p.Label));
        return 0;
    }

    // ---- load ----

    private static int Load(int seed, string inXml, string data, int endDay, int contSeed)
    {
        DefDatabase db = LoadContent(data);
        Find.Reset();
        Pawn.ResetThingIdCounter();
        SimWorld.Map.Map.ResetMapIdCounter();
        Ablation.Clear();

        Game game = Scribe.Load<Game>(File.ReadAllText(inXml), "game", out IReadOnlyList<string> errors, db);
        if (errors.Count > 0) Console.WriteLine("LOAD ERRORS (" + errors.Count + "): " + string.Join(" | ", errors.Take(5)));
        Rand.Current = new RandomStream(contSeed);

        // CivilizationTarget's settlement list is not saved; Game's post-ticker re-syncs it every storyteller
        // interval. Both arms tick identically until it has, so this costs the pairing nothing.
        for (int i = 0; i < 5 * Storyteller.IncidentCycleLengthTicks && game.CivilizationTarget.Seat == null; i++) game.TickManager.DoSingleTick();
        SimWorld.World.Settlement home = game.CivilizationTarget.Seat ?? throw new InvalidOperationException("no seat");
        Faction player = Find.FactionManager.OfPlayer!;
        SimWorld.Map.Map map = home.InteriorMap!;

        var ids = new HashSet<int>(File.ReadAllLines(inXml + ".casualties").Select(l => int.Parse(l.Split('\t')[0], CultureInfo.InvariantCulture)));
        List<Pawn> casualties = map.mapPawns.AllPawns.Where(p => ids.Contains(p.thingIDNumber)).ToList();
        if (casualties.Count != ids.Count) throw new InvalidOperationException("found " + casualties.Count + " of " + ids.Count + " casualties after load");

        game.Storyteller.IncidentFired += fi =>
            Console.WriteLine("  [tick " + Find.TickManager.TicksGame + "] FIRED " + fi.def.defName + " points=" + F(fi.parms.points));
        Find.LetterStack.LetterReceived += l =>
        {
            if (l.label.StartsWith("Death", StringComparison.Ordinal)) Console.WriteLine("  [tick " + Find.TickManager.TicksGame + "] " + l.text);
        };

        int start = Find.TickManager.TicksGame;
        int end = endDay * GenDate.TicksPerDay;
        int maxInfections = 0;
        while (Find.TickManager.TicksGame < end)
        {
            game.TickManager.DoSingleTick();
            int now = Find.TickManager.TicksGame;
            if (now % 30 != 0) continue;
            foreach (Pawn p in casualties)
            {
                if (p.Dead) continue;
                int n = p.health.hediffSet.hediffs.Count(h => h.def == HediffDefOf.WoundInfection);
                if (n > 0 && !firstInfected.ContainsKey(p)) firstInfected[p] = now;
                if (n > maxInfections) maxInfections = n;
            }
        }

        Console.WriteLine("loaded at tick " + start + ", ran to " + Find.TickManager.TicksGame + " with continuation seed " + contSeed);
        foreach (Pawn p in casualties.OrderBy(p => p.thingIDNumber))
        {
            Console.WriteLine("  " + p.Label + "#" + p.thingIDNumber + ": " + (p.Dead ? "DEAD " + p.health.CauseOfDeath + " (" + (p.health.DeathCauseHediff?.defName ?? "-") + ")" : "alive")
                + (firstInfected.ContainsKey(p) ? ", infected" : ""));
        }
        int alive = casualties.Count(p => !p.Dead);
        Console.WriteLine("PAIR casualties=" + casualties.Count + " survived=" + alive
            + " diedOfInfection=" + casualties.Count(p => p.Dead && p.health.DeathCauseHediff == HediffDefOf.WoundInfection)
            + " diedOfBloodLoss=" + casualties.Count(p => p.Dead && p.health.DeathCauseHediff == HediffDefOf.BloodLoss)
            + " diedOther=" + casualties.Count(p => p.Dead && p.health.DeathCauseHediff != HediffDefOf.WoundInfection && p.health.DeathCauseHediff != HediffDefOf.BloodLoss)
            + " everInfected=" + firstInfected.Count
            + " citizensAliveAtEnd=" + map.mapPawns.AllPawns.Count(p => IsCitizen(p, player) && !p.Dead));
        return 0;
    }
}
