using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>The buildings the shelter lane adds, bound in their own <c>[DefOf]</c> class (CLAUDE.md: a new
    /// binding goes in its own class).</summary>
    [DefOf]
    public static class ShelterThingDefOf
    {
        /// <summary>RimWorld 1.0's passive cooler (<c>Buildings_Temperature.xml</c>).</summary>
        public static ThingDef PassiveCooler = null!;

        /// <summary>RimWorld 1.0's campfire, as a hearth (<c>Buildings_Shelter.xml</c>).</summary>
        public static ThingDef Campfire = null!;

        /// <summary>A pit house's dug bank (<see cref="ThingDef.earthBanked"/>).</summary>
        public static ThingDef WallEarth = null!;
    }

    [DefOf]
    public static class ShelterRoofDefOf
    {
        public static RoofDef RoofEarth = null!;
    }

    /// <summary>Tuning for dwellings. RimWorld has no house to port, so this is the port's own figure, and
    /// <c>ShelterRoomTests</c> pins the behaviour it produces rather than the literal.</summary>
    public static class DwellingTuning
    {
        /// <summary>How many dwellings a settlement has under way at once. A house is a few dozen walls; planning
        /// one per household in a single pass would spread the band's first timber over a dozen half-raised rings
        /// and close none of them.</summary>
        public const int MaxUnderway = 2;
    }

    /// <summary>
    /// A household's home: a <see cref="DwellingDef"/> plan laid on one of the village's plots, and the household
    /// that lives in it (shelter lane; translation: citizen-initiated construction). The house itself is the walls,
    /// door and beds standing on the map, raised from blueprints like anything else the settlement builds; this
    /// record is only where the plan lies, which way it faces and whose it is. A household is housed once every
    /// wall, door and bed of the plan stands (<see cref="IsComplete"/>), and its members sleep in the beds inside
    /// it. Anything of it that is lost, to fire or a raid or a quake, is planned again on the next pass
    /// (<see cref="DwellingPlanner"/>).
    /// </summary>
    public sealed class Dwelling : IExposable
    {
        public DwellingDef def = null!;

        /// <summary>The plan's footprint on the map.</summary>
        public CellRect footprint;

        /// <summary>North: the plan is laid as drawn, its front on the south edge. South: turned half round, its
        /// front on the north edge. Either way the front faces the street.</summary>
        public Rot4 rot = Rot4.North;

        /// <summary>The <see cref="Family"/> that lives here, or <see cref="Pawn_RelationsTracker.None"/>. Set only
        /// by <see cref="DwellingUtility.AssignHomes"/>.</summary>
        public int householdId = Pawn_RelationsTracker.None;

        [Unsaved] private HashSet<IntVec3>? interior;

        /// <summary>For Scribe.</summary>
        public Dwelling()
        {
        }

        public Dwelling(DwellingDef def, CellRect footprint, Rot4 rot)
        {
            this.def = def;
            this.footprint = footprint;
            this.rot = rot;
        }

        /// <summary>The map cell of the plan's (<paramref name="x"/>, <paramref name="z"/>), z = 0 the front row.</summary>
        public IntVec3 CellAt(int x, int z) => rot == Rot4.South
            ? new IntVec3(footprint.maxX - x, 0, footprint.maxZ - z)
            : new IntVec3(footprint.minX + x, 0, footprint.minZ + z);

        /// <summary>The floor inside the walls: every cell reachable from a bed without crossing a wall, a door or
        /// open ground. This is the household's room (or rooms joined by an inner door are not; only the
        /// bedroom's).</summary>
        public HashSet<IntVec3> Interior => interior ??= FloodInterior();

        public bool InteriorContains(IntVec3 c) => Interior.Contains(c);

        /// <summary>Everything the plan has builders raise, each with what it is, where it goes and which way it
        /// faces. The material is the caller's to choose (<see cref="DwellingPlanner"/>), so a part lost and planned
        /// again is planned in whatever the settlement has by then.</summary>
        public IEnumerable<(DwellingPartKind Kind, IntVec3 Cell, Rot4 Rot)> Parts
        {
            get
            {
                IntVec2 size = def.Size;
                for (int z = 0; z < size.z; z++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        DwellingPartKind kind = def.PartAt(x, z);
                        switch (kind)
                        {
                            case DwellingPartKind.Wall:
                                yield return (kind, CellAt(x, z), Rot4.North);
                                break;
                            case DwellingPartKind.Door:
                                yield return (kind, CellAt(x, z), DoorRotation(x, z));
                                break;
                            case DwellingPartKind.Bed:
                                yield return (kind, CellAt(x, z), rot == Rot4.South ? Rot4.South : Rot4.North);
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>The cells kept for a device of <paramref name="kind"/> (<see cref="DwellingPartKind.HearthSpot"/>
        /// or <see cref="DwellingPartKind.CoolerSpot"/>).</summary>
        public IEnumerable<IntVec3> Spots(DwellingPartKind kind)
        {
            IntVec2 size = def.Size;
            for (int z = 0; z < size.z; z++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    if (def.PartAt(x, z) == kind) yield return CellAt(x, z);
                }
            }
        }

        /// <summary>Whether <paramref name="kind"/>'s part stands built at <paramref name="cell"/>: a finished wall
        /// of any material, a finished door, or a bed placed there. A blueprint or frame is not built.</summary>
        public static bool IsBuilt(Map.Map map, DwellingPartKind kind, IntVec3 cell)
        {
            if (!GenGrid.InBounds(cell, map)) return false;
            switch (kind)
            {
                case DwellingPartKind.Wall:
                {
                    Thing? edifice = map.edificeGrid[cell];
                    return edifice != null && edifice.def.category == ThingCategory.Building
                        && edifice.def.Fillage == FillCategory.Full && !(edifice is Door);
                }
                case DwellingPartKind.Door:
                    return map.edificeGrid[cell] is Door;
                case DwellingPartKind.Bed:
                    return BedAt(map, cell) != null;
                default:
                    return true;
            }
        }

        /// <summary>The bed placed on <paramref name="cell"/>, if any.</summary>
        public static Thing? BedAt(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (ReferenceEquals(here[i].def, ConstructionThingDefOf.Bed) && here[i].Position == cell) return here[i];
            }
            return null;
        }

        /// <summary>Whether every wall, door and bed of the plan stands: the household can move in.</summary>
        public bool IsComplete(Map.Map map)
        {
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in Parts)
            {
                if (!IsBuilt(map, kind, cell)) return false;
            }
            return true;
        }

        /// <summary>The beds standing in this house, in the plan's order.</summary>
        public List<Thing> Beds(Map.Map map)
        {
            var beds = new List<Thing>();
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in Parts)
            {
                if (kind != DwellingPartKind.Bed) continue;
                Thing? bed = BedAt(map, cell);
                if (bed != null) beds.Add(bed);
            }
            return beds;
        }

        /// <summary>RimWorld turns a door to lie in its wall: North in a wall running across the plan, East in one
        /// running front to back (see <see cref="SettlementCompound.RotationAt"/>). Read off the plan, not the map,
        /// so a door planned before its walls stand is turned the same.</summary>
        private Rot4 DoorRotation(int x, int z)
        {
            bool across = IsSolid(def.PartAt(x - 1, z)) && IsSolid(def.PartAt(x + 1, z));
            return across ? Rot4.North : Rot4.East;
        }

        private static bool IsSolid(DwellingPartKind k) => k == DwellingPartKind.Wall || k == DwellingPartKind.Door;

        private HashSet<IntVec3> FloodInterior()
        {
            var cells = new HashSet<IntVec3>();
            IntVec2 size = def.Size;
            var seen = new bool[size.x, size.z];
            var queue = new Queue<(int, int)>();
            for (int x = 0; x < size.x; x++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    if (def.PartAt(x, z) != DwellingPartKind.Bed) continue;
                    seen[x, z] = true;
                    queue.Enqueue((x, z));
                }
            }
            while (queue.Count > 0)
            {
                (int x, int z) = queue.Dequeue();
                cells.Add(CellAt(x, z));
                foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= size.x || nz >= size.z || seen[nx, nz]) continue;
                    DwellingPartKind k = def.PartAt(nx, nz);
                    if (IsSolid(k) || k == DwellingPartKind.Outside) continue;
                    seen[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }
            return cells;
        }

        public void ExposeData()
        {
            DwellingDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref footprint.minX, "minX");
            Scribe_Values.Look(ref footprint.minZ, "minZ");
            Scribe_Values.Look(ref footprint.width, "width");
            Scribe_Values.Look(ref footprint.height, "height");
            int r = rot.AsInt;
            Scribe_Values.Look(ref r, "rot");
            rot = new Rot4(r);
            Scribe_Values.Look(ref householdId, "householdId", Pawn_RelationsTracker.None);
            interior = null;
        }
    }

    /// <summary>
    /// Who lives where, how many homes a settlement still wants, and which bed a resident sleeps in. Everything
    /// here is a read of the map and the settlement except <see cref="AssignHomes"/>, the one writer of
    /// <see cref="Dwelling.householdId"/>, and nothing draws from <see cref="Rand"/>: households are taken in
    /// <see cref="Family.id"/> order and dwellings in the order they were planned.
    /// </summary>
    public static class DwellingUtility
    {
        private static List<DwellingDef>? ladder;

        /// <summary>The ladder, lowest rung first.</summary>
        public static IReadOnlyList<DwellingDef> Ladder
        {
            get
            {
                if (ladder == null || ladder.Count != DefDatabase<DwellingDef>.DefCount)
                {
                    ladder = new List<DwellingDef>(DefDatabase<DwellingDef>.AllDefsListForReading);
                    ladder.Sort((a, b) => a.rung != b.rung ? a.rung.CompareTo(b.rung) : string.CompareOrdinal(a.defName, b.defName));
                }
                return ladder;
            }
        }

        /// <summary>The newest rung the civilization knows how to build, or null before it knows any: a founding
        /// band sleeps in its dormitory until it has learned to raise a windbreak.</summary>
        public static DwellingDef? PreferredDwellingDef()
        {
            IReadOnlyList<DwellingDef> rungs = Ladder;
            for (int i = rungs.Count - 1; i >= 0; i--)
            {
                if (rungs[i].IsResearchFinished) return rungs[i];
            }
            return null;
        }

        /// <summary>The size of the village's plots: wide and deep enough for every rung, so a plot laid out in
        /// the first age still takes a house of the last.</summary>
        public static IntVec2 PlotSize
        {
            get
            {
                int x = 1, z = 1;
                IReadOnlyList<DwellingDef> rungs = Ladder;
                for (int i = 0; i < rungs.Count; i++)
                {
                    x = Math.Max(x, rungs[i].Size.x);
                    z = Math.Max(z, rungs[i].Size.z);
                }
                return new IntVec2(x, z);
            }
        }

        /// <summary>Every dwelling planned on <paramref name="map"/>, in the order planned. None without a
        /// compound: the village's plots are laid along its street.</summary>
        public static IReadOnlyList<Dwelling> DwellingsOn(Map.Map map) =>
            (IReadOnlyList<Dwelling>?)map.settlementCompound?.dwellings ?? Array.Empty<Dwelling>();

        /// <summary>The dwelling whose footprint holds <paramref name="cell"/>, if any.</summary>
        public static Dwelling? DwellingAt(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Dwelling> all = DwellingsOn(map);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].footprint.Contains(cell)) return all[i];
            }
            return null;
        }

        /// <summary>The roof builders lay on <paramref name="cell"/>: the dwelling's own when one stands there and
        /// names one, otherwise RimWorld's constructed roof.</summary>
        public static RoofDef RoofFor(Map.Map map, IntVec3 cell) => DwellingAt(map, cell)?.def.roofDef ?? RoofDefOf.RoofConstructed;

        /// <summary>The households of <paramref name="settlement"/>'s living citizens, by id, with their member
        /// counts. A citizen with no household is not one.</summary>
        public static SortedDictionary<int, int> Households(Settlement settlement)
        {
            var members = new SortedDictionary<int, int>();
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead) continue;
                int family = p.relations.familyId;
                if (family == Pawn_RelationsTracker.None) continue;
                members[family] = members.TryGetValue(family, out int n) ? n + 1 : 1;
            }
            return members;
        }

        /// <summary>
        /// Moves households in and out. A dwelling whose household has no living citizen left is vacated; then
        /// every finished, vacant dwelling, oldest first, goes to the lowest-id household that has none. A house
        /// still going up has nobody in it yet; one that has lost a wall since keeps its household while it is
        /// put right.
        /// </summary>
        public static void AssignHomes(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            SortedDictionary<int, int> households = Households(settlement);
            IReadOnlyList<Dwelling> dwellings = DwellingsOn(map);
            var housed = new HashSet<int>();
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None) continue;
                if (!households.ContainsKey(d.householdId) || !housed.Add(d.householdId)) d.householdId = Pawn_RelationsTracker.None;
            }

            var waiting = new List<int>();
            foreach (int household in households.Keys)
            {
                if (!housed.Contains(household)) waiting.Add(household);
            }

            int next = 0;
            for (int i = 0; i < dwellings.Count && next < waiting.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None && d.IsComplete(map)) d.householdId = waiting[next++];
            }
        }

        /// <summary>How many of <paramref name="settlement"/>'s citizens have a bed in their own household's
        /// finished house: per household that has one, its members up to the beds standing in it. The rest sleep
        /// in the dormitory, and the dormitory's bed need counts only them.</summary>
        public static int HousedCitizens(Settlement settlement, Map.Map map)
        {
            SortedDictionary<int, int> households = Households(settlement);
            int housed = 0;
            IReadOnlyList<Dwelling> dwellings = DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (!households.TryGetValue(d.householdId, out int members) || !d.IsComplete(map)) continue;
                housed += Math.Min(members, d.Beds(map).Count);
            }
            return housed;
        }

        /// <summary>Dwellings planned, finished or not: a household with a house going up does not want another.</summary>
        public static int CountBuiltOrPlanned(Map.Map map) => DwellingsOn(map).Count;

        /// <summary>Dwellings not yet finished.</summary>
        public static int CountUnderway(Map.Map map)
        {
            int n = 0;
            IReadOnlyList<Dwelling> dwellings = DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                if (!dwellings[i].IsComplete(map)) n++;
            }
            return n;
        }

        /// <summary>Whether <paramref name="bed"/> stands inside a house: a household's, not the dormitory's.</summary>
        public static bool IsDwellingBed(Thing bed) => bed.Map != null && DwellingOfBed(bed) != null;

        /// <summary>The house whose room <paramref name="bed"/> stands in, if any.</summary>
        public static Dwelling? DwellingOfBed(Thing bed)
        {
            Map.Map? map = bed.Map;
            if (map == null) return null;
            Dwelling? d = DwellingAt(map, bed.Position);
            return d != null && d.InteriorContains(bed.Position) ? d : null;
        }

        /// <summary>Whether <paramref name="bed"/> is in <paramref name="pawn"/>'s own household's house.</summary>
        public static bool IsOwnHomeBed(Pawn pawn, Thing bed)
        {
            int family = pawn.relations?.familyId ?? Pawn_RelationsTracker.None;
            if (family == Pawn_RelationsTracker.None) return false;
            Dwelling? d = DwellingOfBed(bed);
            return d != null && d.householdId == family;
        }

        /// <summary>
        /// A bed in <paramref name="pawn"/>'s own household's house that they can reserve and walk to, or null, and
        /// the pawn looks for a dormitory bed as before (<see cref="RestUtility.FindBedFor"/>). RimWorld's
        /// <c>RestUtility.FindBedFor</c> tries the pawn's owned bed first in the same way; the household owns the
        /// beds here, not the pawn, so any free one of them will do.
        /// </summary>
        public static Thing? FindDwellingBedFor(Pawn pawn) => FindDwellingBedFor(pawn, pawn);

        /// <summary>A bed in <paramref name="sleeper"/>'s own household's house that <paramref name="traveler"/>
        /// can reserve and reach, with nobody else lying in it, or null (RimWorld: <c>RestUtility.FindBedFor</c>'s
        /// owned-bed check, asked for the sleeper and walked by the traveler, as a rescuer carries a patient to the
        /// patient's own bed).</summary>
        public static Thing? FindDwellingBedFor(Pawn sleeper, Pawn traveler)
        {
            Map.Map? map = sleeper.Map;
            int family = sleeper.relations.familyId;
            if (map == null || traveler.Map != map || family == Pawn_RelationsTracker.None) return null;

            IReadOnlyList<Dwelling> dwellings = DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId != family) continue;
                List<Thing> beds = d.Beds(map);
                for (int b = 0; b < beds.Count; b++)
                {
                    Thing bed = beds[b];
                    if (!map.reservationManager.CanReserve(traveler, bed)) continue;
                    if (RestUtility.OccupiedByAnother(bed, sleeper)) continue;
                    if (!Reachability.CanReach(traveler, new LocalTargetInfo(bed.Position), PathEndMode.OnCell)) continue;
                    return bed;
                }
            }
            return null;
        }

        /// <summary>The material a part of <paramref name="dwelling"/> is planned in on <paramref name="map"/> now:
        /// its wall (the rung's own, or the era's), and for anything made from stuff, the stuff.</summary>
        public static (ThingDef Def, ThingDef? Stuff) PartDef(Map.Map map, Dwelling dwelling, DwellingPartKind kind)
        {
            switch (kind)
            {
                case DwellingPartKind.Wall:
                {
                    ThingDef wall = dwelling.def.wallDef ?? StoneWallMaterials.PreferredWallDef(map);
                    ThingDef? stuff = null;
                    // Only the rung's own stuff, and only when it is on hand: a plain wall otherwise stays wood, so
                    // stone never goes up round Masonry's gate (StoneWallMaterials decides stone walls).
                    if (wall.MadeFromStuff && dwelling.def.wallStuff != null
                        && ReferenceEquals(GenStuff.AffordableStuffFor(map, wall, dwelling.def.wallStuff), dwelling.def.wallStuff))
                    {
                        stuff = dwelling.def.wallStuff;
                    }
                    return (wall, stuff);
                }
                case DwellingPartKind.Door:
                    return (CompoundThingDefOf.Door, GenStuff.AffordableStuffFor(map, CompoundThingDefOf.Door));
                case DwellingPartKind.Bed:
                    return (ConstructionThingDefOf.Bed, GenStuff.AffordableStuffFor(map, ConstructionThingDefOf.Bed));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "only walls, doors and beds are built from the plan");
            }
        }
    }
}
