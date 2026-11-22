using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Where a settlement put what it built: the host's default new game (seed <c>simworld-host</c>, a band of
    /// 20, three subdivisions, solo; <c>SimHarness.StartGame</c> in simWorld.Host), opened and ticked to
    /// <c>--ticks</c> (default 60,000, the tick of the host's street capture). Reports every dwelling and its
    /// blueprint or frame (def, cell, rotation, whether it is on a planned plot), every bed and where it stands
    /// (compound interior, a room, or outdoors), and what is inside the walled compound. Deterministic: the
    /// same seed reports the same layout.
    /// </summary>
    internal static class LayoutSuite
    {
        public const string HostSeed = "simworld-host";

        public static void Run(BenchOptions opt)
        {
            Report.Heading("Layout: where the settlement built (" + opt.LayoutSeed + ", tick " + opt.LayoutTicks + ")");

            // The host's SimHarness.StartGame resets neither counter, so a second Play in one Editor session
            // (domain reload off) starts with both where the first left them; --layout-host-counters does that.
            if (!opt.LayoutHostCounters) CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, opt.LayoutSeed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            // The host's SimHarness.StartGame reads the god view before it opens the settlement.
            if (opt.LayoutViewEvery > 0) GodViewSnapshot.Capture();
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;
            MapViewVersions? held = null;

            for (int i = 0; i < opt.LayoutTicks; i++)
            {
                game.TickManager.DoSingleTick();
                if (opt.LayoutDigestEvery > 0 && (i + 1) % opt.LayoutDigestEvery == 0) Console.WriteLine("  digest " + SimDigest.Line(map));
                if (opt.LayoutViewEvery > 0 && (i + 1) % opt.LayoutViewEvery == 0)
                {
                    GodViewSnapshot.Capture();
                    GodViewSnapshot.ChronicleSince(-1);
                    GodViewSnapshot.CitizensOf(settlement.tile);
                    GodViewSnapshot.WorkPrioritiesOf(settlement.tile);
                    MapViewSnapshot.Capture();
                    held = MapViewSnapshot.CaptureChanges(held).Versions;
                }
                if (opt.LayoutEvery > 0 && (i + 1) % opt.LayoutEvery == 0) Console.WriteLine(Describe(map, settlement, i + 1, brief: true));
            }
            Console.WriteLine(Describe(map, settlement, opt.LayoutTicks, brief: false));
        }

        public static string Describe(CoreMap map, Settlement settlement, int tick, bool brief)
        {
            var sb = new System.Text.StringBuilder();
            SettlementCompound? compound = map.settlementCompound;
            sb.AppendLine("tick " + tick + ": citizens " + settlement.Citizens.Count + ", map " + map.Size.x + "x" + map.Size.z + ", home area " + map.areaManager.Home.TrueCount + " cells");
            if (compound == null)
            {
                sb.AppendLine("  no compound planned");
            }
            else
            {
                sb.AppendLine("  compound interior " + Rect(compound.Interior) + ", door " + Cell(compound.DoorCell) + ", street z=" + compound.StreetZ
                    + ", village " + Rect(compound.Village));
                sb.Append("  plots:");
                sb.AppendLine();
                foreach ((CellRect plot, Rot4 rot) in compound.DwellingPlots(compound.PlannedDwellings))
                {
                    int notHome = plot.Cells.Count(c => !map.areaManager.Home[c]);
                    int unwalkable = plot.Cells.Count(c => !map.pathGrid.Walkable(c));
                    var on = plot.Cells.SelectMany(c => map.thingGrid.ThingsListAt(c))
                        .Where(t => t.def.category != ThingCategory.Pawn && t.def.category != ThingCategory.Plant && t.def.category != ThingCategory.Filth)
                        .Select(t => t.def.defName).Distinct().OrderBy(n => n, StringComparer.Ordinal);
                    int trees = plot.Cells.Count(c => map.thingGrid.ThingsListAt(c).Any(t => t.def.plant != null && t.def.plant.IsTree));
                    sb.AppendLine("    plot " + Rect(plot) + " " + rot + ": not-home " + notHome + ", unwalkable " + unwalkable + ", trees " + trees + ", holds " + string.Join(",", on));
                }
            }

            var plots = compound == null
                ? new List<(CellRect Plot, Rot4 Rot)>()
                : compound.DwellingPlots(int.MaxValue, map.Size).ToList();

            ThingDef wood = MapGen.MapGenThingDefOf.WoodLog;
            int woodOnMap = map.listerThings.AllThings.Where(t => t.def == wood && t.Spawned).Sum(t => t.stackCount);
            int woodStored = settlement.Stores.TryGetValue(wood, out int stored) ? stored : 0;
            var jobs = map.mapPawns.AllPawnsSpawned.Where(p => p.jobs != null)
                .GroupBy(p => p.jobs!.curJob?.def.defName ?? "none")
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + ":" + g.Count());
            sb.AppendLine("  wood on map " + woodOnMap + ", stored " + woodStored + "; jobs " + string.Join(" ", jobs));
            sb.AppendLine("  dwellings:");
            foreach (Dwelling d in DwellingUtility.DwellingsOn(map))
            {
                bool onPlot = plots.Any(p => p.Rot == d.rot && !p.Plot.ClipInsideRect(d.footprint).IsEmpty);
                sb.AppendLine("    " + d.def.defName + " rot " + d.rot + " covers " + Rect(d.footprint) + (d.IsComplete(map) ? " complete" : " going up")
                    + (onPlot ? " ON PLOT" : " off-plot"));
            }

            int beds = 0, inCompound = 0, inRoom = 0, outdoors = 0;
            var bedLines = new List<string>();
            foreach (Thing t in map.listerThings.AllThings.Where(t => ReferenceEquals(BuiltDef(t), ConstructionThingDefOf.Bed)).OrderBy(t => t.thingIDNumber))
            {
                beds++;
                string where;
                Room? room = map.roomTracker.RoomAt(t.Position);
                if (compound != null && compound.Interior.Contains(t.Position)) { where = "compound interior"; inCompound++; }
                else if (room != null && !room.TouchesOutside) { where = "enclosed room (" + room.Cells.Count + " cells)"; inRoom++; }
                else { where = "outdoors"; outdoors++; }
                bedLines.Add("    " + t.def.defName + " at " + Cell(t.Position) + ": " + where);
            }
            sb.AppendLine("  beds (incl. blueprints/frames): " + beds + " = compound " + inCompound + ", room " + inRoom + ", outdoors " + outdoors);
            if (!brief) foreach (string l in bedLines) sb.AppendLine(l);

            if (compound != null)
            {
                var inside = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (IntVec3 c in compound.Interior.Cells)
                {
                    foreach (Thing t in map.thingGrid.ThingsListAt(c))
                    {
                        if (t.def.category == ThingCategory.Pawn) continue;
                        inside[t.def.defName] = inside.TryGetValue(t.def.defName, out int n) ? n + 1 : 1;
                    }
                }
                int ring = compound.WallCells.Count(c => map.edificeGrid[c] != null && StoneWallMaterials.IsWall(map.edificeGrid[c]!.def));
                sb.AppendLine("  ring walls built " + ring + "/" + compound.WallCells.Count()
                    + "; interior holds: " + (inside.Count == 0 ? "nothing" : string.Join(", ", inside.Select(kv => kv.Key + " x" + kv.Value))));
            }
            if (!brief && compound != null) sb.Append(Picture(map, compound.Village.ExpandedBy(6).ClipInsideMap(map)));
            return sb.ToString();
        }

        /// <summary>North up: # wall, D door, b bed, H a house's wall, + other building, B blueprint/frame, ~ rock or
        /// other unwalkable, T tree, f field, . home area, blank outside it.</summary>
        private static string Picture(CoreMap map, CellRect view)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("  map " + Rect(view) + ", north up:");
            for (int z = view.maxZ; z >= view.minZ; z--)
            {
                sb.Append("  ").Append(z.ToString("000", CultureInfo.InvariantCulture)).Append(' ');
                for (int x = view.minX; x <= view.maxX; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                    Thing? e = map.edificeGrid[c];
                    char ch;
                    if (here.Any(t => t is Blueprint || t is Frame)) ch = 'B';
                    else if (e != null && ReferenceEquals(e.def, CompoundThingDefOf.Door)) ch = 'D';
                    else if (e != null && DwellingUtility.DwellingAt(map, c) != null && e.def.Fillage == FillCategory.Full) ch = 'H';
                    else if (e != null && StoneWallMaterials.IsWall(e.def)) ch = '#';
                    else if (e != null && ReferenceEquals(e.def, ConstructionThingDefOf.Bed)) ch = 'b';
                    else if (!map.pathGrid.Walkable(c)) ch = '~';
                    else if (e != null) ch = '+';
                    else if (here.Any(t => t.def.plant != null && t.def.plant.IsTree)) ch = 'T';
                    else if (map.zoneManager.ZoneAt(c) is Zone_Growing) ch = 'f';
                    else ch = map.areaManager.Home[c] ? '.' : ' ';
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static ThingDef? BuiltDef(Thing t) => t switch
        {
            Blueprint bp => bp.EntityToBuild,
            Frame f => f.EntityToBuild,
            _ => t.def,
        };

        private static string Cell(IntVec3 c) => "(" + c.x.ToString(CultureInfo.InvariantCulture) + "," + c.z.ToString(CultureInfo.InvariantCulture) + ")";

        private static string Rect(CellRect r) => "[" + r.minX + ".." + r.maxX + " x " + r.minZ + ".." + r.maxZ + "]";
    }
}
