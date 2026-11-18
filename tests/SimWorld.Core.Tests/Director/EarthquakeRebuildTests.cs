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

        private static void Shake(CoreMap map)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
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

        /// <summary>A wall the settlement raised in granite is rebuilt in granite; a ruin's steel wall, which no
        /// one there chose, is rebuilt in the default stuff (wood) rather than leaving a blueprint for steel a
        /// tribe may never have.</summary>
        [Fact]
        public void A_fallen_wall_of_ours_is_rebuilt_in_its_own_stuff_and_a_ruins_in_the_default()
        {
            CoreMap map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef wall = Def("Wall");
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var walls = new List<Thing>();
            for (int x = 2; x < 22; x++)
            {
                bool owned = x % 2 == 0;
                Thing w = ThingMaker.MakeThing(wall, owned ? Def("BlocksGranite") : Def("Steel"));
                if (owned) w.SetFaction(ours);
                walls.Add(GenSpawn.Spawn(w, new IntVec3(x, 0, 10), map));
            }

            Shake(map);

            List<Blueprint> blueprints = BlueprintsFor(map, wall);
            Assert.NotEmpty(blueprints);
            Assert.All(blueprints, b => Assert.Same(b.Position.x % 2 == 0 ? Def("BlocksGranite") : Def("WoodLog"), b.stuffToUse));
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
