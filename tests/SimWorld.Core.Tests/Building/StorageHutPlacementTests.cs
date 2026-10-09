using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <b>Where</b> storage huts go. Drawn uniformly over the home area they sprawled: the home area grows four
    /// cells round every building, so each hut could extend the area the next one was drawn from, and seed 777
    /// scattered 453 of them over a 200x200 map. Storage now gathers round the settlement's existing storage
    /// (<see cref="StorageAnchor"/>), still inside the home area when there is one, and beds and walls are sited
    /// exactly as before.
    /// <para/>
    /// Structure, never coordinates: nearer one another than uniform placement, nearer the stockpile than the
    /// hub, inside the painted area, off the zone cells. The cells themselves are a seeded draw, and pinning
    /// them would pin the stream.
    /// </summary>
    public class StorageHutPlacementTests : ContentTestBase
    {
        public StorageHutPlacementTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int size) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static IntVec3 Hub(CoreMap map) => new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

        private static double Distance(IntVec3 a, IntVec3 b)
        {
            double dx = a.x - b.x, dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Chebyshev(IntVec3 a, IntVec3 b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.z - b.z));

        private static IntVec3 Mean(IEnumerable<IntVec3> cells)
        {
            List<IntVec3> list = cells.ToList();
            return new IntVec3((int)Math.Round(list.Average(c => c.x)), 0, (int)Math.Round(list.Average(c => c.z)));
        }

        private static List<Blueprint> HutBlueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint>()
                .Where(bp => bp.EntityToBuild == ConstructionThingDefOf.StorageHut)
                .ToList();

        /// <summary>Plans storage huts up to <paramref name="target"/>, a pass at a time, through the loop body the
        /// initiative itself runs.</summary>
        private static int PlanHuts(CoreMap map, int target, int passes = 10) =>
            Enumerable.Range(0, passes).Sum(_ => SettlementConstructionInitiative.PlaceShortfall(
                map, ConstructionThingDefOf.StorageHut, target, ConstructionInitiativeTuning.MaxBlueprintsPerTick));

        /// <summary>A stockpile zone over <paramref name="cells"/>. Laying a zone marks home area round it
        /// (<see cref="AutoHomeAreaMaker.Notify_ZoneCellAdded"/>), which is the realistic thing, but a test that
        /// wants the road-hub branch or its own painted area clears it afterwards.</summary>
        private static Zone_Stockpile Stockpile(CoreMap map, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile { label = "Granary" };
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) Assert.True(map.zoneManager.AddCell(zone, c));
            return zone;
        }

        private static IntVec3[] Block(int x, int z, int w, int h) =>
            new CellRect(x, z, w, h).Cells.ToArray();

        private static void ClearHome(CoreMap map)
        {
            foreach (IntVec3 c in map.AllCells) map.areaManager.Home[c] = false;
        }

        private static void PaintHome(CoreMap map, CellRect rect)
        {
            foreach (IntVec3 c in rect.ClipInsideMap(map).Cells) map.areaManager.Home[c] = true;
        }

        private static double UniformMeanDistanceTo(CoreMap map, IntVec3 point) => map.AllCells.Average(c => Distance(c, point));

        // ---- where the first hut goes ----

        [Fact]
        public void With_no_storage_home_area_or_stockpile_the_first_hut_goes_beside_the_road_hub()
        {
            CoreMap map = NewMap(100);

            Assert.Equal(1, PlanHuts(map, target: 1));

            Blueprint hut = Assert.Single(HutBlueprints(map));
            Assert.True(Chebyshev(hut.Position, Hub(map)) <= ConstructionInitiativeTuning.StorageClusterRadius,
                "The first hut at " + hut.Position + " should sit within the cluster radius of the hub " + Hub(map) + ".");
        }

        [Fact]
        public void A_stockpile_draws_the_huts_to_it_not_to_the_road_hub()
        {
            CoreMap map = NewMap(200);
            IntVec3[] granary = Block(30, 30, 5, 5);
            Stockpile(map, granary);
            ClearHome(map); // isolate: no home area, so only the stockpile can pull the huts

            Assert.Equal(3, PlanHuts(map, target: 3));

            IntVec3 centre = Mean(granary);
            Assert.All(HutBlueprints(map), hut =>
            {
                Assert.True(Distance(hut.Position, centre) < Distance(hut.Position, Hub(map)),
                    "Hut at " + hut.Position + " is nearer the hub than the granary at " + centre + ".");
                Assert.True(Distance(hut.Position, centre) < 4 * ConstructionInitiativeTuning.StorageClusterRadius,
                    "Hut at " + hut.Position + " strayed from the granary at " + centre + ".");
            });
        }

        // ---- gathering ----

        [Fact]
        public void The_huts_of_a_founding_band_gather_in_one_place_rather_than_scattering()
        {
            CoreMap map = NewMap(200);
            int huts = StorageHutTarget.For(26, totalStored: 1);

            Assert.Equal(huts, PlanHuts(map, huts));

            List<IntVec3> cells = HutBlueprints(map).Select(bp => bp.Position).ToList();
            Assert.Equal(huts, cells.Count);
            IntVec3 centroid = Mean(cells);
            double mean = cells.Average(c => Distance(c, centroid));
            double uniform = UniformMeanDistanceTo(map, centroid);
            Assert.True(mean < uniform / 10,
                "Huts averaged " + mean.ToString("F1") + " cells from their own centre; uniform placement over this map gives "
                + uniform.ToString("F1") + ". Storage should gather, not scatter.");
        }

        [Fact]
        public void A_real_band_of_26_plans_the_huts_its_people_want_all_in_one_place()
        {
            // The scenario this lane measured: 26 citizens, a ledger with plenty in it, the whole initiative
            // running (beds and walls too), on a map as big as the game's.
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Band", 0);
            for (int i = 0; i < 26; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            settlement.AddStore(Def("WoodLog"), 20_000);
            CoreMap map = NewMap(200);

            for (int pass = 0; pass < 120; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }

            List<IntVec3> cells = HutBlueprints(map).Select(bp => bp.Position).ToList();
            Assert.Equal(StorageHutTarget.For(settlement), cells.Count);
            IntVec3 centroid = Mean(cells);
            Assert.True(cells.Average(c => Distance(c, centroid)) < UniformMeanDistanceTo(map, centroid) / 10);
        }

        // ---- what still binds ----

        [Fact]
        public void The_home_area_still_binds_storage_even_when_the_stockpile_is_elsewhere()
        {
            CoreMap map = NewMap(120);
            Stockpile(map, Block(80, 80, 4, 4));
            ClearHome(map);
            var home = new CellRect(5, 5, 12, 12); // far from the stockpile and the hub
            PaintHome(map, home);

            Assert.Equal(4, PlanHuts(map, target: 4));

            Assert.All(HutBlueprints(map), hut => Assert.True(map.areaManager.Home[hut.Position],
                "Hut at " + hut.Position + " is outside the painted home area " + home));
        }

        [Fact]
        public void A_hut_is_never_sited_on_a_zone_cell()
        {
            // A 3x3 granary inside a home area that has exactly three other cells: the hut takes one of those.
            CoreMap map = NewMap(20);
            IntVec3[] granary = Block(5, 5, 3, 3);
            Stockpile(map, granary);
            ClearHome(map);
            PaintHome(map, new CellRect(5, 5, 4, 3)); // the granary and the column beside it

            Assert.Equal(1, PlanHuts(map, target: 1));

            Blueprint hut = Assert.Single(HutBlueprints(map));
            Assert.Null(map.zoneManager.ZoneAt(hut.Position));
            Assert.Equal(8, hut.Position.x);
        }

        [Fact]
        public void A_home_area_that_is_all_zone_waits_for_the_player_and_says_so_rather_than_building_on_the_granary()
        {
            CoreMap map = NewMap(20);
            IntVec3[] granary = Block(5, 5, 3, 3);
            Stockpile(map, granary);
            ClearHome(map);
            PaintHome(map, new CellRect(5, 5, 3, 3)); // exactly the granary

            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Walled", 0);
            settlement.AddStore(Def("WoodLog"), 5); // wants a hut, nobody to want anything else
            Assert.Equal(1, StorageHutTarget.For(settlement));

            Find.TickManager.DebugSetTicksGame(0);
            SettlementConstructionInitiative.TickSettlement(settlement, map);

            Assert.Empty(HutBlueprints(map));
            string? why = SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map);
            Assert.NotNull(why);
            Assert.Contains("storage hut", why);
        }

        [Fact]
        public void Gathering_never_walls_anyone_in()
        {
            // Packed as tight as the cluster is, the ground around it stays one connected piece. (The general
            // guard is pinned by PlacementConnectivityTests; this is that guard under the new, denser siting.)
            CoreMap map = NewMap(30);
            PlanHuts(map, target: 150, passes: 80);
            List<Blueprint> huts = HutBlueprints(map);
            Assert.True(huts.Count >= 40, "test setup: should have packed in dozens of huts, packed " + huts.Count);

            foreach (Blueprint bp in huts)
            {
                IntVec3 at = bp.Position;
                bp.Destroy();
                GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.StorageHut), at, map);
            }

            var seen = new HashSet<IntVec3>();
            int components = 0;
            foreach (IntVec3 start in map.AllCells)
            {
                if (seen.Contains(start) || !map.pathGrid.Walkable(start)) continue;
                components++;
                var queue = new Queue<IntVec3>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    IntVec3 c = queue.Dequeue();
                    foreach (IntVec3 offset in GenAdj.AdjacentCells)
                    {
                        IntVec3 n = c + offset;
                        if (!GenGrid.InBounds(n, map) || seen.Contains(n) || !map.pathGrid.Walkable(n)) continue;
                        if (offset.x != 0 && offset.z != 0
                            && (!map.pathGrid.Walkable(new IntVec3(n.x, 0, c.z)) || !map.pathGrid.Walkable(new IntVec3(c.x, 0, n.z))))
                        {
                            continue;
                        }
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            Assert.Equal(1, components);
        }

        // ---- beds and walls are untouched ----

        [Fact]
        public void Beds_and_walls_are_sited_as_before_not_drawn_to_the_storage()
        {
            CoreMap map = NewMap(200);
            IntVec3[] granary = Block(30, 30, 5, 5);
            Stockpile(map, granary);
            ClearHome(map); // no home area: the general placer gathers round the hub

            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Mixed", 0);
            for (int i = 0; i < 20; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            settlement.AddStore(Def("WoodLog"), 100);

            for (int pass = 0; pass < 40; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }

            List<Blueprint> general = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint>()
                .Where(bp => bp.EntityToBuild != ConstructionThingDefOf.StorageHut)
                .ToList();
            Assert.Equal(20 + 40, general.Count);

            IntVec3 centre = Mean(granary);
            double toHub = general.Average(bp => Distance(bp.Position, Hub(map)));
            double toGranary = general.Average(bp => Distance(bp.Position, centre));
            Assert.True(toHub < toGranary,
                "Beds and walls averaged " + toHub.ToString("F1") + " from the hub and " + toGranary.ToString("F1")
                + " from the granary: storage should not have pulled them.");

            // And the huts did go to the granary.
            Assert.All(HutBlueprints(map), hut => Assert.True(Distance(hut.Position, centre) < Distance(hut.Position, Hub(map))));
        }

        // ---- the anchor ----

        [Fact]
        public void The_anchor_is_the_huts_then_the_stockpile_then_the_home_area_then_the_hub()
        {
            CoreMap map = NewMap(100);
            Assert.Equal(Hub(map), StorageAnchor.For(map));

            PaintHome(map, new CellRect(10, 10, 5, 5));
            Assert.Equal(new IntVec3(12, 0, 12), StorageAnchor.For(map));

            Stockpile(map, Block(60, 70, 3, 3));
            Assert.Equal(new IntVec3(61, 0, 71), StorageAnchor.For(map)); // the stockpile outranks the home area

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_StorageHut")), new IntVec3(30, 0, 40), map);
            Assert.Equal(new IntVec3(30, 0, 40), StorageAnchor.For(map)); // and the huts outrank both
        }

        [Fact]
        public void A_hut_counts_toward_the_anchor_whether_built_framed_or_planned()
        {
            CoreMap map = NewMap(100);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("StorageHut")), new IntVec3(20, 0, 20), map);
            Assert.Equal(new IntVec3(20, 0, 20), StorageAnchor.For(map));

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Frame_StorageHut")), new IntVec3(30, 0, 20), map);
            Assert.Equal(new IntVec3(25, 0, 20), StorageAnchor.For(map));

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_StorageHut")), new IntVec3(25, 0, 32), map);
            Assert.Equal(new IntVec3(25, 0, 24), StorageAnchor.For(map));

            // Something that is not a hut is not storage.
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(90, 0, 90), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), new IntVec3(80, 0, 10), map);
            Assert.Equal(new IntVec3(25, 0, 24), StorageAnchor.For(map));
        }

        // ---- determinism and Scribe ----

        [Fact]
        public void The_same_seed_sites_the_same_huts_in_the_same_cells()
        {
            List<IntVec3> Run(int seed)
            {
                CoreMap.ResetMapIdCounter();
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap(120);
                Stockpile(map, Block(40, 40, 4, 4));
                PlanHuts(map, target: 6);
                return HutBlueprints(map).Select(bp => bp.Position).OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            }

            List<IntVec3> first = Run(777);
            List<IntVec3> again = Run(777);
            List<IntVec3> otherSeed = Run(778);

            Assert.Equal(6, first.Count);
            Assert.Equal(first, again);
            Assert.NotEqual(first, otherSeed); // the seeded stream chooses the cell, not a fixed scan order
        }

        [Fact]
        public void A_saved_and_loaded_map_sites_the_next_hut_exactly_where_the_unsaved_one_does()
        {
            // The siting keeps no state of its own: the anchor is re-derived from the huts and zones on the map.
            // So what has to survive a save is the map, and a loaded map must then site the very same cells.
            CoreMap original = NewMap(120);
            Stockpile(original, Block(70, 20, 4, 4));
            PlanHuts(original, target: 3);
            Assert.Equal(3, HutBlueprints(original).Count);

            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(StorageAnchor.For(original), StorageAnchor.For(loaded));

            List<string> Continue(CoreMap map)
            {
                Rand.Current = new RandomStream(2024);
                PlanHuts(map, target: 8);
                return HutBlueprints(map).Select(bp => bp.Position.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
            }

            List<string> fromOriginal = Continue(original);
            List<string> fromLoaded = Continue(loaded);
            Assert.Equal(8, fromOriginal.Count);
            Assert.Equal(fromOriginal, fromLoaded);
        }
    }
}
