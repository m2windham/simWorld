using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <see cref="AutoHomeAreaMaker"/> (RimWorld: <c>RimWorld.AutoHomeAreaMaker</c>, read from
    /// <c>josh-m/rw-decompile/RimWorld/AutoHomeAreaMaker.cs</c>) — the home area growing on its own around
    /// what a settlement builds, rather than existing only where a player painted it. See that class's own
    /// doc for the full translation this pins: why it is called from <see cref="Frame.CompleteConstruction"/>
    /// rather than from every <see cref="Building"/> spawn, and why <see cref="MarkHomeAroundThing"/>'s rect
    /// can land one row or column off the footprint's true centre for an even span (RimWorld's own quirk,
    /// kept rather than smoothed away).
    /// </summary>
    public class AutoHomeAreaMakerTests : ContentTestBase
    {
        public AutoHomeAreaMakerTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>Runs a real Blueprint through <see cref="Frame"/> to a finished building via
        /// <see cref="Frame.CompleteConstruction"/> — the one call site this port wires
        /// <see cref="AutoHomeAreaMaker"/> to (see its own doc). Materials are never delivered:
        /// <c>CompleteConstruction</c> does not check them itself, only the work giver that would normally
        /// drive a pawn to call it does.</summary>
        private static Thing CompleteWall(CoreMap map, IntVec3 pos, string builderName = "Builder")
        {
            var blueprint = (Blueprint)GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), pos, map);
            Frame frame = blueprint.ReplaceWithFrame();
            return frame.CompleteConstruction(NewHuman(builderName));
        }

        // ---- the settlement's own construction marks home area ----

        [Fact]
        public void A_settlement_building_marks_home_around_itself_with_RimWorlds_radius()
        {
            CoreMap map = NewMap(40, 40);
            var pos = new IntVec3(20, 0, 20);

            Assert.Equal(0, map.areaManager.Home.TrueCount);
            Thing built = CompleteWall(map, pos);
            Assert.Same(Def("Wall"), built.def);
            Assert.Equal(pos, built.Position);

            // RimWorld: new CellRect(pos.x - RotatedSize.x/2 - 4, pos.z - RotatedSize.z/2 - 4, RotatedSize.x + 8,
            // RotatedSize.z + 8) — for a 1x1 wall that is a 9x9 square centred on pos.
            var expected = new CellRect(16, 16, 9, 9);
            Assert.Equal(4, AutoHomeAreaMaker.BorderWidth);
            Assert.All(expected.Cells, c => Assert.True(map.areaManager.Home[c], c + " should be inside the marked 9x9"));
            Assert.Equal(expected.Area, map.areaManager.Home.TrueCount);

            // Just outside the marked square on every side stays unmarked.
            Assert.False(map.areaManager.Home[new IntVec3(15, 0, 20)]);
            Assert.False(map.areaManager.Home[new IntVec3(25, 0, 20)]);
            Assert.False(map.areaManager.Home[new IntVec3(20, 0, 15)]);
            Assert.False(map.areaManager.Home[new IntVec3(20, 0, 25)]);
        }

        [Fact]
        public void Marking_clips_to_the_map_edge_instead_of_throwing()
        {
            CoreMap map = NewMap(20, 20);
            var corner = new IntVec3(0, 0, 0);
            CompleteWall(map, corner);

            // The full 9x9 would run from -4 to 4; only the in-bounds quarter is marked.
            var expected = new CellRect(0, 0, 5, 5);
            Assert.All(expected.Cells, c => Assert.True(map.areaManager.Home[c]));
            Assert.Equal(expected.Area, map.areaManager.Home.TrueCount);
        }

        /// <summary>A Def that opts out (<see cref="BuildingProperties.expandHomeArea"/> false) marks nothing,
        /// the same switch RimWorld's own content can set per building.</summary>
        [Fact]
        public void A_building_def_that_opts_out_of_expandHomeArea_marks_nothing()
        {
            CoreMap map = NewMap(20, 20);
            var optedOut = new ThingDef
            {
                defName = "OptedOutBuilding",
                label = "opted out",
                thingClass = typeof(global::SimWorld.Building.Building),
                category = ThingCategory.Building,
                size = new IntVec2(1, 1),
                building = new BuildingProperties { expandHomeArea = false },
            };

            Thing b = GenSpawn.Spawn(ThingMaker.MakeThing(optedOut), new IntVec3(10, 0, 10), map);
            AutoHomeAreaMaker.Notify_BuildingSpawned(b);

            Assert.Equal(0, map.areaManager.Home.TrueCount);
        }

        // ---- a ruin's (or a raider's) building does not mark home area ----

        /// <summary>
        /// <c>MapGen.GenStep_Ruins.SpawnWeatheredWall</c> spawns its ancient walls with exactly this call
        /// shape — <c>ThingMaker.MakeThing</c> then <c>GenSpawn.Spawn</c> straight onto the map, never through a
        /// Blueprint or a Frame. RimWorld leaves the same wall unmarked because it carries no Faction at all,
        /// so <c>b.Faction == Faction.OfPlayer</c> is false; this port reaches the same outcome because nothing
        /// but <see cref="Frame.CompleteConstruction"/> ever calls <see cref="AutoHomeAreaMaker"/>, and a ruin
        /// (like a hypothetical raider-placed building — see <see cref="AutoHomeAreaMaker"/>'s own doc: nothing
        /// in this codebase ever lets a raider reach that call site either) never goes through one.
        /// </summary>
        [Fact]
        public void A_ruins_or_a_raiders_building_spawned_outside_construction_does_not_mark_home_area()
        {
            CoreMap map = NewMap(20, 20);
            Thing ancientWall = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(10, 0, 10), map);

            Assert.True(ancientWall.Spawned);
            Assert.Equal(0, map.areaManager.Home.TrueCount);

            // The same building, spawned the settlement's own way instead, does mark — the difference is the
            // call site reached, not anything about the Thing itself.
            CompleteWall(map, new IntVec3(15, 0, 15));
            Assert.True(map.areaManager.Home.TrueCount > 0);
        }

        // ---- a multi-cell footprint marks around its whole rect ----

        [Theory]
        [InlineData(Rot4.NorthInt)]
        [InlineData(Rot4.EastInt)]
        public void A_multi_cell_bed_marks_home_around_its_whole_rotated_rect(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap(40, 40);
            var pos = new IntVec3(20, 0, 20);

            var blueprint = (Blueprint)GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), pos, map, rot);
            Frame frame = blueprint.ReplaceWithFrame();
            Thing bed = frame.CompleteConstruction(NewHuman());

            Assert.Equal(new IntVec2(1, 2), Def("Bed").size);
            // Hand-derived from RimWorld's formula (pos - RotatedSize/2 - 4, RotatedSize + 8), not re-derived
            // through the class under test, so this does not just check the implementation against itself.
            CellRect expected = rot.IsHorizontal
                ? new CellRect(15, 16, 10, 9)   // RotatedSize (2,1): x-span even, z-span odd
                : new CellRect(16, 15, 9, 10);  // RotatedSize (1,2): x-span odd, z-span even
            Assert.All(expected.Cells, c => Assert.True(map.areaManager.Home[c], c + " outside the marked rect for facing " + rot));
            Assert.Equal(expected.Area, map.areaManager.Home.TrueCount);

            // Both cells of the bed's own footprint land inside the marked rect.
            Assert.All(bed.OccupiedRect().Cells, c => Assert.True(map.areaManager.Home[c]));
        }

        // ---- the player's erase is authoritative until another building's range reaches it ----

        /// <summary>
        /// RimWorld's <c>MarkHomeAroundThing</c> sets every cell in range to <c>true</c> unconditionally — it
        /// never reads the cell first — so a player's erased cell is only safe until some building's own range
        /// reaches it again; when that happens, RimWorld silently re-includes it. Both halves are pinned here,
        /// not just the reassuring one.
        /// </summary>
        [Fact]
        public void An_erased_cell_stays_erased_until_a_later_buildings_range_reaches_it_again()
        {
            CoreMap map = NewMap(60, 60);
            var posA = new IntVec3(10, 0, 10);
            CompleteWall(map, posA, "BuilderA");

            // The far edge of A's own marked square (still inside it, one cell shy of the boundary A itself
            // set) — erasing this does not touch any other building's range yet.
            var erased = new IntVec3(posA.x, 0, posA.z + AutoHomeAreaMaker.BorderWidth);
            Assert.True(map.areaManager.Home[erased]);
            map.areaManager.Home[erased] = false;
            Assert.False(map.areaManager.Home[erased]);

            // A second building, far enough away that its own 9x9 never reaches the erased cell: the erase holds.
            CompleteWall(map, new IntVec3(40, 0, 40), "BuilderB");
            Assert.False(map.areaManager.Home[erased], "an unrelated building's range should not touch this cell");

            // A third building close enough that its own range does reach the erased cell: RimWorld's rule
            // re-marks it, silently, exactly as it would in the original game.
            CompleteWall(map, new IntVec3(posA.x, 0, posA.z + 1), "BuilderC");
            Assert.True(map.areaManager.Home[erased], "a later building's range should re-include an erased cell, matching RimWorld");
        }

        // ---- Scribe ----

        [Fact]
        public void The_auto_marked_home_area_round_trips_through_Scribe()
        {
            CoreMap map = NewMap(30, 30);
            CompleteWall(map, new IntVec3(15, 0, 15));
            int before = map.areaManager.Home.TrueCount;
            Assert.True(before > 0);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(before, loaded.areaManager.Home.TrueCount);
            Assert.All(map.AllCells, c => Assert.Equal(map.areaManager.Home[c], loaded.areaManager.Home[c]));

            // The loaded map still marks around a further building exactly as the original would have.
            CompleteWall(loaded, new IntVec3(5, 0, 5));
            CompleteWall(map, new IntVec3(5, 0, 5));
            Assert.Equal(map.areaManager.Home.TrueCount, loaded.areaManager.Home.TrueCount);
        }

        // ---- in play: firefighting is bounded by the auto-marked area, with nothing painted ----

        /// <summary>
        /// The same claim <c>AI.EmergencyWorkTests.A_sleeping_citizen_does_not_wake_for_a_fire_outside_the_home_area_but_wakes_for_one_inside</c>
        /// pins for a <i>painted</i> home area, reused here for one that nobody ever painted: a settlement's
        /// own first completed building is enough. Before this class existed, an unpainted settlement's home
        /// area was permanently empty, so <c>WorkGiver_FightFires</c>'s home-area gate never bound anything in
        /// real play — see that class's own updated doc. This is the test that closes the gap: a citizen with
        /// a wall standing (auto-marking around it) sleeps through a distant grass fire and wakes for one on
        /// its own doorstep, with no player command involved at all.
        /// </summary>
        [Fact]
        public void A_sleeping_citizen_does_not_wake_for_a_fire_outside_the_auto_marked_home_area_but_wakes_for_one_inside()
        {
            CoreMap map = NewMap(30, 30);
            Find.FactionManager = new FactionManager();
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Colony", "F_Colony_AutoHome");
            Find.FactionManager.Add(faction);

            // A wall the settlement itself finishes building — no MapCommands.SetHomeArea call anywhere here.
            var wallPos = new IntVec3(10, 0, 10);
            CompleteWall(map, wallPos);
            Assert.True(map.areaManager.Home.TrueCount > 0, "the settlement's own construction should have auto-marked home area");
            Assert.False(map.areaManager.Home[new IntVec3(25, 0, 25)], "test setup: this far corner must be outside the auto-marked area");
            Assert.True(map.areaManager.Home[new IntVec3(12, 0, 10)], "test setup: this near cell must be inside the auto-marked area");

            Pawn sleeper = NewHuman("Sleeper");
            sleeper.faction = faction;
            GenSpawn.Spawn(sleeper, new IntVec3(2, 0, 2), map);

            sleeper.needs.rest!.CurLevel = 0.1f;
            RunTicks(5, sleeper);
            Assert.Equal(JobDefOf.LayDown, sleeper.jobs.curJob?.def);
            Assert.True(sleeper.Asleep, "test setup: the sleeper should be asleep before the fires are lit");

            var farCell = new IntVec3(25, 0, 25);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), farCell, map);
            Assert.True(FireUtility.TryStartFireIn(farCell, map, 1f));

            RunTicks(2 * JobDriver_LayDown.LookForOtherJobsIntervalTicks, sleeper);
            Assert.True(sleeper.Asleep, "a fire outside the auto-marked home area should not have woken the sleeper");
            Assert.Equal(JobDefOf.LayDown, sleeper.jobs.curJob?.def);

            var nearCell = new IntVec3(12, 0, 10);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), nearCell, map);
            Assert.True(FireUtility.TryStartFireIn(nearCell, map, 1f));

            int woke = -1;
            for (int t = 0; t < 2 * JobDriver_LayDown.LookForOtherJobsIntervalTicks && woke < 0; t++)
            {
                RunTicks(1, sleeper);
                if (!sleeper.Asleep) woke = t;
            }
            Assert.True(woke >= 0, "a fire inside the auto-marked home area should have woken the sleeper");
            Assert.Equal(FireJobDefOf.BeatFire, sleeper.jobs.curJob?.def);
        }
    }
}
