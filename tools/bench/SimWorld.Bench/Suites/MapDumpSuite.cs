using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimWorld.God.View;
using SimWorld.Map.View;
using SimWorld.Scenario;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Not a measurement: a picture. Starts one real seeded game the way <c>--suite storyteller</c> does, opens
    /// the player's settlement and writes its <see cref="MapViewSnapshot"/> as JSON on each of
    /// <c>--capture-days</c>. <c>tools/maprender</c> draws those files with the host's real models, so a map can
    /// be looked at without Unity.
    ///
    /// <para/>It exists because the probe's numbers did not show what one picture did: on seed 777 every
    /// mountain on the map was mined away by day 6 and 207 storage huts covered the land by day 20, and no
    /// metric anyone was watching moved. The JSON is the same values-and-defNames read model the host
    /// binds to, so a render of it is what the host could draw.
    /// </summary>
    internal static class MapDumpSuite
    {
        public static void Run(BenchOptions opt)
        {
            Bootstrap.ResetSim(opt.Seed);
            Ablation.Clear();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: opt.Solo,
                bandSize: opt.Band);

            SimWorld.World.Settlement home = game.CivilizationTarget.Seat
                ?? throw new InvalidOperationException("mapdump: the new game founded no settlement");
            GodCommands.OpenSettlement(home.tile);

            Directory.CreateDirectory(opt.OutDir);
            var days = new HashSet<int>(opt.CaptureDays);
            int last = opt.CaptureDays.Max();

            Console.WriteLine("## Map dump: seed " + opt.Seed.ToString(CultureInfo.InvariantCulture)
                + ", " + (opt.Solo ? "solo" : "populated") + " world, settlement " + home.name);
            Console.WriteLine();

            if (days.Contains(0)) Dump(opt, home.tile, 0);
            for (int day = 1; day <= last; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                if (days.Contains(day)) Dump(opt, home.tile, day);
            }
        }

        private static void Dump(BenchOptions opt, int tile, int day)
        {
            MapViewSnapshot snap = MapViewSnapshot.Capture(tile);
            if (!snap.HasMap)
            {
                Console.WriteLine("- day " + day.ToString(CultureInfo.InvariantCulture) + ": no map (" + snap.AbsenceReason + ")");
                return;
            }

            string path = Path.Combine(opt.OutDir, "map-" + opt.Seed.ToString(CultureInfo.InvariantCulture)
                + "-day" + day.ToString(CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, ToJson(snap, day));

            var top = snap.AllThings()
                .GroupBy(t => t.DefName)
                .OrderByDescending(g => g.Count())
                .Take(12)
                .Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("- day " + day.ToString(CultureInfo.InvariantCulture) + " → `" + path + "`: "
                + string.Join(", ", top));
        }

        private static string ToJson(MapViewSnapshot snap, int day)
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"day\":").Append(I(day))
              .Append(",\"tick\":").Append(I(snap.TicksGame))
              .Append(",\"settlement\":").Append(S(snap.SettlementName))
              .Append(",\"sizeX\":").Append(I(snap.SizeX))
              .Append(",\"sizeZ\":").Append(I(snap.SizeZ));

            sb.Append(",\"terrainPalette\":[").Append(string.Join(",", snap.Terrain.Palette.Select(S))).Append(']');
            sb.Append(",\"terrain\":[").Append(string.Join(",", snap.Terrain.Cells.Select(I))).Append(']');
            sb.Append(",\"roofPalette\":[").Append(string.Join(",", snap.Roofs.Palette.Select(r =>
                "{\"def\":" + S(r.DefName) + ",\"natural\":" + B(r.IsNatural) + ",\"thick\":" + B(r.IsThickRoof) + "}"))).Append(']');
            sb.Append(",\"roofs\":[").Append(string.Join(",", snap.Roofs.Cells.Select(I))).Append(']');

            sb.Append(",\"things\":[");
            bool first = true;
            foreach (ThingView t in snap.AllThings())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(I(t.ThingId))
                  .Append(",\"def\":").Append(S(t.DefName))
                  .Append(",\"stuff\":").Append(S(t.StuffDefName))
                  .Append(",\"x\":").Append(I(t.Position.x)).Append(",\"z\":").Append(I(t.Position.z))
                  .Append(",\"rot\":").Append(I(t.Rotation.AsInt))
                  .Append(",\"minX\":").Append(I(t.OccupiedMin.x)).Append(",\"minZ\":").Append(I(t.OccupiedMin.z))
                  .Append(",\"w\":").Append(I(t.OccupiedSize.x)).Append(",\"h\":").Append(I(t.OccupiedSize.z))
                  .Append(",\"cat\":").Append(S(t.Category.ToString()))
                  .Append(",\"growth\":").Append(F(t.PlantGrowth))
                  .Append(",\"build\":").Append(F(t.BuildProgress))
                  .Append(",\"stack\":").Append(I(t.StackCount))
                  .Append('}');
            }
            sb.Append(']');

            sb.Append(",\"pawns\":[");
            first = true;
            foreach (PawnView p in snap.Pawns)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(I(p.ThingId))
                  .Append(",\"def\":").Append(S(p.DefName))
                  .Append(",\"label\":").Append(S(p.Label))
                  .Append(",\"faction\":").Append(S(p.FactionDefName))
                  .Append(",\"x\":").Append(I(p.Position.x)).Append(",\"z\":").Append(I(p.Position.z))
                  .Append(",\"rot\":").Append(I(p.Rotation.AsInt))
                  .Append(",\"downed\":").Append(B(p.Downed))
                  .Append(",\"dead\":").Append(B(p.Dead))
                  .Append(",\"size\":").Append(F(p.BodySize))
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string B(bool v) => v ? "true" : "false";

        private static string S(string? s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
