using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The shape a settlement builds itself into (<see cref="SettlementCompound"/>): its beds in one block, a
    /// ring of wall closed round them, a door in the east side of the ring, storage against the outside.
    /// <c>ConstructionInitiativeTests</c> owns what is built and <c>ConstructionPlacementTests</c> owns where
    /// the random draw goes; this file owns the layout.
    /// <para/>
    /// Everything is asserted off the map, never off <see cref="SettlementCompound"/>'s own rectangles, and
    /// never as a coordinate: the anchor is a seeded draw. "Enclosed" is a flood fill over the ground a pawn
    /// can walk, the door counted as shut, so a ring with a hole anywhere in it fails whatever the plan says.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SettlementCompoundTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public SettlementCompoundTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Bed => ConstructionThingDefOf.Bed;

        private static ThingDef Hut => ConstructionThingDefOf.StorageHut;

        private static ThingDef Door => CompoundThingDefOf.Door;

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Settlement Band(int citizens, int storedWood = 0)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Compound", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            if (storedWood > 0) settlement.AddStore(Def("WoodLog"), storedWood);
            return settlement;
        }

        /// <summary>Gated passes of the initiative, the clock moved onto each interval boundary in turn.</summary>
        private static void RunPasses(Settlement settlement, CoreMap map, int passes, int from = 0)
        {
            for (int i = from; i < from + passes; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
        }

        /// <summary>Passes until a whole pass places nothing: the settlement has planned all it can.</summary>
        private static void RunUntilSettled(Settlement settlement, CoreMap map)
        {
            for (int pass = 0; pass < 400; pass++)
            {
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                RunPasses(settlement, map, 1, pass);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) return;
            }
            Assert.Fail("the settlement was still placing blueprints after 400 passes");
        }

        /// <summary>Finishes every blueprint on the map through the real pipeline's last two steps, so
        /// <see cref="AutoHomeAreaMaker"/> sees each building exactly as it would in a game.</summary>
        private static void BuildEverythingPlanned(CoreMap map)
        {
            Pawn builder = NewHuman("Builder");
            foreach (Blueprint bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
            {
                bp.ReplaceWithFrame().CompleteConstruction(builder);
            }
        }

        /// <summary>Everything standing or planned for <paramref name="wanted"/>, as the rect each one covers.</summary>
        private static List<CellRect> Sites(CoreMap map, Func<ThingDef, bool> wanted)
        {
            var sites = new List<CellRect>();
            foreach (Thing t in map.listerThings.AllThings)
            {
                ThingDef? what = t switch
                {
                    Blueprint bp => bp.EntityToBuild,
                    Frame f => f.EntityToBuild,
                    _ => t.def,
                };
                if (what != null && wanted(what)) sites.Add(t.OccupiedRect());
            }
            return sites;
        }

        private static List<CellRect> Sites(CoreMap map, ThingDef def) => Sites(map, d => ReferenceEquals(d, def));

        private static List<string> Placements(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint>()
                .Select(bp => bp.EntityToBuild.defName + "@" + bp.Position)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        private static HashSet<IntVec3> CellsOf(IEnumerable<CellRect> sites) => new HashSet<IntVec3>(sites.SelectMany(r => r.Cells));

        private static bool OnMapEdge(CoreMap map, IntVec3 c) => c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1;

        /// <summary>Every cell reachable from <paramref name="start"/> by cardinal steps over ground that can
        /// be walked and is not in <paramref name="shut"/>.</summary>
        private static HashSet<IntVec3> Flood(CoreMap map, IntVec3 start, HashSet<IntVec3> shut)
        {
            var seen = new HashSet<IntVec3> { start };
            var queue = new Queue<IntVec3>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                IntVec3 c = queue.Dequeue();
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 n = c + offset;
                    if (!GenGrid.InBounds(n, map) || shut.Contains(n) || !map.pathGrid.Walkable(n) || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        /// <summary>What a compound has to be, read off the map. Returns the ground inside the ring.
        /// <paramref name="notTheSettlements"/> is any wall that stood before the settlement built: it blocks
        /// the way like any other, but it is not held to being part of the ring.</summary>
        private HashSet<IntVec3> AssertIsACompound(CoreMap map, int bedsInside, HashSet<IntVec3>? notTheSettlements = null)
        {
            List<CellRect> beds = Sites(map, Bed);
            HashSet<IntVec3> walls = CellsOf(Sites(map, StoneWallMaterials.IsWall));
            List<CellRect> doors = Sites(map, Door);
            output.WriteLine(Picture(map));

            CellRect door = Assert.Single(doors);
            var doorCell = new IntVec3(door.minX, 0, door.minZ);
            var ringShut = new HashSet<IntVec3>(walls) { doorCell };

            // Enclosed: with the door shut, the ground just west of it reaches no edge of the map.
            IntVec3 justInside = doorCell + new IntVec3(-1, 0, 0);
            HashSet<IntVec3> inside = Flood(map, justInside, ringShut);
            Assert.DoesNotContain(inside, c => OnMapEdge(map, c));

            // The beds are one block inside it: every cell of bedsInside beds, none shared, each bed beside another.
            List<CellRect> bedsWithin = beds.Where(r => r.Cells.All(inside.Contains)).ToList();
            Assert.Equal(bedsInside, bedsWithin.Count);
            Assert.Equal(bedsInside * Bed.size.x * Bed.size.z, CellsOf(bedsWithin).Count);
            if (bedsInside > 1)
            {
                Assert.All(bedsWithin, bed => Assert.Contains(bedsWithin, other => other != bed && !other.ClipInsideRect(bed.ExpandedBy(1)).IsEmpty));
            }

            // The door is in the east side: open country to its east, and every cell inside lies west of it.
            IntVec3 threshold = doorCell + new IntVec3(1, 0, 0);
            Assert.DoesNotContain(threshold, inside);
            Assert.All(inside, c => Assert.True(c.x < doorCell.x, c + " is inside the ring but not west of the door at " + doorCell));

            // And it is a door, not a second wall: with it open the inside reaches the edge of the map.
            // Nobody is shut in, and the way in is not built over.
            Assert.Contains(Flood(map, justInside, walls), c => OnMapEdge(map, c));
            Assert.True(GenGrid.Standable(threshold, map), "the cell in front of the door at " + doorCell + " is blocked");

            // No stray wall: every one the settlement raised bounds the inside.
            Assert.All(walls.Where(w => notTheSettlements == null || !notTheSettlements.Contains(w)),
                w => Assert.Contains(GenAdj.AdjacentCells, o => inside.Contains(w + o)));

            // Storage stands outside the ring.
            Assert.All(CellsOf(Sites(map, Hut)), hut => Assert.DoesNotContain(hut, inside));
            return inside;
        }

        /// <summary>The ground round the door (or round the first bed, when there is no door), north up, for
        /// a failing run's log: D door, # wall, b bed, s storage, ~ unwalkable, . home area.</summary>
        private static string Picture(CoreMap map)
        {
            HashSet<IntVec3> beds = CellsOf(Sites(map, Bed));
            HashSet<IntVec3> walls = CellsOf(Sites(map, StoneWallMaterials.IsWall));
            HashSet<IntVec3> doors = CellsOf(Sites(map, Door));
            HashSet<IntVec3> huts = CellsOf(Sites(map, Hut));
            if (doors.Count + beds.Count == 0) return "(nothing placed)";
            CellRect view = CellRect.CenteredOn(doors.Concat(beds).First(), 22, 14).ClipInsideMap(map);

            var sb = new StringBuilder();
            for (int z = view.maxZ; z >= view.minZ; z--)
            {
                for (int x = view.minX; x <= view.maxX; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    sb.Append(doors.Contains(c) ? 'D' : walls.Contains(c) ? '#' : beds.Contains(c) ? 'b' : huts.Contains(c) ? 's'
                        : !map.pathGrid.Walkable(c) ? '~' : map.areaManager.Home[c] ? '.' : ' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ---- the shape ----

        /// <summary>The beds the biggest ring holds: a bed for the largest founding band but one, whose slot is the
        /// dormitory's hearth (exposure4 lane).</summary>
        private static int BiggestRingBeds => SettlementTuning.FoundingBandRange.max - 1;

        [Fact]
        public void A_founding_band_plans_its_beds_in_a_block_inside_a_ring_with_a_door_in_the_east_side()
        {
            IntRange bands = SettlementTuning.FoundingBandRange;
            foreach (int citizens in new[] { bands.min, (bands.min + bands.max) / 2, bands.max })
            {
                CoreMap map = NewMap(120, 120);
                Settlement settlement = Band(citizens, storedWood: WoodFor(3));
                RunUntilSettled(settlement, map);

                // Every citizen's bed is inside the ring but the biggest band's last (one slot is the dormitory's
                // hearth, exposure4 lane), and the storage it wanted is against the ring.
                HashSet<IntVec3> inside = AssertIsACompound(map, bedsInside: Math.Min(citizens, BiggestRingBeds));
                Assert.Equal(citizens, Sites(map, Bed).Count);
                HashSet<IntVec3> walls = CellsOf(Sites(map, StoneWallMaterials.IsWall));
                List<CellRect> huts = Sites(map, Hut);
                Assert.Equal(3, huts.Count);
                Assert.All(CellsOf(huts), hut => Assert.True(hut.AdjacentToCardinal(walls), "the hut at " + hut + " is not against the ring"));
                Assert.All(CellsOf(huts), hut => Assert.False(hut.AdjacentToCardinal(inside), "the hut at " + hut + " opens onto the inside of the ring"));
            }
        }

        [Fact]
        public void The_ring_spends_the_walls_the_settlement_already_wanted_and_no_more()
        {
            // The layout decides where, never how many. A band this size wanted this many walls before there
            // was a compound; the ring is that many cells, one of them the door.
            int citizens = SettlementTuning.FoundingBandRange.min;
            int wallsWanted = Math.Clamp(
                (int)Math.Ceiling(citizens * ConstructionInitiativeTuning.WallsPerCitizen),
                ConstructionInitiativeTuning.MinWallShelterCount,
                ConstructionInitiativeTuning.MaxWallShelterCount);

            CoreMap map = NewMap(120, 120);
            RunUntilSettled(Band(citizens), map);

            Assert.Equal(wallsWanted, Sites(map, StoneWallMaterials.IsWall).Count + Sites(map, Door).Count);
            Assert.Single(Sites(map, Door));
        }

        [Fact]
        public void A_band_too_small_for_its_walls_to_go_round_its_beds_builds_as_it_did_before()
        {
            // Five citizens want ten walls, and no ring of ten cells holds five beds. No compound, no door,
            // and the same eighteen-odd pieces the settlement always placed.
            CoreMap map = NewMap(60, 60);
            Settlement settlement = Band(5);
            RunUntilSettled(settlement, map);

            Assert.Null(map.settlementCompound);
            Assert.Empty(Sites(map, Door));
            Assert.Equal(5, Sites(map, Bed).Count);
            Assert.NotEmpty(Sites(map, StoneWallMaterials.IsWall));
        }

        [Fact]
        public void Finishing_the_first_bed_does_not_strand_the_rest_of_the_compound_outside_the_home_area()
        {
            // AutoHomeAreaMaker marks a few cells of home area round each finished building, and from the
            // first one on the home area binds. The compound must already be inside it by then.
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);

            RunPasses(settlement, map, 1);
            BuildEverythingPlanned(map); // the first three beds, finished before anything else is planned
            Assert.True(map.areaManager.Home.TrueCount > 0);

            for (int pass = 1; pass < 200; pass++)
            {
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                RunPasses(settlement, map, 1, pass);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) break;
                BuildEverythingPlanned(map);
            }

            AssertIsACompound(map, bedsInside: citizens);
            Assert.All(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial),
                t => Assert.All(t.OccupiedRect().Cells, c => Assert.True(map.areaManager.Home[c], t.def.defName + " at " + c + " is outside the home area")));
        }

        [Fact]
        public void A_ring_wall_destroyed_by_anything_is_laid_out_again_where_it_stood()
        {
            // docs/research/era-arc.md read eroding walls off every artificial building on the map, ruins
            // included. The settlement's own ring is what it keeps: whatever brings one of its walls down, not
            // only an earthquake (which lays its own rebuild blueprint), the gap is planned over again.
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);
            int pass = 0;
            for (; pass < 200; pass++)
            {
                RunPasses(settlement, map, 1, pass);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == 0) break;
                BuildEverythingPlanned(map);
            }
            List<Thing> walls = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => StoneWallMaterials.IsWall(t.def)).ToList();
            Assert.NotEmpty(walls);
            int standing = walls.Count;
            IntVec3 fallen = walls[0].Position;

            walls[0].Destroy(DestroyMode.KillFinalize);
            Assert.Equal(standing - 1, Sites(map, StoneWallMaterials.IsWall).Count);

            RunPasses(settlement, map, 1, pass + 1);

            Assert.Equal(standing, Sites(map, StoneWallMaterials.IsWall).Count);
            Assert.Contains(fallen, CellsOf(Sites(map, StoneWallMaterials.IsWall)));
        }

        // ---- which way things are turned ----

        /// <summary>Stored wood enough for the settlement to want <paramref name="huts"/> storage huts.</summary>
        private static int WoodFor(int huts) => huts * StorageHutNeed.StacksPerStorageHut * Def("WoodLog").stackLimit;

        [Fact]
        public void The_door_lies_in_the_wall_run_it_closes()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            RunUntilSettled(Band(citizens), map);

            // Planned turned, and it keeps the turn through the frame into the finished door.
            Blueprint planned = Assert.Single(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>(),
                bp => ReferenceEquals(bp.EntityToBuild, Door));
            Rot4 plannedTurn = planned.Rotation;
            BuildEverythingPlanned(map);
            Thing door = Assert.Single(map.listerThings.ThingsOfDef(Door));
            Assert.Equal(plannedTurn, door.Rotation);

            // In line with its wall: a wall either side of the way it faces, open ground before and behind
            // it. That is RimWorld's reading of a door's rotation, and for a door in the east side, a wall
            // running north-south, it comes to East.
            HashSet<IntVec3> walls = CellsOf(Sites(map, StoneWallMaterials.IsWall));
            IntVec3 through = door.Rotation.FacingCell;
            IntVec3 along = door.Rotation.Rotated(RotationDirection.Clockwise).FacingCell;
            Assert.Contains(door.Position + along, walls);
            Assert.Contains(door.Position - along, walls);
            Assert.DoesNotContain(door.Position + through, walls);
            Assert.DoesNotContain(door.Position - through, walls);
            Assert.Equal(Rot4.East, door.Rotation);

            // Turning it changed nothing about what it is: it can be walked through, the ground either side
            // of it can be stood on, and with it shut the ring is still closed round every bed.
            Assert.True(map.pathGrid.Walkable(door.Position), "the door at " + door.Position + " cannot be walked through");
            Assert.True(GenGrid.Standable(door.Position + through, map));
            Assert.True(GenGrid.Standable(door.Position - through, map));
            AssertIsACompound(map, bedsInside: citizens);
        }

        [Fact]
        public void Storage_against_the_ring_turns_its_back_to_it_on_every_side()
        {
            // More storage than the ring has room for, so all four sides fill and some huts go elsewhere.
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            RunUntilSettled(Band(citizens, storedWood: WoodFor(60)), map);

            HashSet<IntVec3> inside = AssertIsACompound(map, bedsInside: citizens);
            HashSet<IntVec3> ring = CellsOf(Sites(map, StoneWallMaterials.IsWall));
            ring.UnionWith(CellsOf(Sites(map, Door)));

            var turns = new HashSet<Rot4>();
            int elsewhere = 0;
            foreach (Blueprint hut in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => ReferenceEquals(bp.EntityToBuild, Hut)))
            {
                if (!hut.Position.AdjacentToCardinal(ring))
                {
                    // Not one of the compound's: placed by the draw, unturned as it always was.
                    Assert.Equal(Rot4.North, hut.Rotation);
                    elsewhere++;
                    continue;
                }

                // A model's front is authored toward -Z at North, the opposite of the way a thing faces. So
                // a hut whose front is to open onto the ground outside faces the wall it stands against,
                // and what lies behind its facing is neither the ring nor the inside of it.
                IntVec3 faces = hut.Position + hut.Rotation.FacingCell;
                IntVec3 front = hut.Position - hut.Rotation.FacingCell;
                Assert.True(ring.Contains(faces), "the hut at " + hut.Position + " turned " + hut.Rotation + " does not face the ring");
                Assert.False(ring.Contains(front) || inside.Contains(front), "the hut at " + hut.Position + " opens onto the ring");
                turns.Add(hut.Rotation);
            }

            Assert.Equal(4, turns.Count); // a side each: west, north, south and east of the ring
            Assert.True(elsewhere > 0, "the ring took every hut, so the unturned case was never exercised");
        }

        // ---- determinism ----

        [Fact]
        public void The_same_seed_on_the_same_map_plans_the_same_compound_and_another_seed_another()
        {
            List<string> Run(int seed)
            {
                SimWorld.Map.Map.ResetMapIdCounter();
                Pawn.ResetThingIdCounter();
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap(120, 120);
                RunUntilSettled(Band(SettlementTuning.FoundingBandRange.min, storedWood: 120), map);
                Assert.NotNull(map.settlementCompound);
                return Placements(map);
            }

            List<string> first = Run(777);
            Assert.Equal(first, Run(777));
            // The anchor is a draw from the seeded stream, not a fixed scan order.
            Assert.NotEqual(first, Run(778));
        }

        // ---- what placement already refused, the layout refuses too ----

        [Fact]
        public void Rock_water_and_what_is_already_built_move_the_compound_and_never_bend_it()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            IntVec3 hub = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

            // A lake over the hub, a ruin's walls strewn round it, and somebody else's blueprints.
            var lake = new HashSet<IntVec3>(CellRect.CenteredOn(hub, 4).Cells);
            foreach (IntVec3 c in lake)
            {
                map.terrainGrid.SetTerrain(c, DefDatabase<TerrainDef>.GetNamed("WaterDeep"));
                map.pathGrid.RecalculatePerceivedPathCostAt(c);
            }
            var ruin = new HashSet<IntVec3>();
            for (int i = 0; i < 12; i++) ruin.Add(new IntVec3(hub.x - 11 + 2 * i, 0, hub.z + (i % 2 == 0 ? 8 : -8)));
            foreach (IntVec3 c in ruin) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            var planned = new HashSet<IntVec3>();
            for (int i = 0; i < 6; i++) planned.Add(new IntVec3(hub.x + (i % 2 == 0 ? 9 : -9), 0, hub.z - 5 + 2 * i));
            foreach (IntVec3 c in planned) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Heater")), c, map);

            Settlement settlement = Band(citizens);
            RunUntilSettled(settlement, map);

            // Nothing was planned on the lake, on a ruin or across somebody else's plan ...
            List<Blueprint> own = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => !planned.Contains(bp.Position)).ToList();
            Assert.All(own, bp => Assert.All(bp.OccupiedRect().Cells, c =>
                Assert.False(lake.Contains(c) || ruin.Contains(c) || planned.Contains(c), bp.EntityToBuild.defName + " was planned on blocked ground at " + c)));

            // ... and the compound is whole all the same. The ruin's dozen walls did not come off the ring: a
            // wall is wanted where the ring has a gap, not wherever one happens to stand.
            AssertIsACompound(map, bedsInside: citizens, notTheSettlements: ruin);
        }

        [Fact]
        public void A_map_edge_moves_the_compound_inward()
        {
            // A map barely taller than the compound: the square round the hub runs off it top and bottom.
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(60, 14);
            RunUntilSettled(Band(citizens, storedWood: 60), map);

            AssertIsACompound(map, bedsInside: citizens);
        }

        [Fact]
        public void A_map_with_no_room_for_a_compound_builds_as_it_did_before()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(40, 10); // shorter than the compound is
            RunUntilSettled(Band(citizens), map);

            Assert.Null(map.settlementCompound);
            Assert.Empty(Sites(map, Door));
            Assert.Equal(citizens, Sites(map, Bed).Count);
        }

        // ---- the home area still binds ----

        [Fact]
        public void A_painted_home_area_holds_the_whole_compound()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            var painted = new CellRect(6, 80, 30, 30); // a corner, nowhere near the hub
            foreach (IntVec3 c in painted.Cells) map.areaManager.Home[c] = true;

            RunUntilSettled(Band(citizens, storedWood: 120), map);

            AssertIsACompound(map, bedsInside: citizens);
            Assert.Equal(painted.Area, map.areaManager.Home.TrueCount); // it claimed nothing of its own
            Assert.All(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint),
                bp => Assert.All(bp.OccupiedRect().Cells, c => Assert.True(painted.Contains(c), bp.def.defName + " at " + c + " is outside the painted home area")));
        }

        [Fact]
        public void With_no_home_area_the_compound_becomes_the_home_area()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);

            RunPasses(settlement, map, 1);
            Assert.True(map.areaManager.Home.TrueCount > 0, "the first pass should have claimed a home area");
            int claimed = map.areaManager.Home.TrueCount;

            RunUntilSettled(settlement, map);
            Assert.Equal(claimed, map.areaManager.Home.TrueCount); // claimed once, not grown by planning
            Assert.All(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint),
                bp => Assert.All(bp.OccupiedRect().Cells, c => Assert.True(map.areaManager.Home[c])));
        }

        [Fact]
        public void A_home_area_the_player_moves_away_takes_the_beds_with_it_and_no_wall_follows()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);
            RunPasses(settlement, map, 2); // six beds, in the compound
            List<string> inTheCompound = Placements(map);

            // The player unpaints all of it and paints somewhere else.
            foreach (IntVec3 c in map.AllCells) map.areaManager.Home[c] = false;
            var elsewhere = new CellRect(4, 4, 16, 16);
            foreach (IntVec3 c in elsewhere.Cells) map.areaManager.Home[c] = true;

            RunPasses(settlement, map, 30, from: 2);

            List<Blueprint> later = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Where(bp => !inTheCompound.Contains(bp.EntityToBuild.defName + "@" + bp.Position)).ToList();
            Assert.Equal(citizens - inTheCompound.Count, later.Count(bp => ReferenceEquals(bp.EntityToBuild, Bed)));
            Assert.All(later, bp => Assert.All(bp.OccupiedRect().Cells, c => Assert.True(elsewhere.Contains(c), bp.EntityToBuild.defName + " at " + c + " is outside the home area")));
            Assert.Empty(Sites(map, StoneWallMaterials.IsWall)); // the ring is outside the home area now, and a wall belongs nowhere else
            Assert.Empty(Sites(map, Door));
        }

        [Fact]
        public void A_home_area_no_bigger_than_the_compound_says_so_only_once_the_block_is_full()
        {
            // HomeAreaFullExplanation has to count a free site in the compound as room: the random draw keeps
            // off the compound's ground, so by the draw alone this home area was full before the first bed.
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);
            RunPasses(settlement, map, 1);
            CellRect footprint = map.settlementCompound!.Footprint;
            foreach (IntVec3 c in map.AllCells) map.areaManager.Home[c] = footprint.Contains(c);

            Assert.Null(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map)); // beds to place, and sites for them
            RunUntilSettled(settlement, map);
            Assert.Null(SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map)); // nothing left to place

            // More people than the block has beds for, and nowhere else inside the home area to put one.
            int blockHolds = map.settlementCompound.BedCells.Count();
            for (int i = citizens; i < blockHolds + 1; i++) settlement.AddCitizen(NewHuman("Latecomer" + i));
            RunUntilSettled(settlement, map);

            Assert.Equal(blockHolds, Sites(map, Bed).Count);
            string? why = SettlementConstructionInitiative.HomeAreaFullExplanation(settlement, map);
            Assert.NotNull(why);
            Assert.Contains("1 bed", why);
            Assert.Contains("town bounds", why);
        }

        // ---- growth past the ring ----

        [Fact]
        public void A_settlement_too_big_for_the_biggest_ring_keeps_its_ring_whole_and_beds_the_rest_outside()
        {
            int citizens = SettlementTuning.FoundingBandRange.max + 6;
            CoreMap map = NewMap(120, 120);
            Settlement settlement = Band(citizens);
            for (int pass = 0; pass < 200; pass++)
            {
                RunPasses(settlement, map, 1, pass);
                BuildEverythingPlanned(map); // so the home area grows the way it does in a game
            }

            HashSet<IntVec3> inside = AssertIsACompound(map, bedsInside: BiggestRingBeds);
            List<CellRect> beds = Sites(map, Bed);
            Assert.Equal(citizens, beds.Count);
            HashSet<IntVec3> walls = CellsOf(Sites(map, StoneWallMaterials.IsWall));
            Assert.All(beds.Where(b => !b.Cells.All(inside.Contains)), b => Assert.All(b.Cells, c => Assert.DoesNotContain(c, walls)));
        }

        [Fact]
        public void A_bed_the_player_already_placed_counts_and_the_block_holds_one_fewer()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap map = NewMap(120, 120);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), new IntVec3(3, 0, 3), map);

            RunUntilSettled(Band(citizens), map);

            Assert.Equal(citizens, Sites(map, Bed).Count);
            AssertIsACompound(map, bedsInside: citizens - 1);
        }

        // ---- Scribe ----

        [Fact]
        public void A_compound_half_planned_round_trips_through_Scribe_and_is_finished_where_it_was_begun()
        {
            int citizens = SettlementTuning.FoundingBandRange.min;
            CoreMap original = NewMap(120, 120);
            Settlement settlement = Band(citizens, storedWood: 120);
            RunPasses(settlement, original, 4);
            Assert.NotNull(original.settlementCompound);

            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.NotNull(loaded.settlementCompound);
            Assert.Equal(original.settlementCompound!.Interior, loaded.settlementCompound!.Interior);
            Assert.Equal(Placements(original), Placements(loaded));

            // The loaded map draws nothing to find its compound again: with different streams behind them the
            // two maps still finish the same layout in the same cells.
            List<string> Continue(CoreMap map, int seed)
            {
                Rand.Current = new RandomStream(seed);
                for (int pass = 4; pass < 40; pass++) RunPasses(settlement, map, 1, pass);
                return Placements(map);
            }

            Assert.Equal(Continue(original, 1), Continue(loaded, 2));
            AssertIsACompound(loaded, bedsInside: citizens);
        }

        [Fact]
        public void A_map_with_no_compound_round_trips_with_none()
        {
            CoreMap original = NewMap(30, 30);
            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Null(loaded.settlementCompound);
        }

        // ---- a generated map, a real band ----

        [Fact]
        public void A_founded_settlement_on_a_generated_map_plans_a_compound_nobody_is_shut_out_of()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld("compound-founding", 0.3f, OverallRainfall.Normal,
                OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(7));
            CoreMap map = settlement.EnterMap(world);
            Find.World = world;
            HashSet<IntVec3> ancient = CellsOf(Sites(map, StoneWallMaterials.IsWall)); // MapGen.GenStep_Ruins' walls

            for (int pass = 0; pass < 60; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.Tick();
            }

            Assert.NotNull(map.settlementCompound);
            HashSet<IntVec3> inside = AssertIsACompound(map, bedsInside: settlement.Citizens.Count, notTheSettlements: ancient);

            // Once the ring is up, a founder standing outside it can still walk to a bed.
            BuildEverythingPlanned(map);
            Pawn founder = settlement.Citizens.First(p => p.Spawned && GenGrid.Standable(p.Position, map) && !inside.Contains(p.Position));
            IntVec3 aBed = inside.First();
            Assert.True(map.reachability.CanReachTarget(founder, new SimWorld.AI.LocalTargetInfo(aBed), SimWorld.AI.PathEndMode.OnCell),
                "the founder at " + founder.Position + " cannot reach the bed block at " + aBed);
        }

        /// <summary>
        /// The whole loop with nothing placed or finished by hand: a band on a wooded tile fells its own
        /// timber, raises the compound it planned, and sleeps inside it. The same world and band
        /// <c>MultiCellBedTests</c> and <c>WoodSupplyTests</c> found.
        /// </summary>
        [Fact]
        public void A_founding_band_raises_its_own_compound_and_sleeps_inside_the_ring()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(
                "wood-supply-founding", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i =>
                !world.grid.Tiles[i].WaterCovered && SimWorld.MapGen.TreeTuning.TimberMagnitude(world.grid.Tiles[i]) >= 0.4f);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(4343));
            Find.World = world;
            CoreMap map = settlement.EnterMap(world);
            HashSet<IntVec3> ancient = CellsOf(Sites(map, StoneWallMaterials.IsWall));

            int ringUpTick = -1;
            int sleptInside = 0;
            for (int tick = 0; tick < 15 * GenDate.TicksPerDay; tick++)
            {
                SettlementConstructionInitiative.Tick();
                Find.TickManager.DoSingleTick();
                if (tick % 250 != 0) continue;

                SettlementCompound? compound = map.settlementCompound;
                if (compound == null) continue;
                if (ringUpTick < 0 && map.edificeGrid[compound.DoorCell]?.def == Door
                    && compound.WallCells.All(c => map.edificeGrid[c] is Thing wall && StoneWallMaterials.IsWall(wall.def)))
                {
                    ringUpTick = tick;
                }
                if (ringUpTick < 0) continue;

                // Inside the ring, or in a house the band built on its street: a household that has a home
                // sleeps there and leaves the dormitory (DwellingUtility.FindDwellingBedFor), and with timber to
                // spare the houses can be up before the first night. Counting only the ring then ran all fifteen
                // days looking for a sleeper who was, correctly, at home.
                sleptInside += settlement.Citizens.Count(p => p.Spawned && p.Asleep
                    && (compound.Interior.Contains(p.Position)
                        || (SimWorld.AI.RestUtility.CurrentBed(p) is Thing slept && DwellingUtility.IsDwellingBed(slept))));
                if (sleptInside > 0) break;
            }

            output.WriteLine("ring finished at tick " + ringUpTick + " (day "
                + (ringUpTick / (float)GenDate.TicksPerDay).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "); sleepers seen inside it: " + sleptInside);
            Assert.True(ringUpTick >= 0, "the band never finished its ring:\n" + Picture(map));
            Assert.True(sleptInside > 0, "nobody was ever seen asleep inside the ring or in a house it built");

            HashSet<IntVec3> inside = AssertIsACompound(map, bedsInside: Sites(map, Bed).Count, notTheSettlements: ancient);

            // Nobody was walled in or out while it went up.
            IntVec3 aBed = inside.First();
            Assert.All(settlement.Citizens.Where(p => p.Spawned && !p.Dead && !p.Downed), p =>
                Assert.True(map.reachability.CanReachTarget(p, new SimWorld.AI.LocalTargetInfo(aBed), SimWorld.AI.PathEndMode.OnCell),
                    "the citizen at " + p.Position + " cannot reach the bed block"));
        }
    }

    internal static class CompoundTestExtensions
    {
        public static bool AdjacentToCardinal(this IntVec3 cell, HashSet<IntVec3> cells) =>
            GenAdj.CardinalDirections.Any(o => cells.Contains(cell + o));
    }
}
