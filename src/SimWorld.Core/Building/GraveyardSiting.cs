using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Where a settlement puts its dead (translation, like <see cref="StorageAnchor"/>: RimWorld's player paints
    /// graves and zones wherever they like, so there is no placement rule to port). Two sites, found once each
    /// and then <b>kept by what stands on the map</b>, never by a field: the <i>graveyard</i>, graves in rows, and
    /// the <i>dump</i>, one small stockpile for the bodies a grave refuses.
    /// <para/>
    /// <b>The graveyard is a fixed grid, and the graves already there are its anchor.</b> A grave is 1x2, and
    /// the only places the settlement digs one are the slots of a grid laid over the whole map:
    /// <c>x</c> a multiple of <see cref="GraveyardTuning.ColumnPitch"/> and <c>z</c> a multiple of
    /// <see cref="GraveyardTuning.RowPitch"/>, which leaves a one-cell lane between every pair of graves in a
    /// row and between rows. Rows come from the grid, not from remembering where the last one went. The
    /// <i>first</i> grave takes the slot nearest the settlement's middle that is clear of its beds and has room
    /// round it to grow (<see cref="TryFindGraveSlot"/>); every later one takes the free slot nearest the
    /// <b>mean of the graves already there</b> — the way <see cref="StorageAnchor"/> gathers huts round the
    /// mean of the huts — so the graveyard fills outward from its first grave as a compact patch of rows. Nothing
    /// is stored: a saved and loaded settlement finds the graveyard in the graves and blueprints that stand.
    /// <para/>
    /// <b>Why a global grid and not "rows from an origin".</b> An earlier form measured the rows from the lowest
    /// x and z among the graves, and asked the ground for a clear 11x5 rectangle. A real generated valley has
    /// sixty cells in a hundred under rock and no clear 11x5 rectangle anywhere on it (measured on the tribal
    /// start of seed "graveyard-wired": the largest clear rectangle with no clearance at all was under 11x5), so the
    /// graveyard was never laid and nobody was buried. A grid asks only that individual slots are free, so it
    /// fits whatever room there is, and it cannot drift: every grave in the game is on the same grid.
    /// <para/>
    /// <b>The dump</b> is sited with wider clearances (<see cref="GraveyardTuning.DumpBedClearance"/> from beds,
    /// <see cref="GraveyardTuning.HubClearance"/> from the road hub, <see cref="GraveyardTuning.DumpGraveClearance"/>
    /// from the graveyard): a body in the dump is seen by everyone who looks at it, so it goes out of the way.
    /// It is a rectangle, the biggest of a short list that fits nearest the settlement, so a cramped valley gets a
    /// smaller dump rather than none.
    /// </summary>
    internal static class GraveyardSiting
    {
        // ---- the graveyard grid ----

        /// <summary>Every grave that stands or is planned: built, blueprint or frame.</summary>
        public static List<IntVec3> GravePositions(Map.Map map)
        {
            ThingDef grave = GraveDefOf.Grave;
            var positions = new List<IntVec3>();

            IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(grave);
            for (int i = 0; i < built.Count; i++) positions.Add(built[i].Position);

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, grave)) positions.Add(bp.Position);
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, grave)) positions.Add(f.Position);
            }
            return positions;
        }

        /// <summary>The rectangle that holds every grave, with their footprints; false when there are none.</summary>
        public static bool TryGetBounds(Map.Map map, out CellRect bounds)
        {
            List<IntVec3> graves = GravePositions(map);
            bounds = CellRect.Empty;
            if (graves.Count == 0) return false;
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < graves.Count; i++)
            {
                CellRect footprint = GenAdj.OccupiedRect(graves[i], default, GraveDefOf.Grave.size);
                minX = System.Math.Min(minX, footprint.minX);
                minZ = System.Math.Min(minZ, footprint.minZ);
                maxX = System.Math.Max(maxX, footprint.maxX);
                maxZ = System.Math.Max(maxZ, footprint.maxZ);
            }
            bounds = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            return true;
        }

        /// <summary>
        /// The slot the next grave goes in: beside the graves already there, or — with none yet — the first slot
        /// of a new graveyard. False only when the whole map has no free slot.
        /// </summary>
        public static bool TryFindGraveSlot(Map.Map map, out IntVec3 slot)
        {
            List<IntVec3> graves = GravePositions(map);
            bool[] nearBeds = CellsNearBeds(map, GraveyardTuning.BedClearance);
            if (graves.Count > 0)
            {
                IntVec3 centre = Mean(graves);
                return TryFindNearestSlot(map, centre, nearBeds, null, 0, out slot);
            }

            // A new graveyard. Inside the home area first, when the settlement has one — it is the settlement's
            // ground — and anywhere on the map if nothing there will do. A graveyard is not a building of the
            // settlement's, and is not held to its boundary the way a bed is.
            IntVec3 middle = SettlementCentre(map, BedCells(map));
            Area home = map.areaManager.Home;
            if (home.TrueCount > 0 && TryFindNearestSlot(map, middle, nearBeds, home, GraveyardTuning.RoomToGrow, out slot)) return true;
            return TryFindNearestSlot(map, middle, nearBeds, null, GraveyardTuning.RoomToGrow, out slot);
        }

        /// <summary>
        /// The free slot nearest <paramref name="centre"/>, searched ring by ring outward over the grid, then the
        /// one ring further because a ring is square and distance is not. <paramref name="within"/> confines the
        /// slots to an area; <paramref name="roomToGrow"/> asks that this many slots in the block round it are free
        /// too (a graveyard begun in a one-slot nook has nowhere to go). Ties go to the lowest z, then lowest x.
        /// </summary>
        private static bool TryFindNearestSlot(Map.Map map, IntVec3 centre, bool[] nearBeds, Area? within, int roomToGrow, out IntVec3 slot)
        {
            int ci = FloorDiv(centre.x, GraveyardTuning.ColumnPitch);
            int cj = FloorDiv(centre.z, GraveyardTuning.RowPitch);
            int reach = System.Math.Max(map.Size.x / GraveyardTuning.ColumnPitch, map.Size.z / GraveyardTuning.RowPitch) + 1;

            long bestCost = long.MaxValue;
            IntVec3 best = IntVec3.Invalid;
            int foundRing = -1;
            for (int k = 0; k <= reach; k++)
            {
                if (foundRing >= 0 && k > foundRing + 1) break;
                for (int dj = -k; dj <= k; dj++)
                {
                    for (int di = -k; di <= k; di++)
                    {
                        if (System.Math.Max(System.Math.Abs(di), System.Math.Abs(dj)) != k) continue;
                        var cell = new IntVec3((ci + di) * GraveyardTuning.ColumnPitch, 0, (cj + dj) * GraveyardTuning.RowPitch);
                        if (!SlotIsFree(map, cell, nearBeds, within)) continue;
                        if (roomToGrow > 0 && FreeSlotsAround(map, cell, nearBeds, within) < roomToGrow) continue;

                        long dx = cell.x - centre.x, dz = cell.z - centre.z;
                        long cost = dx * dx + dz * dz;
                        if (cost < bestCost || (cost == bestCost && (cell.z < best.z || (cell.z == best.z && cell.x < best.x))))
                        {
                            bestCost = cost;
                            best = cell;
                        }
                        if (foundRing < 0) foundRing = k;
                    }
                }
            }
            slot = best;
            return best.IsValid;
        }

        /// <summary>Free slots in the block of slots two either side of <paramref name="cell"/>, itself included.</summary>
        private static int FreeSlotsAround(Map.Map map, IntVec3 cell, bool[] nearBeds, Area? within)
        {
            int free = 0;
            for (int dj = -2; dj <= 2; dj++)
            {
                for (int di = -2; di <= 2; di++)
                {
                    var other = new IntVec3(cell.x + di * GraveyardTuning.ColumnPitch, 0, cell.z + dj * GraveyardTuning.RowPitch);
                    if (SlotIsFree(map, other, nearBeds, within)) free++;
                }
            }
            return free;
        }

        private static bool SlotIsFree(Map.Map map, IntVec3 cell, bool[] nearBeds, Area? within)
        {
            ThingDef grave = GraveDefOf.Grave;
            if (!GenConstruct.CanPlaceBlueprintAt(grave, cell, map, out _)) return false;
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, default, grave.size).Cells)
            {
                if (map.zoneManager.ZoneAt(c) != null || nearBeds[map.cellIndices.CellToIndex(c)]) return false;
                if (within != null && !within[c]) return false;
            }
            return true;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        private static IntVec3 Mean(List<IntVec3> cells)
        {
            long sumX = 0, sumZ = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                sumX += cells[i].x;
                sumZ += cells[i].z;
            }
            int n = cells.Count;
            return new IntVec3((int)((sumX + n / 2) / n), 0, (int)((sumZ + n / 2) / n));
        }

        /// <summary>Every cell within <paramref name="clearance"/> (Chebyshev, exclusive: a clearance of 6 allows a
        /// distance of 6) of a bed, built or planned — one pass over the beds, so a slot test is a lookup.</summary>
        internal static bool[] CellsNearBeds(Map.Map map, int clearance)
        {
            var near = new bool[map.cellIndices.NumGridCells];
            foreach (IntVec3 bed in BedCells(map))
            {
                CellRect rect = new CellRect(bed.x - (clearance - 1), bed.z - (clearance - 1), clearance * 2 - 1, clearance * 2 - 1).ClipInsideMap(map);
                foreach (IntVec3 c in rect.Cells) near[map.cellIndices.CellToIndex(c)] = true;
            }
            return near;
        }

        // ---- finding a clear rectangle (the dump) ----

        /// <summary>
        /// The nearest clear rectangle to the settlement's middle that honours the clearances, the largest of
        /// <paramref name="sizes"/> (listed biggest first) that fits within each search square; its low corner in
        /// <paramref name="origin"/> and its size in <paramref name="width"/> and <paramref name="height"/>. Inside
        /// the home area first, when the settlement has one, and anywhere on the map only if nothing inside fits.
        /// <paramref name="avoid"/> is an extra rectangle to keep <paramref name="avoidClearance"/> cells from.
        /// </summary>
        public static bool TryFindPlot((int width, int height)[] sizes, Map.Map map, int bedClearance, int hubClearance,
            CellRect? avoid, int avoidClearance, out IntVec3 origin, out int width, out int height)
        {
            List<IntVec3> beds = BedCells(map);
            IntVec3 centre = SettlementCentre(map, beds);
            Area home = map.areaManager.Home;

            for (int pass = home.TrueCount > 0 ? 0 : 1; pass < 2; pass++)
            {
                Area? within = pass == 0 ? home : null;
                for (int radius = GraveyardTuning.SearchRadius; ; radius *= 2)
                {
                    CellRect window = CellRect.CenteredOn(centre, radius).ClipInsideMap(map);
                    if (TrySearchWindow(map, window, centre, sizes, beds, bedClearance, hubClearance, avoid, avoidClearance, within,
                        out origin, out width, out height))
                    {
                        return true;
                    }
                    if (window.Width >= map.Size.x && window.Height >= map.Size.z) break;
                }
            }
            origin = IntVec3.Invalid;
            width = height = 0;
            return false;
        }

        private static bool TrySearchWindow(Map.Map map, CellRect window, IntVec3 centre, (int width, int height)[] sizes,
            List<IntVec3> beds, int bedClearance, int hubClearance, CellRect? avoid, int avoidClearance, Area? within,
            out IntVec3 origin, out int foundWidth, out int foundHeight)
        {
            origin = IntVec3.Invalid;
            foundWidth = foundHeight = 0;
            int w = window.Width, h = window.Height;

            // blocked[x, z] marks a cell no part of the rectangle may cover, built once; a rectangle fits when
            // the prefix sums say it covers no blocked cell. The whole search is O(window area) per size.
            var blocked = new bool[w, h];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = new IntVec3(window.minX + x, 0, window.minZ + z);
                    blocked[x, z] = !ClearGround(map, c) || (within != null && !within[c]);
                }
            }
            foreach (IntVec3 bed in beds) Block(blocked, window, bed, bedClearance);
            Block(blocked, window, SettlementConstructionInitiative.RoadHub(map), hubClearance);
            if (avoid.HasValue) BlockRect(blocked, window, avoid.Value.ExpandedBy(avoidClearance));

            var prefix = new int[w + 1, h + 1];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    prefix[x + 1, z + 1] = prefix[x, z + 1] + prefix[x + 1, z] - prefix[x, z] + (blocked[x, z] ? 1 : 0);
                }
            }

            foreach ((int width, int height) in sizes)
            {
                if (w < width || h < height) continue;
                long bestCost = long.MaxValue;
                for (int z = 0; z + height <= h; z++)
                {
                    for (int x = 0; x + width <= w; x++)
                    {
                        int blockedInside = prefix[x + width, z + height] - prefix[x, z + height] - prefix[x + width, z] + prefix[x, z];
                        if (blockedInside != 0) continue;

                        // Cost: squared distance from the settlement's middle to the rectangle's middle. Strictly
                        // less wins, so ties fall to the first in scan order (lowest z, then lowest x).
                        long dx = window.minX + x + width / 2 - centre.x;
                        long dz = window.minZ + z + height / 2 - centre.z;
                        long cost = dx * dx + dz * dz;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            origin = new IntVec3(window.minX + x, 0, window.minZ + z);
                            foundWidth = width;
                            foundHeight = height;
                        }
                    }
                }
                if (origin.IsValid) return true;
            }
            return false;
        }

        /// <summary>Open ground a plot may cover: walkable, nothing built, planned or growing on it, no zone.</summary>
        private static bool ClearGround(Map.Map map, IntVec3 c)
        {
            if (!GenGrid.Standable(c, map) || map.zoneManager.ZoneAt(c) != null || map.edificeGrid[c] != null) return false;
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < here.Count; i++)
            {
                ThingCategory category = here[i].def.category;
                if (category == ThingCategory.Building || category == ThingCategory.Blueprint
                    || category == ThingCategory.Frame || category == ThingCategory.Plant) return false;
            }
            return true;
        }

        private static void Block(bool[,] blocked, CellRect window, IntVec3 around, int clearance)
        {
            if (clearance <= 0) return;
            BlockRect(blocked, window, new CellRect(around.x - (clearance - 1), around.z - (clearance - 1), clearance * 2 - 1, clearance * 2 - 1));
        }

        private static void BlockRect(bool[,] blocked, CellRect window, CellRect rect)
        {
            CellRect clipped = rect.ClipInsideRect(window);
            if (clipped.IsEmpty) return;
            for (int z = clipped.minZ; z <= clipped.maxZ; z++)
            {
                for (int x = clipped.minX; x <= clipped.maxX; x++) blocked[x - window.minX, z - window.minZ] = true;
            }
        }

        // ---- the settlement's beds ----

        /// <summary>Every cell a bed covers or will cover: built, framed or planned.</summary>
        public static List<IntVec3> BedCells(Map.Map map)
        {
            ThingDef bed = ConstructionThingDefOf.Bed;
            var cells = new List<IntVec3>();

            IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(bed);
            for (int i = 0; i < built.Count; i++) AddFootprint(cells, built[i].Position, built[i].Rotation, bed);

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, bed)) AddFootprint(cells, bp.Position, bp.Rotation, bed);
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, bed)) AddFootprint(cells, f.Position, f.Rotation, bed);
            }
            return cells;
        }

        private static void AddFootprint(List<IntVec3> cells, IntVec3 at, Rot4 rot, ThingDef def)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(at, rot, def.size).Cells) cells.Add(c);
        }

        /// <summary>The middle of where the settlement sleeps: the mean of its bed cells, else the road hub. Integer
        /// arithmetic on non-negative sums, as <see cref="StorageAnchor"/> does, so it cannot differ between
        /// platforms.</summary>
        public static IntVec3 SettlementCentre(Map.Map map, List<IntVec3> beds)
        {
            if (beds.Count == 0) return SettlementConstructionInitiative.RoadHub(map);
            long sumX = 0, sumZ = 0;
            for (int i = 0; i < beds.Count; i++)
            {
                sumX += beds[i].x;
                sumZ += beds[i].z;
            }
            int n = beds.Count;
            return new IntVec3((int)((sumX + n / 2) / n), 0, (int)((sumZ + n / 2) / n));
        }
    }
}
