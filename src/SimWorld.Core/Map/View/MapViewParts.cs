using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The ground of every cell, as a palette plus one index per cell.
    ///
    /// <para/><b>Why a palette and not a list of defNames.</b> A 200x200 map is 40,000 cells and a generated
    /// interior uses on the order of ten distinct terrains. One <c>string</c> reference per cell would be
    /// 40,000 references to a dozen objects; the palette says that once and leaves an <c>int</c> per cell,
    /// which is a flat array the host can hand almost straight to a texture or an instanced draw. It is also
    /// the shape that makes the terrain layer worth re-pulling wholesale when its one version moves, instead
    /// of needing a per-cell diff nobody would use.
    /// </summary>
    public sealed class TerrainLayer
    {
        internal TerrainLayer(int version, int sizeX, int sizeZ, IReadOnlyList<string> palette, IReadOnlyList<int> cells)
        {
            Version = version;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Palette = palette;
            Cells = cells;
        }

        /// <summary>The <see cref="MapViewTracker.TerrainVersion"/> this layer was read at.</summary>
        public int Version { get; }

        public int SizeX { get; }

        public int SizeZ { get; }

        /// <summary>Distinct terrain defNames, in no particular order; <see cref="Cells"/> indexes into it.</summary>
        public IReadOnlyList<string> Palette { get; }

        /// <summary>One <see cref="Palette"/> index per cell, row-major: cell (x, z) is at <c>z * SizeX + x</c>,
        /// the same layout <c>CellIndices</c> uses, so a host that already computed a cell index does not
        /// convert.</summary>
        public IReadOnlyList<int> Cells { get; }

        /// <summary>The terrain defName at a cell. Convenience over the palette indirection; a host drawing
        /// the whole grid should walk <see cref="Cells"/> and resolve through <see cref="Palette"/> itself
        /// rather than call this 40,000 times.</summary>
        public string TerrainAt(int x, int z) => Palette[Cells[z * SizeX + x]];
    }

    /// <summary>
    /// One kind of roof in a <see cref="RoofLayer"/>'s palette: its name, and the two facts about it a host
    /// needs in order to draw it differently.
    ///
    /// <para/><b>The flags are carried rather than left to the registry</b> because otherwise every host
    /// hardcodes the string <c>"RoofRockThick"</c> to find the overhead mountain, and a host that gets that
    /// wrong draws a solid mountain as open sky. The distinction is the core's — <c>RoofDef.isNatural</c> and
    /// <c>RoofDef.isThickRoof</c> — so it crosses the seam as the core states it.
    /// </summary>
    public readonly struct RoofView
    {
        internal RoofView(string defName, bool isNatural, bool isThickRoof)
        {
            DefName = defName;
            IsNatural = isNatural;
            IsThickRoof = isThickRoof;
        }

        public string DefName { get; }

        /// <summary>Rock the map was generated with, rather than something built.</summary>
        public bool IsNatural { get; }

        /// <summary>Overhead mountain: deep rock that cannot be removed by ordinary means and that the player
        /// most needs to see before building under it.</summary>
        public bool IsThickRoof { get; }
    }

    /// <summary>
    /// Overhead roof per cell, in the same palette-plus-index shape as <see cref="TerrainLayer"/> and for the
    /// same reason.
    ///
    /// <para/>This is here for one concrete reason beyond completeness: a settlement dug into a mountain is
    /// drawn wrong without it, and the thing the player most needs to see is which cells are under an
    /// overhead mountain. A host cannot infer that from terrain or from the things standing on a cell.
    /// </summary>
    public sealed class RoofLayer
    {
        /// <summary>The value <see cref="Cells"/> carries for a cell with no roof. Not a palette entry: an
        /// absent roof is the common case by a wide margin and giving it a sentinel keeps the palette free of
        /// a null a host would have to test for on every lookup.</summary>
        public const int Unroofed = -1;

        internal RoofLayer(int version, int sizeX, int sizeZ, IReadOnlyList<RoofView> palette, IReadOnlyList<int> cells)
        {
            Version = version;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Palette = palette;
            Cells = cells;
        }

        /// <summary>The <see cref="MapViewTracker.RoofVersion"/> this layer was read at.</summary>
        public int Version { get; }

        public int SizeX { get; }

        public int SizeZ { get; }

        /// <summary>Distinct roofs. Empty on a map with no roof anywhere, which is most open ground.</summary>
        public IReadOnlyList<RoofView> Palette { get; }

        /// <summary>One <see cref="Palette"/> index per cell, row-major, or <see cref="Unroofed"/>.</summary>
        public IReadOnlyList<int> Cells { get; }

        public bool IsRoofed(int x, int z) => Cells[z * SizeX + x] != Unroofed;

        /// <summary>The roof defName at a cell, or null where there is none.</summary>
        public string? RoofAt(int x, int z)
        {
            int i = Cells[z * SizeX + x];
            return i == Unroofed ? null : Palette[i].DefName;
        }

        /// <summary>Whether the cell is under overhead mountain — the one roof fact a settlement dug into a
        /// hillside is drawn wrong without, and the reason <see cref="RoofView"/> carries flags rather than
        /// leaving a host to recognise a defName.</summary>
        public bool IsThickRoofed(int x, int z)
        {
            int i = Cells[z * SizeX + x];
            return i != Unroofed && Palette[i].IsThickRoof;
        }
    }

    /// <summary>
    /// One spawned Thing, as much of it as drawing needs: where it is, what it is, which way it faces and how
    /// big its footprint is.
    ///
    /// <para/><b>A struct, not a class, and that is a cost decision.</b> A full capture of a generated
    /// <c>TribalStart</c> interior carries ~13,800 of these. As a class that is 13,800 heap objects to
    /// allocate and collect every time the host re-pulls; as a struct in one list it is one array. The type
    /// stays small and flat for the same reason — everything here is a value or a string, nothing is a
    /// reference into the simulation, and <c>MapViewTests</c> asserts that structurally.
    ///
    /// <para/><b>Pawns are never in here.</b> They are <see cref="PawnView"/> and they are captured whole on
    /// every read; see <see cref="MapViewTracker"/> for why mixing the two would defeat the chunking. A
    /// <i>corpse</i>, on the other hand, is an ordinary Item Thing and does appear here.
    /// </summary>
    public readonly struct ThingView
    {
        internal ThingView(
            int thingId, string defName, string? stuffDefName, IntVec3 position, Rot4 rotation,
            IntVec3 occupiedMin, IntVec2 occupiedSize, int stackCount,
            ThingCategory category, AltitudeLayer altitude, float healthFraction, float plantGrowth,
            float buildProgress)
        {
            ThingId = thingId;
            DefName = defName;
            StuffDefName = stuffDefName;
            Position = position;
            Rotation = rotation;
            OccupiedMin = occupiedMin;
            OccupiedSize = occupiedSize;
            StackCount = stackCount;
            Category = category;
            Altitude = altitude;
            HealthFraction = healthFraction;
            PlantGrowth = plantGrowth;
            BuildProgress = buildProgress;
        }

        /// <summary>The Thing's stable id (<c>Thing.thingIDNumber</c>). A handle, not a reference: it survives
        /// a save and identifies the same Thing across two captures, so a host can keep one drawn object alive
        /// across a chunk re-send instead of rebuilding the chunk's whole scene.</summary>
        public int ThingId { get; }

        /// <summary>The handle for everything else — the key into the host's own <c>defName -&gt; mesh</c>
        /// registry. Not a Def: see <see cref="MapViewSnapshot"/> on why the seam is a string.</summary>
        public string DefName { get; }

        /// <summary>What it is made of (steel, wood, a granite block), or null for a Thing with no material of
        /// its own. The host's registry keys tint or material off this; the core has no colour.</summary>
        public string? StuffDefName { get; }

        /// <summary>Its own cell — the Thing's <c>Position</c>, which for a multi-cell Thing is inside, not at
        /// the corner of, <see cref="OccupiedMin"/>.</summary>
        public IntVec3 Position { get; }

        public Rot4 Rotation { get; }

        /// <summary>Low corner of the footprint it actually occupies, rotation already applied.</summary>
        public IntVec3 OccupiedMin { get; }

        /// <summary>Footprint size in cells, rotation already applied — so an east-facing 1x2 bed reports
        /// (2, 1) here and the host never reimplements <c>GenAdj.OccupiedRect</c>. <c>Bed</c> is 1x2, as in
        /// RimWorld; everything else shipped is (1, 1). See the note on <c>ThingDef.size</c> in
        /// <c>docs/perf/map-view.md</c>.</summary>
        public IntVec2 OccupiedSize { get; }

        /// <summary>How many identical items this stack holds; 1 for buildings and anything unstackable.</summary>
        public int StackCount { get; }

        /// <summary>
        /// Which broad family it belongs to, so the host can split draw layers without a registry lookup per
        /// Thing.
        ///
        /// <para/>This is the core's own enum rather than a parallel one defined here. An enum is a value and
        /// cannot reach a worker, so it costs nothing the seam cares about; and a second vocabulary for the
        /// same distinction is exactly the drift <c>GodCommands</c> avoids by reusing the read model's own
        /// wording on a refusal.
        /// </summary>
        public ThingCategory Category { get; }

        /// <summary>Draw order, straight from <c>ThingDef.altitudeLayer</c> — filth under items under
        /// buildings. The core already had this concept and a host inventing its own would disagree with the
        /// simulation about what is on top of what.</summary>
        public AltitudeLayer Altitude { get; }

        /// <summary>Hit points over max, 0-1. 1 for anything with no hit points of its own, so a host can
        /// shade damage without testing whether damage applies.</summary>
        public float HealthFraction { get; }

        /// <summary>Growth 0-1 for a plant, or -1 for anything that is not one. A seedling and a ripe berry
        /// bush are the same defName and must not be the same mesh scale.</summary>
        public float PlantGrowth { get; }

        /// <summary>True when <see cref="PlantGrowth"/> means something.</summary>
        public bool IsPlant => PlantGrowth >= 0f;

        /// <summary>Construction completion for a <c>Blueprint</c> or <c>Frame</c> (0 = unstarted, 1 =
        /// finished): a <c>Frame</c>'s own <see cref="Building.Frame.PercentComplete"/> clamped to [0, 1], 0
        /// for a <c>Blueprint</c> (no materials delivered yet), and 1 for anything else — a finished building
        /// included, so the host can use this one field regardless of which stage a site is in.</summary>
        public float BuildProgress { get; }
    }

    /// <summary>
    /// One pawn, as much of one as drawing needs — and deliberately no more.
    ///
    /// <para/><b>What is missing is the point.</b> There is no mood here, no skill, no health breakdown, no
    /// relations. Spec §11.3's tiering exists precisely to stop a view from opening every person, and
    /// <c>God/View</c> makes the same refusal one scale up. A host that wants to show a named citizen's inner
    /// life needs a narrower query for that one citizen; it is a different seam from this one, and this one
    /// stays the size of "what do I draw".
    /// </summary>
    public sealed class PawnView
    {
        internal PawnView(
            int thingId, string defName, string? kindDefName, string label,
            IntVec3 position, Rot4 rotation, Gender gender, float bodySize,
            string? lifeStageDefName, string? factionDefName,
            bool downed, bool dead, bool asleep, bool moving, IntVec3 destination,
            string? mentalStateDefName, float healthFraction,
            IReadOnlyList<string> apparelDefNames, string? primaryEquipmentDefName)
        {
            ThingId = thingId;
            DefName = defName;
            KindDefName = kindDefName;
            Label = label;
            Position = position;
            Rotation = rotation;
            Gender = gender;
            BodySize = bodySize;
            LifeStageDefName = lifeStageDefName;
            FactionDefName = factionDefName;
            Downed = downed;
            Dead = dead;
            Asleep = asleep;
            Moving = moving;
            Destination = destination;
            MentalStateDefName = mentalStateDefName;
            HealthFraction = healthFraction;
            ApparelDefNames = apparelDefNames;
            PrimaryEquipmentDefName = primaryEquipmentDefName;
        }

        /// <summary>Stable across captures and across a save — this is what lets a host interpolate a walking
        /// pawn between two reads instead of teleporting it, since nothing else in the snapshot says "this is
        /// the same person you drew last frame".</summary>
        public int ThingId { get; }

        /// <summary>The race def — <c>Human</c>, <c>Husky</c>, <c>Muffalo</c>. The key into the host's body
        /// registry.</summary>
        public string DefName { get; }

        /// <summary>The <c>PawnKindDef</c>, where one applies: which kind of human, which is what a host would
        /// key a default outfit off. Null for a pawn made without one.</summary>
        public string? KindDefName { get; }

        /// <summary>A name to draw over them, already resolved — a person's short name, or an animal's label.</summary>
        public string Label { get; }

        public IntVec3 Position { get; }

        public Rot4 Rotation { get; }

        public Gender Gender { get; }

        /// <summary>Body size including the life-stage factor, so a child and an adult of one race differ.</summary>
        public float BodySize { get; }

        public string? LifeStageDefName { get; }

        /// <summary>Whose they are, or null for a wild animal. The host colours by faction; the core does not
        /// know what a colour is.</summary>
        public string? FactionDefName { get; }

        /// <summary>Cannot act: pain shock, unconsciousness, no working legs. Drawn lying down.</summary>
        public bool Downed { get; }

        /// <summary>
        /// Dead but still the pawn object. A host should expect a dead pawn to be replaced by a
        /// <c>Corpse</c> Thing — which appears as an ordinary <see cref="ThingView"/> in a chunk — rather than
        /// linger here, but the flag is carried because a pawn can be read in the same tick it died.
        /// </summary>
        public bool Dead { get; }

        public bool Asleep { get; }

        /// <summary>Walking right now. Its own flag rather than something the host infers from two positions,
        /// because a pawn standing still for one read and a pawn between two cells look identical otherwise.</summary>
        public bool Moving { get; }

        /// <summary>Where they are walking to, or <c>IntVec3.Invalid</c> when not moving. The final
        /// destination, not the next cell — there is no next-cell accessor on <c>Pawn_PathFollower</c>, so a
        /// host smooths between observed positions and uses this only for intent (an arrow, a debug line).</summary>
        public IntVec3 Destination { get; }

        /// <summary>The mental state they are in, or null. A berserk pawn should not be drawn like a calm one.</summary>
        public string? MentalStateDefName { get; }

        /// <summary>Health 0-1, for whatever the host does with a badly hurt person.</summary>
        public float HealthFraction { get; }

        /// <summary>Worn apparel defNames, in the order worn. The host attaches these to the body rig; which
        /// mesh each one is, and where it hangs, is the host's registry, not the core's business.</summary>
        public IReadOnlyList<string> ApparelDefNames { get; }

        /// <summary>The weapon in hand, or null.</summary>
        public string? PrimaryEquipmentDefName { get; }

        /// <summary>Lying down for any reason — asleep, downed or dead — which is the one thing a host needs
        /// to pick between an upright and a prone pose. Derived rather than carried so the three flags above
        /// stay the truth and this stays a convenience.</summary>
        public bool Lying => Asleep || Downed || Dead;
    }

    /// <summary>
    /// One square region of the map and every non-pawn Thing standing in it, with the version it was read at.
    ///
    /// <para/>The version is the whole point: a host holds it, hands it back, and gets this chunk again only
    /// if something in it moved. See <see cref="MapViewTracker"/> for the rates that make that worth doing.
    /// </summary>
    public sealed class MapViewChunk
    {
        internal MapViewChunk(int index, CellRect rect, int version, IReadOnlyList<ThingView> things)
        {
            Index = index;
            MinX = rect.minX;
            MinZ = rect.minZ;
            Width = rect.width;
            Height = rect.height;
            Version = version;
            Things = things;
        }

        /// <summary>Position of this chunk in the tracker's chunk grid, row-major. The key a host stores its
        /// cached geometry under.</summary>
        public int Index { get; }

        public int MinX { get; }

        public int MinZ { get; }

        /// <summary>Cells across. Short on a map whose size is not a multiple of the chunk edge, which is why
        /// this is carried rather than assumed square.</summary>
        public int Width { get; }

        public int Height { get; }

        public int Version { get; }

        /// <summary>Every non-pawn Thing whose own <c>Position</c> falls in this chunk. A multi-cell Thing
        /// belongs to exactly one chunk — the one holding its position — even where its footprint spills into
        /// the next, so nothing is drawn twice; <see cref="ThingView.OccupiedMin"/> and
        /// <see cref="ThingView.OccupiedSize"/> tell the host about the spill.</summary>
        public IReadOnlyList<ThingView> Things { get; }
    }

    /// <summary>
    /// Everything the host holds between two reads, and the only thing it hands back.
    ///
    /// <para/>It is a value like the rest of the seam: a host may keep it, serialize it, or drop it, and none
    /// of that touches the simulation. Handing back a stale one — or one from another map — is safe and costs
    /// exactly one full resync, which is the failure mode this design wants.
    /// </summary>
    public sealed class MapViewVersions
    {
        internal MapViewVersions(long sessionId, int terrain, int roofs, IReadOnlyList<int> chunks)
        {
            SessionId = sessionId;
            Terrain = terrain;
            Roofs = roofs;
            Chunks = chunks;
        }

        /// <summary>Which map-view session these belong to; see <see cref="MapViewTracker.SessionId"/>.</summary>
        public long SessionId { get; }

        public int Terrain { get; }

        public int Roofs { get; }

        /// <summary>One version per chunk, indexed by <see cref="MapViewChunk.Index"/>.</summary>
        public IReadOnlyList<int> Chunks { get; }
    }
}
