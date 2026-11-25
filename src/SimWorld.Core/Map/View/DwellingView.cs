using SimWorld.Building;
using SimWorld.Pawns;

namespace SimWorld.Map.View
{
    /// <summary>
    /// One household's house as the plan lies on the map (REQ 007, ask 1): which rung it is, where it stands,
    /// whether it is finished, how many beds it holds, whose it is and how warm it is inside.
    ///
    /// <para/><b>Why the house crosses as its own record.</b> Since dwellings became rooms of real walls, a door and
    /// beds (<see cref="Dwelling"/>), every part of a house is an ordinary <see cref="ThingView"/>, and nothing in a
    /// chunk says which walls make one house or whose house it is. That is a fact about the plan, held by the
    /// <see cref="SettlementCompound"/>, not by any Thing, so it is carried here beside the chunks.
    ///
    /// <para/><b>Re-read on every capture, like the pawns.</b> A settlement has tens of houses, not thousands, and
    /// what changes about one (a bed placed, a household moving in, the air inside warming) is a field write with
    /// no grid call to hook. Versioning them would cost more than reading twenty records, and a list that is always
    /// complete keeps the snapshot and every delta saying the same thing.
    /// </summary>
    public sealed class DwellingView
    {
        internal DwellingView(
            string defName, string label, int rung, IntVec3 occupiedMin, IntVec2 occupiedSize, Rot4 rotation,
            bool isComplete, int bedsStanding, int bedsPlanned, int? householdId, string? householdName,
            float? indoorTemperature, string? roofDefName,
            int dwellingId, string? eraDefName, int builtTick, float ageYears, float condition, bool isObsolete,
            bool isHeritage, string renewal, int? replacedByDwellingId, int? replacesDwellingId)
        {
            DwellingId = dwellingId;
            EraDefName = eraDefName;
            BuiltTick = builtTick;
            AgeYears = ageYears;
            Condition = condition;
            IsObsolete = isObsolete;
            IsHeritage = isHeritage;
            Renewal = renewal;
            ReplacedByDwellingId = replacedByDwellingId;
            ReplacesDwellingId = replacesDwellingId;
            DefName = defName;
            Label = label;
            Rung = rung;
            OccupiedMin = occupiedMin;
            OccupiedSize = occupiedSize;
            Rotation = rotation;
            IsComplete = isComplete;
            BedsStanding = bedsStanding;
            BedsPlanned = bedsPlanned;
            HouseholdId = householdId;
            HouseholdName = householdName;
            IndoorTemperature = indoorTemperature;
            RoofDefName = roofDefName;
        }

        /// <summary>The rung, as a <c>DwellingDef</c> defName (<c>House_Paleo</c>, <c>House_Neo</c>, ...).</summary>
        public string DefName { get; }

        /// <summary>The rung's own label ("windbreak hut"), already resolved, for the inspector's title.</summary>
        public string Label { get; }

        /// <summary>Its place on the dwelling ladder, lowest first.</summary>
        public int Rung { get; }

        /// <summary>Low corner of the plot, the same shape <see cref="ThingView.OccupiedMin"/> has.</summary>
        public IntVec3 OccupiedMin { get; }

        /// <summary>Plot size in cells. A plan is laid turned North or South only, so this is the plan's own
        /// width (along the street) and depth whichever way it faces.</summary>
        public IntVec2 OccupiedSize { get; }

        /// <summary>North: laid as drawn, its front (the door side) on the south edge. South: turned half round,
        /// its front on the north edge.</summary>
        public Rot4 Rotation { get; }

        /// <summary>Every wall, door and bed of the plan stands (<see cref="Dwelling.IsComplete"/>). False while
        /// it is still going up, and again while a lost part is being put right.</summary>
        public bool IsComplete { get; }

        /// <summary>Beds placed in the house now.</summary>
        public int BedsStanding { get; }

        /// <summary>Beds the plan holds (<see cref="DwellingDef.BedCount"/>).</summary>
        public int BedsPlanned { get; }

        /// <summary>The household living here (<c>FamilyManager</c>'s family id, the same id
        /// <c>CitizenLine.HouseholdId</c> carries), or null while the house is vacant.</summary>
        public int? HouseholdId { get; }

        /// <summary>The household's name exactly as <c>CitizenLine.HouseholdName</c> gives it, or null while
        /// vacant.</summary>
        public string? HouseholdName { get; }

        /// <summary>The air temperature in the bedroom, °C, from the room temperature model
        /// (<see cref="RoomTracker"/>), or null when that room is not closed against the outdoors — a house still
        /// going up, a door frame with no door, a roof not yet laid — and so simply has the outdoor air.</summary>
        public float? IndoorTemperature { get; }

        /// <summary>The roof the rung is roofed with (<c>RoofEarth</c> on a pit house), or null for RimWorld's
        /// constructed roof. What is actually laid is in <see cref="MapViewSnapshot.Roofs"/>.</summary>
        public string? RoofDefName { get; }

        // ---- renewal (docs/design/era-progression.md §7.8) ----

        /// <summary>This house's handle, unique on the map and never reused. <see cref="ReplacedByDwellingId"/> and
        /// <see cref="ReplacesDwellingId"/> name houses by it. A house is a plan, not one Thing, so its handle is its
        /// own id rather than a thing id. 0 for a house planned this very pass, until the next.</summary>
        public int DwellingId { get; }

        /// <summary>The era of the house's form (<c>DwellingDef.era</c>), as an <c>EraDef</c> defName.</summary>
        public string? EraDefName { get; }

        /// <summary>The game tick every part of the house first stood, or -1 while it is still going up.</summary>
        public int BuiltTick { get; }

        /// <summary>Years since <see cref="BuiltTick"/>; 0 while it is still going up.</summary>
        public float AgeYears { get; }

        /// <summary>Its walls' hit points over their most, 0-1 (1 with no wall standing).</summary>
        public float Condition { get; }

        /// <summary>The form is behind the era the town is in, so renewal will replace it in time.</summary>
        public bool IsObsolete { get; }

        /// <summary>Kept on purpose: repaired in its own form, never renewed.</summary>
        public bool IsHeritage { get; }

        /// <summary>What renewal is doing to it: <c>None</c>, <c>Planned</c> (its replacement is laid out),
        /// <c>Underway</c> (its replacement is going up and its household still lives here) or <c>Replaced</c> (its
        /// household has moved out and it is coming down).</summary>
        public string Renewal { get; }

        /// <summary>The house being built to replace this one, by <see cref="DwellingId"/>, or null.</summary>
        public int? ReplacedByDwellingId { get; }

        /// <summary>The house this one is being built to replace, by <see cref="DwellingId"/>, or null; cleared once
        /// the old one is gone. With <see cref="ReplacedByDwellingId"/>, what the host cross-fades between.</summary>
        public int? ReplacesDwellingId { get; }

        internal static DwellingView Of(SimWorld.Map.Map map, Dwelling d)
        {
            int? household = d.householdId == Pawn_RelationsTracker.None ? (int?)null : d.householdId;
            return new DwellingView(
                d.def.defName,
                d.def.label ?? d.def.defName,
                d.def.rung,
                new IntVec3(d.footprint.minX, 0, d.footprint.minZ),
                new IntVec2(d.footprint.width, d.footprint.height),
                d.rot,
                d.IsComplete(map),
                d.Beds(map).Count,
                d.def.BedCount,
                household,
                household == null ? null : God.View.CitizenLine.HouseholdNameOf(household.Value),
                IndoorTemperatureOf(map, d),
                d.def.roofDef?.defName,
                d.id,
                d.def.era?.defName,
                d.completedTick,
                d.completedTick < 0 ? 0f : (float)(Sim.Find.TickManager.TicksGame - d.completedTick) / Sim.GenDate.TicksPerYear,
                RenewalInitiative.WallCondition(map, d),
                RenewalInitiative.IsObsolete(d.def),
                d.isHeritage,
                RenewalInitiative.StageOf(map, d).ToString(),
                d.replacedById == 0 ? (int?)null : d.replacedById,
                d.replacesId == 0 ? (int?)null : d.replacesId);
        }

        /// <summary>The room at the plan's first bed, the cell <see cref="Dwelling.Interior"/> floods from.</summary>
        private static float? IndoorTemperatureOf(SimWorld.Map.Map map, Dwelling d)
        {
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in d.Parts)
            {
                if (kind != DwellingPartKind.Bed) continue;
                return CellTemperatureView.RoomTemperatureAt(map, cell);
            }
            return null;
        }
    }

    /// <summary>
    /// How warm one cell is, inside and out (REQ 007, ask 2): the temperature of the room it is in, and the outdoor
    /// temperature beside it, so the inspector can say "18 C inside, 34 C outside" on a house in a heat wave. That
    /// gap is what the shelter ladder buys, and without this the player has no way to read it.
    ///
    /// <para/>A query for one cell (<see cref="MapViewSnapshot.TemperatureAt(IntVec3)"/>) rather than a per-cell
    /// layer: a 40,000-float grid re-sent as the air drifts would cost every frame for what an inspector reads on
    /// a click. The houses carry their own (<see cref="DwellingView.IndoorTemperature"/>), and the outdoor air
    /// rides on every snapshot and delta.
    /// </summary>
    public sealed class CellTemperatureView
    {
        internal CellTemperatureView(
            bool hasMap, string? absenceReason, IntVec3 cell, float? roomTemperature, int roomCellCount,
            float outdoorTemperature)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            Cell = cell;
            RoomTemperature = roomTemperature;
            RoomCellCount = roomCellCount;
            OutdoorTemperature = outdoorTemperature;
        }

        /// <summary>False when there is no interior to read; <see cref="AbsenceReason"/> says why.</summary>
        public bool HasMap { get; }

        public string? AbsenceReason { get; }

        public IntVec3 Cell { get; }

        /// <summary>The room's air, °C, or null for a cell under the open sky or in a room open to it (one
        /// unroofed cell, or a doorway to the outside, is enough), which simply has the outdoor air.</summary>
        public float? RoomTemperature { get; }

        /// <summary>True when the cell is in a room closed against the outdoors.</summary>
        public bool Indoors => RoomTemperature != null;

        /// <summary>Cells in that closed room, or 0 outdoors: the size of the space the temperature belongs to.</summary>
        public int RoomCellCount { get; }

        /// <summary>The map's outdoor air, °C.</summary>
        public float OutdoorTemperature { get; }

        /// <summary>The temperature anything standing on the cell feels: the room's, or the outdoor air.</summary>
        public float Temperature => RoomTemperature ?? OutdoorTemperature;

        internal static CellTemperatureView Of(SimWorld.Map.Map map, IntVec3 cell)
        {
            if (!GenGrid.InBounds(cell, map))
            {
                return new CellTemperatureView(true, null, cell, null, 0, map.outdoorTemperature);
            }
            RoomGroup? group = ClosedGroupAt(map, cell);
            return new CellTemperatureView(
                true, null, cell, group?.Temperature, group?.CellCount ?? 0, map.outdoorTemperature);
        }

        internal static CellTemperatureView Absent(string reason) =>
            new CellTemperatureView(false, reason, IntVec3.Invalid, null, 0, 0f);

        internal static float? RoomTemperatureAt(SimWorld.Map.Map map, IntVec3 cell) =>
            GenGrid.InBounds(cell, map) ? ClosedGroupAt(map, cell)?.Temperature : null;

        private static RoomGroup? ClosedGroupAt(SimWorld.Map.Map map, IntVec3 cell)
        {
            RoomGroup? group = map.roomTracker.RoomAt(cell)?.Group;
            return group == null || group.UsesOutdoorTemperature ? null : group;
        }
    }
}
