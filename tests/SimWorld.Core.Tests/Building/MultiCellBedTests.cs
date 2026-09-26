using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.MapGen;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <c>Bed</c> is 1x2, as in RimWorld — the first shipped def with a footprint, driven through every path a
    /// bed goes through in play, at every facing: placed, delivered to, built, slept in, carried to, drawn,
    /// destroyed and saved.
    ///
    /// <para/>The machinery was mostly here and had never run: <c>ThingGrid</c>, <c>EdificeGrid</c>,
    /// <c>GenConstruct</c>, the blueprint and frame sizes and <c>ThingView</c> all read the footprint. Driving
    /// it found two that were wrong. <see cref="GenAdj.OccupiedRect"/> had no <c>AdjustForRotation</c>, so a
    /// South bed covered a North bed's cells; and a <see cref="PathEndMode.Touch"/> path aimed at the
    /// neighbours of a Thing's <c>Position</c>, so a builder coming from a bed's foot end stood on the bed it
    /// was building. The facing theories below catch the first, the "never inside" assertions the second.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MultiCellBedTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public MultiCellBedTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        /// <summary>The four facings, as ints: xUnit's inline data cannot carry a <see cref="Rot4"/>.</summary>
        public static IEnumerable<object[]> Facings =>
            new[] { Rot4.NorthInt, Rot4.EastInt, Rot4.SouthInt, Rot4.WestInt }.Select(r => new object[] { r });

        private static readonly IntVec3 Centre = new IntVec3(10, 0, 10);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Bed => ConstructionThingDefOf.Bed;

        private static ThingDef Wood => Def("WoodLog");

        private static CoreMap NewMap(int size = 21) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Thing SpawnBed(CoreMap map, IntVec3 at, Rot4 rot) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Bed), at, map, rot);

        private static Blueprint SpawnBedBlueprint(CoreMap map, IntVec3 at, Rot4 rot) =>
            (Blueprint)GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), at, map, rot);

        private static Pawn SpawnPawn(CoreMap map, IntVec3 at, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, at, map);
            return p;
        }

        private static Pawn SpawnBuilder(CoreMap map, IntVec3 at)
        {
            Pawn p = SpawnPawn(map, at, "Builder");
            p.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;
            return p;
        }

        private static void SpawnWood(CoreMap map, IntVec3 at, int count)
        {
            Thing wood = ThingMaker.MakeThing(Wood);
            wood.stackCount = count;
            GenSpawn.Spawn(wood, at, map);
        }

        /// <summary>The cells touching <paramref name="rect"/> and outside it: where a builder may stand.</summary>
        private static bool Touches(CellRect rect, IntVec3 c) => rect.ExpandedBy(1).Contains(c) && !rect.Contains(c);

        // ---- content ----

        [Fact]
        public void Bed_is_one_by_two_and_its_blueprint_and_frame_take_that_from_it()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(new IntVec2(1, 2), Bed.size);

            // Declared by nobody but the bed: the hand-authored defs carry no size and derive it.
            Assert.Equal(IntVec2.One, Def("Blueprint_Bed").size);
            Assert.Equal(IntVec2.One, Def("Frame_Bed").size);
            Assert.Equal(new IntVec2(1, 2), ThingMaker.MakeThing(Def("Blueprint_Bed")).Size);
            Assert.Equal(new IntVec2(1, 2), ThingMaker.MakeThing(Def("Frame_Bed")).Size);

            // A single bed: one sleeping slot (RimWorld: one per cell of width).
            Assert.Equal(1, BedUtility.GetSleepingSlotsCount(Bed.size));
        }

        // ---- geometry, at every facing ----

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_bed_covers_its_own_cell_and_the_one_it_faces_and_is_slept_in_at_its_own_cell(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Thing bed = SpawnBed(map, Centre, rot);

            // RimWorld turns the footprint about the bed's own cell. Before AdjustForRotation was ported the
            // foot of a South bed was north of it and of a West bed east of it — the same two cells as North
            // and East.
            List<IntVec3> cells = bed.OccupiedRect().Cells.ToList();
            Assert.Equal(2, cells.Count);
            Assert.Contains(Centre, cells);
            Assert.Contains(Centre + rot.FacingCell, cells);

            Assert.Equal(Centre, bed.GetSleepingSlotPos());

            foreach (IntVec3 c in cells)
            {
                Assert.Contains(bed, map.thingGrid.ThingsListAt(c));
                Assert.Same(bed, map.edificeGrid[c]);
            }
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void The_host_is_told_the_rotated_footprint_and_sees_the_bed_once(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Thing bed = SpawnBed(map, Centre, rot);

            ThingView view = Assert.Single(MapViewSnapshot.Capture(map).AllThings(), t => t.ThingId == bed.thingIDNumber);

            IntVec3 foot = Centre + rot.FacingCell;
            Assert.Equal(new IntVec3(Math.Min(Centre.x, foot.x), 0, Math.Min(Centre.z, foot.z)), view.OccupiedMin);
            Assert.Equal(rot.IsHorizontal ? new IntVec2(2, 1) : new IntVec2(1, 2), view.OccupiedSize);
            Assert.Equal(Centre, view.Position);
            Assert.Equal(rot, view.Rotation);
        }

        // ---- placement ----

        [Theory]
        [MemberData(nameof(Facings))]
        public void Placement_is_refused_when_either_of_the_two_cells_is_taken(int facing)
        {
            var rot = new Rot4(facing);
            foreach (IntVec3 blocked in new[] { Centre, Centre + rot.FacingCell })
            {
                CoreMap map = NewMap();
                Assert.True(GenConstruct.CanPlaceBlueprintAt(Bed, Centre, map, out _, rot));

                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), blocked, map);

                Assert.False(GenConstruct.CanPlaceBlueprintAt(Bed, Centre, map, out string? reason, rot), "blocked at " + blocked);
                Assert.Equal("a building already occupies this cell", reason);
            }

            // The cell behind the head is not the bed's: a wall there takes nothing from it.
            CoreMap behind = NewMap();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), Centre - rot.FacingCell, behind);
            Assert.True(GenConstruct.CanPlaceBlueprintAt(Bed, Centre, behind, out _, rot));
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_second_bed_cannot_put_its_foot_on_the_first_ones_blueprint(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            SpawnBedBlueprint(map, Centre, rot);

            // Two cells further on, facing back: its foot lands on the first bed's foot.
            IntVec3 other = Centre + rot.FacingCell + rot.FacingCell;
            Assert.False(GenConstruct.CanPlaceBlueprintAt(Bed, other, map, out string? reason, rot.Opposite));
            Assert.Equal("something is already planned here", reason);

            // Three cells on, facing back, they meet foot to foot and do not overlap.
            Assert.True(GenConstruct.CanPlaceBlueprintAt(Bed, other + rot.FacingCell, map, out _, rot.Opposite));
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_bed_whose_foot_would_hang_off_the_map_is_refused(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();

            // The last cell of the map in the direction the bed faces.
            IntVec3 edge = new IntVec3(
                rot.FacingCell.x > 0 ? map.Size.x - 1 : rot.FacingCell.x < 0 ? 0 : Centre.x,
                0,
                rot.FacingCell.z > 0 ? map.Size.z - 1 : rot.FacingCell.z < 0 ? 0 : Centre.z);

            Assert.False(GenConstruct.CanPlaceBlueprintAt(Bed, edge, map, out string? reason, rot));
            Assert.Equal("out of bounds", reason);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpawnBed(map, edge, rot));

            // Facing back into the map, the same cell takes a bed.
            Assert.True(GenConstruct.CanPlaceBlueprintAt(Bed, edge, map, out _, rot.Opposite));
        }

        // ---- building one, in play ----

        /// <summary>
        /// A builder with wood lying beyond the bed's foot end delivers and builds from there. The approach is
        /// the one that exposed the old Touch goals: from the foot end, the first cell next to the bed's
        /// <c>Position</c> a pawn meets is the bed's own foot, and it used to stop there — inside the frame it
        /// was building. Every tick the site moves on, the builder must be standing on a cell touching the
        /// footprint and outside it.
        /// </summary>
        [Theory]
        [MemberData(nameof(Facings))]
        public void A_bed_is_built_at_its_facing_by_a_builder_who_never_stands_inside_it(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Blueprint blueprint = SpawnBedBlueprint(map, Centre, rot);
            CellRect site = blueprint.OccupiedRect();

            IntVec3 far = Centre + new IntVec3(rot.FacingCell.x * 5, 0, rot.FacingCell.z * 5);
            SpawnWood(map, far, Bed.CostListCountFor(Wood));
            Pawn builder = SpawnBuilder(map, far + rot.FacingCell);

            var standing = new List<IntVec3>();
            Frame? frame = null;
            float lastWork = 0f;
            for (int tick = 0; tick < 3000; tick++)
            {
                RunTicks(1, builder);

                if (frame == null)
                {
                    frame = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).OfType<Frame>().FirstOrDefault();
                    if (frame != null) standing.Add(builder.Position); // delivered: the blueprint just became this
                }
                else if (frame.Spawned && frame.workDone > lastWork)
                {
                    lastWork = frame.workDone;
                    standing.Add(builder.Position);
                }

                if (map.listerThings.ThingsOfDef(Bed).Count > 0) break;
            }

            Thing bed = Assert.Single(map.listerThings.ThingsOfDef(Bed));
            Assert.Equal(Centre, bed.Position);
            Assert.Equal(rot, bed.Rotation);
            Assert.Equal(site, bed.OccupiedRect());
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));

            Assert.True(standing.Count > 2, "saw the delivery and some work: " + standing.Count);
            foreach (IntVec3 c in standing)
            {
                Assert.False(site.Contains(c), "builder stood inside the bed it was building, at " + c);
                Assert.True(Touches(site, c), "builder worked from " + c + ", not beside the site " + site);
            }
        }

        [Fact]
        public void A_builder_standing_on_the_site_steps_out_to_build_it()
        {
            CoreMap map = NewMap();
            Blueprint blueprint = SpawnBedBlueprint(map, Centre, Rot4.East);
            Frame frame = blueprint.ReplaceWithFrame();
            frame.AddMaterial(Wood, Bed.CostListCountFor(Wood));
            CellRect site = frame.OccupiedRect();

            // On the foot of the frame: a Touch path from inside a walkable footprint ends on its ring here.
            Pawn builder = SpawnBuilder(map, Centre + Rot4.East.FacingCell);
            builder.jobs.StartJob(new Job(JobDefOf.ConstructFinishFrame, frame));

            var standing = new List<IntVec3>();
            float lastWork = 0f;
            for (int tick = 0; tick < 1000 && frame.Spawned; tick++)
            {
                RunTicks(1, builder);
                if (frame.Spawned && frame.workDone > lastWork)
                {
                    lastWork = frame.workDone;
                    standing.Add(builder.Position);
                }
            }

            Assert.False(frame.Spawned, "the frame did not finish");
            Assert.NotEmpty(standing);
            Assert.All(standing, c => Assert.True(Touches(site, c), "worked from " + c));
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void The_bed_is_reachable_from_its_foot_end_through_a_gap_only_its_foot_touches(int facing)
        {
            // Wall the bed in on every cell touching it except one beyond its foot: the only way to it is
            // the one cell that touches the foot and not the head. Position's neighbours alone never saw it.
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Thing bed = SpawnBed(map, Centre, rot);
            CellRect rect = bed.OccupiedRect();
            IntVec3 gap = Centre + rot.FacingCell + rot.FacingCell;
            foreach (IntVec3 c in rect.ExpandedBy(1).Cells)
            {
                if (rect.Contains(c) || c == gap) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            }
            Pawn visitor = SpawnPawn(map, gap + rot.FacingCell + rot.FacingCell, "Visitor");

            Assert.True(Reachability.CanReach(visitor, bed, PathEndMode.Touch));

            PawnPath path = map.pathFinder.FindPath(visitor, visitor.Position, bed, PathEndMode.Touch);
            try
            {
                Assert.True(path.Found);
                Assert.Equal(gap, path.Peek(path.NodesLeftCount - 1));
            }
            finally
            {
                path.ReleaseToPool();
            }
        }

        // ---- sleeping in one ----

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_tired_pawn_coming_from_the_foot_end_sleeps_on_the_sleeping_slot(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Thing bed = SpawnBed(map, Centre, rot);
            IntVec3 start = Centre + new IntVec3(rot.FacingCell.x * 4, 0, rot.FacingCell.z * 4);
            Pawn sleeper = SpawnPawn(map, start, "Sleeper");
            sleeper.needs.rest!.CurLevel = 0.05f;

            for (int tick = 0; tick < 600 && !sleeper.Asleep; tick++) RunTicks(1, sleeper);

            Assert.True(sleeper.Asleep);
            Assert.Equal(JobDefOf.LayDown, sleeper.jobs.curJob?.def);
            Assert.Same(bed, sleeper.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(bed.GetSleepingSlotPos(), sleeper.Position);
            Assert.Equal(Centre, sleeper.Position);
            Assert.Equal(Need_Rest.BedRestEffectiveness, sleeper.needs.rest.lastRestEffectiveness);
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_downed_colonist_is_carried_to_the_sleeping_slot(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Colony", "F_Colony");
            Find.FactionManager.Add(faction);

            Thing bed = SpawnBed(map, Centre, rot);
            Pawn rescuer = SpawnPawn(map, new IntVec3(1, 0, 1), "Rescuer");
            rescuer.faction = faction;
            Pawn downed = SpawnPawn(map, new IntVec3(18, 0, 18), "Downed");
            downed.faction = faction;
            downed.health.ForceDowned = true;

            for (int tick = 0; tick < 3000 && downed.Position != Centre; tick++) RunTicks(1, rescuer);

            Assert.Equal(bed.GetSleepingSlotPos(), downed.Position);
            Assert.Equal(Centre, downed.Position);
        }

        // ---- taking one down ----

        [Theory]
        [MemberData(nameof(Facings))]
        public void Destroying_a_bed_or_its_frame_frees_both_cells(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();

            Thing bed = SpawnBed(map, Centre, rot);
            List<IntVec3> cells = bed.OccupiedRect().Cells.ToList();
            bed.Destroy();
            AssertFree(map, cells, rot);

            Frame frame = SpawnBedBlueprint(map, Centre, rot).ReplaceWithFrame();
            Assert.Equal(cells, frame.OccupiedRect().Cells.ToList());
            foreach (IntVec3 c in cells) Assert.Same(frame, map.edificeGrid[c]);
            frame.Destroy();
            AssertFree(map, cells, rot);
        }

        private static void AssertFree(CoreMap map, List<IntVec3> cells, Rot4 rot)
        {
            foreach (IntVec3 c in cells)
            {
                Assert.Empty(map.thingGrid.ThingsListAt(c));
                Assert.Null(map.edificeGrid[c]);
            }
            Assert.Empty(MapViewSnapshot.Capture(map).AllThings());
            Assert.True(GenConstruct.CanPlaceBlueprintAt(Bed, Centre, map, out _, rot));
        }

        // ---- save/load ----

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_rotated_bed_half_built_round_trips_through_Scribe_and_is_finished_where_it_stood(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            Frame frame = SpawnBedBlueprint(map, Centre, rot).ReplaceWithFrame();
            frame.AddMaterial(Wood, Bed.CostListCountFor(Wood));
            frame.workDone = frame.WorkToBuild * 0.4f;
            CellRect site = frame.OccupiedRect();

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Frame again = loaded.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).OfType<Frame>().Single();
            Assert.Equal(Centre, again.Position);
            Assert.Equal(rot, again.Rotation);
            Assert.Equal(site, again.OccupiedRect());
            Assert.Equal(frame.workDone, again.workDone, 3);
            Assert.True(again.MaterialsFullySatisfied());
            foreach (IntVec3 c in site.Cells)
            {
                Assert.Contains(again, loaded.thingGrid.ThingsListAt(c));
                Assert.Same(again, loaded.edificeGrid[c]);
            }

            Pawn builder = SpawnBuilder(loaded, Centre - rot.FacingCell);
            builder.jobs.StartJob(new Job(JobDefOf.ConstructFinishFrame, again));
            for (int tick = 0; tick < 1000 && again.Spawned; tick++) RunTicks(1, builder);

            Thing bed = Assert.Single(loaded.listerThings.ThingsOfDef(Bed));
            Assert.Equal(Centre, bed.Position);
            Assert.Equal(rot, bed.Rotation);
            Assert.Equal(site, bed.OccupiedRect());
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void A_finished_rotated_bed_round_trips_through_Scribe(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = NewMap();
            CellRect rect = SpawnBed(map, Centre, rot).OccupiedRect();

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing bed = Assert.Single(loaded.listerThings.ThingsOfDef(Bed));
            Assert.Equal(rot, bed.Rotation);
            Assert.Equal(rect, bed.OccupiedRect());
            foreach (IntVec3 c in rect.Cells) Assert.Same(bed, loaded.edificeGrid[c]);
        }

        // ---- the player's placement ----

        [Fact]
        public void The_player_places_a_bed_facing_any_way_and_the_whole_footprint_is_the_designation()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "multicell-bed-place", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;

            IntVec3 cell = map.AllCells.First(c =>
                GenConstruct.CanPlaceBlueprintAt(Bed, c, map, out _, Rot4.West)
                && !map.thingGrid.ThingsListAt(c).Any(t => t is Pawn)
                && !map.thingGrid.ThingsListAt(c + Rot4.West.FacingCell).Any(t => t is Pawn));
            IntVec3 foot = cell + Rot4.West.FacingCell;

            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Bed", cell, Rot4.West).Outcome);
            Blueprint blueprint = map.thingGrid.ThingsListAt(cell).OfType<Blueprint>().Single();
            Assert.Equal(Rot4.West, blueprint.Rotation);
            Assert.Contains(blueprint, map.thingGrid.ThingsListAt(foot));

            // A second bed anchored on the first one's foot is refused, whichever way it faces.
            Assert.Equal(MapCommandOutcome.Occupied, MapCommands.PlaceBlueprint("Bed", foot, Rot4.South).Outcome);

            // And cancelling at the foot cancels the bed, not half of it.
            Assert.Equal(MapCommandOutcome.Done, MapCommands.CancelDesignation(foot).Outcome);
            Assert.DoesNotContain(blueprint, map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.Empty(map.thingGrid.ThingsListAt(cell).OfType<Blueprint>());
            Assert.Empty(map.thingGrid.ThingsListAt(foot).OfType<Blueprint>());
        }

        // ---- the settlement's own placement ----

        /// <summary>
        /// A founding band on a wooded tile, nothing placed by hand, builds itself one 1x2 bed per citizen —
        /// no more — and sleeps in them at their sleeping slots. The same world and band
        /// <see cref="WoodSupplyTests"/> founds, held to every citizen rather than three in four, and watched
        /// the whole way: a bed never overlaps another, and nobody asleep in one lies anywhere but its slot.
        /// </summary>
        [Fact]
        public void A_founding_band_builds_one_two_cell_bed_per_citizen_and_sleeps_on_the_slots()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "wood-supply-founding", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i =>
                !world.grid.Tiles[i].WaterCovered && TreeTuning.TimberMagnitude(world.grid.Tiles[i]) >= 0.4f);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(4343));
            Find.World = world;
            CoreMap map = settlement.EnterMap(world);

            int citizens = settlement.Citizens.Count;
            Assert.True(citizens > 1, "a band of one proves little: " + citizens);
            int beds = 0;
            int sleptInABed = 0;
            int lastBedTick = -1;
            for (int tick = 0; tick < 4 * GenDate.TicksPerDay; tick++)
            {
                SettlementConstructionInitiative.Tick();
                Find.TickManager.DoSingleTick();
                if (tick % 250 != 0) continue;

                beds = map.listerThings.ThingsOfDef(Bed).Count;
                Assert.True(beds <= citizens, beds + " beds for " + citizens + " citizens: one each, no more");
                if (beds == citizens && lastBedTick < 0) lastBedTick = tick;

                foreach (Pawn p in settlement.Citizens)
                {
                    if (!p.Spawned || !p.Asleep || p.jobs.curJob?.def != JobDefOf.LayDown) continue;
                    Thing? bed = p.jobs.curJob.GetTarget(TargetIndex.A).Thing;
                    if (bed == null) continue;
                    Assert.Equal(bed.GetSleepingSlotPos(), p.Position);
                    sleptInABed++;
                }

                if (beds == citizens && sleptInABed > 0) break;
            }

            output.WriteLine(beds + " beds for " + citizens + " citizens, the last by tick " + lastBedTick
                + " (day " + (lastBedTick / (float)GenDate.TicksPerDay).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "); sleepers seen in beds: " + sleptInABed);
            Assert.Equal(citizens, beds);
            Assert.True(sleptInABed > 0, "nobody was ever seen asleep in a bed");

            var taken = new HashSet<IntVec3>();
            foreach (Thing bed in map.listerThings.ThingsOfDef(Bed))
            {
                CellRect rect = bed.OccupiedRect();
                Assert.Equal(2, rect.Area);
                foreach (IntVec3 c in rect.Cells)
                {
                    Assert.True(taken.Add(c), "two beds share " + c);
                    Assert.Same(bed, map.edificeGrid[c]);
                }
            }
        }

        // ---- the settlement's placement-connectivity gate ----

        /// <summary>
        /// The placement-connectivity gate judges an impassable building by its whole footprint. Nothing the
        /// settlement builds is both impassable and bigger than one cell yet — the bed is standable, so it never
        /// asks — so this drives the gate with a stand-in: a 1x2 wall-like block just south of the one gap in a
        /// wall. Its own cell leaves the gap open; its second cell plugs it.
        /// </summary>
        [Fact]
        public void A_two_cell_impassable_is_judged_by_both_cells_when_it_might_wall_off_ground()
        {
            CoreMap map = NewMap(12);
            var gap = new IntVec3(5, 0, 6);
            for (int x = 0; x < map.Size.x; x++)
            {
                if (x != gap.x) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(x, 0, gap.z), map);
            }

            ThingDef single = ImpassableDef("OneCellBlock", 1, 1);
            ThingDef twoCell = ImpassableDef("TwoCellBlock", 1, 2);
            var below = new IntVec3(5, 0, 4);

            Assert.False(SettlementConstructionInitiative.WouldCutOffGround(map, single, below));
            Assert.True(SettlementConstructionInitiative.WouldCutOffGround(map, twoCell, below));
            Assert.False(SettlementConstructionInitiative.WouldCutOffGround(map, twoCell, new IntVec3(2, 0, 1)));

            // And it is right to: built there, the two cells do cut the map in two.
            GenSpawn.Spawn(ThingMaker.MakeThing(twoCell), below, map);
            var south = new IntVec3(0, 0, 0);
            var north = new IntVec3(0, 0, 11);
            Pawn walker = SpawnPawn(map, south, "Walker");
            Assert.False(Reachability.CanReach(walker, north, PathEndMode.OnCell));
        }

        private static ThingDef ImpassableDef(string defName, int x, int z) => new ThingDef
        {
            defName = defName,
            label = defName,
            thingClass = typeof(Thing),
            category = ThingCategory.Building,
            size = new IntVec2(x, z),
            passability = Traversability.Impassable,
            fillPercent = 1f,
        };
    }
}
