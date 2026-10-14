using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// <b>The settlement buries its own.</b> Translation: the settlement plays the RimWorld player. RimWorld's
    /// colony deals with its dead because its player does — digs a grave when somebody dies, paints a dumping
    /// stockpile for the raiders — and a god-game settlement has no such player, so it decides for itself the
    /// way <see cref="SettlementConstructionInitiative"/> decides about beds. Without it a body lay where it fell
    /// for ever and cost the people who walked past it a bad moment for ever
    /// (<see cref="Corpse.GiveObservedThought"/>).
    /// <para/>
    /// <b>What "needs a grave" means.</b> Read straight off the map: a humanlike body of the settlement's own
    /// faction (<see cref="PawnUtility.IsColonist"/>) lying on it, with no free grave — standing and empty, or
    /// planned — to take it. <b>Never in advance for the living</b>: a grave is dug for a body that exists, and a
    /// settlement with none lying about places nothing and pays a modulo. The number it digs is the shortfall,
    /// at most <see cref="GraveyardTuning.MaxGravesPerPass"/> per pass.
    /// <para/>
    /// <b>Where.</b> One graveyard: the first grave's site is chosen once (<see cref="GraveyardSiting"/>) near the
    /// settlement and away from its beds, and every later grave is the next slot in the same rows, so the
    /// graveyard is a place and not a scatter. The graves are built by the pipeline every other building goes
    /// through (blueprint, frame, hauled wood, a citizen's work) and carried to by
    /// <see cref="AI.WorkGiver_HaulCorpses"/>.
    /// <para/>
    /// <b>Strangers.</b> A grave refuses them by default, as RimWorld's does. The bodies of raiders and of other
    /// factions' dead go to <b>one dumping stockpile</b>, sited out of the way (away from beds and the hub),
    /// laid the first time one lies on the map and grown by rows as more fall, up to a bound. It takes humanlike
    /// bodies only: an animal's carcass is the butchers' (<see cref="AI.WorkGiver_ButcherCorpse"/>), and a dump
    /// that took it would haul the meat away before anyone cut it.
    /// <para/>
    /// <b>No state of its own</b>, like its siblings: everything is re-derived from what stands on the map each
    /// gated pass, so there is nothing to Scribe and a loaded save resumes with no catch-up. A settlement of a
    /// faction that is not the player's is left alone (it has no player to play).
    /// </summary>
    public static class GraveyardInitiative
    {
        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c>; a
        /// silent no-op with no world running, like its siblings.</summary>
        public static void Tick()
        {
            if (!DueNow) return;
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements)
            {
                Map.Map? map = settlement.InteriorMap;
                if (map != null) Run(settlement, map);
            }
        }

        /// <summary>The gated per-settlement pass against an explicit map, so a test can drive it without paying
        /// for a generated world.</summary>
        public static void TickSettlement(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!DueNow) return;
            Run(settlement, map);
        }

        /// <summary>The label <see cref="Ablation"/> switches this initiative off by, so a run can be compared with
        /// and without a settlement that buries its dead.</summary>
        public const string AblationName = "Graveyard";

        private static bool DueNow => Find.TickManager.TicksGame % GraveyardTuning.IntervalTicks == 0;

        /// <summary>Whether <paramref name="zone"/> is the settlement's dump. Matched by label, as the granary
        /// is (<c>SettlementStockInitiative.GranaryOf</c>); the goods economy asks it to leave the dump out of
        /// what it counts as room to store things.</summary>
        public static bool IsDump(Zone zone) => zone is Zone_Stockpile && zone.label == GraveyardTuning.DumpLabel;

        /// <summary>The settlement's dump on <paramref name="map"/>, or null before it has laid one.</summary>
        public static Zone_Stockpile? DumpOf(Map.Map map)
        {
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (IsDump(zones[i])) return (Zone_Stockpile)zones[i];
            }
            return null;
        }

        /// <summary>
        /// The ungated pass. Public so a test can drive one pass without arranging for the tick number to land on
        /// the interval. Does nothing for a settlement that is not the player's, and nothing at all when no
        /// humanlike body lies on the map.
        /// </summary>
        public static void Run(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (settlement.faction == null || !settlement.faction.def.isPlayer) return;
            if (Ablation.IsDisabled(AblationName)) return; // a measurement control, never read from content

            IReadOnlyList<Thing> bodies = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            if (bodies.Count == 0) return;

            int ownWithoutGrave = 0;
            Corpse? firstOwn = null;
            int strangersWithoutPlace = 0;
            Zone_Stockpile? dump = DumpOf(map);
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!(bodies[i] is Corpse body) || body.InnerPawn == null || !body.InnerPawn.RaceProps.Humanlike) continue;
                if (PawnUtility.IsColonist(body.InnerPawn))
                {
                    ownWithoutGrave++;
                    firstOwn ??= body;
                }
                else if (dump == null || !ReferenceEquals(map.zoneManager.ZoneAt(body.Position), dump)) strangersWithoutPlace++;
            }

            if (firstOwn != null) EnsureGraves(map, ownWithoutGrave, firstOwn);
            if (strangersWithoutPlace > 0) EnsureDump(map, strangersWithoutPlace);
        }

        // ---- graves ----

        /// <summary>Plans graves for the shortfall: bodies of its own lying about, less graves standing empty or
        /// already planned.</summary>
        private static void EnsureGraves(Map.Map map, int bodies, Corpse probe)
        {
            int free = CountFreeGraves(map, probe);
            int shortfall = bodies - free;
            for (int placed = 0; shortfall > 0 && placed < GraveyardTuning.MaxGravesPerPass; shortfall--, placed++)
            {
                if (!TryFindGraveCell(map, out IntVec3 cell)) return;
                ThingDef? blueprint = GenConstruct.BlueprintDefFor(GraveDefOf.Grave);
                if (blueprint == null) return;
                GenSpawn.Spawn(ThingMaker.MakeThing(blueprint), cell, map);
            }
        }

        /// <summary>Graves a body of its own could go into now or soon: built, empty and set to take
        /// <paramref name="ours"/> (one of the settlement's own bodies — a grave an empty-handed player has set to
        /// refuse them is no grave for them), plus blueprints and frames for one.</summary>
        private static int CountFreeGraves(Map.Map map, Corpse ours)
        {
            ThingDef grave = GraveDefOf.Grave;
            int free = 0;

            IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(grave);
            for (int i = 0; i < built.Count; i++)
            {
                if (built[i] is Building_Grave g && g.Accepts(ours)) free++;
            }

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, grave)) free++;
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, grave)) free++;
            }
            return free;
        }

        /// <summary>The cell of the next grave: the next free slot of the graveyard grid
        /// (<see cref="GraveyardSiting.TryFindGraveSlot"/>), beside the graves already there or, with none yet, where
        /// a new graveyard is begun.</summary>
        private static bool TryFindGraveCell(Map.Map map, out IntVec3 cell) => GraveyardSiting.TryFindGraveSlot(map, out cell);

        // ---- the dump ----

        /// <summary>Lays the dump if the settlement has none, and grows it by rows until it has a free cell for
        /// every stranger lying about, up to <see cref="GraveyardTuning.MaxDumpCells"/>.</summary>
        private static void EnsureDump(Map.Map map, int strangers)
        {
            Zone_Stockpile? dump = DumpOf(map);
            if (dump == null)
            {
                if (!TryFindDumpSite(map, out CellRect site)) return;
                dump = NewDump(map);
                AddCells(map, dump, site, site.Area, null);
            }

            int deficit = strangers - EmptyCells(map, dump);
            if (deficit > 0) Grow(map, dump, deficit);
        }

        private static Zone_Stockpile NewDump(Map.Map map)
        {
            var dump = new Zone_Stockpile { label = GraveyardTuning.DumpLabel };
            dump.filter.SetFromPreset(StorageSettingsPreset.DumpingStockpile);
            // Humanlike bodies only: an animal's carcass is meat for the butchers, not a body to put away.
            dump.filter.SetAllow(DefDatabase<ThingCategoryDef>.GetNamed(CorpseDefGenerator.AnimalCorpseCategory), false);
            map.zoneManager.RegisterZone(dump);
            return dump;
        }

        private static bool TryFindDumpSite(Map.Map map, out CellRect site)
        {
            CellRect? graveyard = GraveyardSiting.TryGetBounds(map, out CellRect bounds) ? bounds : (CellRect?)null;
            bool found = GraveyardSiting.TryFindPlot(DumpSizes, map, GraveyardTuning.DumpBedClearance, GraveyardTuning.HubClearance,
                graveyard, GraveyardTuning.DumpGraveClearance, out IntVec3 origin, out int width, out int height);
            site = found ? new CellRect(origin.x, origin.z, width, height) : CellRect.Empty;
            return found;
        }

        /// <summary>The rectangles a dump may be, biggest first: the full size, then smaller for a cramped valley.</summary>
        private static readonly (int width, int height)[] DumpSizes =
        {
            (GraveyardTuning.DumpWidth, GraveyardTuning.DumpHeight),
            (3, 3),
            (3, 2),
            (2, 2),
            (2, 1),
            (1, 1),
        };

        /// <summary>Cells of the dump with nothing lying on them.</summary>
        private static int EmptyCells(Map.Map map, Zone_Stockpile dump)
        {
            int empty = 0;
            IReadOnlyList<IntVec3> cells = dump.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                if (AI.HaulAIUtility.ExistingStackAt(map, cells[i]) == null) empty++;
            }
            return empty;
        }

        /// <summary>Claims up to <paramref name="count"/> cells of <paramref name="area"/> for the dump, row by row, skipping
        /// any that is not clear ground (or, when <paramref name="nearBeds"/> is given, is too near a bed). Never past
        /// <see cref="GraveyardTuning.MaxDumpCells"/>.</summary>
        private static void AddCells(Map.Map map, Zone_Stockpile dump, CellRect area, int count, bool[]? nearBeds)
        {
            int added = 0;
            for (int z = area.minZ; z <= area.maxZ && added < count; z++)
            {
                for (int x = area.minX; x <= area.maxX && added < count; x++)
                {
                    if (dump.CellCount >= GraveyardTuning.MaxDumpCells) return;
                    var cell = new IntVec3(x, 0, z);
                    if (!GenGrid.InBounds(cell, map) || map.zoneManager.ZoneAt(cell) != null) continue;
                    if (!GenGrid.Standable(cell, map) || map.edificeGrid[cell] != null) continue;
                    if (nearBeds != null && nearBeds[map.cellIndices.CellToIndex(cell)]) continue;
                    if (map.zoneManager.AddCell(dump, cell)) added++;
                }
            }
        }

        /// <summary>Grows the dump by <paramref name="count"/> cells in the rows under its own width, clear of the
        /// beds as the first cells were.</summary>
        private static void Grow(Map.Map map, Zone_Stockpile dump, int count)
        {
            if (dump.CellCount == 0) return;
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            IReadOnlyList<IntVec3> cells = dump.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                minX = Math.Min(minX, cells[i].x);
                minZ = Math.Min(minZ, cells[i].z);
                maxX = Math.Max(maxX, cells[i].x);
                maxZ = Math.Max(maxZ, cells[i].z);
            }
            int width = maxX - minX + 1;
            int rows = GraveyardTuning.MaxDumpCells / width + 2;
            AddCells(map, dump, new CellRect(minX, minZ, width, maxZ - minZ + 1 + rows), count,
                GraveyardSiting.CellsNearBeds(map, GraveyardTuning.DumpBedClearance));
        }
    }
}
