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

        /// <summary>Whether <paramref name="rect"/> touches any ground the compound keeps for itself. The
        /// initiative's own random draw asks this, so an overflow bed never lands on the wall line.</summary>
        public bool Reserves(CellRect rect) => !rect.ClipInsideRect(Footprint).IsEmpty;

        /// <summary>
        /// The next free site for <paramref name="entityDef"/>, in the order the layout fills. False when the
        /// compound has no site left for it, or never had one (it only places beds, walls, its door and
        /// storage). With <paramref name="within"/>, a site must lie wholly inside that area.
        /// </summary>
        public bool TryFindCell(Map.Map map, ThingDef entityDef, Area? within, out IntVec3 cell)
        {
            foreach (IntVec3 c in CellsFor(entityDef))
            {
                if (!Free(map, entityDef, c, within)) continue;
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
            foreach (IntVec3 c in CellsFor(entityDef))
            {
                if (Free(map, entityDef, c, within)) n++;
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
        /// Marks as home area what <see cref="AutoHomeAreaMaker"/> would mark round the finished ring, before
        /// the ring exists. Called only while the map has no home area at all.
        /// <para/>
        /// Without it the first bed to be finished would make a home area of a few cells round itself, and
        /// from that pass on the rest of the compound would lie outside the home area and be refused. This
        /// class does not weaken that rule; the compound declares where home is instead.
        /// </summary>
        public void ClaimHome(Map.Map map)
        {
            Area home = map.areaManager.Home;
            foreach (IntVec3 c in Ring.ExpandedBy(AutoHomeAreaMaker.BorderWidth).ClipInsideMap(map).Cells) home[c] = true;
        }

        private IEnumerable<IntVec3> CellsFor(ThingDef entityDef)
        {
            if (ReferenceEquals(entityDef, ConstructionThingDefOf.Bed)) return BedCells;
            if (ReferenceEquals(entityDef, CompoundThingDefOf.Door)) return new[] { DoorCell };
            if (ReferenceEquals(entityDef, ConstructionThingDefOf.StorageHut)) return StorageCells;
            if (StoneWallMaterials.IsWall(entityDef)) return WallCells;
            return Array.Empty<IntVec3>();
        }

        private static bool Free(Map.Map map, ThingDef entityDef, IntVec3 cell, Area? within)
        {
            if (within != null)
            {
                foreach (IntVec3 c in GenAdj.OccupiedRect(cell, default, entityDef.size).Cells)
                {
                    if (!within[c]) return false;
                }
            }
            if (!GenConstruct.CanPlaceBlueprintAt(entityDef, cell, map, out _)) return false;

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
        /// </summary>
        public static SettlementCompound? TryPlan(Map.Map map, int citizens, int wallBudget)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!TrySize(citizens, wallBudget, out int width, out int height)) return null;

            var wholeMap = new CellRect(0, 0, map.Size.x, map.Size.z);
            Area home = map.areaManager.Home;
            if (home.TrueCount > 0) return TryPick(map, wholeMap, width, height, home);

            var hub = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            for (int radius = Math.Max(1, ConstructionInitiativeTuning.HubPlacementRadius); ; radius *= 2)
            {
                CellRect square = CellRect.CenteredOn(hub, radius).ClipInsideMap(map);
                SettlementCompound? compound = TryPick(map, square, width, height, null);
                if (compound != null || square == wholeMap) return compound;
            }
        }

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

        private static SettlementCompound? TryPick(Map.Map map, CellRect domain, int width, int height, Area? within)
        {
            var fitting = new List<SettlementCompound>();
            for (int z = domain.minZ + 2; z + height + 1 <= domain.maxZ; z++)
            {
                for (int x = domain.minX + 2; x + width + 1 <= domain.maxX; x++)
                {
                    var interior = new CellRect(x, z, width, height);
                    if (!GroundIsOpen(map, interior.ExpandedBy(2), within)) continue;
                    var candidate = new SettlementCompound(interior);
                    if (candidate.SitesAreFree(map)) fitting.Add(candidate);
                }
            }
            return fitting.Count == 0 ? null : fitting[Rand.Range(0, fitting.Count)];
        }

        private static bool GroundIsOpen(Map.Map map, CellRect footprint, Area? within)
        {
            foreach (IntVec3 c in footprint.Cells)
            {
                if (within != null && !within[c]) return false;
                if (!map.pathGrid.Walkable(c)) return false;
            }
            return true;
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
