using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>
    /// One local play area (RimWorld: <c>Verse.Map</c>): a grid of terrain and roof plus every Thing spawned
    /// on it. Local map generation (later systems) builds the terrain/roof layout; this module is the grid
    /// machinery and Thing bookkeeping everything else spawns onto.
    /// </summary>
    public sealed class Map : IExposable
    {
        private static int nextMapId;

        public int uniqueID = -1;

        /// <summary>Index of the world tile this local map sits on; -1 until the world module assigns one.</summary>
        public int tile = -1;

        public IntVec3 Size { get; private set; }

        public CellIndices cellIndices = null!;
        public TerrainGrid terrainGrid = null!;
        public RoofGrid roofGrid = null!;
        public ThingGrid thingGrid = null!;
        public EdificeGrid edificeGrid = null!;
        public PathGrid pathGrid = null!;
        public ListerThings listerThings = null!;
        public MapPawns mapPawns = null!;

        /// <summary>Who has claimed what, so two pawns never act on the same target (system 9 / AI).</summary>
        public ReservationManager reservationManager = null!;

        /// <summary>Cached region-reachability for this map (system 9 / AI); rebuilt lazily off <see cref="PathGrid.Version"/>.</summary>
        public Reachability reachability = null!;

        /// <summary>This map's A* search (system 9 / AI); one instance, its working arrays reused across searches.</summary>
        public PathFinder pathFinder = null!;

        /// <summary>Things read from a save but not yet re-spawned; consumed by <see cref="FinalizeLoading"/>.</summary>
        private List<Thing>? loadedThings;

        /// <summary>For Scribe: fields are populated by <see cref="ExposeData"/> before anything else touches this map.</summary>
        private Map()
        {
        }

        public Map(int sizeX, int sizeZ, TerrainDef fill)
        {
            if (fill == null) throw new ArgumentNullException(nameof(fill));
            uniqueID = AllocateMapId();
            InitializeGridsExceptPath(sizeX, sizeZ, fill);
            pathGrid = new PathGrid(this);
            InitializeAIManagers();
        }

        public static int AllocateMapId() => nextMapId++;

        public int Area => Size.x * Size.z;

        public IEnumerable<IntVec3> AllCells
        {
            get
            {
                for (int z = 0; z < Size.z; z++)
                {
                    for (int x = 0; x < Size.x; x++)
                    {
                        yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }

        /// <summary>Advances nothing yet: every spawned Thing ticks through <see cref="Find.TickManager"/> instead.</summary>
        public void MapTick()
        {
        }

        private void InitializeGridsExceptPath(int sizeX, int sizeZ, TerrainDef fill)
        {
            Size = new IntVec3(sizeX, 1, sizeZ);
            cellIndices = new CellIndices(sizeX, sizeZ);
            terrainGrid = new TerrainGrid(this, fill);
            roofGrid = new RoofGrid(this);
            thingGrid = new ThingGrid(this);
            edificeGrid = new EdificeGrid(this);
            listerThings = new ListerThings();
            mapPawns = new MapPawns();
        }

        /// <summary>Constructed after <see cref="pathGrid"/> exists so <see cref="Reachability"/>/<see cref="PathFinder"/>
        /// can size their working arrays off the map's own cell count; <see cref="reservationManager"/> is
        /// fresh state that a load overwrites via <see cref="ExposeData"/> right after this runs.</summary>
        private void InitializeAIManagers()
        {
            reservationManager = new ReservationManager();
            reachability = new Reachability(this);
            pathFinder = new PathFinder(this);
        }

        // ---- Scribe ----

        /// <summary>
        /// Map presence of things is saved as a flat list; each Thing's own <c>ExposeData</c> carries its
        /// position, so no separate per-cell thing grid needs saving. Terrain and roofs are run-length
        /// encoded ("defName*count" tokens in row-major cell order) since most of a map repeats long runs
        /// of the same terrain. Only the visible terrain layer is saved — the under-layer <see cref="TerrainGrid.RemoveTopLayer"/>
        /// can reveal is not yet persisted; nothing depends on that round-tripping yet.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref uniqueID, "uniqueID", -1);
            Scribe_Values.Look(ref tile, "tile", -1);

            int sizeX = Size.x, sizeZ = Size.z;
            Scribe_Values.Look(ref sizeX, "sizeX");
            Scribe_Values.Look(ref sizeZ, "sizeZ");

            string terrainString = Scribe.mode == LoadSaveMode.Saving ? EncodeTerrain() : "";
            Scribe_Values.Look(ref terrainString, "terrain", "");
            string roofString = Scribe.mode == LoadSaveMode.Saving ? EncodeRoof() : "";
            Scribe_Values.Look(ref roofString, "roof", "");

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Rebuild every grid at the saved size before decoding into them. TerrainGrid needs some
                // non-null fill to start from; any loaded TerrainDef works since every cell is about to be
                // overwritten from the decoded string.
                TerrainDef placeholder = FirstLoadedTerrain();
                InitializeGridsExceptPath(sizeX, sizeZ, placeholder);
                DecodeTerrainInto(terrainString ?? "");
                DecodeRoofInto(roofString ?? "");
                pathGrid = new PathGrid(this);
                InitializeAIManagers();
            }

            List<Thing>? things = Scribe.mode == LoadSaveMode.Saving ? new List<Thing>(listerThings.AllThings) : loadedThings;
            Scribe_Collections.Look(ref things, "things", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                loadedThings = things;
            }

            ReservationManager? rm = reservationManager;
            Scribe_Deep.Look(ref rm, "reservationManager");
            reservationManager = rm ?? new ReservationManager();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                FinalizeLoading();
            }
        }

        /// <summary>Re-spawns every loaded Thing at its saved position, without minting new ids.</summary>
        private void FinalizeLoading()
        {
            if (loadedThings == null) return;
            foreach (Thing thing in loadedThings)
            {
                thing.SpawnSetup(this, respawningAfterLoad: true);
            }
            loadedThings = null;
        }

        private static TerrainDef FirstLoadedTerrain()
        {
            foreach (TerrainDef t in Scribe.loader.Defs.For<TerrainDef>().AllDefs) return t;
            throw new InvalidOperationException("No TerrainDef is loaded; cannot rebuild a map's terrain grid.");
        }

        private string EncodeTerrain() =>
            EncodeRunLength(cellIndices.NumGridCells, i => terrainGrid.TerrainAt(cellIndices.IndexToCell(i)).defName);

        private string EncodeRoof() =>
            EncodeRunLength(cellIndices.NumGridCells, i =>
            {
                RoofDef? roof = roofGrid.RoofAt(cellIndices.IndexToCell(i));
                return roof == null ? "null" : roof.defName;
            });

        private static string EncodeRunLength(int n, Func<int, string> tokenAt)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < n)
            {
                string cur = tokenAt(i);
                int count = 1;
                while (i + count < n && tokenAt(i + count) == cur) count++;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(cur).Append('*').Append(count.ToString(CultureInfo.InvariantCulture));
                i += count;
            }
            return sb.ToString();
        }

        private void DecodeTerrainInto(string s)
        {
            int n = cellIndices.NumGridCells;
            int i = 0;
            TerrainDef? last = null;
            foreach ((string name, int count) in ParseRunLength(s))
            {
                TerrainDef? def = Scribe.loader.Defs.GetNamedSilentFail<TerrainDef>(name);
                if (def == null)
                {
                    Scribe.loader.Error("Map terrain: no TerrainDef named '" + name + "'.");
                    def = last;
                }
                if (def == null) continue;
                last = def;
                for (int k = 0; k < count && i < n; k++, i++)
                {
                    terrainGrid.SetTerrainDirect(cellIndices.IndexToCell(i), def);
                }
            }
        }

        private void DecodeRoofInto(string s)
        {
            int n = cellIndices.NumGridCells;
            int i = 0;
            foreach ((string name, int count) in ParseRunLength(s))
            {
                RoofDef? def = null;
                if (name != "null")
                {
                    def = Scribe.loader.Defs.GetNamedSilentFail<RoofDef>(name);
                    if (def == null) Scribe.loader.Error("Map roof: no RoofDef named '" + name + "'.");
                }
                for (int k = 0; k < count && i < n; k++, i++)
                {
                    roofGrid.SetRoof(cellIndices.IndexToCell(i), def);
                }
            }
        }

        private static IEnumerable<(string name, int count)> ParseRunLength(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (string token in s.Split(','))
            {
                int star = token.LastIndexOf('*');
                if (star < 0) continue;
                string name = token.Substring(0, star);
                if (!int.TryParse(token.Substring(star + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) continue;
                yield return (name, count);
            }
        }

        public override string ToString() => "Map" + uniqueID + " (" + Size.x + "x" + Size.z + ")";
    }
}
