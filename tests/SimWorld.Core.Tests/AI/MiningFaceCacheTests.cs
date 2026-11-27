using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <see cref="MiningInitiative.FaceCandidates"/> keeps the rock a miner could stand beside between rare ticks
    /// (tick-cost lane), rebuilt when the building list or some cell's walkability changes. Each test changes the map
    /// one way and holds the kept list to a fresh walk of the building list: a rock spawned, a rock mined (which
    /// opens the one behind it), and ground turned to deep water round a face (walkability alone, no building
    /// coming or going) — and a rock spawned on water and mined away, the building list alone, no walkability flip.
    /// </summary>
    public class MiningFaceCacheTests : ContentTestBase
    {
        public MiningFaceCacheTests(CoreContentFixture content) : base(content)
        {
        }

        private static TerrainDef WaterDeep => DefDatabase<TerrainDef>.GetNamed("WaterDeep");

        private static Thing Rock(CoreMap map, int x, int z) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), new IntVec3(x, 0, z), map);

        /// <summary>A 6x6 block of rock in a 20x20 field: twenty faces round a buried core of sixteen.</summary>
        private static CoreMap Quarry()
        {
            var map = new CoreMap(20, 20, SimWorld.Map.TerrainDefOf.Soil);
            for (int z = 7; z < 13; z++)
            {
                for (int x = 7; x < 13; x++) Rock(map, x, z);
            }
            return map;
        }

        /// <summary>The walk the cache saves: every mineable rock with a walkable cell beside it, in list order.</summary>
        private static List<Thing> Fresh(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .Where(t => t.def.mineable && t.Spawned && MiningInitiative.FaceReachable(map, t.Position, null))
                .ToList();

        [Fact]
        public void The_kept_faces_are_the_walked_faces()
        {
            CoreMap map = Quarry();
            Assert.Equal(20, MiningInitiative.FaceCandidates(map).Count);
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));
            // Asked again with nothing changed, the same list comes back without a walk.
            Assert.Same(MiningInitiative.FaceCandidates(map), MiningInitiative.FaceCandidates(map));
        }

        [Fact]
        public void A_rock_spawned_in_the_open_is_a_face_at_once()
        {
            CoreMap map = Quarry();
            MiningInitiative.FaceCandidates(map);
            Thing boulder = Rock(map, 2, 2);

            Assert.Contains(boulder, MiningInitiative.FaceCandidates(map));
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));
        }

        [Fact]
        public void Mining_a_face_drops_it_and_opens_the_rock_behind()
        {
            CoreMap map = Quarry();
            MiningInitiative.FaceCandidates(map);
            Thing face = map.edificeGrid[new IntVec3(7, 0, 9)]!;
            Thing behind = map.edificeGrid[new IntVec3(8, 0, 9)]!;
            Assert.DoesNotContain(behind, MiningInitiative.FaceCandidates(map));

            face.Destroy();

            Assert.DoesNotContain(face, MiningInitiative.FaceCandidates(map));
            Assert.Contains(behind, MiningInitiative.FaceCandidates(map));
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));
        }

        [Fact]
        public void Ground_turned_to_deep_water_closes_a_face_with_no_building_changed()
        {
            CoreMap map = Quarry();
            Thing corner = map.edificeGrid[new IntVec3(7, 0, 7)]!;
            Assert.Contains(corner, MiningInitiative.FaceCandidates(map));
            int buildings = map.listerThings.GroupVersion(ThingRequestGroup.Building);

            // Every open cell round the corner rock goes under water: rock on two sides, deep water on the rest.
            foreach (IntVec3 c in GenAdj.AdjacentCells.Select(d => corner.Position + d))
            {
                if (map.edificeGrid[c] == null) map.terrainGrid.SetTerrain(c, WaterDeep);
            }

            Assert.Equal(buildings, map.listerThings.GroupVersion(ThingRequestGroup.Building));
            Assert.DoesNotContain(corner, MiningInitiative.FaceCandidates(map));
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));
        }

        [Fact]
        public void A_rock_on_water_comes_and_goes_with_no_walkability_changed()
        {
            CoreMap map = Quarry();
            var pool = new IntVec3(3, 0, 3);
            map.terrainGrid.SetTerrain(pool, WaterDeep);
            MiningInitiative.FaceCandidates(map);
            int walkability = map.pathGrid.WalkabilityVersion;

            Thing islet = Rock(map, pool.x, pool.z);

            Assert.Equal(walkability, map.pathGrid.WalkabilityVersion);
            Assert.Contains(islet, MiningInitiative.FaceCandidates(map));
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));

            // And mined away again: the water stays unwalkable, so only the building list says it is gone.
            islet.Destroy();

            Assert.Equal(walkability, map.pathGrid.WalkabilityVersion);
            Assert.DoesNotContain(islet, MiningInitiative.FaceCandidates(map));
            Assert.Equal(Fresh(map), MiningInitiative.FaceCandidates(map));
        }
    }
}
