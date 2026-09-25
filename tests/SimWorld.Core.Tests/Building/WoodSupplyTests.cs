using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.MapGen;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using IntVec3 = SimWorld.Map.IntVec3;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Where a settlement's wood comes from. Every building <see cref="SettlementConstructionInitiative"/>
    /// plans costs wood, and before this there was no tree on any generated map: the storyteller bench's seed
    /// 777 held no wood at all and built no bed in 45 days, with 25 bed blueprints standing unframed from the
    /// first day. Placement never failed; construction had nothing to build with. These tests pin the three
    /// links that close it: a map is born with trees in proportion to its tile's Timber
    /// (<see cref="GenStep_Trees"/>), a builder fells one only when the settlement's sites are short of wood
    /// (<see cref="WorkGiver_ConstructChopWood"/>), and a founding band on a wooded tile ends up sleeping in
    /// beds it built from its own trees, with nothing placed by hand.
    /// </summary>
    public class WoodSupplyTests : ContentTestBase
    {
        public WoodSupplyTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wood => Def("WoodLog");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static WorkGiver_ConstructChopWood ChopGiver =>
            (WorkGiver_ConstructChopWood)DefDatabase<WorkGiverDef>.GetNamed("ConstructChopWood").Worker;

        private static Plant SpawnTree(CoreMap map, IntVec3 cell, float growth = 1f)
        {
            var tree = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            tree.Growth = growth;
            GenSpawn.Spawn(tree, cell, map);
            return tree;
        }

        private static Pawn SpawnBuilder(CoreMap map, IntVec3 cell, string name = "Builder")
        {
            Pawn p = NewHuman(name);
            p.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            p.skills!.GetSkill(SkillDefOf.Plants)!.Level = 20;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static void SpawnBlueprint(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_" + defName)), cell, map);

        private static void SpawnWood(CoreMap map, IntVec3 cell, int count)
        {
            Thing wood = ThingMaker.MakeThing(Wood);
            wood.stackCount = count;
            GenSpawn.Spawn(wood, cell, map);
        }

        /// <summary>The job the chop giver would hand <paramref name="pawn"/> right now, through the same
        /// nearest-candidate scan the think tree uses.</summary>
        private static Job? ChopJobFor(Pawn pawn) =>
            ChopGiver.ShouldSkip(pawn) ? null : WorkGiverScanUtility.TryFindJobOnScanner(pawn, ChopGiver);

        // ---- content ----

        [Fact]
        public void The_tree_is_content_and_its_harvest_is_wood()
        {
            Assert.Empty(Content.Result.Errors);
            ThingDef tree = TreeDefOf.Plant_TreePoplar;
            Assert.NotNull(tree.plant);
            Assert.True(tree.plant!.IsTree);
            Assert.Same(Wood, tree.plant.harvestedThingDef);
            Assert.True(tree.plant.harvestYield > 0);
            Assert.True(tree.plant.harvestMinGrowth < 1f, "A tree can be felled before it is fully grown, as in RimWorld.");
            Assert.Equal(Traversability.PassThroughOnly, tree.passability);
            Assert.False(Def("Plant_Berry").plant!.IsTree);
            Assert.False(Def("WildPlant").plant!.IsTree);

            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamed("ConstructChopWood");
            Assert.Same(WorkTypeDefOf.Construction, giver.workType);
            Assert.IsType<WorkGiver_ConstructChopWood>(giver.Worker);
        }

        // ---- the root: a generated map had nothing to build with ----

        private static (CoreMap map, Tile tile) GeneratedMapOn(string seed, float timber, string biome = "TemperateForest", int size = 80)
        {
            var world = new global::SimWorld.World.World(
                new WorldInfo { name = "Test", seedString = seed, seed = 11, subdivisionLevel = 2 },
                WorldGrid.Generate(2));
            Find.World = world;
            Tile tile = world.grid.Tiles[5];
            tile.biome = DefDatabase<BiomeDef>.GetNamed(biome);
            tile.hilliness = Hilliness.Flat;
            tile.elevation = 100f;
            tile.rainfall = 1200f;
            tile.deposits.Add(new TileDeposit(DepositDefOf.Timber, timber));
            CoreMap map = MapGenerator.GenerateMap(tile, 5, seed, new global::SimWorld.Map.IntVec2(size, size));
            return (map, tile);
        }

        private static int TreeCount(CoreMap map) => map.listerThings.ThingsOfDef(TreeDefOf.Plant_TreePoplar).Count;

        [Fact]
        public void A_generated_wooded_map_holds_trees_a_builder_can_fell_for_wood()
        {
            (CoreMap map, _) = GeneratedMapOn("wood-supply-forest", timber: 0.6f);

            List<Plant> trees = map.listerThings.ThingsOfDef(TreeDefOf.Plant_TreePoplar).OfType<Plant>().ToList();
            Assert.NotEmpty(trees);
            Assert.All(trees, t => Assert.True(t.HarvestableNow, "A tree a map is born with is established, not a seedling."));

            // And the wood is real: felling one drops a stack of logs where it stood.
            Plant tree = trees[0];
            IntVec3 where = tree.Position;
            tree.Harvest(null);
            Assert.Contains(map.thingGrid.ThingsListAt(where), t => t.def == Wood && t.stackCount > 0);
        }

        [Fact]
        public void Trees_follow_the_tiles_timber_and_a_tile_with_none_grows_none()
        {
            (CoreMap bare, _) = GeneratedMapOn("wood-supply-trend", timber: 0f);
            int none = TreeCount(bare);
            (CoreMap sparse, _) = GeneratedMapOn("wood-supply-trend", timber: 0.15f);
            int few = TreeCount(sparse);
            (CoreMap forest, Tile forestTile) = GeneratedMapOn("wood-supply-trend", timber: 0.6f);
            int many = TreeCount(forest);

            Assert.Equal(0, none);
            Assert.True(few > 0, "A little timber still grows a few trees.");
            Assert.True(many > few * 2, "A forest tile carries several times the trees of a sparse one: " + many + " vs " + few + ".");

            // Generation fills the target the spawner regrows toward, or comes close; a map is not born
            // short of its own forest and left to thicken for weeks.
            int target = WildPlantSpawner.DesiredTreeCount(forest, forestTile);
            Assert.InRange(many, target * 9 / 10, target);
        }

        [Fact]
        public void A_tree_never_shares_its_cell()
        {
            (CoreMap map, _) = GeneratedMapOn("wood-supply-cells", timber: 0.9f, biome: "TropicalRainforest");
            foreach (Thing tree in map.listerThings.ThingsOfDef(TreeDefOf.Plant_TreePoplar))
            {
                Assert.Single(map.thingGrid.ThingsListAt(tree.Position));
                Assert.Null(map.edificeGrid[tree.Position]);
                Assert.False(map.roofGrid.Roofed(tree.Position));
            }
        }

        // ---- the demand: a tree is felled for a site that needs its wood, and only then ----

        [Fact]
        public void A_builder_fells_a_tree_only_while_a_site_is_short_of_wood()
        {
            CoreMap map = NewMap(16, 16);
            Pawn builder = SpawnBuilder(map, new IntVec3(1, 0, 1));
            Plant tree = SpawnTree(map, new IntVec3(12, 0, 12));

            // Nothing to build: the forest stands.
            Assert.Null(ChopJobFor(builder));

            // A bed that needs wood nobody has: fell the tree.
            SpawnBlueprint(map, new IntVec3(4, 0, 4), "Bed");
            Assert.Equal(ConstructionThingDefOf.Bed.CostListCountFor(Wood), WorkGiver_ConstructChopWood.WoodShortfall(map, Wood));
            Job? job = ChopJobFor(builder);
            Assert.NotNull(job);
            Assert.Same(PlantCuttingJobDefOf.CutPlant, job!.def);
            Assert.Same(tree, job.targetA.Thing);

            // Enough loose wood for the bed already lying about: carry that instead, fell nothing.
            SpawnWood(map, new IntVec3(2, 0, 2), ConstructionThingDefOf.Bed.CostListCountFor(Wood));
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, Wood) <= 0);
            Assert.Null(ChopJobFor(builder));
        }

        [Fact]
        public void A_tree_already_being_felled_counts_as_wood_on_its_way()
        {
            CoreMap map = NewMap(20, 20);
            Pawn first = SpawnBuilder(map, new IntVec3(1, 0, 1), "First");
            Pawn second = SpawnBuilder(map, new IntVec3(1, 0, 3), "Second");
            SpawnTree(map, new IntVec3(10, 0, 10));
            SpawnTree(map, new IntVec3(14, 0, 14));
            SpawnBlueprint(map, new IntVec3(4, 0, 4), "Bed"); // needs less wood than one grown tree yields

            Job? job = ChopJobFor(first);
            Assert.NotNull(job);
            first.jobs.StartJob(job!);

            // One tree coming down covers the bed, so a second builder is not sent to fell another.
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, Wood) <= 0);
            Assert.Null(ChopJobFor(second));
        }

        [Fact]
        public void A_tree_standing_on_a_building_site_is_cleared_even_with_wood_to_spare()
        {
            CoreMap map = NewMap(16, 16);
            Pawn builder = SpawnBuilder(map, new IntVec3(1, 0, 1));
            var site = new IntVec3(8, 0, 8);
            Plant onSite = SpawnTree(map, site, growth: 0.1f); // a seedling: not worth felling for its wood
            SpawnTree(map, new IntVec3(3, 0, 3)); // nearer, grown, and not in anybody's way
            SpawnBlueprint(map, site, "Bed");
            SpawnWood(map, new IntVec3(2, 0, 2), 50);

            Job? job = ChopJobFor(builder);
            Assert.NotNull(job);
            Assert.Same(onSite, job!.targetA.Thing);
        }

        [Fact]
        public void Growers_leave_wild_trees_to_the_builders_but_harvest_one_in_a_growing_zone()
        {
            CoreMap map = NewMap(12, 12);
            Pawn grower = SpawnBuilder(map, new IntVec3(1, 0, 1), "Grower");
            Plant wild = SpawnTree(map, new IntVec3(6, 0, 6));
            var harvest = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("GrowerHarvest").Worker;

            Assert.False(harvest.HasJobOnThing(grower, wild), "A wild tree is not a crop: foraging would clear-cut the map.");

            var zone = new Zone_Growing();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, wild.Position);
            Assert.True(harvest.HasJobOnThing(grower, wild), "Inside a growing zone the grower harvests whatever grew there, as in RimWorld.");
        }

        // ---- the loop: a bed nobody supplied wood for, built from a tree ----

        [Fact]
        public void A_bed_with_no_wood_anywhere_is_built_from_a_felled_tree()
        {
            CoreMap map = NewMap(16, 16);
            Pawn builder = SpawnBuilder(map, new IntVec3(1, 0, 1));
            SpawnTree(map, new IntVec3(10, 0, 10));
            SpawnBlueprint(map, new IntVec3(4, 0, 4), "Bed");
            Assert.Empty(map.listerThings.ThingsOfDef(Wood));

            RunTicks(6000, builder);

            Assert.Single(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed));
            Assert.Empty(map.listerThings.ThingsOfDef(TreeDefOf.Plant_TreePoplar));
        }

        // ---- in play: a founded settlement, nothing placed by hand ----

        [Fact]
        public void A_founding_band_on_a_wooded_tile_sleeps_in_beds_it_built_itself_within_two_days()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "wood-supply-founding", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i =>
                !world.grid.Tiles[i].WaterCovered && TreeTuning.TimberMagnitude(world.grid.Tiles[i]) >= 0.4f);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(4343));
            Find.World = world;
            CoreMap map = settlement.EnterMap(world);

            // Nothing is placed by hand: no wood, no blueprint, no pawn. The only wood the map will ever hold
            // is what its own citizens fell.
            int citizens = settlement.Citizens.Count;
            int beds = 0;
            for (int tick = 0; tick < 2 * GenDate.TicksPerDay; tick++)
            {
                SettlementConstructionInitiative.Tick();
                Find.TickManager.DoSingleTick();
                if (tick % 2500 != 0) continue;
                beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count;
                if (beds * 4 >= citizens * 3) break;
            }

            Assert.True(beds * 4 >= citizens * 3,
                "Three in four of the founding band should have a bed by the end of day two; " + beds + " beds for " + citizens + " citizens.");
        }

        // ---- save/load ----

        [Fact]
        public void A_part_grown_tree_round_trips_through_scribe_and_is_still_a_tree()
        {
            CoreMap map = NewMap(6, 6);
            SpawnTree(map, new IntVec3(2, 0, 3), growth: 0.55f);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Plant tree = loaded.thingGrid.ThingsListAt(new IntVec3(2, 0, 3)).OfType<Plant>().Single();
            Assert.Same(TreeDefOf.Plant_TreePoplar, tree.def);
            Assert.True(tree.def.plant!.IsTree);
            Assert.Equal(0.55f, tree.Growth, 3);
            Assert.True(tree.HarvestableNow);
        }
    }
}
