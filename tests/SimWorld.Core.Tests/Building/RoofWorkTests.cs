using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreJob = SimWorld.AI.Job;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <see cref="WorkGiver_BuildRoof"/>/<see cref="JobDriver_BuildRoof"/> and
    /// <see cref="WorkGiver_RemoveRoof"/>/<see cref="JobDriver_RemoveRoof"/> (RimWorld:
    /// <c>RimWorld.WorkGiver_BuildRoof</c>/<c>JobDriver_BuildRoof</c> and their <c>RemoveRoof</c> siblings) —
    /// the other half of <see cref="AutoBuildRoofAreaSetterTests"/>: a build-roof area is only a plan until a
    /// citizen actually carries it out.
    /// </summary>
    public class RoofWorkTests : ContentTestBase
    {
        public RoofWorkTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static void BuildWallRing(CoreMap map, CellRect interior, IntVec3? doorAt = null)
        {
            CellRect ring = interior.ExpandedBy(1);
            foreach (IntVec3 c in ring.EdgeCells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                ThingDef def = doorAt.HasValue && c == doorAt.Value ? Def("Door") : Def("Wall");
                GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
            }
        }

        private static void MarkHome(CoreMap map, CellRect area)
        {
            foreach (IntVec3 c in area.ClipInsideMap(map).Cells) map.areaManager.Home[c] = true;
        }

        /// <summary>Drives both halves a real settlement's own tick would: the pawn tick list
        /// (<see cref="ContentTestBase.RunTicks"/>'s own job) and <see cref="CoreMap.MapTick"/> (rooms, and
        /// with them <see cref="Building.AutoBuildRoofAreaSetter"/>) — neither runs the other, and
        /// <see cref="ContentTestBase.RunTicks"/> alone only does the first. Only <see cref="SimWorld.Sim.Game"/>
        /// wires the two together automatically; a hand-built map has no <c>Game</c>, so this test drives them
        /// both by hand, in RimWorld's own order (map before pawns, matching <c>TickManager.PostTickers</c>'s
        /// own placement).</summary>
        private static void RunTicksWithMap(CoreMap map, int ticks, params Pawn[] pawns)
        {
            TickManager tm = Find.TickManager;
            foreach (Pawn p in pawns)
            {
                if (!tm.TickListFor(TickerType.Normal)!.Contains(p)) tm.RegisterAllTickabilityFor(p);
            }
            for (int i = 0; i < ticks; i++)
            {
                map.MapTick();
                tm.DoSingleTick();
            }
        }

        private static Pawn SpawnBuilder(CoreMap map, IntVec3 cell, string name = "Builder")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // ---- the whole pipeline: a citizen actually builds the roof RoomTracker asked for ----

        [Fact]
        public void A_builder_roofs_an_enclosed_room_and_it_stops_touching_outside()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 2, 2);
            var doorAt = new IntVec3(8, 0, 7);
            BuildWallRing(map, interior, doorAt);
            MarkHome(map, interior.ExpandedBy(1));
            map.MapTick();

            Room? room = map.roomTracker.RoomAt(new IntVec3(8, 0, 8));
            Assert.NotNull(room);
            Assert.True(room!.TouchesOutside, "test setup: the room must start unroofed");
            int cellsToRoof = interior.ExpandedBy(1).Cells.Count(c => map.areaManager.BuildRoof[c]);
            Assert.True(cellsToRoof > 0, "test setup: AutoBuildRoofAreaSetter should have queued something");

            Pawn builder = SpawnBuilder(map, doorAt);

            const int maxTicks = 20000;
            bool fullyRoofed = false;
            for (int t = 0; t < maxTicks && !fullyRoofed; t += 50)
            {
                RunTicksWithMap(map, 50, builder);
                fullyRoofed = interior.ExpandedBy(1).Cells.All(c => map.roofGrid.Roofed(c));
            }

            Assert.True(fullyRoofed, "the builder never finished roofing the room within " + maxTicks + " ticks");

            // Re-fetch: RoofGrid.SetRoof's own dirty call made RoomTracker rebuild every Room afresh (see that
            // class's own doc) — the reference taken before ticking is a now-stale object from before the roof
            // went up, not the one this question should be asked of.
            Room? roomNow = map.roomTracker.RoomAt(new IntVec3(8, 0, 8));
            Assert.NotNull(roomNow);
            Assert.False(roomNow!.TouchesOutside, "a fully roofed, wall-enclosed room should no longer touch outside");
            Assert.True(
                interior.ExpandedBy(1).Cells.All(c => !map.areaManager.BuildRoof[c] || map.roofGrid.Roofed(c)),
                "every BuildRoof cell should have been roofed by now");
        }

        /// <summary>
        /// A tree standing inside a room the settlement walled in (RimWorld: <c>TreeBase</c> sets
        /// <c>interferesWithRoof</c>). The roof work over its cell is a cut job first, and the room is only
        /// roofed once the tree is gone.
        /// </summary>
        [Fact]
        public void A_tree_inside_a_walled_room_is_felled_before_the_roof_goes_over_it()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 2, 2);
            var doorAt = new IntVec3(8, 0, 7);
            BuildWallRing(map, interior, doorAt);
            MarkHome(map, interior.ExpandedBy(1));

            var treeCell = new IntVec3(9, 0, 9);
            var tree = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            tree.Growth = 1f;
            GenSpawn.Spawn(tree, treeCell, map);
            map.MapTick();
            Assert.True(map.areaManager.BuildRoof[treeCell], "test setup: the tree's cell should be queued for a roof");

            Pawn builder = SpawnBuilder(map, doorAt);
            CoreJob? first = new WorkGiver_BuildRoof().JobOnCell(builder, treeCell);
            Assert.NotNull(first);
            Assert.Same(SimWorld.AI.PlantCuttingJobDefOf.CutPlant, first!.def);
            Assert.Same(tree, first.targetA.Thing);

            const int maxTicks = 30000;
            bool fullyRoofed = false;
            for (int t = 0; t < maxTicks && !fullyRoofed; t += 50)
            {
                RunTicksWithMap(map, 50, builder);
                fullyRoofed = interior.ExpandedBy(1).Cells.All(c => map.roofGrid.Roofed(c));
            }

            Assert.True(tree.Destroyed, "the tree should have been felled");
            Assert.True(fullyRoofed, "the builder never finished roofing the room within " + maxTicks + " ticks");
        }

        // ---- WorkGiver_BuildRoof: unit-level accept/reject ----

        [Fact]
        public void WorkGiver_BuildRoof_offers_only_unroofed_BuildRoof_cells_within_support_range()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 2, 2);
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));
            map.MapTick();

            Pawn builder = SpawnBuilder(map, new IntVec3(8, 0, 8));
            var giver = new WorkGiver_BuildRoof();

            IntVec3 candidate = interior.Cells.First();
            Assert.True(map.areaManager.BuildRoof[candidate]);
            Assert.True(giver.HasJobOnCell(builder, candidate));

            // Already roofed: no longer offered.
            map.roofGrid.SetRoof(candidate, RoofDefOf.RoofConstructed);
            Assert.False(giver.HasJobOnCell(builder, candidate));

            // A cell nobody ever added to BuildRoof: never offered, wherever it is.
            Assert.False(giver.HasJobOnCell(builder, new IntVec3(2, 0, 2)));
        }

        /// <summary>
        /// A wall cell in <see cref="AreaManager.BuildRoof"/> — not standable, but still a legitimate roof
        /// target (RimWorld roofs the walls holding a room up, not only its floor) — still gets a real job,
        /// targeting the cell itself. <b>Not exercised here: RimWorld's own <c>targetB</c> substitution</b> (a
        /// non-standable cell whose direct <see cref="PathEndMode.Touch"/> reach fails gets an adjacent
        /// edifice as target B instead). This port's <see cref="Reachability.CanReachTarget"/> computes
        /// reachability for a bare-cell <see cref="PathEndMode.Touch"/> target and for a 1x1 Thing at that same
        /// cell through the identical ring-of-neighbours check (see that method's own footprint branch), so
        /// for every 1x1 wall or door this port ships, the substitution's condition
        /// (<c>!pawn.CanReach(c, Touch)</c>) and its replacement's own reachability
        /// (<c>pawn.CanReach(edifice, Touch)</c>) always agree — there is no reachable map layout in which the
        /// two differ, so a test asserting the substitution actually swaps target B would be asserting
        /// something this port's own reachability model cannot produce. The call shape is still ported (see
        /// <see cref="WorkGiver_BuildRoof.JobOnCell"/>'s own doc) for fidelity and for a future multi-cell
        /// roof holder, where the two footprints would stop being identical.
        /// </summary>
        [Fact]
        public void JobOnCell_still_produces_a_job_when_the_roof_cell_itself_is_not_standable()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 2, 2);
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));
            map.MapTick();

            Pawn builder = SpawnBuilder(map, new IntVec3(8, 0, 8));
            var giver = new WorkGiver_BuildRoof();

            IntVec3 wallCell = interior.ExpandedBy(1).EdgeCells.First(c => map.areaManager.BuildRoof[c]);
            Assert.False(GenGrid.Standable(wallCell, map));

            CoreJob? job = giver.JobOnCell(builder, wallCell);
            Assert.NotNull(job);
            Assert.Equal(wallCell, job!.targetA.Cell);
            Assert.Equal(wallCell, job.targetB.Cell);
        }

        // ---- WorkGiver_RemoveRoof / JobDriver_RemoveRoof ----

        [Fact]
        public void A_builder_removes_the_roof_from_a_NoRoof_cell()
        {
            CoreMap map = NewMap(20, 20);
            var roofed = new IntVec3(10, 0, 10);
            map.roofGrid.SetRoof(roofed, RoofDefOf.RoofConstructed);
            map.areaManager.NoRoof[roofed] = true;

            Pawn builder = SpawnBuilder(map, new IntVec3(10, 0, 9));

            const int maxTicks = 5000;
            bool cleared = false;
            for (int t = 0; t < maxTicks && !cleared; t += 25)
            {
                RunTicksWithMap(map, 25, builder);
                cleared = !map.roofGrid.Roofed(roofed);
            }

            Assert.True(cleared, "the builder never removed the forbidden roof within " + maxTicks + " ticks");
        }

        [Fact]
        public void WorkGiver_RemoveRoof_ignores_cells_not_in_NoRoof_or_already_bare()
        {
            CoreMap map = NewMap(20, 20);
            var roofed = new IntVec3(10, 0, 10);
            map.roofGrid.SetRoof(roofed, RoofDefOf.RoofConstructed);
            Pawn builder = SpawnBuilder(map, new IntVec3(10, 0, 9));
            var giver = new WorkGiver_RemoveRoof();

            Assert.False(giver.HasJobOnCell(builder, roofed), "not in NoRoof yet");

            map.areaManager.NoRoof[roofed] = true;
            Assert.True(giver.HasJobOnCell(builder, roofed));

            map.roofGrid.SetRoof(roofed, null);
            Assert.False(giver.HasJobOnCell(builder, roofed), "already bare");
        }

        // ---- MapCommands.Roofs: the player's own paint ----

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        [Fact]
        public void SetBuildRoofArea_and_SetNoRoofArea_are_mutually_exclusive_by_paint()
        {
            Settlement settlement = OpenedSettlement("roof-cmd-a");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 c = FirstOpenGroundCell(map);
            var cells = new List<IntVec3> { c };

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetNoRoofArea(cells, true).Outcome);
            Assert.True(map.areaManager.NoRoof[c]);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetBuildRoofArea(cells, true).Outcome);
            Assert.True(map.areaManager.BuildRoof[c]);
            Assert.False(map.areaManager.NoRoof[c], "painting BuildRoof should have cleared NoRoof at the same cell");

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetNoRoofArea(cells, true).Outcome);
            Assert.True(map.areaManager.NoRoof[c]);
            Assert.False(map.areaManager.BuildRoof[c], "painting NoRoof should have cleared BuildRoof at the same cell");
        }

        [Fact]
        public void SetNoRoofArea_refuses_a_thick_natural_roof()
        {
            Settlement settlement = OpenedSettlement("roof-cmd-b");
            CoreMap map = settlement.InteriorMap!;

            IntVec3? thick = FirstCellWhere(map, c =>
            {
                RoofDef? r = map.roofGrid.RoofAt(c);
                return r != null && r.isThickRoof;
            });
            Assert.NotNull(thick); // every generated TribalStart interior carries overhead mountain somewhere.

            MapCommandResult result = MapCommands.SetNoRoofArea(new List<IntVec3> { thick!.Value }, true);
            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
            Assert.False(map.areaManager.NoRoof[thick.Value]);
        }

        private static IntVec3 FirstOpenGroundCell(CoreMap map) =>
            FirstCellWhere(map, c => GenGrid.Standable(c, map) && !map.roofGrid.Roofed(c))
            ?? throw new System.InvalidOperationException("test setup: no open ground cell found");

        private static IntVec3? FirstCellWhere(CoreMap map, System.Func<IntVec3, bool> predicate)
        {
            foreach (IntVec3 c in map.AllCells)
            {
                if (predicate(c)) return c;
            }
            return null;
        }
    }
}
