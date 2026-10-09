using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <see cref="WorkGiver_PlantsCut"/> and <see cref="WorkGiver_ConstructChopWood"/> used to hand the scan
    /// every plant on the map and let <c>HasJobOnThing</c> discard nearly all of them; they now find the few
    /// that qualify in the cells of growing zones and building sites (<see cref="PlantScanUtility"/>). RimWorld
    /// keeps such a list as designations, so the change is the right shape; what has to hold is that it is the
    /// <i>same</i> list, in the same order, since a scan keeps the first of several equally near candidates.
    /// These tests do not trust hand-picked cases for that: each builds a few dozen seeded random maps — plants
    /// of every kind, zones that overlap them, sites of one and several cells at the map's edge, wood on the
    /// floor or none — and compares the new answer with the old whole-list walk, written out below.
    /// </summary>
    public class PlantScanEquivalenceTests : ContentTestBase
    {
        public PlantScanEquivalenceTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Size = 36;

        private static CoreMap NewMap() => new CoreMap(Size, Size, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static WorkGiver_PlantsCut CutGiver => (WorkGiver_PlantsCut)DefDatabase<WorkGiverDef>.GetNamed("PlantsCut").Worker;

        private static WorkGiver_ConstructChopWood ChopGiver => (WorkGiver_ConstructChopWood)DefDatabase<WorkGiverDef>.GetNamed("ConstructChopWood").Worker;

        private static readonly string[] PlantDefs = { "Plant_Rice", "Plant_Potato", "Plant_Healroot", "WildPlant", "Plant_Berry", "Plant_TreePoplar", "Plant_TreePoplar" };

        private static readonly string[] CropDefs = { "Plant_Rice", "Plant_Potato", "Plant_Healroot" };

        private static readonly string[] SiteDefs = { "Blueprint_Wall", "Blueprint_Bed", "Blueprint_StorageHut", "Frame_Wall", "Frame_Bed", "Frame_StorageHut", "Blueprint_Door" };

        private static IntVec3 RandomCell(RandomStream rng, int margin = 0) =>
            new IntVec3(rng.Range(margin, Size - margin), 0, rng.Range(margin, Size - margin));

        /// <summary>A seeded random settlement-shaped map, and a citizen on it.</summary>
        private static CoreMap Scenario(int seed, out Pawn pawn)
        {
            var rng = new RandomStream(seed);
            CoreMap map = NewMap();
            pawn = NewHuman("Scanner");
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);

            int zones = rng.Range(0, 4);
            for (int z = 0; z < zones; z++)
            {
                var zone = new Zone_Growing
                {
                    plantDefToGrow = rng.Range(0, 5) == 0 ? null : Def(CropDefs[rng.Range(0, CropDefs.Length)]),
                    allowSow = rng.Range(0, 5) != 0,
                };
                map.zoneManager.RegisterZone(zone);
                IntVec3 origin = RandomCell(rng, 2);
                foreach (IntVec3 c in new CellRect(origin.x, origin.z, rng.Range(1, 9), rng.Range(1, 9)).ClipInsideMap(map).Cells)
                {
                    map.zoneManager.AddCell(zone, c);
                }
            }

            int plants = rng.Range(10, 260);
            for (int i = 0; i < plants; i++)
            {
                IntVec3 cell = RandomCell(rng);
                if (map.thingGrid.CellContains(cell, ThingCategory.Plant)) continue;
                var plant = (Plant)ThingMaker.MakeThing(Def(PlantDefs[rng.Range(0, PlantDefs.Length)]));
                plant.Growth = rng.Value;
                GenSpawn.Spawn(plant, cell, map);
            }

            int sites = rng.Range(0, 6);
            for (int s = 0; s < sites; s++)
            {
                // Clamped so the whole footprint fits (GenSpawn refuses one that would hang off the map), which
                // still leaves sites flush against every edge.
                IntVec3 at = RandomCell(rng);
                at = new IntVec3(System.Math.Min(at.x, Size - 3), 0, System.Math.Min(at.z, Size - 3));
                GenSpawn.Spawn(ThingMaker.MakeThing(Def(SiteDefs[rng.Range(0, SiteDefs.Length)])), at, map);
            }

            if (rng.Range(0, 2) == 0)
            {
                Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
                wood.stackCount = rng.Range(1, 120);
                GenSpawn.Spawn(wood, RandomCell(rng), map);
            }
            return map;
        }

        // ---- the old whole-list walks, kept here as the standard the new answers are held to ----

        /// <summary>What <c>WorkGiver_PlantsCut.PotentialWorkThingsGlobal</c> handed the scan: every plant. The
        /// scan then kept the ones its <c>HasJobOnThing</c> accepted, of which <see cref="WorkGiver_PlantsCut.ShouldBeCut"/>
        /// is the part that depends on the map.</summary>
        private static List<Thing> OldPlantsCutCandidates(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)
                .Where(t => t is Plant p && WorkGiver_PlantsCut.ShouldBeCut(p))
                .ToList();

        /// <summary>What <c>WorkGiver_ConstructChopWood.PotentialWorkThingsGlobal</c> yielded before: every
        /// tree standing on a site, and every felling-ready tree whose material the sites are short of.</summary>
        private static List<Thing> OldChopCandidates(CoreMap map)
        {
            var result = new List<Thing>();
            var shortfall = new Dictionary<ThingDef, int>();
            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            for (int i = 0; i < plants.Count; i++)
            {
                if (!(plants[i] is Plant plant) || !plant.Spawned) continue;
                PlantProperties? props = plant.def.plant;
                if (props == null || !props.IsTree) continue;

                bool onSite = map.thingGrid.CellContains(plant.Position, ThingCategory.Blueprint)
                    || map.thingGrid.CellContains(plant.Position, ThingCategory.Frame);
                if (onSite)
                {
                    result.Add(plant);
                    continue;
                }
                if (!plant.HarvestableNow || props.harvestedThingDef == null) continue;

                if (!shortfall.TryGetValue(props.harvestedThingDef, out int missing))
                {
                    missing = WorkGiver_ConstructChopWood.WoodShortfall(map, props.harvestedThingDef);
                    shortfall[props.harvestedThingDef] = missing;
                }
                if (missing > 0) result.Add(plant);
            }
            return result;
        }

        // ---- PlantsCut ----

        [Fact]
        public void PlantsCut_offers_the_same_plants_in_the_same_order_as_the_old_walk_across_many_random_maps()
        {
            int mapsWithCandidates = 0;
            int mapsWithSeveral = 0;
            for (int seed = 1; seed <= 80; seed++)
            {
                CoreMap map = Scenario(seed, out Pawn pawn);

                List<Thing> expected = OldPlantsCutCandidates(map);
                List<Thing> offered = CutGiver.PotentialWorkThingsGlobal(pawn).ToList();

                Assert.True(expected.SequenceEqual(offered), "seed " + seed + ": expected " + expected.Count + " plants, offered " + offered.Count);
                if (expected.Count > 0) mapsWithCandidates++;
                if (expected.Count > 1) mapsWithSeveral++;
            }

            // The comparison only means something if the scenarios exercise it: most maps must have had plants
            // to cut, and several must have had more than one, which is where order matters.
            Assert.True(mapsWithCandidates >= 20, "only " + mapsWithCandidates + " of 80 maps had a plant to cut");
            Assert.True(mapsWithSeveral >= 10, "only " + mapsWithSeveral + " of 80 maps had several");
        }

        [Fact]
        public void PlantsCut_offers_nothing_on_a_map_whose_plants_block_nothing()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);
            for (int i = 0; i < 40; i++)
            {
                var plant = (Plant)ThingMaker.MakeThing(Def("WildPlant"));
                plant.Growth = 1f;
                GenSpawn.Spawn(plant, new IntVec3(3 + i % 8, 0, 3 + i / 8), map);
            }

            Assert.Empty(CutGiver.PotentialWorkThingsGlobal(pawn));
        }

        [Fact]
        public void PlantsCut_finds_a_plant_under_any_cell_of_a_multi_cell_site_and_at_the_edge_of_the_map()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);

            // A bed is two cells; the plant stands under the second one, not the one the blueprint sits on.
            Thing bed = ThingMaker.MakeThing(Def("Blueprint_Bed"));
            GenSpawn.Spawn(bed, new IntVec3(10, 0, 10), map);
            IntVec3 secondCell = bed.OccupiedRect().Cells.First(c => c != bed.Position);
            var underBed = (Plant)ThingMaker.MakeThing(Def("WildPlant"));
            GenSpawn.Spawn(underBed, secondCell, map);

            // A wall blueprint on the very edge of the map, a plant beneath it.
            var edge = new IntVec3(0, 0, Size - 1);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), edge, map);
            var underWall = (Plant)ThingMaker.MakeThing(Def("WildPlant"));
            GenSpawn.Spawn(underWall, edge, map);

            List<Thing> offered = CutGiver.PotentialWorkThingsGlobal(pawn).ToList();

            Assert.Equal(2, offered.Count);
            Assert.Contains(underBed, offered);
            Assert.Contains(underWall, offered);
            Assert.True(OldPlantsCutCandidates(map).SequenceEqual(offered));
        }

        [Fact]
        public void PlantsCut_still_finds_the_plant_the_scan_would_choose_when_several_are_equally_near()
        {
            CoreMap map = NewMap();
            var zone = new Zone_Growing { plantDefToGrow = Def("Plant_Potato") };
            map.zoneManager.RegisterZone(zone);
            // Two wrong-crop plants exactly as far from the citizen as each other; the scan keeps whichever it
            // meets first, which is the one spawned first.
            IntVec3 left = new IntVec3(10, 0, 5);
            IntVec3 right = new IntVec3(10, 0, 9);
            map.zoneManager.AddCell(zone, left);
            map.zoneManager.AddCell(zone, right);
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(10, 0, 7), map);
            var first = (Plant)ThingMaker.MakeThing(Def("Plant_Rice"));
            var second = (Plant)ThingMaker.MakeThing(Def("Plant_Rice"));
            GenSpawn.Spawn(second, right, map); // spawned first, so listed first
            GenSpawn.Spawn(first, left, map);

            Job? job = WorkGiverScanUtility.TryFindJobOnScanner(pawn, CutGiver);

            Assert.NotNull(job);
            Assert.Same(second, job!.targetA.Thing);
        }

        [Fact]
        public void PlantsCut_offers_a_whole_field_of_the_wrong_crop_once_each_and_in_map_order_even_with_a_site_over_part_of_it()
        {
            // A zone switched to another crop while its old crop stands in every cell: hundreds of candidates,
            // some reached from two directions (the zone's cells and a blueprint's), the case a list lookup per
            // plant would make quadratic.
            CoreMap map = NewMap();
            var zone = new Zone_Growing { plantDefToGrow = Def("Plant_Potato") };
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in new CellRect(4, 4, 28, 28).Cells)
            {
                map.zoneManager.AddCell(zone, c);
                var rice = (Plant)ThingMaker.MakeThing(Def("Plant_Rice"));
                GenSpawn.Spawn(rice, c, map);
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_StorageHut")), new IntVec3(10, 0, 10), map); // over part of the field
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);

            List<Thing> expected = OldPlantsCutCandidates(map);
            List<Thing> offered = CutGiver.PotentialWorkThingsGlobal(pawn).ToList();

            Assert.Equal(28 * 28, expected.Count);
            Assert.True(expected.SequenceEqual(offered));
            Assert.Equal(offered.Count, offered.Distinct().Count());
        }

        // ---- ConstructChopWood ----

        [Fact]
        public void ChopWood_offers_the_same_trees_in_the_same_order_as_the_old_walk_across_many_random_maps()
        {
            int mapsWithCandidates = 0;
            int mapsWithSeveral = 0;
            int mapsShortOfWood = 0;
            int mapsWithEnoughWood = 0;
            for (int seed = 1; seed <= 120; seed++)
            {
                CoreMap map = Scenario(1000 + seed, out Pawn pawn);

                List<Thing> expected = OldChopCandidates(map);
                List<Thing> offered = ChopGiver.PotentialWorkThingsGlobal(pawn).ToList();

                Assert.True(expected.SequenceEqual(offered), "seed " + seed + ": expected " + expected.Count + " trees, offered " + offered.Count);
                if (expected.Count > 0) mapsWithCandidates++;
                if (expected.Count > 1) mapsWithSeveral++;

                bool anySite = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count + map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count > 0;
                if (anySite && WorkGiver_ConstructChopWood.WoodShortfall(map, Def("WoodLog")) > 0) mapsShortOfWood++;
                if (anySite && WorkGiver_ConstructChopWood.WoodShortfall(map, Def("WoodLog")) <= 0) mapsWithEnoughWood++;
            }

            // Both regimes of the new code (wood short: every tree is a candidate; wood enough: only the trees
            // on a site) must have been compared, and compared on maps where the answer was not empty.
            Assert.True(mapsWithCandidates >= 30, "only " + mapsWithCandidates + " of 120 maps had a tree on offer");
            Assert.True(mapsWithSeveral >= 15, "only " + mapsWithSeveral + " of 120 maps had several");
            Assert.True(mapsShortOfWood >= 10, "only " + mapsShortOfWood + " maps were short of wood");
            Assert.True(mapsWithEnoughWood >= 10, "only " + mapsWithEnoughWood + " maps had enough wood");
        }

        [Fact]
        public void ChopWood_offers_only_the_tree_on_the_site_while_the_wood_is_in_hand_and_every_tree_when_it_is_not()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);
            var onSite = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            var elsewhere = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            onSite.Growth = elsewhere.Growth = 1f;
            GenSpawn.Spawn(elsewhere, new IntVec3(20, 0, 20), map);
            GenSpawn.Spawn(onSite, new IntVec3(8, 0, 8), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), new IntVec3(8, 0, 8), map);

            // No wood anywhere: a bed's cost is short, so any felling-ready tree is wanted.
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, Def("WoodLog")) > 0);
            Assert.Equal(new Thing[] { elsewhere, onSite }, ChopGiver.PotentialWorkThingsGlobal(pawn).ToArray());

            // Plenty of wood lying about: only the tree in the bed's way still has to go.
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 500;
            GenSpawn.Spawn(wood, new IntVec3(15, 0, 15), map);
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, Def("WoodLog")) <= 0);
            Assert.Equal(new Thing[] { onSite }, ChopGiver.PotentialWorkThingsGlobal(pawn).ToArray());
        }

        [Fact]
        public void ChopWood_skips_outright_with_no_blueprint_and_no_frame_and_not_with_either()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);
            var tree = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            tree.Growth = 1f;
            GenSpawn.Spawn(tree, new IntVec3(8, 0, 8), map);

            Assert.True(ChopGiver.ShouldSkip(pawn), "nothing is being built, so no tree is wanted");
            Assert.Empty(ChopGiver.PotentialWorkThingsGlobal(pawn));

            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, new IntVec3(12, 0, 12), map);
            Assert.False(ChopGiver.ShouldSkip(pawn));

            blueprint.DeSpawn();
            Assert.True(ChopGiver.ShouldSkip(pawn));

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Frame_Wall")), new IntVec3(12, 0, 12), map);
            Assert.False(ChopGiver.ShouldSkip(pawn));
        }

        [Fact]
        public void ChopWood_with_no_sites_skips_exactly_when_the_old_walk_would_have_found_nothing()
        {
            // The ShouldSkip claim in one sentence: no site means the old walk yields no tree, whatever the
            // plants are. Checked on random maps with every site removed.
            for (int seed = 1; seed <= 40; seed++)
            {
                CoreMap map = Scenario(5000 + seed, out Pawn pawn);
                foreach (Thing site in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)).ToList())
                {
                    site.DeSpawn();
                }

                Assert.True(ChopGiver.ShouldSkip(pawn));
                Assert.Empty(OldChopCandidates(map));
            }
        }
    }
}
