using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// The cell a new <c>StorageHut</c> is sited around (<see cref="SettlementConstructionInitiative"/>): where
    /// the settlement already keeps its things. Translation, like the hut itself. RimWorld has no hut, and
    /// its stockpile zones go wherever the player paints them, so there is no "where does storage go" rule to
    /// port. This one says what a player laying out a village would: storage sits together, beside the store
    /// that is already there.
    /// <para/>
    /// <b>Taken in order, first that exists wins.</b>
    /// <list type="number">
    /// <item><b>The huts the settlement already has</b>, built, framed or planned: their mean cell. The first
    /// hut decides the neighbourhood and every later one gathers round it, so a settlement's storage is one
    /// cluster however many passes it takes to raise.</item>
    /// <item><b>Its stockpile zones</b>, the granary included: the mean of their cells. The hut stands beside the
    /// goods it is "for".</item>
    /// <item><b>The centre of its home area</b>, once there is one.</item>
    /// <item><b>The road hub</b>, which is where the founding band stands before anything is built.</item>
    /// </list>
    /// The anchor is only the middle of a search square (see
    /// <see cref="ConstructionInitiativeTuning.StorageClusterRadius"/>). It need not be buildable, and it keeps
    /// no state: it is re-derived from the map on every call, so a saved and reloaded map sites exactly as one
    /// that was never saved, and nothing here needs a Scribe entry.
    /// </summary>
    internal static class StorageAnchor
    {
        public static IntVec3 For(Map.Map map)
        {
            if (TryMean(HutCells(map), out IntVec3 at)) return at;
            if (TryMean(StockpileCells(map), out at)) return at;
            if (TryMean(map.areaManager.Home.ActiveCells, out at)) return at;
            return SettlementConstructionInitiative.RoadHub(map);
        }

        /// <summary>Every storage hut that stands, is being built or is planned, counted the way
        /// <c>SettlementConstructionInitiative.CountBuiltOrPlanned</c> counts them, so the cluster the shortfall
        /// is measured against is the cluster the next hut gathers round.</summary>
        private static IEnumerable<IntVec3> HutCells(Map.Map map)
        {
            ThingDef hut = ConstructionThingDefOf.StorageHut;

            IReadOnlyList<Thing> built = map.listerThings.ThingsOfDef(hut);
            for (int i = 0; i < built.Count; i++) yield return built[i].Position;

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, hut)) yield return bp.Position;
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, hut)) yield return f.Position;
            }
        }

        private static IEnumerable<IntVec3> StockpileCells(Map.Map map)
        {
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (!(zones[z] is Zone_Stockpile pile)) continue;
                IReadOnlyList<IntVec3> cells = pile.Cells;
                for (int c = 0; c < cells.Count; c++) yield return cells[c];
            }
        }

        /// <summary>The mean of <paramref name="cells"/>, rounded to the nearest cell by integer arithmetic on
        /// non-negative sums (no floating point, no banker's rounding, nothing a platform can disagree on).
        /// False when there are none.</summary>
        private static bool TryMean(IEnumerable<IntVec3> cells, out IntVec3 mean)
        {
            long sumX = 0, sumZ = 0;
            int n = 0;
            foreach (IntVec3 c in cells)
            {
                sumX += c.x;
                sumZ += c.z;
                n++;
            }
            if (n == 0)
            {
                mean = default;
                return false;
            }
            mean = new IntVec3((int)((sumX + n / 2) / n), 0, (int)((sumZ + n / 2) / n));
            return true;
        }
    }
}
