using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Conditions;
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
            if (map.settlementCompound is SettlementCompound compound)
            {
                Room? dorm = map.roomTracker.RoomAt(compound.BedCells.First());
                Console.WriteLine("dormitory: interior " + compound.Interior + ", room " + (dorm == null ? "none" : dorm.Cells.Count + " cells, outdoor air " + dorm.Group.UsesOutdoorTemperature)
                    + ", beds " + compound.Interior.Cells.Count(c => map.thingGrid.ThingsListAt(c).Any(t => t.def == ConstructionThingDefOf.Bed))
                    + ", hearth cell " + compound.HearthCell + " holds " + string.Join(",", map.thingGrid.ThingsListAt(compound.HearthCell).Select(t => t.def.defName)));
            }

            Console.WriteLine(OpenHouses(map));
            var watch = new ExposureWatch();
            int quakesBefore = Fired(game, "Earthquake");
            bool forced = false, frosted = false;
            int end = Find.TickManager.TicksGame + opt.Days * GenDate.TicksPerDay;
            while (Find.TickManager.TicksGame < end)
            {
                game.TickManager.DoSingleTick();
                int now = Find.TickManager.TicksGame;
                if (!frosted && opt.FrostAt >= 0 && now >= opt.FrostAt * GenDate.TicksPerDay)
                {
                    frosted = true;
                    GameConditionDef snap = DefDatabase<GameConditionDef>.GetNamed("ColdSnap");
                    for (int k = 0; k < 2; k++) map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(snap, opt.Days * GenDate.TicksPerDay));
                    Console.WriteLine("frost from day " + Day(now) + ": two cold snaps on the seat's map");
                }
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
                if (now % GenDate.TicksPerDay == 0) Console.WriteLine(OpenHouses(map));
            }
        }

        /// <summary>Every lived-in or planned house that is not whole: what of it is missing, and what stands on each
        /// missing part's cell (blueprint and its stuff, frame and what it still needs, anything else, and who is
        /// on it or working it). Read-only.</summary>
        private static string OpenHouses(Map.Map map)
        {
            var sb = new System.Text.StringBuilder();
            List<Pawn> pawns = map.mapPawns.AllPawnsSpawned.ToList();
            foreach (Dwelling d in DwellingUtility.DwellingsOn(map))
            {
                if (d.IsComplete(map)) continue;
                sb.Append("  open house id " + d.id + " " + d.def.defName + " at " + d.footprint + " household " + d.householdId
                    + " replaces " + d.replacesId + " replacedBy " + d.replacedById + " demolish " + d.beingDemolished + Environment.NewLine);
                foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in d.Parts)
                {
                    if (Dwelling.IsBuilt(map, kind, cell)) continue;
                    string things = string.Join(",", map.thingGrid.ThingsListAt(cell).Select(t => t switch
                    {
                        Frame f => "FRAME(" + f.def.defName + "/" + f.Stuff?.defName + " work " + f.workDone.ToString("0") + "/" + f.WorkToBuild.ToString("0")
                            + " need " + string.Join("+", f.TotalMaterialCost().Select(c => c.thingDef.defName + " " + f.MaterialStillNeeded(c.thingDef))) + (map.reservationManager.IsReserved(f) ? " reserved" : "") + ")",
                        Blueprint b => "BP(" + b.def.defName + "/" + b.stuffToUse?.defName + (map.reservationManager.IsReserved(b) ? " reserved" : "") + ")",
                        Pawn p => "PAWN(" + p.Label + " " + (p.jobs?.curJob?.def.defName ?? "-") + (p.Downed ? " downed" : "") + ")",
                        _ => t.def.defName,
                    }));
                    string workers = string.Join(",", pawns.Where(p => p.jobs?.curJob is Job j && (j.targetA.Cell == cell || j.targetB.Cell == cell))
                        .Select(p => p.Label + ":" + p.jobs!.curJob!.def.defName));
                    sb.Append("    missing " + kind + " " + cell + " edifice " + (map.edificeGrid[cell]?.def.defName ?? "-") + " : " + (things.Length == 0 ? "(nothing)" : things)
                        + (workers.Length > 0 ? " workers " + workers : "") + Environment.NewLine);
                }
            }
            return sb.Length == 0 ? "  open houses: none" : sb.ToString().TrimEnd();
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
                + " | children worn " + worn + " | adults: " + jobs + Coldest(alive.Where(Child), map) + Babies(alive.Where(Baby), map);
        }

        /// <summary>Each cold baby: whether it needs moving, the safe bed a rescuer would find for it, and every bed that
        /// could warm it (finished house, fuel in the hearth) or whose air is already comfortable.</summary>
        private static string Babies(IEnumerable<Pawn> babies, Map.Map map)
        {
            var sb = new System.Text.StringBuilder();
            Pawn? carer = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.RaceProps.Humanlike && !p.Downed && p.ageTracker.CurLifeStage?.developmentalStage == DevelopmentalStage.Adult);
            foreach (Pawn b in babies.Where(b => b.Spawned))
            {
                if (!BabySafetyUtility.NeedsMovingForTemperature(b)) continue;
                Thing? safe = carer == null ? null : BabySafetyUtility.SafeBedFor(b, carer);
                sb.Append(Environment.NewLine + "      cold baby " + b.Label + " at " + b.Position + " air " + TemperatureInjuryUtility.AmbientTemperature(b).ToString("0.0", CultureInfo.InvariantCulture)
                    + " rescuer " + (map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.jobs?.curJob is Job j && j.def == JobDefOf.Rescue && j.targetA.Thing == b)?.Label ?? "-") + " safe bed " + (safe == null ? "none" : safe.Position.ToString()));
            }
            if (sb.Length == 0) return "";
            int warmable = 0, comfy = 0, beds = 0;
            foreach (Thing bed in map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed))
            {
                beds++;
                if (SettlementClimate.WillWarm(map, bed)) warmable++;
                if (RotUtility.AmbientTemperatureAt(map, bed.GetSleepingSlotPos()) >= 16f) comfy++;
            }
            string hearths = string.Join(",", DwellingUtility.DwellingsOn(map).Where(d => d.IsComplete(map) && d.householdId != Pawn_RelationsTracker.None)
                .Select(d => d.def.defName + (SettlementClimate.HasFueledHearth(map, d) ? ":fuel" : ":nofuel")));
            sb.Append(Environment.NewLine + "      beds " + beds + ", warmable " + warmable + ", air>=16 " + comfy + "; finished houses " + hearths
                + "; carer " + (carer?.Label ?? "none"));
            return sb.ToString();
        }

        /// <summary>Where the coldest of <paramref name="who"/> is and what it is doing.</summary>
        private static string Coldest(IEnumerable<Pawn> who, Map.Map map)
        {
            Pawn? p = who.Where(c => c.Spawned).OrderByDescending(c => c.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f).FirstOrDefault();
            if (p == null) return "";
            Thing? bed = p.CurrentBed();
            string where = bed == null ? (map.roomTracker.RoomAt(p.Position)?.Group.UsesOutdoorTemperature != false ? "outdoors" : "indoors")
                : DwellingUtility.DwellingOfBed(bed) is Dwelling d ? (d.householdId == p.relations.familyId ? "ownbed" : "otherhouse") : "dormbed";
            return Environment.NewLine + "      coldest child " + p.Label + " sev "
                + (p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia)?.Severity ?? 0f).ToString("0.00", CultureInfo.InvariantCulture)
                + " " + where + " air " + TemperatureInjuryUtility.AmbientTemperature(p).ToString("0.0", CultureInfo.InvariantCulture)
                + " comfyMin " + TemperatureInjuryUtility.ComfortableTemperatureRange(p).min.ToString("0.0", CultureInfo.InvariantCulture)
                + " job " + (p.jobs?.curJob?.def.defName ?? "-") + (p.Downed ? " DOWNED" : "") + (p.Awake() ? "" : " asleep")
                + " worn " + string.Join("+", p.apparel?.WornApparel.Select(a => a.def.defName) ?? Enumerable.Empty<string>());
        }

        private static string Day(int tick) => (tick / (double)GenDate.TicksPerDay).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
