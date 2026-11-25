using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Who dies, and where they were: <c>--death-detail true</c> on the arc. Read-only and draws nothing from
    /// <c>Rand</c>, so a run with it on is the same run as one without. Every <see cref="Every"/> ticks it notes each
    /// citizen with hypothermia (where they lie, the air, the bed, the job, their household's hearth); when one of
    /// them dies it prints the first and the last notes. Every other death prints a line with age, stage and cause.
    /// Hourly it also counts where the town's babies lie, printed at each arc sample.
    /// </summary>
    internal sealed class ExposureWatch
    {
        public const int Every = 250;
        private const int Keep = 6;

        private sealed class Track
        {
            public string Onset = "";
            public readonly Queue<string> Last = new Queue<string>();
            public float Peak;
        }

        private readonly Dictionary<Pawn, Track> tracks = new Dictionary<Pawn, Track>();
        private readonly HashSet<Pawn> known = new HashSet<Pawn>();
        private readonly HashSet<Pawn> reported = new HashSet<Pawn>();
        private readonly SortedDictionary<string, int> babyCensus = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        private int babySamples;

        public void Sample(SimWorld.World.Settlement settlement, int tick)
        {
            Map.Map? map = settlement.InteriorMap;
            foreach (Pawn p in settlement.Citizens) known.Add(p);
            foreach (Pawn p in known)
            {
                if (p.Dead || reported.Contains(p)) continue;
                Hediff? cold = p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia);
                if (cold == null || cold.Severity < 0.04f) continue;
                if (!tracks.TryGetValue(p, out Track? t))
                {
                    t = new Track();
                    tracks[p] = t;
                    t.Onset = Describe(p, settlement, map, tick);
                }
                if (cold.Severity > t.Peak) t.Peak = cold.Severity;
                t.Last.Enqueue(Describe(p, settlement, map, tick));
                while (t.Last.Count > Keep) t.Last.Dequeue();
            }
            foreach (Pawn p in known)
            {
                if (!p.Dead || !reported.Add(p)) continue;
                var sb = new StringBuilder();
                sb.Append("  DEATH ").Append(p.Label).Append(" day ").Append(Day(p.health.DeathTick))
                    .Append(' ').Append(p.health.CauseOfDeath).Append(" (").Append(p.health.DeathCauseHediff?.defName ?? "-")
                    .Append(") age ").Append(Age(p)).Append(' ').Append(p.ageTracker?.CurLifeStage?.defName ?? "-")
                    .Append(" family ").Append(p.relations.familyId);
                System.Console.WriteLine(sb.ToString());
                if (tracks.TryGetValue(p, out Track? t))
                {
                    System.Console.WriteLine("    onset: " + t.Onset);
                    foreach (string line in t.Last) System.Console.WriteLine("    last:  " + line);
                    tracks.Remove(p);
                }
            }
            // A survivor's track is dropped once it has warmed through, so a later cold spell gets its own onset.
            foreach (Pawn p in tracks.Keys.ToList())
            {
                Hediff? cold = p.Dead ? null : p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia);
                if (!p.Dead && cold == null) tracks.Remove(p);
            }
        }

        public void SampleBabies(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            foreach (Pawn p in settlement.Citizens)
            {
                if (p.Dead || !p.health.LifeStageForcesDowned) continue;
                babySamples++;
                string where = Where(p, map);
                if (p.Spawned && p.Asleep) where += "+asleep";
                if (p.Spawned && TemperatureInjuryUtility.AmbientTemperature(p) < TemperatureInjuryUtility.ComfortableTemperatureRange(p).min)
                    where += "+cold";
                babyCensus[where] = babyCensus.TryGetValue(where, out int n) ? n + 1 : 1;
            }
        }

        public string CensusLine()
        {
            string line = "  babies (hourly samples " + babySamples + "): "
                + (babyCensus.Count == 0 ? "-" : string.Join(", ", babyCensus.Select(kv => kv.Key + " " + kv.Value)));
            babyCensus.Clear();
            babySamples = 0;
            return line;
        }

        private static string Where(Pawn p, Map.Map? map)
        {
            if (!p.Spawned || p.Map == null) return "unspawned";
            if (p.Map != map) return "othermap";
            Thing? bed = p.CurrentBed();
            Dwelling? d = DwellingUtility.DwellingAt(p.Map, p.Position);
            string house = d == null ? "nohouse" : d.householdId == p.relations.familyId ? "ownhouse" : "otherhouse";
            if (bed != null) return (DwellingUtility.IsOwnHomeBed(p, bed) ? "ownbed" : "otherbed") + "@" + house;
            Room? room = p.Map.roomTracker.RoomAt(p.Position);
            string air = room == null ? "noroom" : room.TouchesOutside ? "outdoors" : "indoors";
            return "ground-" + air + "@" + house;
        }

        private static string Describe(Pawn p, SimWorld.World.Settlement settlement, Map.Map? map, int tick)
        {
            var sb = new StringBuilder();
            Hediff? cold = p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia);
            sb.Append("d").Append(Day(tick)).Append(" h").Append((tick % 60000) / 2500)
                .Append(' ').Append(p.Label).Append(" age ").Append(Age(p)).Append(' ')
                .Append(p.ageTracker?.CurLifeStage?.defName ?? "-")
                .Append(" sev ").Append(F(cold?.Severity ?? 0f));
            if (p.Spawned && p.Map != null)
            {
                FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(p);
                sb.Append(" at ").Append(p.Position.x).Append(',').Append(p.Position.z)
                    .Append(' ').Append(Where(p, map))
                    .Append(" air ").Append(F(TemperatureInjuryUtility.AmbientTemperature(p)))
                    .Append(" out ").Append(F(p.Map.outdoorTemperature))
                    .Append(" comfyMin ").Append(F(comfy.min));
            }
            else
            {
                sb.Append(" unspawned");
            }
            sb.Append(p.Asleep ? " asleep" : " awake")
                .Append(p.Downed ? (p.health.LifeStageForcesDowned ? " downed(baby)" : " DOWNED") : "")
                .Append(" job ").Append(p.jobs?.curJob?.def.defName ?? "-");
            if (p.jobs?.curJob?.def != null && p.jobs.curJob.targetA.Thing is Thing ta) sb.Append('>').Append(ta.Label);
            if (p.MentalStateDef != null) sb.Append(" mental ").Append(p.MentalStateDef.defName);
            sb.Append(" worn ").Append(p.apparel?.WornApparel.Count ?? 0);
            sb.Append(" home ").Append(HomeLine(p, map));
            return sb.ToString();
        }

        private static string HomeLine(Pawn p, Map.Map? map)
        {
            if (map == null || p.relations.familyId == Pawn_RelationsTracker.None) return "nofamily";
            var parts = new List<string>();
            foreach (Dwelling d in DwellingUtility.DwellingsOn(map))
            {
                if (d.householdId != p.relations.familyId) continue;
                var sb = new StringBuilder();
                sb.Append(d.def.defName).Append(d.IsComplete(map) ? "" : "(incomplete)").Append(d.beingDemolished ? "(demolish)" : "");
                foreach (IntVec3 spot in d.Spots(DwellingPartKind.HearthSpot))
                {
                    Thing? fire = null;
                    foreach (Thing t in map.thingGrid.ThingsListAt(spot)) if (t.def == ShelterThingDefOf.Campfire) fire = t;
                    if (fire == null) { sb.Append(" hearth:none"); continue; }
                    CompFlickable? flick = (fire as ThingWithComps)?.GetComp<CompFlickable>();
                    CompRefuelable? fuel = (fire as ThingWithComps)?.GetComp<CompRefuelable>();
                    sb.Append(" hearth:").Append(flick?.WantSwitchOn == true ? "want" : "nowant")
                        .Append(flick?.SwitchIsOn == true ? "/on" : "/off")
                        .Append(" fuel ").Append(F(fuel?.Fuel ?? -1f));
                }
                Room? room = null;
                foreach (IntVec3 c in d.Interior) { room = map.roomTracker.RoomAt(c); if (room != null) break; }
                if (room != null) sb.Append(" room ").Append(F(room.Temperature)).Append(room.TouchesOutside ? " open" : "");
                parts.Add(sb.ToString());
            }
            return parts.Count == 0 ? "none" : string.Join(" | ", parts);
        }

        private static string Day(int tick) => (tick / 60000f).ToString("0.00", CultureInfo.InvariantCulture);
        private static string Age(Pawn p) => (p.ageTracker?.AgeBiologicalYearsFloat ?? -1f).ToString("0.0", CultureInfo.InvariantCulture);
        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
