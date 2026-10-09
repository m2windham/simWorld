using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <see cref="HaulAIUtility.TryFindBestStockpileCell"/> tests a cell's distance before its reservation and
    /// its reach, as RimWorld's <c>StoreUtility.TryFindBestBetterStoreCellForWorker</c> does (read from
    /// <c>josh-m/rw-decompile/RimWorld/StoreUtility.cs</c>): on the day a farm is harvested a hundred stacks
    /// look for a home across a few hundred cells, and reading the whole reservation list for every one of the
    /// thousands of pairs was the day's whole cost. The chosen cell has to be the one the old order chose, so
    /// the old order is written out below and held against the new on seeded random maps.
    /// </summary>
    public class HaulBestCellTests : ContentTestBase
    {
        public HaulBestCellTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Size = 32;

        private static CoreMap NewMap() => new CoreMap(Size, Size, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static readonly string[] ItemDefs = { "WoodLog", "Pemmican", "RawRice", "RawBerries" };

        /// <summary>The search as it was written before the reorder: capacity, standable, reserve, reach, and
        /// only then distance.</summary>
        private static bool OldTryFindBestStockpileCell(Pawn pawn, Thing thing, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            CoreMap map = pawn.Map!;

            IntVec3 best = IntVec3.Invalid;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!(zones[i] is Zone_Stockpile stockpile) || !stockpile.filter.Allows(thing.def)) continue;
                IReadOnlyList<IntVec3> cells = stockpile.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    IntVec3 candidate = cells[c];
                    if (HaulAIUtility.CapacityAt(map, candidate, thing.def) <= 0) continue;
                    if (!GenGrid.Standable(candidate, map)) continue;
                    if (!map.reservationManager.CanReserve(pawn, candidate)) continue;
                    if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;

                    int distSq = (candidate - thing.Position).LengthHorizontalSquared;
                    if (distSq >= bestDistSq) continue;
                    best = candidate;
                    bestDistSq = distSq;
                }
            }
            cell = best;
            return best.IsValid;
        }

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName, int count = 1)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>A seeded random storage yard: stockpiles that overlap nothing, part-filled and full cells,
        /// walls that break standability and wall off pockets, cells other citizens have reserved, and a loose
        /// item to find a home for.</summary>
        private static CoreMap Yard(int seed, out Pawn hauler, out Thing loose, out int reservations)
        {
            var rng = new RandomStream(seed);
            CoreMap map = NewMap();

            // Walls first, so a stockpile cell can land on one (not standable) and pockets can be sealed off.
            int walls = rng.Range(0, 60);
            for (int i = 0; i < walls; i++)
            {
                var c = new IntVec3(rng.Range(0, Size), 0, rng.Range(0, Size));
                if (!map.thingGrid.CellContains(c, ThingCategory.Building)) Spawn(map, c, "Wall");
            }

            int stockpiles = rng.Range(1, 4);
            for (int s = 0; s < stockpiles; s++)
            {
                var zone = new Zone_Stockpile();
                map.zoneManager.RegisterZone(zone);
                foreach (string item in ItemDefs)
                {
                    if (rng.Range(0, 4) != 0) zone.filter.SetAllow(Def(item), true);
                }
                var origin = new IntVec3(rng.Range(0, Size - 4), 0, rng.Range(0, Size - 4));
                foreach (IntVec3 c in new CellRect(origin.x, origin.z, rng.Range(2, 10), rng.Range(2, 10)).ClipInsideMap(map).Cells)
                {
                    map.zoneManager.AddCell(zone, c);
                }
            }

            // Stacks already lying on stockpile cells: some the same def as the loose item (room left or none),
            // some a different one (no room at all).
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                foreach (IntVec3 c in zone.Cells)
                {
                    if (rng.Range(0, 3) != 0 || HaulAIUtility.ExistingStackAt(map, c) != null) continue;
                    string def = ItemDefs[rng.Range(0, ItemDefs.Length)];
                    Thing existing = Spawn(map, c, def, 1);
                    existing.stackCount = rng.Range(1, Def(def).stackLimit + 1);
                }
            }

            hauler = NewHuman("Hauler");
            IntVec3 start = FreeCell(map, rng);
            GenSpawn.Spawn(hauler, start, map);

            // Other citizens' claims on some cells.
            reservations = 0;
            int claims = rng.Range(0, 25);
            for (int r = 0; r < claims; r++)
            {
                Pawn other = NewHuman("Other" + r);
                GenSpawn.Spawn(other, FreeCell(map, rng), map);
                if (map.reservationManager.Reserve(other, new LocalTargetInfo(new IntVec3(rng.Range(0, Size), 0, rng.Range(0, Size))))) reservations++;
            }

            loose = Spawn(map, FreeCell(map, rng), ItemDefs[rng.Range(0, ItemDefs.Length)], 5);
            return map;
        }

        private static IntVec3 FreeCell(CoreMap map, RandomStream rng)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                var c = new IntVec3(rng.Range(0, Size), 0, rng.Range(0, Size));
                if (GenGrid.Standable(c, map) && !map.thingGrid.CellContains(c, ThingCategory.Item)) return c;
            }
            return new IntVec3(0, 0, 0);
        }

        [Fact]
        public void The_cell_chosen_is_the_one_the_old_order_chose_across_many_random_yards()
        {
            int found = 0;
            int none = 0;
            int reservedNearer = 0;
            for (int seed = 1; seed <= 150; seed++)
            {
                CoreMap map = Yard(seed, out Pawn hauler, out Thing loose, out int reservations);

                bool expected = OldTryFindBestStockpileCell(hauler, loose, out IntVec3 expectedCell);
                bool actual = HaulAIUtility.TryFindBestStockpileCell(hauler, loose, out IntVec3 actualCell);

                Assert.True(expected == actual, "seed " + seed + ": found/not found differs");
                Assert.True(expectedCell == actualCell, "seed " + seed + ": expected " + expectedCell + " got " + actualCell);
                if (actual) found++; else none++;
                if (actual && reservations > 0) reservedNearer++;
            }

            // Both outcomes, and reservations in play, or the comparison proves little.
            Assert.True(found >= 60, "only " + found + " yards had a home for the item");
            Assert.True(none >= 5, "only " + none + " yards had none");
            Assert.True(reservedNearer >= 40, "only " + reservedNearer + " yards had reservations in play");
        }

        [Fact]
        public void The_nearest_free_cell_wins_and_a_reserved_nearer_one_is_passed_over()
        {
            CoreMap map = NewMap();
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            zone.filter.SetAllow(Def("WoodLog"), true);
            foreach (IntVec3 c in new CellRect(10, 10, 6, 1).Cells) map.zoneManager.AddCell(zone, c);
            Thing loose = Spawn(map, new IntVec3(10, 0, 14), "WoodLog", 5);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, new IntVec3(1, 0, 1), map);
            Pawn rival = NewHuman("Rival");
            GenSpawn.Spawn(rival, new IntVec3(2, 0, 2), map);

            Assert.True(HaulAIUtility.TryFindBestStockpileCell(hauler, loose, out IntVec3 nearest));
            Assert.Equal(new IntVec3(10, 0, 10), nearest);

            map.reservationManager.Reserve(rival, new LocalTargetInfo(nearest));

            Assert.True(HaulAIUtility.TryFindBestStockpileCell(hauler, loose, out IntVec3 next));
            Assert.Equal(new IntVec3(11, 0, 10), next);
        }

        [Fact]
        public void Equally_near_cells_go_to_the_one_the_zone_lists_first()
        {
            CoreMap map = NewMap();
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            zone.filter.SetAllow(Def("WoodLog"), true);
            var firstListed = new IntVec3(12, 0, 8);
            var secondListed = new IntVec3(12, 0, 12); // exactly as far from the loose item at (12, 10)
            map.zoneManager.AddCell(zone, firstListed);
            map.zoneManager.AddCell(zone, secondListed);
            Thing loose = Spawn(map, new IntVec3(12, 0, 10), "WoodLog", 5);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, new IntVec3(1, 0, 1), map);

            Assert.True(HaulAIUtility.TryFindBestStockpileCell(hauler, loose, out IntVec3 chosen));

            Assert.Equal(firstListed, chosen);
        }
    }
}
