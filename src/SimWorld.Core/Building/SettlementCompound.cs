using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>The one buildable Def <see cref="SettlementCompound"/> adds to what a settlement builds for
    /// itself, bound in its own <c>[DefOf]</c> class rather than appended to
    /// <see cref="ConstructionThingDefOf"/> (CLAUDE.md: a new binding goes in its own class).</summary>
    [DefOf]
    public static class CompoundThingDefOf
    {
        public static ThingDef Door = null!;
    }

    /// <summary>
    /// The layout a settlement builds itself into: a block of beds, a ring of wall round the block with a
    /// door in its east side, and storage against the outside of the ring (translation: citizen-initiated
    /// construction, <c>docs/design/the-loop.md</c> §5 item 2). RimWorld has nothing to port here; its player
    /// lays out every base by hand.
    ///
    /// <para/><b>The defect this closes.</b> <see cref="SettlementConstructionInitiative"/> knew what a
    /// settlement needed and drew a random cell for each piece, so forty walls stood as forty separate tiles
    /// and no bed was ever inside anything. That class's own remarks left layout as "a later decision"; this
    /// is that decision. It is about where things go. The ring is cut from the wall count the settlement
    /// already wanted (<see cref="TrySize"/>); the one change to how many is that only a wall on the ring
    /// now counts toward it (<see cref="SettlementConstructionInitiative.Shortfall"/>).
    ///
    /// <para/><b>The whole layout is one rectangle.</b> <see cref="Interior"/> is all that is remembered and
    /// all that is saved. Every site is derived from it each time it is asked for, and a site counts as taken
    /// exactly when <see cref="GenConstruct.CanPlaceBlueprintAt"/> refuses it, so there is no record of what
    /// has been placed to drift from what is on the map.
    /// <code>
    ///   . s s s s s s s .      s  storage, against the ring
    ///   s # # # # # # # s      #  wall
    ///   s # b b b b b # .      D  door, in the east side
    ///   s # b b b b b D .      b  bed block: each bed is 1x2, head on the southern cell
    ///   s # b b b b b # .      .  kept clear: the corners, and the ground in front of the door
    ///   s # b b b b b # s
    ///   s # # # # # # # s
    ///   . s s s s s s s .
    /// </code>
    ///
    /// <para/><b>Why the ring may close when nothing else this settlement builds may.</b>
    /// <see cref="SettlementConstructionInitiative.WouldCutOffGround"/> refuses any impassable placement that
    /// would be the only link between two pieces of ground, which a wall completing a ring always is. The
    /// door is what makes that safe here: <see cref="DoorCell"/> is never offered to anything but a
    /// <c>Door</c>, a door and its blueprint and frame are all standable, and the cell in front of it is kept
    /// out of <see cref="StorageCells"/>. So the inside is reachable at every stage of the build.
    ///
    /// <para/><b>It lives inside the home area.</b> A compound is only planned where its whole
    /// <see cref="Footprint"/> is home area, or, when there is no home area at all, it claims its own
    /// (<see cref="ClaimHome"/>). A site the player has since taken out of the home area is not built on; the
    /// initiative's ordinary draw inside the home area takes over for beds and storage, and walls and the
    /// door, which only mean anything on the ring, wait.
    /// </summary>
    public sealed class SettlementCompound : IExposable
    {
        private CellRect interior;

        /// <summary>For Scribe.</summary>
        private SettlementCompound()
        {
        }

        private SettlementCompound(CellRect interior)
        {
            this.interior = interior;
        }

        /// <summary>The ground inside the wall: the bed block.</summary>
        public CellRect Interior => interior;

        /// <summary>The interior and the line of wall round it. The wall stands on this rect's edge.</summary>
        public CellRect Ring => interior.ExpandedBy(1);

        /// <summary>Everything the compound keeps for itself: <see cref="Ring"/> and the one-cell strip
        /// outside it that storage stands in.</summary>
        public CellRect Footprint => interior.ExpandedBy(2);

        /// <summary>The gap in the east side of the ring, halfway up it.</summary>
        public IntVec3 DoorCell => new IntVec3(interior.maxX + 1, 0, interior.minZ + interior.height / 2);

        /// <summary>Where each bed goes, as the cell it is placed on, south-west first and row by row. A bed
        /// is placed unrotated (<see cref="RotationAt"/> turns only the door and the storage): it covers this
        /// cell and the one north of it, and is slept in on this one.</summary>
        public IEnumerable<IntVec3> BedCells
        {
            get
            {
                IntVec2 bed = ConstructionThingDefOf.Bed.size;
                for (int z = interior.minZ; z + bed.z - 1 <= interior.maxZ; z += bed.z)
                {
                    for (int x = interior.minX; x + bed.x - 1 <= interior.maxX; x += bed.x)
                    {
                        yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }

        /// <summary>Every cell of the ring but <see cref="DoorCell"/>.</summary>
        public IEnumerable<IntVec3> WallCells
        {
            get
            {
                IntVec3 door = DoorCell;
                foreach (IntVec3 c in Ring.EdgeCells)
                {
                    if (c != door) yield return c;
                }
            }
        }

        /// <summary>
        /// Where storage goes: the cells that touch the outside of the ring, the west side first (the back,
        /// away from the door), then north, south and east. The four corners touch the ring only diagonally
        /// and are left out. So are the cell in front of the door and the one either side of it, so the way
        /// in is never built over.
        /// </summary>
        public IEnumerable<IntVec3> StorageCells
        {
            get
            {
                CellRect ring = Ring;
                CellRect footprint = Footprint;
                int doorZ = DoorCell.z;
                for (int z = ring.minZ; z <= ring.maxZ; z++) yield return new IntVec3(footprint.minX, 0, z);
                for (int x = ring.minX; x <= ring.maxX; x++) yield return new IntVec3(x, 0, footprint.maxZ);
                for (int x = ring.minX; x <= ring.maxX; x++) yield return new IntVec3(x, 0, footprint.minZ);
                for (int z = ring.minZ; z <= ring.maxZ; z++)
                {
                    if (Math.Abs(z - doorZ) > 1) yield return new IntVec3(footprint.maxX, 0, z);
                }
            }
        }

        /// <summary>Whether <paramref name="rect"/> touches any ground the compound keeps for itself: the
        /// whole <see cref="Village"/>, ring, street, work yard and planned dwellings. Every fallback search
        /// that places a building asks this (<see cref="Refuses"/>), so nothing drawn at random lands on a
        /// wall slot, the door threshold, a work site or a household's plot.</summary>
        public bool Reserves(CellRect rect) => !rect.ClipInsideRect(Village).IsEmpty;

        /// <summary>Whether a fallback search on <paramref name="map"/> must refuse <paramref name="entityDef"/>
        /// at <paramref name="cell"/> because the map's compound keeps that ground. False with no compound.</summary>
        public static bool Refuses(Map.Map map, ThingDef entityDef, IntVec3 cell, Rot4 rot = default) =>
            map.settlementCompound != null && map.settlementCompound.Reserves(GenAdj.OccupiedRect(cell, rot, entityDef.size));

        // ---------------------------------------------------------------------------------------------
        // The village: a street out of the door, a work yard either side of it, dwellings along it.
        // Seen from above, north up, a dwelling drawn as 2x2 rather than its 4x5 to fit:
        //
        //   n n . n n   s s s s s s   . . . .   n n . n n      n  dwelling north of the street, turned North
        //   n n . n n   # # # # # #   w . w .   n n . n n      u  dwelling south of the street, turned South
        //   = = = = = = # b b b b D = = = = = = = = = = =      =  the street, three cells wide
        //   u u . u u   # # # # # #   k . c .   u u . u u      w  research, k kitchen, c stonecutter
        //   u u . u u   s s s s s s   . . . .   u u . u u      (art fills the yard rows behind the benches)
        //
        // The street runs east from the door and west from the back of the ring, centred on the door's row.
        // Every dwelling is turned so its front, authored toward -Z at North (simWorld.Model's contract),
        // faces the street. The work yard is one dwelling plot either side of the street just outside the
        // door, so the benches stand beside the storage, on the way in.
        // ---------------------------------------------------------------------------------------------

        private static IntVec2 DwellingSize => DwellingThingDefOf.House_Paleo.size;

        /// <summary>The row the street is centred on: the door's.</summary>
        public int StreetZ => DoorCell.z;

        /// <summary>The work yard: a dwelling's width of ground east of the compound's footprint, spanning
        /// both rows of plots and the street between them.</summary>
        public CellRect WorkYard => new CellRect(Footprint.maxX + 1, StreetZ - 1 - DwellingSize.z, DwellingSize.x, 2 * DwellingSize.z + 3);

        /// <summary>How many dwellings the village is laid out for: one per two beds of the dormitory, a
        /// founding couple to a house. More can be built past these, further along the street.</summary>
        public int PlannedDwellings
        {
            get
            {
                IntVec2 bed = ConstructionThingDefOf.Bed.size;
                return ((interior.width / bed.x) * (interior.height / bed.z) + 1) / 2;
            }
        }

        /// <summary>Everything the compound keeps for itself: the <see cref="Footprint"/>, the
        /// <see cref="WorkYard"/>, and the street with the <see cref="PlannedDwellings"/> plots along it, as
        /// one rectangle.</summary>
        public CellRect Village
        {
            get
            {
                CellRect bounds = Footprint;
                bounds = Union(bounds, WorkYard);
                foreach ((CellRect plot, Rot4 _) in DwellingPlots(PlannedDwellings)) bounds = Union(bounds, plot);
                return bounds;
            }
        }

        /// <summary>
        /// The dwelling plots in the order the village fills them, as the rect each covers and the way it is
        /// turned: east and west of the compound in turn, the north side of the street then the south, one
        /// plot's width plus a one-cell alley apart. North of the street a dwelling is turned North and so
        /// opens south onto it; south of the street it is turned South. <paramref name="count"/> plots, or
        /// every plot until both ends of the street leave a map of <paramref name="mapSize"/>.
        /// </summary>
        public IEnumerable<(CellRect Plot, Rot4 Rot)> DwellingPlots(int count, IntVec3? mapSize = null)
        {
            IntVec2 size = DwellingSize;
            int step = size.x + 1;
            int northMinZ = StreetZ + 2;
            int southMinZ = StreetZ - 1 - size.z;
            int yielded = 0;
            for (int k = 0; ; k++)
            {
                int eastMinX = FirstEastPlotX + k * step;
                int westMinX = FirstWestPlotX - k * step;
                if (mapSize.HasValue && eastMinX >= mapSize.Value.x && westMinX + size.x <= 0) yield break;
                foreach (int minX in new[] { eastMinX, westMinX })
                {
                    if (yielded++ >= count) yield break;
                    yield return (new CellRect(minX, northMinZ, size.x, size.z), Rot4.North);
                    if (yielded++ >= count) yield break;
                    yield return (new CellRect(minX, southMinZ, size.x, size.z), Rot4.South);
                }
            }
        }

        /// <summary>The cell a thing of <paramref name="size"/> turned <paramref name="rot"/> is placed on to
        /// cover exactly <paramref name="rect"/> (the inverse of <see cref="GenAdj.OccupiedRect"/>, which turns
        /// even spans about the placed cell).</summary>
        internal static IntVec3 PlacementCellFor(CellRect rect, Rot4 rot, IntVec2 size)
        {
            foreach (IntVec3 c in rect.Cells)
            {
                if (GenAdj.OccupiedRect(c, rot, size) == rect) return c;
            }
            throw new ArgumentException("no cell places a " + size + " footprint turned " + rot + " on " + rect);
        }

        /// <summary>The west edge of the first plot east of the work yard, past a one-cell alley.</summary>
        private int FirstEastPlotX => WorkYard.maxX + 2;

        /// <summary>The west edge of the first plot west of the compound, past a one-cell alley beside the
        /// storage on that side.</summary>
        private int FirstWestPlotX => Footprint.minX - 1 - DwellingSize.x;

        private IEnumerable<IntVec3> DwellingCells(Map.Map map)
        {
            foreach ((CellRect plot, Rot4 rot) in DwellingPlots(int.MaxValue, map.Size))
            {
                yield return PlacementCellFor(plot, rot, DwellingSize);
            }
        }

        private static CellRect Union(CellRect a, CellRect b)
        {
            int minX = Math.Min(a.minX, b.minX), minZ = Math.Min(a.minZ, b.minZ);
            return new CellRect(minX, minZ, Math.Max(a.maxX, b.maxX) - minX + 1, Math.Max(a.maxZ, b.maxZ) - minZ + 1);
        }

        /// <summary>The work a settlement raises in its <see cref="WorkYard"/>.</summary>
        public enum WorkSite
        {
            Kitchen,
            Stonecutter,
            Research,
            Butcher,
            Tailor,
            Smithy,
            Art,
        }

        /// <summary>
        /// Where each kind of work goes in the yard. The benches stand on the row beside the street, a cell
        /// apart, so each is worked from the street and none walls in another: research north of it, the
        /// kitchen and the stonecutter south. The workshops stand in the next row back, worked from the open
        /// row between: the butcher's table behind the kitchen it feeds, the smithy beside it, the tailor's
        /// north. Art fills the rest, where everyone going in or out of the door passes it (beauty is read
        /// over what a citizen sees, <c>Needs.BeautyUtility</c>).
        /// </summary>
        public IEnumerable<IntVec3> WorkSiteCells(WorkSite site)
        {
            int x = WorkYard.minX;
            int z = StreetZ;
            switch (site)
            {
                case WorkSite.Research:
                    yield return new IntVec3(x, 0, z + 2);
                    yield return new IntVec3(x + 2, 0, z + 2);
                    break;
                case WorkSite.Kitchen:
                    yield return new IntVec3(x, 0, z - 2);
                    break;
                case WorkSite.Stonecutter:
                    yield return new IntVec3(x + 2, 0, z - 2);
                    break;
                case WorkSite.Butcher:
                    yield return new IntVec3(x, 0, z - 4);
                    break;
                case WorkSite.Smithy:
                    yield return new IntVec3(x + 2, 0, z - 4);
                    break;
                case WorkSite.Tailor:
                    yield return new IntVec3(x, 0, z + 4);
                    break;
                case WorkSite.Art:
                    yield return new IntVec3(x + 2, 0, z + 4);
                    yield return new IntVec3(x + 1, 0, z + 6);
                    yield return new IntVec3(x + 1, 0, z - 6);
                    yield return new IntVec3(x + 3, 0, z + 6);
                    yield return new IntVec3(x + 3, 0, z - 6);
                    break;
            }
        }

        /// <summary>The first free site of <paramref name="site"/>'s kind for <paramref name="entityDef"/>, held
        /// to the same rules as every other site of the compound: inside the home area once there is one, and
        /// <see cref="GenConstruct.CanPlaceBlueprintAt"/> accepting it.</summary>
        public bool TryFindWorkSite(Map.Map map, WorkSite site, ThingDef entityDef, out IntVec3 cell)
        {
            Area home = map.areaManager.Home;
            Area? within = home.TrueCount > 0 ? home : null;
            foreach (IntVec3 c in WorkSiteCells(site))
            {
                if (!Free(map, entityDef, c, within, Rot4.North)) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>Whether any spawned item <paramref name="accepts"/> lies within <paramref name="radius"/>
        /// cells of <paramref name="site"/>, measured as <c>Crafting.WorkGiver_DoBill</c> measures its
        /// ingredient search (a square). A bench whose ingredients are out of that reach is never worked.</summary>
        public static bool ItemWithin(Map.Map map, IntVec3 site, int radius, Func<ThingDef, bool> accepts)
        {
            foreach (IntVec3 c in CellRect.CenteredOn(site, radius).ClipInsideMap(map).Cells)
            {
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < here.Count; i++)
                {
                    Thing t = here[i];
                    if (t.def.category == ThingCategory.Item && t.stackCount > 0 && accepts(t.def)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The next free site for <paramref name="entityDef"/>, in the order the layout fills. False when the
        /// compound has no site left for it, or never had one (it places beds, walls, its door,
        /// storage and dwellings; the work yard has <see cref="TryFindWorkSite"/>). With <paramref name="within"/>, a site must lie wholly inside that area.
        /// </summary>
        public bool TryFindCell(Map.Map map, ThingDef entityDef, Area? within, out IntVec3 cell)
        {
            foreach (IntVec3 c in CellsFor(map, entityDef))
            {
                if (!Free(map, entityDef, c, within, RotationAt(entityDef, c))) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>How many of the compound's sites for <paramref name="entityDef"/> could still take one.
        /// For the ring this is the wall need itself: a wall is wanted where the ring has a gap, and
        /// nowhere else.</summary>
        public int CountFree(Map.Map map, ThingDef entityDef, Area? within)
        {
            int n = 0;
            foreach (IntVec3 c in CellsFor(map, entityDef))
            {
                if (Free(map, entityDef, c, within, RotationAt(entityDef, c))) n++;
            }
            return n;
        }

        /// <summary>
        /// Which way what the compound puts on <paramref name="cell"/> is turned. North, the facing everything
        /// else this settlement builds has, for anything but the door and the storage.
        /// <para/>
        /// <b>The door lies in its wall.</b> RimWorld never lets the player turn a door: it reads the wall
        /// either side (<c>Building_Door.DoorRotationAt</c>, recalled from the decompiled source, which is not
        /// reachable from here to quote) and answers North for a door in a wall running east-west and East for
        /// one in a wall running north-south. The compound's door is in the east side, a north-south run, so
        /// it is East. That is stated from the layout and not read off the neighbours, because the door's
        /// blueprint can be placed, and built, before the walls either side of it exist. Nothing in the core
        /// reads a door's facing: a door is one cell and <c>Standable</c> whichever way it is turned.
        /// <para/>
        /// <b>Storage turns its back to the ring.</b> A thing faces <see cref="Rot4.FacingCell"/>, and the
        /// host's models are authored with their front toward -Z at North, which is the opposite way: the
        /// front of a thing is behind its facing. So a hut is turned to <i>face</i> the wall it stands
        /// against, and its front then opens onto the ground outside: East on the west side, South on the
        /// north side, North on the south side and West on the east side.
        /// </summary>
        public Rot4 RotationAt(ThingDef entityDef, IntVec3 cell)
        {
            if (ReferenceEquals(entityDef, CompoundThingDefOf.Door)) return cell == DoorCell ? Rot4.East : Rot4.North;
            if (DwellingUtility.IsDwelling(entityDef)) return DwellingRotationAt(cell);
            if (!ReferenceEquals(entityDef, ConstructionThingDefOf.StorageHut)) return Rot4.North;

            CellRect ring = Ring;
            CellRect footprint = Footprint;
            bool alongside = cell.z >= ring.minZ && cell.z <= ring.maxZ;
            bool aboveOrBelow = cell.x >= ring.minX && cell.x <= ring.maxX;
            if (alongside && cell.x == footprint.minX) return Rot4.East;
            if (alongside && cell.x == footprint.maxX) return Rot4.West;
            if (aboveOrBelow && cell.z == footprint.maxZ) return Rot4.South;
            return Rot4.North; // the south side, and anything that is not one of this compound's sites
        }

        /// <summary>
        /// Marks as home area what <see cref="AutoHomeAreaMaker"/> would mark round the finished
        /// <see cref="Village"/>, before any of it exists. Called only while the map has no home area at all.
        /// <para/>
        /// Without it the first bed to be finished would make a home area of a few cells round itself, and
        /// from that pass on the rest of the compound would lie outside the home area and be refused. This
        /// class does not weaken that rule; the compound declares where home is instead.
        /// </summary>
        public void ClaimHome(Map.Map map)
        {
            Area home = map.areaManager.Home;
            foreach (IntVec3 c in Village.ExpandedBy(AutoHomeAreaMaker.BorderWidth).ClipInsideMap(map).Cells) home[c] = true;
        }

        /// <summary>A dwelling on one of the village's plots south of the street is turned South, so it opens
        /// onto the street; anywhere else, North, like everything else. Only the exact cell a plot is placed
        /// from counts: a dwelling the fallback draw put south of the street was checked turned North.</summary>
        private Rot4 DwellingRotationAt(IntVec3 cell)
        {
            if (cell.z >= StreetZ) return Rot4.North;
            IntVec2 size = DwellingSize;
            int step = size.x + 1;
            int east = FirstEastPlotX, west = FirstWestPlotX;
            int minX;
            if (cell.x >= east) minX = east + (cell.x - east) / step * step;
            else if (cell.x < west + size.x) minX = west - (west + size.x - 1 - cell.x) / step * step;
            else return Rot4.North;
            var plot = new CellRect(minX, StreetZ - 1 - size.z, size.x, size.z);
            return plot.Contains(cell) && PlacementCellFor(plot, Rot4.South, size) == cell ? Rot4.South : Rot4.North;
        }

        private IEnumerable<IntVec3> CellsFor(Map.Map map, ThingDef entityDef)
        {
            if (ReferenceEquals(entityDef, ConstructionThingDefOf.Bed)) return BedCells;
            if (DwellingUtility.IsDwelling(entityDef)) return DwellingCells(map);
            if (ReferenceEquals(entityDef, CompoundThingDefOf.Door)) return new[] { DoorCell };
            if (ReferenceEquals(entityDef, ConstructionThingDefOf.StorageHut)) return StorageCells;
            if (StoneWallMaterials.IsWall(entityDef)) return WallCells;
            return Array.Empty<IntVec3>();
        }

        private static bool Free(Map.Map map, ThingDef entityDef, IntVec3 cell, Area? within, Rot4 rot)
        {
            CellRect rect = GenAdj.OccupiedRect(cell, rot, entityDef.size);
            foreach (IntVec3 c in rect.Cells)
            {
                if (within != null && !within[c]) return false;
                // A house is lived in: none on deep water or rock, which nothing else here needs saying for
                // (a bed's or a wall's cell is the compound's own, already checked open when it was planned).
                if (DwellingUtility.IsDwelling(entityDef) && (!GenGrid.InBounds(c, map) || !map.pathGrid.Walkable(c))) return false;
            }
            if (!GenConstruct.CanPlaceBlueprintAt(entityDef, cell, map, out _, rot)) return false;

            // The ring is the one impassable thing allowed to close round ground (see the class remarks).
            // Storage is held to the same rule as everywhere else.
            return entityDef.passability != Traversability.Impassable
                || StoneWallMaterials.IsWall(entityDef)
                || !SettlementConstructionInitiative.WouldCutOffGround(map, entityDef, cell);
        }

        // ---------------------------------------------------------------------------------------------
        // Planning: how big, and where.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Plans a compound for a settlement of <paramref name="citizens"/> on <paramref name="map"/>, or
        /// returns null when the wall it wants cannot enclose its beds (<see cref="TrySize"/>) or no site
        /// fits. Null is not a failure: the initiative then places as it always did.
        /// <para/>
        /// <b>Where.</b> The same two domains <c>SettlementConstructionInitiative.TryFindPlacementCell</c>
        /// uses, for the same reasons: inside the home area when there is one, otherwise the square round the
        /// road hub, doubled outward until it covers the map. Within the domain it is one seeded draw from the
        /// ambient <see cref="Rand"/> stream over every position where the whole compound fits, so the same
        /// seed on the same map plans the same compound and another seed plans another. A position fits when
        /// every cell of <see cref="Footprint"/> is on the map and walkable (no rock, no deep water) and
        /// every site accepts what the layout puts on it. A map edge or an outcrop therefore moves the
        /// compound; it never bends it.
        /// <para/>
        /// <b>Room for the village first.</b> Within each domain, a position whose whole <see cref="Village"/>
        /// (the street, the work yard and every planned dwelling plot) is on the map and open ground is
        /// preferred over one where only the compound fits; the square round the hub only grows when neither
        /// kind fits in it, so the village never moves away from where its founders stand just to be whole.
        /// Plots that cannot be had are skipped (<see cref="TryFindCell"/>). Either way it is one draw.
        /// </summary>
        public static SettlementCompound? TryPlan(Map.Map map, int citizens, int wallBudget)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!TrySize(citizens, wallBudget, out int width, out int height)) return null;

            var wholeMap = new CellRect(0, 0, map.Size.x, map.Size.z);
            Area home = map.areaManager.Home;
            var open = new OpenGround(map, home.TrueCount > 0 ? home : null);
            if (home.TrueCount > 0) return TryPickPreferringVillage(map, wholeMap, width, height, open);

            var hub = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            for (int radius = Math.Max(1, ConstructionInitiativeTuning.HubPlacementRadius); ; radius *= 2)
            {
                CellRect square = CellRect.CenteredOn(hub, radius).ClipInsideMap(map);
                SettlementCompound? compound = TryPickPreferringVillage(map, square, width, height, open);
                if (compound != null || square == wholeMap) return compound;
            }
        }

        private static SettlementCompound? TryPickPreferringVillage(Map.Map map, CellRect domain, int width, int height, OpenGround open) =>
            TryPick(map, domain, width, height, open, wholeVillage: true) ?? TryPick(map, domain, width, height, open, wholeVillage: false);

        /// <summary>
        /// The interior a ring of <paramref name="wallBudget"/> cells closes round, chosen to hold the most
        /// beds. False when that is fewer beds than there are citizens.
        /// <para/>
        /// <b>The need model is not changed, only spent in one place.</b> The initiative already wants a
        /// wall count per citizen. A ring round a <c>w</c> by <c>h</c> interior is <c>2(w + h) + 4</c> cells, so
        /// that count fixes <c>w + h</c>, and this picks the split with the most room for beds. One ring cell
        /// is the door, so the settlement builds one wall fewer than it wanted and one door. At the founding
        /// band's size the wall count is at <see cref="ConstructionInitiativeTuning.MaxWallShelterCount"/> and
        /// the ring it closes holds a bed for the largest band <c>SettlementTuning.FoundingBandRange</c>
        /// founds. Pinned by <c>SettlementCompoundTests</c> as "every citizen's bed fits inside", never as
        /// those literals.
        /// <para/>
        /// A band too small for its walls to go round its beds gets no compound and places as before. A
        /// settlement whose wall count is already at the ceiling gets the biggest ring it will ever want even
        /// when that no longer holds everyone; the beds that do not fit go where the initiative's own draw
        /// puts them.
        /// </summary>
        internal static bool TrySize(int citizens, int wallBudget, out int width, out int height)
        {
            IntVec2 bed = ConstructionThingDefOf.Bed.size;
            int span = (wallBudget - 4) / 2;
            int beds = 0;
            width = height = 0;
            for (int h = bed.z; h < span; h += bed.z)
            {
                int w = span - h;
                int holds = (w / bed.x) * (h / bed.z);
                if (holds <= beds) continue;
                beds = holds;
                width = w;
                height = h;
            }
            if (beds == 0) return false;
            return beds >= citizens || wallBudget >= ConstructionInitiativeTuning.MaxWallShelterCount;
        }

        private static SettlementCompound? TryPick(Map.Map map, CellRect domain, int width, int height, OpenGround open, bool wholeVillage)
        {
            var fitting = new List<SettlementCompound>();
            for (int z = domain.minZ + 2; z + height + 1 <= domain.maxZ; z++)
            {
                for (int x = domain.minX + 2; x + width + 1 <= domain.maxX; x++)
                {
                    var interior = new CellRect(x, z, width, height);
                    if (!open.IsOpen(interior.ExpandedBy(2))) continue;
                    var candidate = new SettlementCompound(interior);
                    if (wholeVillage && !open.IsOpen(candidate.Village)) continue;
                    if (candidate.SitesAreFree(map)) fitting.Add(candidate);
                }
            }
            return fitting.Count == 0 ? null : fitting[Rand.Range(0, fitting.Count)];
        }

        /// <summary>Which ground a compound may stand on — on the map, walkable (no rock, no deep water), and
        /// inside the home area when one binds — summed once per plan so that asking it of a whole village's
        /// rectangle at every candidate position costs four lookups rather than a thousand.</summary>
        private sealed class OpenGround
        {
            private readonly int sizeX;
            private readonly int sizeZ;
            private readonly int[] blockedBelow; // (sizeX + 1) * (sizeZ + 1) prefix sums of blocked cells

            public OpenGround(Map.Map map, Area? within)
            {
                sizeX = map.Size.x;
                sizeZ = map.Size.z;
                blockedBelow = new int[(sizeX + 1) * (sizeZ + 1)];
                for (int z = 0; z < sizeZ; z++)
                {
                    int row = 0;
                    for (int x = 0; x < sizeX; x++)
                    {
                        var c = new IntVec3(x, 0, z);
                        if (!map.pathGrid.Walkable(c) || (within != null && !within[c])) row++;
                        blockedBelow[(z + 1) * (sizeX + 1) + x + 1] = blockedBelow[z * (sizeX + 1) + x + 1] + row;
                    }
                }
            }

            public bool IsOpen(CellRect rect)
            {
                if (rect.minX < 0 || rect.minZ < 0 || rect.maxX >= sizeX || rect.maxZ >= sizeZ) return false;
                int w = sizeX + 1;
                int blocked = blockedBelow[(rect.maxZ + 1) * w + rect.maxX + 1] - blockedBelow[rect.minZ * w + rect.maxX + 1]
                    - blockedBelow[(rect.maxZ + 1) * w + rect.minX] + blockedBelow[rect.minZ * w + rect.minX];
                return blocked == 0;
            }
        }

        private bool SitesAreFree(Map.Map map)
        {
            foreach (IntVec3 c in BedCells)
            {
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, map, out _)) return false;
            }
            foreach (IntVec3 c in WallCells)
            {
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Wall, c, map, out _)) return false;
            }
            if (!GenConstruct.CanPlaceBlueprintAt(CompoundThingDefOf.Door, DoorCell, map, out _)) return false;
            foreach (IntVec3 c in Footprint.EdgeCells)
            {
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.StorageHut, c, map, out _)) return false;
            }
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref interior.minX, "minX");
            Scribe_Values.Look(ref interior.minZ, "minZ");
            Scribe_Values.Look(ref interior.width, "width");
            Scribe_Values.Look(ref interior.height, "height");
        }
    }
}
