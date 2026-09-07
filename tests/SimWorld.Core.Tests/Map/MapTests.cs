using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>Geometry, grids, Thing/ThingWithComps and Scribe round trips for the map core (RimWorld's Verse.Map layer).</summary>
    public class MapTests : ContentTestBase
    {
        public MapTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ, TerrainDef? fill = null) =>
            new CoreMap(sizeX, sizeZ, fill ?? TerrainDefOf.Soil);

        private static ThingDef Rock(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        // ---- IntVec3 / IntVec2 ----

        [Fact]
        public void IntVec3_distance_and_length_match_expected_values()
        {
            var a = new IntVec3(0, 0, 0);
            var b = new IntVec3(3, 0, 4);

            Assert.Equal(5f, a.DistanceTo(b), 3);
            Assert.Equal(25, a.DistanceToSquared(b));
            Assert.Equal(5f, b.LengthHorizontal, 3);
            Assert.True(a.IsValid);
            Assert.False(IntVec3.Invalid.IsValid);
            Assert.Equal(new IntVec3(3, 0, 4), a + b);
            Assert.Equal(new IntVec3(-3, 0, -4), a - b);
        }

        [Fact]
        public void IntVec3_adjacency_helpers_identify_neighbors()
        {
            var center = new IntVec3(5, 0, 5);

            Assert.True(center.AdjacentToCardinal(new IntVec3(6, 0, 5)));
            Assert.False(center.AdjacentToCardinal(new IntVec3(6, 0, 6)));

            Assert.True(center.AdjacentTo8Way(new IntVec3(6, 0, 6)));
            Assert.False(center.AdjacentTo8Way(new IntVec3(7, 0, 5)));

            Assert.True(center.InHorDistOf(new IntVec3(7, 0, 5), 2f));
            Assert.False(center.InHorDistOf(new IntVec3(8, 0, 5), 2f));

            Assert.Equal(new IntVec2(5, 5), center.ToIntVec2());
        }

        // ---- Rot4 ----

        [Fact]
        public void Rot4_rotated_and_opposite_cycle_through_facings()
        {
            Rot4 north = Rot4.North;

            Assert.Equal(Rot4.East, north.Rotated(RotationDirection.Clockwise));
            Assert.Equal(Rot4.West, north.Rotated(RotationDirection.Counterclockwise));
            Assert.Equal(Rot4.South, north.Opposite);
            Assert.False(north.IsHorizontal);
            Assert.True(Rot4.East.IsHorizontal);
            Assert.Equal(default, north);
        }

        [Fact]
        public void Rot4_facing_cell_matches_direction()
        {
            Assert.Equal(new IntVec3(0, 0, 1), Rot4.North.FacingCell);
            Assert.Equal(new IntVec3(1, 0, 0), Rot4.East.FacingCell);
            Assert.Equal(new IntVec3(0, 0, -1), Rot4.South.FacingCell);
            Assert.Equal(new IntVec3(-1, 0, 0), Rot4.West.FacingCell);
        }

        // ---- CellRect ----

        [Fact]
        public void CellRect_cells_enumerates_every_cell_once_in_bounds()
        {
            var rect = new CellRect(2, 3, 4, 2);
            List<IntVec3> cells = rect.Cells.ToList();

            Assert.Equal(8, cells.Count);
            Assert.Equal(8, cells.Distinct().Count());
            Assert.Contains(new IntVec3(2, 0, 3), cells);
            Assert.Contains(new IntVec3(5, 0, 4), cells);
            Assert.DoesNotContain(new IntVec3(6, 0, 3), cells);
        }

        [Fact]
        public void CellRect_contains_and_expanded_by_grow_and_shrink()
        {
            CellRect rect = CellRect.CenteredOn(new IntVec3(5, 0, 5), 1);

            Assert.True(rect.Contains(new IntVec3(5, 0, 5)));
            Assert.False(rect.Contains(new IntVec3(7, 0, 5)));

            CellRect grown = rect.ExpandedBy(1);
            Assert.Equal(25, grown.Area);

            CellRect shrunk = grown.ContractedBy(1);
            Assert.Equal(rect, shrunk);
        }

        [Fact]
        public void CellRect_clip_inside_map_truncates_at_edges()
        {
            CoreMap map = NewMap(5, 5);

            var rect = new CellRect(-2, -2, 4, 4);
            CellRect clipped = rect.ClipInsideMap(map);
            Assert.False(clipped.IsEmpty);
            Assert.Equal(0, clipped.minX);
            Assert.Equal(0, clipped.minZ);
            Assert.True(clipped.maxX < 5 && clipped.maxZ < 5);

            var outside = new CellRect(10, 10, 2, 2);
            Assert.True(outside.ClipInsideMap(map).IsEmpty);
        }

        // ---- GenAdj ----

        [Fact]
        public void GenAdj_cardinal_and_8way_neighbors_are_correct()
        {
            var c = new IntVec3(3, 0, 3);

            List<IntVec3> cardinal = GenAdj.CellsAdjacentCardinal(c).ToList();
            Assert.Equal(4, cardinal.Count);
            Assert.Contains(new IntVec3(4, 0, 3), cardinal);
            Assert.DoesNotContain(new IntVec3(4, 0, 4), cardinal);

            List<IntVec3> all8 = GenAdj.CellsAdjacent8Way(c).ToList();
            Assert.Equal(8, all8.Count);
            Assert.Contains(new IntVec3(4, 0, 4), all8);
            Assert.DoesNotContain(c, all8);
        }

        [Fact]
        public void GenAdj_occupied_rect_for_a_2x3_building_rotates_with_facing()
        {
            var center = new IntVec3(10, 0, 10);
            var size = new IntVec2(2, 3);

            CellRect north = GenAdj.OccupiedRect(center, Rot4.North, size);
            Assert.Equal(2, north.Width);
            Assert.Equal(3, north.Height);
            Assert.Equal(10, north.minX);
            Assert.Equal(9, north.minZ);

            CellRect east = GenAdj.OccupiedRect(center, Rot4.East, size);
            Assert.Equal(3, east.Width);
            Assert.Equal(2, east.Height);
            Assert.Equal(9, east.minX);
            Assert.Equal(10, east.minZ);
        }

        // ---- GenRadial ----

        [Fact]
        public void GenRadial_num_cells_in_radius_matches_known_counts()
        {
            Assert.Equal(5, GenRadial.NumCellsInRadius(1f));
            Assert.Equal(13, GenRadial.NumCellsInRadius(2f));

            int bruteForce = 0;
            for (int x = -6; x <= 6; x++)
            {
                for (int z = -6; z <= 6; z++)
                {
                    if (x * x + z * z <= 25) bruteForce++;
                }
            }
            Assert.Equal(bruteForce, GenRadial.NumCellsInRadius(5f));
        }

        [Fact]
        public void GenRadial_radial_pattern_is_sorted_by_distance()
        {
            List<IntVec3> pattern = GenRadial.RadialPatternInRadius(5f).ToList();
            Assert.Equal(IntVec3.Zero, pattern[0]);
            for (int i = 1; i < pattern.Count; i++)
            {
                Assert.True(pattern[i - 1].LengthHorizontalSquared <= pattern[i].LengthHorizontalSquared);
            }

            List<IntVec3> around = GenRadial.RadialCellsAround(new IntVec3(20, 0, 20), 1f, useCenter: false).ToList();
            Assert.Equal(4, around.Count);
            Assert.DoesNotContain(new IntVec3(20, 0, 20), around);
        }

        // ---- CellIndices ----

        [Fact]
        public void CellIndices_round_trips_cell_to_index_and_back()
        {
            var indices = new CellIndices(7, 4);
            Assert.Equal(28, indices.NumGridCells);

            var cell = new IntVec3(5, 0, 2);
            int idx = indices.CellToIndex(cell);
            Assert.Equal(cell, indices.IndexToCell(idx));
            Assert.Equal(idx, indices.CellToIndex(cell.x, cell.z));
        }

        // ---- Map / grids ----

        [Fact]
        public void Map_constructor_fills_every_cell_with_the_given_terrain()
        {
            CoreMap map = NewMap(6, 4, TerrainDefOf.Gravel);

            Assert.Equal(6, map.Size.x);
            Assert.Equal(4, map.Size.z);
            Assert.Equal(24, map.Area);
            foreach (IntVec3 c in map.AllCells)
            {
                Assert.Same(TerrainDefOf.Gravel, map.terrainGrid.TerrainAt(c));
            }
        }

        [Fact]
        public void SetTerrain_updates_the_terrain_and_the_path_cost()
        {
            CoreMap map = NewMap(3, 3, TerrainDefOf.Soil);
            var cell = new IntVec3(1, 0, 1);

            int before = map.pathGrid.PerceivedPathCostAt(cell);
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.WaterShallow);

            Assert.Same(TerrainDefOf.WaterShallow, map.terrainGrid.TerrainAt(cell));
            int after = map.pathGrid.PerceivedPathCostAt(cell);
            Assert.NotEqual(before, after);
            Assert.Equal(TerrainDefOf.WaterShallow.pathCost, after);
        }

        [Fact]
        public void Deep_water_is_impassable_shallow_water_is_walkable_and_standable()
        {
            CoreMap map = NewMap(3, 3, TerrainDefOf.Soil);
            var shallow = new IntVec3(0, 0, 0);
            var deep = new IntVec3(1, 0, 0);
            map.terrainGrid.SetTerrain(shallow, TerrainDefOf.WaterShallow);
            map.terrainGrid.SetTerrain(deep, TerrainDefOf.WaterDeep);

            Assert.True(GenGrid.Walkable(shallow, map));
            Assert.True(GenGrid.Standable(shallow, map));

            Assert.False(GenGrid.Walkable(deep, map));
            Assert.False(GenGrid.Standable(deep, map));
        }

        [Fact]
        public void RoofGrid_set_and_get_round_trip()
        {
            CoreMap map = NewMap(3, 3);
            var cell = new IntVec3(1, 0, 1);

            Assert.False(map.roofGrid.Roofed(cell));
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            Assert.True(map.roofGrid.Roofed(cell));
            Assert.Same(RoofDefOf.RoofRockThin, map.roofGrid.RoofAt(cell));
            Assert.True(GenGrid.Roofed(cell, map));

            map.roofGrid.SetRoof(cell, null);
            Assert.False(map.roofGrid.Roofed(cell));
        }

        // ---- Thing / ThingMaker / spawning ----

        [Fact]
        public void ThingMaker_makes_a_rock_with_full_hit_points()
        {
            ThingDef def = Rock("Sandstone");
            Thing rock = ThingMaker.MakeThing(def);

            Assert.Equal(400, def.BaseMaxHitPoints);
            Assert.Equal(400, rock.HitPoints);
            Assert.Equal(400, rock.MaxHitPoints);
            Assert.False(rock.Spawned);
            Assert.True(rock.thingIDNumber >= 0);
        }

        [Fact]
        public void Spawning_a_rock_registers_it_in_every_grid_and_blocks_the_cell()
        {
            CoreMap map = NewMap(5, 5);
            var cell = new IntVec3(2, 0, 2);
            Thing rock = ThingMaker.MakeThing(Rock("Granite"));
            GenSpawn.Spawn(rock, cell, map);

            Assert.True(rock.Spawned);
            Assert.Same(map, rock.Map);
            Assert.Contains(rock, map.listerThings.AllThings);
            Assert.Contains(rock, map.thingGrid.ThingsAt(cell));
            Assert.Same(rock, map.edificeGrid[cell]);
            Assert.False(GenGrid.Walkable(cell, map));
            Assert.True(map.pathGrid.PerceivedPathCostAt(cell) >= PathGrid.ImpassableCost);
        }

        [Fact]
        public void DeSpawn_reverts_grid_registration_and_path_cost()
        {
            CoreMap map = NewMap(5, 5);
            var cell = new IntVec3(1, 0, 1);
            Thing rock = ThingMaker.MakeThing(Rock("Sandstone"));
            GenSpawn.Spawn(rock, cell, map);

            rock.DeSpawn();

            Assert.False(rock.Spawned);
            Assert.Null(rock.Map);
            Assert.DoesNotContain(rock, map.listerThings.AllThings);
            Assert.DoesNotContain(rock, map.thingGrid.ThingsAt(cell));
            Assert.Null(map.edificeGrid[cell]);
            Assert.True(GenGrid.Walkable(cell, map));
        }

        [Fact]
        public void TakeDamage_reduces_hit_points_and_destroys_at_zero()
        {
            var def = new ThingDef
            {
                defName = "TestDamageable",
                category = ThingCategory.Item,
                thingClass = typeof(Thing),
                useHitPoints = true,
                destroyable = true,
                statBases = new List<StatModifier> { new StatModifier(DefDatabase<StatDef>.GetNamed("MaxHitPoints"), 10f) },
            };
            Thing thing = ThingMaker.MakeThing(def);
            Assert.Equal(10, thing.HitPoints);

            DamageResult r1 = thing.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f));
            Assert.Equal(6, thing.HitPoints);
            Assert.True(r1.wounded);
            Assert.False(thing.Destroyed);

            DamageResult r2 = thing.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 10f));
            Assert.Equal(0, thing.HitPoints);
            Assert.True(thing.Destroyed);
            Assert.Equal(4f, r1.totalDamageDealt, 3);
            Assert.NotNull(r2);
        }

        [Fact]
        public void Destroy_despawns_a_spawned_thing()
        {
            CoreMap map = NewMap(4, 4);
            Thing rock = ThingMaker.MakeThing(Rock("Limestone"));
            GenSpawn.Spawn(rock, new IntVec3(1, 0, 1), map);

            rock.Destroy();

            Assert.True(rock.Destroyed);
            Assert.False(rock.Spawned);
            Assert.DoesNotContain(rock, map.listerThings.AllThings);
        }

        [Fact]
        public void ThingWithComps_initializes_and_ticks_its_comps()
        {
            // Hand-built, throwaway ThingDef — never registered in the shared content DefDatabase.
            var def = new ThingDef
            {
                defName = "TestCompThing",
                category = ThingCategory.Item,
                thingClass = typeof(ThingWithComps),
                comps = new List<CompProperties> { new TestCompProperties { startValue = 5 } },
            };

            var thing = (ThingWithComps)ThingMaker.MakeThing(def);
            TestComp? comp = thing.GetComp<TestComp>();
            Assert.NotNull(comp);
            Assert.Equal(5, comp!.value);

            thing.Tick();
            Assert.Equal(6, comp.value);
            Assert.Same(comp, thing.TryGetComp<TestComp>());
        }

        [Fact]
        public void ListerThings_groups_spawned_things_by_category_and_def()
        {
            CoreMap map = NewMap(4, 4);
            Thing rock = ThingMaker.MakeThing(Rock("Sandstone"));
            GenSpawn.Spawn(rock, new IntVec3(0, 0, 0), map);
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);

            Assert.Contains(rock, map.listerThings.ThingsInGroup(ThingRequestGroup.Building));
            Assert.Contains(pawn, map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn));
            Assert.Contains(rock, map.listerThings.ThingsOfDef(rock.def));
            Assert.Equal(2, map.listerThings.ThingsInGroup(ThingRequestGroup.Everything).Count);
        }

        // ---- Pawn is a Thing ----

        [Fact]
        public void Spawned_pawn_registers_in_map_pawns_and_thing_grid()
        {
            CoreMap map = NewMap(5, 5);
            Pawn p = NewHuman();
            var cell = new IntVec3(2, 0, 2);

            GenSpawn.Spawn(p, cell, map);

            Assert.True(p.Spawned);
            Assert.Same(map, p.Map);
            Assert.Equal(cell, p.Position);
            Assert.Contains(p, map.mapPawns.AllPawns);
            Assert.Contains(p, map.thingGrid.ThingsAt(cell));
        }

        [Fact]
        public void Spawned_pawn_ticks_via_TickManager_without_RunTicks()
        {
            CoreMap map = NewMap(5, 5);
            Pawn p = NewHuman();
            GenSpawn.Spawn(p, new IntVec3(2, 0, 2), map);

            float before = p.needs.food!.CurLevel;
            // Deliberately not using the ContentTestBase.RunTicks helper: GenSpawn.Spawn must have
            // registered the pawn with Find.TickManager on its own.
            for (int i = 0; i < 200; i++)
            {
                Find.TickManager.DoSingleTick();
            }
            Assert.True(p.needs.food.CurLevel < before);
        }

        // ---- Scribe ----

        [Fact]
        public void Map_round_trips_terrain_roof_and_spawned_things_through_scribe()
        {
            CoreMap map = NewMap(10, 10, TerrainDefOf.Soil);
            map.terrainGrid.SetTerrain(new IntVec3(1, 0, 1), TerrainDefOf.Sand);
            map.terrainGrid.SetTerrain(new IntVec3(2, 0, 2), TerrainDefOf.WaterShallow);
            map.roofGrid.SetRoof(new IntVec3(5, 0, 5), RoofDefOf.RoofConstructed);

            ThingDef rockDef = Rock("Sandstone");
            Thing rock = ThingMaker.MakeThing(rockDef);
            GenSpawn.Spawn(rock, new IntVec3(3, 0, 3), map);

            Pawn pawn = NewHuman("Rescuer");
            pawn.needs.food!.CurLevel = 0.5f;
            GenSpawn.Spawn(pawn, new IntVec3(4, 0, 4), map);

            int maxId = Math.Max(rock.thingIDNumber, pawn.thingIDNumber);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(10, loaded.Size.x);
            Assert.Equal(10, loaded.Size.z);
            Assert.Same(TerrainDefOf.Sand, loaded.terrainGrid.TerrainAt(new IntVec3(1, 0, 1)));
            Assert.Same(TerrainDefOf.WaterShallow, loaded.terrainGrid.TerrainAt(new IntVec3(2, 0, 2)));
            Assert.Same(TerrainDefOf.Soil, loaded.terrainGrid.TerrainAt(new IntVec3(0, 0, 0)));
            Assert.Same(RoofDefOf.RoofConstructed, loaded.roofGrid.RoofAt(new IntVec3(5, 0, 5)));
            Assert.Null(loaded.roofGrid.RoofAt(new IntVec3(0, 0, 0)));

            Thing? loadedRock = loaded.edificeGrid[new IntVec3(3, 0, 3)];
            Assert.NotNull(loadedRock);
            Assert.Equal(rockDef.BaseMaxHitPoints, loadedRock!.HitPoints);
            Assert.True(loadedRock.Spawned);

            Pawn? loadedPawn = loaded.mapPawns.AllPawns.FirstOrDefault(x => x.name == "Rescuer");
            Assert.NotNull(loadedPawn);
            Assert.Equal(new IntVec3(4, 0, 4), loadedPawn!.Position);
            Assert.True(loadedPawn.Spawned);
            Assert.Equal(0.5f, loadedPawn.needs.food!.CurLevel);
            Assert.Contains(loadedPawn, loaded.thingGrid.ThingsAt(new IntVec3(4, 0, 4)));

            Assert.True(Thing.AllocateThingId() > maxId);
        }

        // ---- throwaway comp pair for the ThingWithComps test above ----

        private sealed class TestCompProperties : CompProperties
        {
            public int startValue;

            public TestCompProperties()
            {
                compClass = typeof(TestComp);
            }
        }

        private sealed class TestComp : ThingComp
        {
            public int value;

            public override void Initialize(CompProperties props)
            {
                base.Initialize(props);
                value = ((TestCompProperties)props).startValue;
            }

            public override void CompTick() => value++;
        }
    }
}
