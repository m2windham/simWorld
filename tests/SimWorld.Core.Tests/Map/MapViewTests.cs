using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The read model the Unity host binds to for a settlement interior (<c>docs/spec/simworld-spec.md</c>
    /// §12a, and <c>docs/host/rendering-method.md</c>, which is the brief this came from): a snapshot of
    /// values the host can draw without holding a Def, a Thing or a Map.
    ///
    /// <para/>This mirrors <c>God/View</c>'s <c>GodViewTests</c> one scale down, including the structural test
    /// that the seam hands out no live reference — that is the premise the whole thing rests on, and it is the
    /// kind of property that decays silently the first time somebody adds a convenient field.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewTests : ContentTestBase
    {
        public MapViewTests(CoreContentFixture content) : base(content)
        {
            MapViewTracker.ResetSessionIdCounter();
        }

        private static CoreMap NewMap(int sizeX = 40, int sizeZ = 40) =>
            new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static Thing SpawnRock(CoreMap map, IntVec3 at, string defName = "Sandstone")
        {
            Thing rock = ThingMaker.MakeThing(Def(defName));
            return GenSpawn.Spawn(rock, at, map);
        }

        // ---- the invariant that makes the seam trustworthy ----

        /// <summary>
        /// The structural premise, kept as a test for the same reason <c>GodViewTests</c> keeps its own: a
        /// snapshot hands out names and values, never a Def, never a live simulation object, never a mutable
        /// collection the host could write back through — and never an engine type, since the core is
        /// engine-free by rule and a <c>UnityEngine.Vector3</c> on this seam would be the first breach.
        ///
        /// <para/>It walks every public property of every type in the <c>Map/View</c> namespace rather than a
        /// hand-listed few, so a type added later is covered without anyone remembering to add it here.
        /// </summary>
        [Fact]
        public void The_read_model_hands_out_no_def_no_live_object_and_no_engine_type()
        {
            IEnumerable<Type> viewTypes = typeof(MapViewSnapshot).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && t.Namespace == "SimWorld.Map.View");

            var checkedTypes = 0;
            foreach (Type type in viewTypes)
            {
                // The tracker lives on the simulation's side of the seam: the host never holds one, because
                // it never holds a Map to reach one from. Everything else in the namespace is the read model.
                if (type == typeof(MapViewTracker)) continue;
                checkedTypes++;

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string where = type.Name + "." + property.Name;
                    Type t = property.PropertyType;

                    Assert.False(typeof(Def).IsAssignableFrom(t),
                        where + " exposes a Def — the host could reach its workers through it");
                    Assert.False(typeof(Thing).IsAssignableFrom(t),
                        where + " exposes a live Thing — that is a write surface into the simulation");
                    Assert.False(typeof(CoreMap).IsAssignableFrom(t),
                        where + " exposes the Map itself");
                    Assert.False(typeof(Pawn).IsAssignableFrom(t),
                        where + " exposes a live Pawn");

                    AssertNotAMutableCollection(t, where);
                    AssertNoEngineType(t, where);
                    foreach (Type argument in t.IsGenericType ? t.GetGenericArguments() : Array.Empty<Type>())
                    {
                        Assert.False(typeof(Def).IsAssignableFrom(argument),
                            where + " exposes a collection of Defs");
                        Assert.False(typeof(Thing).IsAssignableFrom(argument),
                            where + " exposes a collection of live Things");
                        AssertNoEngineType(argument, where);
                    }
                }
            }

            // Guards the guard: a namespace typo here would pass vacuously for ever.
            Assert.True(checkedTypes >= 6, "expected the whole Map/View read model, found " + checkedTypes + " types");
        }

        private static void AssertNotAMutableCollection(Type t, string where)
        {
            if (t == typeof(string)) return;
            Assert.False(t.IsArray, where + " exposes an array the host can write through");
            if (!typeof(IEnumerable).IsAssignableFrom(t)) return;

            // IReadOnlyList<T> is the one collection shape allowed: it copies nothing but promises nothing
            // back either. A List<T>, an IList<T> or an ICollection<T> would let the host mutate the very
            // list the snapshot handed it — harmless to the simulation, but it makes the snapshot mutable
            // state two parts of the host can disagree about, which is the bug this seam exists to prevent.
            bool readOnly = t.IsGenericType
                && (t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                    || t.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>));
            Assert.True(readOnly, where + " exposes " + t.Name + " — collections on this seam are IReadOnlyList<T>");
        }

        private static void AssertNoEngineType(Type t, string where)
        {
            string ns = t.Namespace ?? "";
            Assert.False(ns.StartsWith("UnityEngine", StringComparison.Ordinal),
                where + " exposes an engine type — SimWorld.Core is engine-free by rule");
            Assert.False(ns.StartsWith("System.Drawing", StringComparison.Ordinal),
                where + " exposes a System.Drawing type — colour is the host's business, not the core's");
        }

        // ---- the full capture ----

        [Fact]
        public void A_full_capture_describes_every_cell_of_terrain()
        {
            CoreMap map = NewMap(40, 40);
            map.terrainGrid.SetTerrain(new IntVec3(3, 0, 4), TerrainDefOf.Sand);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            Assert.True(snapshot.HasMap);
            Assert.Null(snapshot.AbsenceReason);
            Assert.Equal(40, snapshot.SizeX);
            Assert.Equal(40, snapshot.SizeZ);
            Assert.Equal(map.cellIndices.NumGridCells, snapshot.Terrain.Cells.Count);
            Assert.Equal(map.cellIndices.NumGridCells, snapshot.Roofs.Cells.Count);

            // Every cell resolves to a terrain that exists in content — the host can look every one of them
            // up in its own registry, which is the only thing a terrain defName is for.
            foreach (string name in snapshot.Terrain.Palette)
            {
                Assert.NotNull(DefDatabase<TerrainDef>.GetNamedSilentFail(name));
            }
            Assert.Equal(TerrainDefOf.Sand.defName, snapshot.Terrain.TerrainAt(3, 4));
            Assert.Equal(TerrainDefOf.Soil.defName, snapshot.Terrain.TerrainAt(0, 0));
        }

        [Fact]
        public void A_full_capture_places_things_and_pawns_where_the_map_has_them()
        {
            CoreMap map = NewMap();
            Thing rock = SpawnRock(map, new IntVec3(5, 0, 6));
            Pawn pawn = NewHuman("Walker");
            GenSpawn.Spawn(pawn, new IntVec3(9, 0, 9), map);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            ThingView view = Assert.Single(snapshot.AllThings(), t => t.ThingId == rock.thingIDNumber);
            Assert.Equal(rock.def.defName, view.DefName);
            Assert.Equal(new IntVec3(5, 0, 6), view.Position);
            Assert.Equal(ThingCategory.Building, view.Category);

            PawnView pawnView = Assert.Single(snapshot.Pawns);
            Assert.Equal(pawn.thingIDNumber, pawnView.ThingId);
            Assert.Equal(new IntVec3(9, 0, 9), pawnView.Position);
            Assert.Equal("Human", pawnView.DefName);
            Assert.False(pawnView.Dead);
            Assert.False(pawnView.Downed);
        }

        /// <summary>
        /// Pawns are the one family that never appears among the Things, because they are the one family
        /// re-captured whole on every read. A pawn showing up in both would be drawn twice.
        /// </summary>
        [Fact]
        public void A_pawn_is_a_pawn_and_never_also_a_thing()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman("Only once");
            GenSpawn.Spawn(pawn, new IntVec3(9, 0, 9), map);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            Assert.Single(snapshot.Pawns);
            Assert.DoesNotContain(snapshot.AllThings(), t => t.ThingId == pawn.thingIDNumber);
            Assert.DoesNotContain(snapshot.AllThings(), t => t.Category == ThingCategory.Pawn);
        }

        [Fact]
        public void Every_cell_of_the_map_belongs_to_exactly_one_chunk()
        {
            // A map whose size is not a multiple of the chunk edge, so the short edge chunks are exercised:
            // a host that assumed every chunk square would draw a strip of nothing down two sides.
            CoreMap map = NewMap(70, 50);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            var covered = new HashSet<(int, int)>();
            foreach (MapViewChunk chunk in snapshot.Chunks)
            {
                for (int z = chunk.MinZ; z < chunk.MinZ + chunk.Height; z++)
                {
                    for (int x = chunk.MinX; x < chunk.MinX + chunk.Width; x++)
                    {
                        Assert.True(covered.Add((x, z)), "cell (" + x + ", " + z + ") is in two chunks");
                    }
                }
            }
            Assert.Equal(70 * 50, covered.Count);
        }

        [Fact]
        public void A_roofed_cell_reports_its_roof_and_an_open_one_reports_none()
        {
            CoreMap map = NewMap();
            map.roofGrid.SetRoof(new IntVec3(2, 0, 3), RoofDefOf.RoofConstructed);

            RoofLayer roofs = MapViewSnapshot.Capture(map).Roofs;

            Assert.Equal(RoofDefOf.RoofConstructed.defName, roofs.RoofAt(2, 3));
            Assert.True(roofs.IsRoofed(2, 3));
            Assert.Null(roofs.RoofAt(0, 0));
            Assert.False(roofs.IsRoofed(0, 0));
        }

        /// <summary>
        /// Overhead mountain is the roof fact a host most needs and the one it is likeliest to get wrong. The
        /// distinction is the core's — <c>RoofDef.isThickRoof</c> — so it crosses the seam as a flag rather
        /// than leaving every host to recognise the string "RoofRockThick" and draw a mountain as open sky
        /// when it does not.
        /// </summary>
        [Fact]
        public void Overhead_mountain_is_distinguishable_without_knowing_a_defName()
        {
            CoreMap map = NewMap();
            RoofDef thick = RoofDefOf.RoofRockThick;
            map.roofGrid.SetRoof(new IntVec3(2, 0, 2), thick);
            map.roofGrid.SetRoof(new IntVec3(3, 0, 3), RoofDefOf.RoofConstructed);

            RoofLayer roofs = MapViewSnapshot.Capture(map).Roofs;

            Assert.True(roofs.IsThickRoofed(2, 2));
            Assert.True(roofs.IsRoofed(3, 3));
            Assert.False(roofs.IsThickRoofed(3, 3));
            Assert.False(roofs.IsThickRoofed(0, 0));

            // The flags come from the def, not from a name the view pattern-matched.
            RoofView view = Assert.Single(roofs.Palette, r => r.DefName == thick.defName);
            Assert.Equal(thick.isThickRoof, view.IsThickRoof);
            Assert.Equal(thick.isNatural, view.IsNatural);
        }

        // ---- the incremental read, which is the whole reason this is not one Capture() ----

        [Fact]
        public void A_map_that_did_not_change_sends_back_nothing_but_its_pawns()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(5, 0, 5));
            Pawn pawn = NewHuman("Stander");
            GenSpawn.Spawn(pawn, new IntVec3(9, 0, 9), map);

            MapViewSnapshot first = MapViewSnapshot.Capture(map);
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, first.Versions);

            Assert.False(delta.FullResync);
            Assert.Null(delta.Terrain);
            Assert.Null(delta.Roofs);
            Assert.Empty(delta.ChangedChunks);

            // Pawns are never conditional: the host redraws them every frame from this list.
            Assert.Single(delta.Pawns);
        }

        /// <summary>
        /// The load-bearing one. If a walking pawn dirtied its chunk, every step would drag that chunk's
        /// thousand rocks back across the seam — which is exactly the cost the chunking exists to avoid, and
        /// it would look like it was working right up until somebody measured a real map.
        /// </summary>
        [Fact]
        public void A_pawn_walking_across_the_map_dirties_no_chunk()
        {
            CoreMap map = NewMap();
            for (int i = 0; i < 20; i++) SpawnRock(map, new IntVec3(i, 0, 1));

            Pawn pawn = NewHuman("Walker");
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 10), map);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            // Straight across two chunk boundaries, one cell at a time, the way a pather moves one.
            for (int x = 1; x < 39; x++) pawn.Position = new IntVec3(x, 0, 10);

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.Empty(delta.ChangedChunks);
            Assert.Equal(new IntVec3(38, 0, 10), Assert.Single(delta.Pawns).Position);
        }

        [Fact]
        public void A_thing_spawning_dirties_its_own_chunk_and_no_other()
        {
            CoreMap map = NewMap(70, 70);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            var at = new IntVec3(5, 0, 5);
            Thing rock = SpawnRock(map, at);

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            MapViewChunk chunk = Assert.Single(delta.ChangedChunks);
            Assert.Equal(map.mapView.ChunkIndexAt(at), chunk.Index);
            Assert.Contains(chunk.Things, t => t.ThingId == rock.thingIDNumber);

            // And the rest of the map is not re-sent: on a 70x70 map that is 8 of 9 chunks left alone, which
            // is the property that makes this affordable at 200x200.
            Assert.True(map.mapView.ChunkCount > 1);
            Assert.Null(delta.Terrain);
        }

        [Fact]
        public void A_thing_despawning_dirties_its_chunk_and_leaves_it_out_of_the_redraw()
        {
            CoreMap map = NewMap();
            var at = new IntVec3(5, 0, 5);
            Thing rock = SpawnRock(map, at);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            rock.DeSpawn();

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            MapViewChunk chunk = Assert.Single(delta.ChangedChunks);
            Assert.DoesNotContain(chunk.Things, t => t.ThingId == rock.thingIDNumber);
        }

        [Fact]
        public void Terrain_and_roofs_move_on_their_own_versions()
        {
            CoreMap map = NewMap();
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            map.terrainGrid.SetTerrain(new IntVec3(1, 0, 1), TerrainDefOf.Sand);
            MapViewDelta afterTerrain = MapViewSnapshot.CaptureChanges(map, held);

            Assert.NotNull(afterTerrain.Terrain);
            Assert.Null(afterTerrain.Roofs);
            Assert.Equal(TerrainDefOf.Sand.defName, afterTerrain.Terrain!.TerrainAt(1, 1));

            held = afterTerrain.Versions;
            map.roofGrid.SetRoof(new IntVec3(2, 0, 2), RoofDefOf.RoofConstructed);
            MapViewDelta afterRoof = MapViewSnapshot.CaptureChanges(map, held);

            Assert.Null(afterRoof.Terrain);
            Assert.NotNull(afterRoof.Roofs);
            Assert.True(afterRoof.Roofs!.IsRoofed(2, 2));
        }

        [Fact]
        public void The_first_read_and_a_stale_one_both_come_back_as_a_full_resync()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(5, 0, 5));

            MapViewDelta first = MapViewSnapshot.CaptureChanges(map, null);

            Assert.True(first.FullResync);
            Assert.NotNull(first.Terrain);
            Assert.NotNull(first.Roofs);
            Assert.Equal(map.mapView.ChunkCount, first.ChangedChunks.Count);

            // Versions belonging to some other map come back the same way: a wasted frame, never a wrong
            // picture. Without the session id a reloaded map whose versions restarted at the held numbers
            // would read as "nothing changed" and the host would draw the previous game's town.
            CoreMap other = NewMap();
            MapViewDelta foreign = MapViewSnapshot.CaptureChanges(other, first.Versions);

            Assert.NotEqual(first.SessionId, foreign.SessionId);
            Assert.True(foreign.FullResync);
        }

        // ---- state, and the deliberate lack of it ----

        /// <summary>
        /// The seam holds nothing that needs saving — the same choice <c>God/View</c> made, and the reason
        /// there is no Scribe round-trip for a view type to have. What this asserts instead is the
        /// consequence: a map that round-trips through Scribe still describes itself correctly afterwards, and
        /// the host is told to throw its cache away rather than being silently handed versions that mean
        /// something else now.
        /// </summary>
        [Fact]
        public void A_map_round_tripped_through_scribe_redescribes_itself_and_forces_a_resync()
        {
            CoreMap map = NewMap(20, 20);
            map.terrainGrid.SetTerrain(new IntVec3(1, 0, 1), TerrainDefOf.Sand);
            map.roofGrid.SetRoof(new IntVec3(2, 0, 2), RoofDefOf.RoofConstructed);
            Thing rock = SpawnRock(map, new IntVec3(3, 0, 3));
            Pawn pawn = NewHuman("Sleeper");
            GenSpawn.Spawn(pawn, new IntVec3(4, 0, 4), map);

            MapViewSnapshot before = MapViewSnapshot.Capture(map);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            MapViewSnapshot after = MapViewSnapshot.Capture(loaded);

            Assert.Equal(before.SizeX, after.SizeX);
            Assert.Equal(before.SizeZ, after.SizeZ);
            Assert.Equal(TerrainDefOf.Sand.defName, after.Terrain.TerrainAt(1, 1));
            Assert.True(after.Roofs.IsRoofed(2, 2));
            Assert.Contains(after.AllThings(), t => t.DefName == rock.def.defName && t.Position == new IntVec3(3, 0, 3));
            Assert.Contains(after.Pawns, p => p.Position == new IntVec3(4, 0, 4));

            // A different session, so a host holding the pre-save versions is told to start over rather than
            // trusting numbers that now describe a different object graph.
            Assert.NotEqual(before.SessionId, after.SessionId);
            Assert.True(MapViewSnapshot.CaptureChanges(loaded, before.Versions).FullResync);
        }

        // ---- absence, which a host has to draw too ----

        [Fact]
        public void An_unknown_tile_and_an_ungenerated_interior_each_explain_themselves()
        {
            // No game at all — the main menu, which is a state the view has to draw.
            MapViewSnapshot noGame = MapViewSnapshot.Capture(0);

            Assert.False(noGame.HasMap);
            Assert.False(string.IsNullOrWhiteSpace(noGame.AbsenceReason));
            Assert.Empty(noGame.Pawns);
            Assert.Empty(noGame.Terrain.Cells);

            MapViewDelta delta = MapViewSnapshot.CaptureChanges(0, null);
            Assert.False(delta.HasMap);
            Assert.False(string.IsNullOrWhiteSpace(delta.AbsenceReason));
        }

        // ---- footprints ----

        /// <summary>
        /// A multi-cell Thing is registered in the ThingGrid under every cell it covers, so a chunk walk that
        /// did not filter would emit it once per cell and the host would draw a bed four times. It belongs to
        /// the one chunk holding its own position, and it reports the footprint it actually occupies so the
        /// host never reimplements <c>GenAdj.OccupiedRect</c>.
        /// </summary>
        [Fact]
        public void A_multi_cell_thing_appears_once_and_reports_the_footprint_it_occupies()
        {
            CoreMap map = NewMap();
            ThingDef twoByOne = ThingSizeTests.DefWithSize("ViewFootprintProbe", 1, 2);
            Thing thing = ThingMaker.MakeThing(twoByOne);
            GenSpawn.Spawn(thing, new IntVec3(10, 0, 10), map);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            ThingView view = Assert.Single(snapshot.AllThings(), t => t.ThingId == thing.thingIDNumber);
            Assert.Equal(new IntVec2(1, 2), view.OccupiedSize);
            Assert.Equal(thing.OccupiedRect().minX, view.OccupiedMin.x);
            Assert.Equal(thing.OccupiedRect().minZ, view.OccupiedMin.z);

            // Rotated, the footprint the host draws rotates with it — a host that only had def.size would
            // have to know that rule, and two implementations of it would disagree.
            Thing sideways = ThingMaker.MakeThing(twoByOne);
            GenSpawn.Spawn(sideways, new IntVec3(20, 0, 20), map, Rot4.East);

            ThingView rotated = Assert.Single(
                MapViewSnapshot.Capture(map).AllThings(), t => t.ThingId == sideways.thingIDNumber);
            Assert.Equal(new IntVec2(2, 1), rotated.OccupiedSize);
        }
    }
}
