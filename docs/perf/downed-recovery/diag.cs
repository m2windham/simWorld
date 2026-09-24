// Lane-private diagnostic (lane/downed). Replays the world `--suite storyteller --solo false` builds for a seed
// (same recipe as lane/nonsolo's replay, which was matched to the bench) and follows every downing of a
// humanlike citizen: was a rescue job ever issued for them, did they reach a bed, were they tended, what did
// their bleed rate do, and did they live. Observation reads only: no Rand draws, no reservations, no jobs.
// With "probe" as 5th arg it additionally asks the rescue and tend work givers, every 600 ticks, why they do
// or do not have a job on each downed citizen — those calls path-check, so that mode is not guaranteed to
// leave the world identical and its outcome numbers must not be used as the before/after measurement.
using System;
using System.Collections.Generic;
using System.Globalization;
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
    public float MaxBleed;
    public int FirstRescueJob = -1;
    public string Rescuer = "";
    public int InBed = -1;
    public int FirstTendJob = -1;
    public int FirstTended = -1;
    public int TendJobs;
    public int End = -1;
    public string Outcome = "";
    public float BloodLossAtEnd;
    public Dictionary<string, int> OthersDoing = new Dictionary<string, int>();
    public Dictionary<string, int> RescueGate = new Dictionary<string, int>();
    public Dictionary<string, int> TendGate = new Dictionary<string, int>();
    public Job? lastRescueJob;
    public Job? lastTendJob;
}

internal sealed class Infection
{
    public Pawn P = null!;
    public Hediff H = null!;
    public string Part = "";
    public int Onset;
    public int FirstTended = -1;
    public int TicksTended;
    public int TicksObserved;
    public int Tends;
    public float LastQ = -1f;
    public float SumQ;
    public float MaxSev;
    public float ImmAtMax;
    public int End = -1;
    public string Outcome = "";
    public float Igs;
    public int TicksDowned;
    public int TicksInBed;
}

internal static class Program
{
    private static int day;
    private static bool probe;
    private static readonly List<Infection> infections = new List<Infection>();
    private static readonly Dictionary<Hediff, Infection> infByHediff = new Dictionary<Hediff, Infection>();

    private static int Main(string[] args)
    {
        int seed = int.Parse(args[0], CultureInfo.InvariantCulture);
        int days = int.Parse(args[1], CultureInfo.InvariantCulture);
        bool solo = bool.Parse(args[2]);
        string data = args[3];
        probe = args.Length > 4 && args[4] == "probe";

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

        Game game = Game.NewGame(
            ScenarioDefOf.TribalStart.scenario,
            seed.ToString(CultureInfo.InvariantCulture),
            subdivisionOverride: 3,
            soloStart: solo,
            bandSize: 25);
        SimWorld.World.Settlement home = game.CivilizationTarget.Seat ?? throw new InvalidOperationException("no seat");
        GodCommands.OpenSettlement(home.tile);
        Faction player = Find.FactionManager.OfPlayer!;
        SimWorld.Map.Map map = home.InteriorMap!;

        Find.LetterStack.LetterReceived += l =>
        {
            if (l.label.StartsWith("Death", StringComparison.Ordinal))
                Console.WriteLine("  [day " + day + " tick " + Find.TickManager.TicksGame + "] LETTER " + l.label + " :: " + l.text);
        };
        game.Storyteller.IncidentFired += fi =>
            Console.WriteLine("  [day " + day + " tick " + Find.TickManager.TicksGame + "] FIRED " + fi.def.defName
                + " points=" + fi.parms.points.ToString("F1", CultureInfo.InvariantCulture));

        // Standing facts about the settlement's capacity to recover anyone.
        List<Pawn> humans = map.mapPawns.AllPawns.Where(p => ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike).ToList();
        int doctorsActive = humans.Count(p => p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor));
        Console.WriteLine("start: humans=" + humans.Count + " everWork=" + humans.Count(p => p.workSettings != null && p.workSettings.EverWork)
            + " doctorActive=" + doctorsActive + " beds=" + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count);
        Pawn? sample = humans.FirstOrDefault(p => p.workSettings != null && p.workSettings.EverWork);
        if (sample != null)
        {
            Console.WriteLine("  emergency givers: " + string.Join(", ", sample.workSettings.WorkGiversInOrderEmergency.Select(g => g.defName)));
            Console.WriteLine("  normal givers (first 12): " + string.Join(", ", sample.workSettings.WorkGiversInOrderNormal.Take(12).Select(g => g.defName)));
        }

        var open = new Dictionary<Pawn, Episode>();
        var closed = new List<Episode>();

        Console.WriteLine("day | humans alive | humans downed | beds | episodes open/closed");
        for (day = 1; day <= days; day++)
        {
            for (int i = 0; i < GenDate.TicksPerDay; i++)
            {
                game.TickManager.DoSingleTick();
                int now = Find.TickManager.TicksGame;
                if (now % 30 == 0) Observe(map, player, open, closed, now);
            }
            int alive = map.mapPawns.AllPawns.Count(p => ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike && !p.Dead);
            int down = map.mapPawns.AllPawns.Count(p => ReferenceEquals(p.faction, player) && p.RaceProps.Humanlike && !p.Dead && p.Downed);
            Console.WriteLine(day + " | " + alive + " | " + down + " | " + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count
                + " | " + open.Count + "/" + closed.Count);
        }
        foreach (Episode e in open.Values) { e.Outcome = "still down at end"; closed.Add(e); }

        Console.WriteLine();
        Console.WriteLine("episodes (humanlike citizens of the player's faction, one row per downing):");
        Console.WriteLine("pawn | downed day.tick | bleed@down | maxBleed | rescueJob | inBed | tendJobs | firstTendJob | firstTended | outcome | +ticks | bloodLoss");
        foreach (Episode e in closed.OrderBy(e => e.Downed))
        {
            Console.WriteLine(e.P.Label + "#" + e.P.thingIDNumber + " | " + Stamp(e.Downed) + " | " + F(e.BleedAtDown) + " | " + F(e.MaxBleed)
                + " | " + (e.FirstRescueJob < 0 ? "-" : "+" + (e.FirstRescueJob - e.Downed) + " by " + e.Rescuer)
                + " | " + (e.InBed < 0 ? "-" : "+" + (e.InBed - e.Downed))
                + " | " + e.TendJobs
                + " | " + (e.FirstTendJob < 0 ? "-" : "+" + (e.FirstTendJob - e.Downed))
                + " | " + (e.FirstTended < 0 ? "-" : "+" + (e.FirstTended - e.Downed))
                + " | " + e.Outcome + " | " + (e.End < 0 ? "-" : (e.End - e.Downed).ToString(CultureInfo.InvariantCulture))
                + " | " + F(e.BloodLossAtEnd));
            if (e.OthersDoing.Count > 0)
                Console.WriteLine("    others doing (samples): " + string.Join(", ", e.OthersDoing.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + "=" + kv.Value)));
            if (e.RescueGate.Count > 0)
                Console.WriteLine("    rescue gate: " + string.Join(", ", e.RescueGate.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
            if (e.TendGate.Count > 0)
                Console.WriteLine("    tend gate: " + string.Join(", ", e.TendGate.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)));
        }

        Console.WriteLine();
        Console.WriteLine("wound infections on humanlike citizens:");
        Console.WriteLine("pawn | part | onset | firstTended | %tended | tends | meanQ | maxSev | immAtMax | igs | %downed | %inBed | outcome | +ticks");
        foreach (Infection inf in infections)
        {
            if (inf.End < 0) inf.Outcome = "open at end";
            int obs = Math.Max(1, inf.TicksObserved);
            Console.WriteLine(inf.P.Label + "#" + inf.P.thingIDNumber + " | " + inf.Part + " | " + Stamp(inf.Onset)
                + " | " + (inf.FirstTended < 0 ? "-" : "+" + (inf.FirstTended - inf.Onset))
                + " | " + (100 * inf.TicksTended / obs) + "% | " + inf.Tends + " | " + (inf.Tends == 0 ? "-" : F(inf.SumQ / inf.Tends))
                + " | " + F(inf.MaxSev) + " | " + F(inf.ImmAtMax) + " | " + F(inf.Igs)
                + " | " + (100 * inf.TicksDowned / obs) + "% | " + (100 * inf.TicksInBed / obs) + "%"
                + " | " + inf.Outcome + " | " + (inf.End < 0 ? "-" : (inf.End - inf.Onset).ToString(CultureInfo.InvariantCulture)));
        }
        Console.WriteLine("SUMMARY infections=" + infections.Count + " cured=" + infections.Count(i => i.Outcome == "cured")
            + " died=" + infections.Count(i => i.Outcome.StartsWith("died", StringComparison.Ordinal))
            + " open=" + infections.Count(i => i.Outcome == "open at end")
            + " pawnsInfected=" + infections.Select(i => i.P).Distinct().Count());

        // Per-pawn: did every citizen who was ever downed live to the end?
        var everDowned = closed.Select(e => e.P).Distinct().ToList();
        int survived = everDowned.Count(p => !p.Dead);
        Console.WriteLine();
        Console.WriteLine("SUMMARY episodes=" + closed.Count
            + " recovered=" + closed.Count(e => e.Outcome == "recovered")
            + " died=" + closed.Count(e => e.Outcome.StartsWith("died", StringComparison.Ordinal))
            + " stillDown=" + closed.Count(e => e.Outcome == "still down at end")
            + " | rescueJobIssued=" + closed.Count(e => e.FirstRescueJob >= 0)
            + " reachedBed=" + closed.Count(e => e.InBed >= 0)
            + " tendJobIssued=" + closed.Count(e => e.FirstTendJob >= 0)
            + " tended=" + closed.Count(e => e.FirstTended >= 0));
        Console.WriteLine("SUMMARY citizensEverDowned=" + everDowned.Count + " aliveAtEnd=" + survived
            + " deadAtEnd=" + (everDowned.Count - survived));
        Console.WriteLine("SUMMARY deaths by cause among ever-downed: "
            + string.Join(", ", everDowned.Where(p => p.Dead).GroupBy(p => p.health.CauseOfDeath.ToString()).Select(g => g.Key + "=" + g.Count())));
        return 0;
    }

    private static string F(float f) => f.ToString("F2", CultureInfo.InvariantCulture);
    private static string Stamp(int tick) => (tick / GenDate.TicksPerDay) + "." + (tick % GenDate.TicksPerDay).ToString("D5", CultureInfo.InvariantCulture);

    private static bool OnBed(Pawn p)
    {
        if (!p.Spawned || p.Map == null) return false;
        foreach (Thing b in p.Map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed))
            if (b.Spawned && b.Position == p.Position) return true;
        return false;
    }

    internal static readonly Dictionary<Pawn, string> lastLook = new Dictionary<Pawn, string>();
    internal static readonly HashSet<Pawn> autopsied = new HashSet<Pawn>();

    private static string Look(Pawn p)
    {
        var c = p.health.capacities;
        string caps = "cons=" + F(c.GetLevel(PawnCapacityDefOf.Consciousness)) + " breath=" + F(c.GetLevel(PawnCapacityDefOf.Breathing))
            + " pump=" + F(c.GetLevel(PawnCapacityDefOf.BloodPumping)) + " filt=" + F(c.GetLevel(PawnCapacityDefOf.BloodFiltration))
            + " metab=" + F(c.GetLevel(PawnCapacityDefOf.Metabolism)) + " pain=" + F(p.health.hediffSet.PainTotal);
        string inf = string.Join(",", p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.WoundInfection).Select(h => F(h.Severity)));
        float bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
        return caps + " bloodLoss=" + F(bl) + " infections=[" + inf + "] imm=" + F(p.health.immunity.GetImmunity(HediffDefOf.WoundInfection));
    }

    private static void ObserveInfections(SimWorld.Map.Map map, Faction player, int now)
    {
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
        var seen = new HashSet<Hediff>();
        foreach (KeyValuePair<Pawn, string> kv in lastLook)
        {
            if (!kv.Key.Dead || autopsied.Contains(kv.Key)) continue;
            autopsied.Add(kv.Key);
            Console.WriteLine("  AUTOPSY " + kv.Key.Label + "#" + kv.Key.thingIDNumber + " cause=" + kv.Key.health.CauseOfDeath + " last seen: " + kv.Value);
        }
        for (int i = 0; i < all.Count; i++)
        {
            Pawn p = all[i];
            if (!ReferenceEquals(p.faction, player) || !p.RaceProps.Humanlike || p.Dead) continue;
            if (p.Downed || p.health.hediffSet.hediffs.Any(h => h.def == HediffDefOf.WoundInfection || h.def == HediffDefOf.BloodLoss)) lastLook[p] = Look(p);
            foreach (Hediff h in p.health.hediffSet.hediffs)
            {
                if (h.def != HediffDefOf.WoundInfection) continue;
                seen.Add(h);
                if (!infByHediff.TryGetValue(h, out Infection? inf))
                {
                    inf = new Infection { P = p, H = h, Part = h.Part?.Label ?? "-", Onset = now, Igs = p.ImmunityGainSpeed };
                    infByHediff[h] = inf;
                    infections.Add(inf);
                }
                inf.TicksObserved += 30;
                if (p.Downed) inf.TicksDowned += 30;
                if (OnBed(p)) inf.TicksInBed += 30;
                if (h.IsTended)
                {
                    inf.TicksTended += 30;
                    if (inf.FirstTended < 0) inf.FirstTended = now;
                    if (h.TendQuality != inf.LastQ) { inf.Tends++; inf.SumQ += h.TendQuality; inf.LastQ = h.TendQuality; }
                }
                if (h.Severity > inf.MaxSev) { inf.MaxSev = h.Severity; inf.ImmAtMax = p.health.immunity.GetImmunity(h.def); }
            }
        }
        foreach (Infection inf in infections)
        {
            if (inf.End >= 0 || seen.Contains(inf.H)) continue;
            inf.End = now;
            inf.Outcome = inf.P.Dead ? "died:" + inf.P.health.CauseOfDeath : "cured";
        }
    }

    private static void Observe(SimWorld.Map.Map map, Faction player, Dictionary<Pawn, Episode> open, List<Episode> closed, int now)
    {
        ObserveInfections(map, player, now);
        IReadOnlyList<Pawn> all = map.mapPawns.AllPawns;
        // Close episodes.
        foreach (Pawn p in open.Keys.ToList())
        {
            Episode e = open[p];
            if (p.Dead)
            {
                e.End = now;
                e.Outcome = "died:" + p.health.CauseOfDeath;
                closed.Add(e);
                open.Remove(p);
            }
            else if (!p.Downed)
            {
                e.End = now;
                e.Outcome = "recovered";
                e.BloodLossAtEnd = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
                closed.Add(e);
                open.Remove(p);
            }
        }
        // Open new ones.
        for (int i = 0; i < all.Count; i++)
        {
            Pawn p = all[i];
            if (!ReferenceEquals(p.faction, player) || !p.RaceProps.Humanlike || p.Dead || !p.Downed) continue;
            if (open.ContainsKey(p)) continue;
            float bleed = p.health.hediffSet.BleedRateTotal;
            open[p] = new Episode { P = p, Downed = now, BleedAtDown = bleed, MaxBleed = bleed };
        }
        if (open.Count == 0) return;

        // Jobs aimed at the downed.
        for (int i = 0; i < all.Count; i++)
        {
            Pawn q = all[i];
            Job? job = q.jobs?.curJob;
            if (job == null) continue;
            if (!(job.targetA.Thing is Pawn target) || !open.TryGetValue(target, out Episode? e)) continue;
            if (job.def == JobDefOf.Rescue)
            {
                if (e.FirstRescueJob < 0) { e.FirstRescueJob = now; e.Rescuer = q.Label; }
                e.lastRescueJob = job;
            }
            else if (job.def == JobDefOf.TendPatient)
            {
                if (e.FirstTendJob < 0) e.FirstTendJob = now;
                if (!ReferenceEquals(e.lastTendJob, job)) { e.TendJobs++; e.lastTendJob = job; }
            }
        }

        bool sampleOthers = now % 600 == 0;
        foreach (Episode e in open.Values)
        {
            Pawn p = e.P;
            float bleed = p.health.hediffSet.BleedRateTotal;
            if (bleed > e.MaxBleed) e.MaxBleed = bleed;
            if (e.InBed < 0 && OnBed(p)) e.InBed = now;
            if (e.FirstTended < 0 && p.health.hediffSet.hediffs.Any(h => h.IsTended)) e.FirstTended = now;
            e.BloodLossAtEnd = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;

            if (!sampleOthers) continue;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn q = all[i];
                if (!ReferenceEquals(q.faction, player) || !q.RaceProps.Humanlike || q.Dead || q.Downed) continue;
                string doing = q.jobs?.curJob?.def.defName ?? "(none)";
                e.OthersDoing[doing] = e.OthersDoing.TryGetValue(doing, out int n) ? n + 1 : 1;
                if (probe) Probe(q, p, e);
            }
        }
    }

    private static void Bump(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;

    private static void Probe(Pawn q, Pawn patient, Episode e)
    {
        if (q.workSettings == null || !q.workSettings.EverWork) { Bump(e.RescueGate, "noWorkSettings"); return; }
        if (!q.workSettings.WorkIsActive(WorkTypeDefOf.Doctor)) { Bump(e.RescueGate, "doctorOff"); Bump(e.TendGate, "doctorOff"); return; }
        WorkGiverDef rescueDef = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
        var rescue = (WorkGiver_Scanner)rescueDef.Worker;
        if (rescue.MissingRequiredCapacity(q)) Bump(e.RescueGate, "missingCapacity");
        else if (!DoctorUtility.IsCaredForBy(q, patient)) Bump(e.RescueGate, "notCaredFor");
        else if (RestUtility.FindBedFor(q) == null) Bump(e.RescueGate, "noBedForRescuer");
        else if (!Reachability.CanReach(q, patient, PathEndMode.Touch)) Bump(e.RescueGate, "cantReach");
        else if (!q.Map!.reservationManager.CanReserve(q, patient)) Bump(e.RescueGate, "reserved");
        else Bump(e.RescueGate, "WOULD");

        bool emergency = TendUtility.NeedsEmergencyTend(patient);
        if (!TendUtility.HasAnythingToTend(patient)) Bump(e.TendGate, "nothingToTend");
        else if (!Reachability.CanReach(q, patient, PathEndMode.Touch)) Bump(e.TendGate, "cantReach");
        else if (!q.Map!.reservationManager.CanReserve(q, patient)) Bump(e.TendGate, "reserved");
        else Bump(e.TendGate, emergency ? "WOULD(emergency)" : "WOULD(normal)");
    }
}
