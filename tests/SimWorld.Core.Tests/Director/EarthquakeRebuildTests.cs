using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// What an earthquake brings down is put back: each destroyed building leaves a blueprint for itself where
    /// it stood (RimWorld's auto-rebuild), so the settlement's builders repair the damage instead of the loss
    /// being permanent.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeRebuildTests : ContentTestBase
    {
        public EarthquakeRebuildTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static void Shake(CoreMap map, Faction? faction = null)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, faction, "Quaketown", 0);
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            Assert.True(DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target }));
        }

        private static List<Blueprint> BlueprintsFor(CoreMap map, ThingDef built) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().Where(b => b.EntityToBuild == built).ToList();

        [Fact]
        public void Every_wall_a_quake_brings_down_leaves_a_blueprint_where_it_stood()
        {
            CoreMap map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef wall = Def("Wall");
            var walls = new List<Thing>();
            for (int x = 2; x < 22; x++) walls.Add(GenSpawn.Spawn(ThingMaker.MakeThing(wall), new IntVec3(x, 0, 10), map));

            Shake(map);

            var fallen = walls.Where(w => w.Destroyed).Select(w => w.Position).ToList();
            List<Blueprint> blueprints = BlueprintsFor(map, wall);
            Assert.NotEmpty(blueprints);
            Assert.Equal(walls.Count(w => w.Destroyed), blueprints.Count);
            // Each blueprint stands on a cell that held a wall which is now gone.
            var standing = walls.Where(w => !w.Destroyed).Select(w => w.Position).ToHashSet();
            var wallCells = Enumerable.Range(2, 20).Select(x => new IntVec3(x, 0, 10)).ToHashSet();
            Assert.All(blueprints, b => Assert.True(wallCells.Contains(b.Position) && !standing.Contains(b.Position)));
        }

        /// <summary>A wall the settlement raised in granite is rebuilt in granite. A ruin's steel wall (no
        /// faction) is not rebuilt at all: RimWorld rebuilds only the player's own buildings, by the same faction
        /// test repair and the structure ledger use. Before, the quake left a blueprint on every fallen ruin wall
        /// and the band's builders went out to raise the ruin again.</summary>
        [Fact]
        public void A_fallen_wall_of_ours_is_rebuilt_in_its_own_stuff_and_a_ruins_is_not_rebuilt()
        {
            CoreMap map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef wall = Def("Wall");
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var walls = new List<Thing>();
            foreach (int z in new[] { 6, 10, 14 })
            {
                for (int x = 2; x < 22; x++)
                {
                    bool owned = x % 2 == 0;
                    Thing w = ThingMaker.MakeThing(wall, owned ? Def("BlocksGranite") : Def("Steel"));
                    if (owned) w.SetFaction(ours);
                    walls.Add(GenSpawn.Spawn(w, new IntVec3(x, 0, z), map));
                }
            }

            Shake(map, ours);

            // The quake brought down walls of both kinds (a destroyed thing's Position is reset, so by faction).
            Assert.Contains(walls, w => w.Destroyed && w.Faction == null);
            int oursFallen = walls.Count(w => w.Destroyed && w.Faction == ours);
            Assert.True(oursFallen > 0);
            List<Blueprint> blueprints = BlueprintsFor(map, wall);
            Assert.Equal(oursFallen, blueprints.Count);
            Assert.All(blueprints, b =>
            {
                Assert.True(b.Position.x % 2 == 0, "a blueprint stands where a ruin wall fell");
                Assert.Same(Def("BlocksGranite"), b.stuffToUse);
            });
        }

        /// <summary>A stone wall (a Def of its own) is rebuilt as itself while its stone is to spare, and once it is
        /// not, as the wall the town would raise today: wood, here, where nothing is known of masonry. Kept as
        /// granite with the granite gone, it waited for ever (renewal lane 2).</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_fallen_stone_wall_is_rebuilt_as_itself_while_its_stone_lasts_else_as_todays_wall(bool graniteOnMap)
        {
            CoreMap map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef wall = Def("WallGranite");
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var walls = new List<Thing>();
            foreach (int z in new[] { 6, 10, 14 })
            {
                for (int x = 2; x < 22; x++)
                {
                    Thing w = ThingMaker.MakeThing(wall);
                    w.SetFaction(ours);
                    walls.Add(GenSpawn.Spawn(w, new IntVec3(x, 0, z), map));
                }
            }
            if (graniteOnMap)
            {
                Thing blocks = ThingMaker.MakeThing(Def("BlocksGranite"));
                blocks.stackCount = 5000;
                GenSpawn.Spawn(blocks, new IntVec3(25, 0, 25), map);
            }

            Shake(map, ours);

            int fallen = walls.Count(w => w.Destroyed);
            Assert.True(fallen > 0);
            if (graniteOnMap)
            {
                Assert.Equal(fallen, BlueprintsFor(map, wall).Count);
            }
            else
            {
                Assert.Empty(BlueprintsFor(map, wall));
                List<Blueprint> wooden = BlueprintsFor(map, Def("Wall"));
                Assert.Equal(fallen, wooden.Count);
                Assert.All(wooden, b => Assert.Same(Def("WoodLog"), b.stuffToUse));
            }
        }

        /// <summary>A wooden bed that falls on a map with no wood left but stone blocks on hand is rebuilt in
        /// stone, as a new bed would be placed; while the wood is there it is rebuilt in wood. Before, the
        /// rebuild always asked for the fallen bed's own stuff, and on a wood-poor map (storyteller seed 2024)
        /// that blueprint waited forever.</summary>
        [Theory]
        [InlineData(true, "WoodLog")]
        [InlineData(false, "BlocksGranite")]
        public void A_fallen_bed_is_rebuilt_in_its_own_stuff_while_the_map_has_it_else_in_what_it_has(bool woodOnMap, string expected)
        {
            CoreMap map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            Thing placed = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Bed"), Def("WoodLog")), new IntVec3(10, 0, 10), map, Rot4.North);
            Thing blocks = ThingMaker.MakeThing(Def("BlocksGranite"));
            blocks.stackCount = 50;
            GenSpawn.Spawn(blocks, new IntVec3(3, 0, 3), map);
            if (woodOnMap)
            {
                Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
                wood.stackCount = 50;
                GenSpawn.Spawn(wood, new IntVec3(5, 0, 3), map);
            }

            Shake(map); // one bed: DestroyCount is at least one, so it falls

            Assert.True(placed.Destroyed);
            Blueprint blueprint = Assert.Single(BlueprintsFor(map, Def("Bed")));
            Assert.Same(Def(expected), blueprint.stuffToUse);
        }

        [Fact]
        public void A_fallen_bed_is_laid_out_again_the_way_it_faced()
        {
            CoreMap map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef bed = Def("Bed");
            var at = new IntVec3(10, 0, 10);
            Thing placed = GenSpawn.Spawn(ThingMaker.MakeThing(bed), at, map, Rot4.East);

            Shake(map); // one bed: DestroyCount is at least one, so it falls

            Assert.True(placed.Destroyed);
            Blueprint blueprint = Assert.Single(BlueprintsFor(map, bed));
            Assert.Equal(at, blueprint.Position);
            Assert.Equal(Rot4.East, blueprint.Rotation);
        }
    }
}
