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

        /// <summary>Reachability queries for this map (system 9 / AI); answers via BFS over <see cref="regionGrid"/>.</summary>
        public Reachability reachability = null!;

        /// <summary>The region/region-link graph over this map's passability (RimWorld: <c>Verse.Map.regionGrid</c>);
        /// <see cref="regionAndRoomUpdater"/> keeps it up to date, <see cref="reachability"/> is its consumer.</summary>
        public RegionGrid regionGrid = null!;

        /// <summary>Dirty-cell tracking and incremental rebuilds for <see cref="regionGrid"/> (RimWorld:
        /// <c>Verse.Map.regionAndRoomUpdater</c>) — see that class's own remarks for why its "Room" half is
        /// not built here.</summary>
        public RegionAndRoomUpdater regionAndRoomUpdater = null!;

        /// <summary>This map's A* search (system 9 / AI); one instance, its working arrays reused across searches.</summary>
        public PathFinder pathFinder = null!;

        /// <summary>Every power net on this map, maintained incrementally as transmitters/traders spawn and
        /// despawn (system 16: Building) — see its own remarks for why that beats a full rebuild.</summary>
        public Building.PowerNetManager powerNetManager = null!;

        /// <summary>Rooms and room groups, flood-filled lazily off the edifice grid, and their temperatures
        /// (system 16: Building).</summary>
        public Building.RoomTracker roomTracker = null!;

        /// <summary>Every player-designated Zone on this map, one zone per cell (system 16: Building — zones).</summary>
        public Building.ZoneManager zoneManager = null!;

        /// <summary>Every Area on this map — today, only the home area (system 16: Building — zones).</summary>
        public Building.AreaManager areaManager = null!;

        /// <summary>The weather over this map, its transition to the next, and everything weather does here
        /// (system: weather — <see cref="SimWorld.Weather.WeatherManager"/>). Constructed with this map's
        /// other managers, ticked from <see cref="MapTick"/>, saved by <see cref="ExposeData"/>.</summary>
        public Weather.WeatherManager weatherManager = null!;

        /// <summary>Conditions local to this map, and the way this map reaches the civilization-scale ones
        /// (system: conditions — <see cref="SimWorld.Conditions.GameConditionManager"/>, whose
        /// <c>Parent</c> is the world's). Ticked from <see cref="MapTick"/> ahead of
        /// <see cref="weatherManager"/>, so a condition that ends this tick is already gone before the
        /// outdoor temperature is recomputed from it.</summary>
        public Conditions.GameConditionManager gameConditionManager = null!;

        /// <summary>Outdoor temperature every unroofed/unenclosed cell tracks directly, and every enclosed
        /// room equalises toward (system 16: Building). Still a plain settable value — but on a map that
        /// knows its world tile, <see cref="weatherManager"/> now sets it every tick from that tile's annual
        /// mean, the season, the hour and whatever weather is overhead (<c>Weather.GenTemperature</c>). A map
        /// with no world tile (most tests) keeps whatever its owner set, exactly as before.</summary>
        public float outdoorTemperature = 21f;

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

        /// <summary>
        /// This map's own per-tick systems (weather, power, rooms) — every spawned Thing itself still ticks
        /// through <see cref="Find.TickManager"/>'s own tick lists, not this. A running game reaches this
        /// automatically: <c>Sim.Game.WireTickHooks</c> registers <c>TickMaps</c> as one of
        /// <see cref="Sim.TickManager.PostTickers"/>, which calls this once per live map, exactly as that
        /// list's own doc comment describes ("map post-tick"). Tests call it directly. (This comment used to
        /// say nothing called it automatically; that stopped being true when <c>Sim.Game</c> landed, and
        /// weather depends on it being true — see <see cref="weatherManager"/>.)
        /// </summary>
        public void MapTick()
        {
            // Before the weather, which reads gameConditionManager.AggregateTemperatureOffset() as part of
            // the offset it hands GenTemperature: a heat wave that ends on this tick must already be gone
            // when that number is taken, not one tick later.
            gameConditionManager.GameConditionManagerTick();

            // Then, so the rooms equalise toward the outdoor temperature this tick's weather just set
            // rather than the previous tick's (RimWorld ticks its own weatherManager ahead of the map's
            // temperature work for the same reason).
            weatherManager.WeatherManagerTick();
            powerNetManager.PowerNetManagerTick();
            roomTracker.RoomTrackerTick();
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
            regionGrid = new RegionGrid(this);
            regionAndRoomUpdater = new RegionAndRoomUpdater(this, regionGrid);
            reachability = new Reachability(this);
            pathFinder = new PathFinder(this);
            powerNetManager = new Building.PowerNetManager(this);
            roomTracker = new Building.RoomTracker(this);
            zoneManager = new Building.ZoneManager(this);
            areaManager = new Building.AreaManager(this);
            gameConditionManager = new Conditions.GameConditionManager(this);
            weatherManager = new Weather.WeatherManager(this);
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
            Scribe_Values.Look(ref outdoorTemperature, "outdoorTemperature", 21f);

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

            roomTracker.ExposeTemperatures();
            zoneManager.ExposeData();
            areaManager.ExposeData();
            gameConditionManager.ExposeData();
            weatherManager.ExposeData();

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
