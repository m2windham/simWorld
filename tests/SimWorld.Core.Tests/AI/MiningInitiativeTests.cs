using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The settlement decides what to dig (system: mining; <see cref="MiningInitiative"/>). RimWorld's player
    /// drags a box over rock; this port's settlement has no player to do it, and the giver now reads marks
    /// only, so something has to make them — and has to do it in a way that cannot be mistaken for the
    /// defect it replaced, which dug a whole map's mountains out in six days.
    ///
    /// <para/>Written against a hand-built mountain rather than a generated map, so that every number in an
    /// assertion can be read off the picture: a 20×20 block of granite in the middle of a 40×40 map, open
    /// ground all round it, the road hub (the anchor) at the block's centre. The one test that runs on a real
    /// generated map is in <c>SettlementMiningTests</c>.
    ///
    /// <para/><b>The tuning numbers are pinned as relations, never as literals</b> (CLAUDE.md):
    /// <see cref="MiningTuning.MaxOutstandingDesignations"/> is a design number, so what is asserted is what
    /// it has to be true of — it is a sliver of a mountain, and it holds the longest dig the generated maps
    /// need; <see cref="MiningTuning.OreReserve"/> covers the dearest single build an ore is spent on.
    /// </summary>
    public class MiningInitiativeTests : ContentTestBase
    {
        public MiningInitiativeTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private const int Side = 40;
        private static readonly CellRect Mountain = new CellRect(10, 10, 20, 20);

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        /// <summary>A map with a block of granite in the middle, a citizen standing outside it, and nothing
        /// else: no roof, so the roof guard has nothing to say.</summary>
        private CoreMap MountainMap(out Pawn hand, string rock = "Granite")
        {
            var map = new CoreMap(Side, Side, TerrainDefOf.Soil);
            foreach (IntVec3 c in Mountain.Cells) GenSpawn.Spawn(ThingMaker.MakeThing(Def(rock)), c, map);
            hand = NewHuman("Hand");
            GenSpawn.Spawn(hand, new IntVec3(2, 0, 2), map);
            return map;
        }

        private static void PlaceVein(CoreMap map, IntVec3 cell, string vein = "MineableSteel")
        {
            map.edificeGrid[cell]?.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def(vein)), cell, map);
        }

        private static void Stock(CoreMap map, string defName, int count)
        {
            Thing stack = ThingMaker.MakeThing(Def(defName));
            stack.stackCount = count;
            GenSpawn.Spawn(stack, new IntVec3(1, 0, 38), map);
        }

        private static List<IntVec3> Marks(CoreMap map) =>
            map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine)
                .Select(d => d.target.Cell)
                .OrderBy(c => c.z).ThenBy(c => c.x)
                .ToList();

        private static bool HasOpenNeighbour(CoreMap map, IntVec3 c) =>
            GenAdj.AdjacentCells.Any(a => GenGrid.InBounds(c + a, map) && GenGrid.Walkable(c + a, map));

        private static void PlaceWallBlueprints(CoreMap map, int count, string wall = "WallGranite")
        {
            ThingDef blueprint = Def("Blueprint_" + wall);
            int placed = 0;
            for (int x = 1; x < Side - 1 && placed < count; x++)
            {
                for (int z = 5; z < 9 && placed < count; z++)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(blueprint), new IntVec3(x, 0, z), map);
                    placed++;
                }
            }
            Assert.Equal(count, placed);
        }

        private static Settlement PeopledSettlement(CoreMap map)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "TestSettlement", 0);
            foreach (Pawn p in map.mapPawns.AllPawns.Where(p => p.RaceProps.Humanlike)) settlement.AddCitizen(p);
            return settlement;
        }

        // ---- nothing wanted, nothing dug ----

        [Fact]
        public void With_no_ore_in_the_ground_and_no_build_site_waiting_it_marks_nothing_at_all()
        {
            CoreMap map = MountainMap(out _);

            int marked = MiningInitiative.Run(null, map);

            Assert.Equal(0, marked);
            Assert.Empty(map.designationManager.AllDesignations);
            Assert.Equal(Mountain.Area, map.listerThings.ThingsOfDef(Def("Granite")).Count);
        }

        [Fact]
        public void A_settlement_that_wants_ore_never_reaches_for_plain_rock_to_get_it()
        {
            CoreMap map = MountainMap(out _);
            Assert.NotEmpty(MiningInitiative.WantsOf(null, map)); // it does want steel: the reserve is empty

            Assert.Equal(0, MiningInitiative.Run(null, map));
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void Without_a_citizen_standing_on_the_map_there_are_no_hands_and_nothing_is_marked()
        {
            var map = new CoreMap(Side, Side, TerrainDefOf.Soil);
            foreach (IntVec3 c in Mountain.Cells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Granite")), c, map);
            PlaceVein(map, new IntVec3(19, 0, 15));

            Assert.Equal(0, MiningInitiative.Run(null, map));
        }

        // ---- ore: the vein and the shortest dig to it ----

        [Fact]
        public void An_ore_vein_is_marked_with_the_shortest_dig_to_it_and_nothing_beside_it()
        {
            CoreMap map = MountainMap(out _);
            PlaceVein(map, new IntVec3(19, 0, 15)); // five rock cells in from the south face

            int marked = MiningInitiative.Run(null, map);

            var expected = Enumerable.Range(10, 6).Select(z => new IntVec3(19, 0, z)).ToList(); // the face cell .. the vein
            Assert.Equal(6, marked);
            Assert.Equal(expected, Marks(map));
        }

        [Fact]
        public void A_vein_on_the_surface_is_marked_alone()
        {
            CoreMap map = MountainMap(out _);
            PlaceVein(map, new IntVec3(19, 0, 10));

            Assert.Equal(1, MiningInitiative.Run(null, map));
            Assert.Equal(new[] { new IntVec3(19, 0, 10) }, Marks(map));
        }

        [Fact]
        public void A_vein_further_in_than_the_cap_allows_is_not_marked()
        {
            var map = new CoreMap(60, 60, TerrainDefOf.Soil);
            foreach (IntVec3 c in new CellRect(5, 2, 50, 56).Cells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Granite")), c, map);
            Pawn hand = NewHuman("Hand");
            GenSpawn.Spawn(hand, new IntVec3(1, 0, 1), map);
            PlaceVein(map, new IntVec3(30, 0, 2 + MiningTuning.MaxOutstandingDesignations + 5)); // deeper than any dig the cap holds
            Assert.Contains(MiningInitiative.WantsOf(null, map), w => w.Item.defName == "Steel");

            Assert.Equal(0, MiningInitiative.Run(null, map));
        }

        [Fact]
        public void It_marks_the_vein_nearest_the_settlement_first_and_only_as_many_as_it_wants()
        {
            CoreMap map = MountainMap(out _);
            // Hub is (20,20). Near: three in from the south face. Far: the north-east corner region.
            var near = new IntVec3(19, 0, 12);
            var far = new IntVec3(28, 0, 28);
            PlaceVein(map, near);
            PlaceVein(map, far);
            // Sixty on the ground leaves forty short of the reserve: one vein's nominal yield covers it.
            Stock(map, "Steel", 60);

            MiningInitiative.Run(null, map);

            Assert.Contains(near, Marks(map));
            Assert.DoesNotContain(far, Marks(map));
        }

        [Fact]
        public void The_home_area_is_where_nearest_is_measured_from_once_there_is_one()
        {
            CoreMap map = MountainMap(out _);
            var nearHub = new IntVec3(19, 0, 12);
            var nearHome = new IntVec3(28, 0, 28);
            PlaceVein(map, nearHub);
            PlaceVein(map, nearHome);
            Stock(map, "Steel", 60);
            foreach (IntVec3 c in CellRect.CenteredOn(new IntVec3(33, 0, 33), 2).Cells) map.areaManager.Home[c] = true;

            MiningInitiative.Run(null, map);

            Assert.Contains(nearHome, Marks(map));
            Assert.DoesNotContain(nearHub, Marks(map));
        }

        [Fact]
        public void It_stops_when_the_reserve_is_in_hand_and_starts_when_it_is_one_short()
        {
            CoreMap map = MountainMap(out _);
            PlaceVein(map, new IntVec3(19, 0, 12));
            Stock(map, "Steel", MiningTuning.OreReserve);
            Assert.Equal(0, MiningInitiative.Run(null, map));

            map.listerThings.ThingsOfDef(Def("Steel")).Single().stackCount = MiningTuning.OreReserve - 1;
            Assert.True(MiningInitiative.Run(null, map) > 0);
        }

        [Fact]
        public void The_books_count_toward_a_reserve_and_the_ground_alone_counts_toward_a_build_site()
        {
            CoreMap map = MountainMap(out _);
            PlaceVein(map, new IntVec3(19, 0, 12));
            Settlement settlement = PeopledSettlement(map);
            settlement.AddStore(Def("Steel"), 5 * MiningTuning.OreReserve);

            // A reserve is a statement about what the settlement owns, wherever it keeps it.
            Assert.Equal(0, MiningInitiative.Run(settlement, map));

            // But a frame can only be fed from the ground: a ledger of steel no builder can reach is no steel for it.
            ThingDef steelBuilding = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building && d.entityToBuild == null && d.CostListCountFor(Def("Steel")) > 0)
                .First(d => GenConstruct.BlueprintDefFor(d) != null);
            GenSpawn.Spawn(ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(steelBuilding)!), new IntVec3(1, 0, 20), map);

            Assert.True(MiningInitiative.Run(settlement, map) > 0, "A site waiting on steel with none on the ground should send someone for the vein.");
        }

        [Fact]
        public void A_vein_the_roof_guard_would_refuse_is_never_marked()
        {
            var map = new CoreMap(20, 3, TerrainDefOf.Soil);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(0, 0, 1), map);
            PlaceVein(map, new IntVec3(2, 0, 1));
            for (int x = 1; x <= 8; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), SimWorld.Map.RoofDefOf.RoofConstructed);
            GenSpawn.Spawn(NewHuman("Hand"), new IntVec3(2, 0, 0), map);
            Assert.True(RoofCollapseUtility.WouldCollapseRoofIfRemoved(map.edificeGrid[new IntVec3(2, 0, 1)]!), "Setup: the vein must be a support.");

            Assert.Equal(0, MiningInitiative.Run(null, map));

            for (int x = 1; x <= 8; x++) map.roofGrid.SetRoof(new IntVec3(x, 0, 1), null);
            Assert.Equal(1, MiningInitiative.Run(null, map)); // positive control: the same vein, no roof, is marked
        }

        // ---- stone: only for builds that are waiting, only from a face, only as much as they take ----

        [Fact]
        public void A_stone_wall_waiting_for_blocks_is_dug_for_in_chunks_and_a_chunk_is_a_tenth_of_the_cells()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 1); // five blocks of granite: one chunk cuts twenty
            ThingDef granite = Def("Granite");
            int cellsPerChunk = (int)Math.Round(1f / (granite.mineableYield * granite.mineableDropChance));

            int marked = MiningInitiative.Run(null, map);

            List<IntVec3> marks = Marks(map);
            Assert.Equal(cellsPerChunk, marked);
            Assert.All(marks, c => Assert.Equal("Granite", map.edificeGrid[c]!.def.defName));
            Assert.All(marks, c => Assert.True(HasOpenNeighbour(map, c), c + " is not on a face: stone is quarried, never tunnelled for."));
        }

        [Fact]
        public void Stone_is_taken_from_the_face_nearest_the_settlement_first()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 1);

            MiningInitiative.Run(null, map);

            var hub = new IntVec3(Side / 2, 0, Side / 2);
            List<IntVec3> marks = Marks(map);
            int farthestMarked = marks.Max(c => (c - hub).LengthHorizontalSquared);
            int nearestLeft = Mountain.Cells
                .Where(c => map.designationManager.DesignationAt(c, DesignationDefOf.Mine) == null && HasOpenNeighbour(map, c))
                .Min(c => (c - hub).LengthHorizontalSquared);
            Assert.True(farthestMarked <= nearestLeft, "A face cell nearer the settlement was left while a farther one was marked.");
        }

        [Fact]
        public void Chunks_or_blocks_already_on_the_ground_cover_a_wall_and_nothing_is_dug()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 1);

            Stock(map, "ChunkGranite", 1);
            Assert.Equal(0, MiningInitiative.Run(null, map));
        }

        [Fact]
        public void Blocks_already_on_the_ground_cover_a_wall_and_nothing_is_dug()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 1);

            Stock(map, "BlocksGranite", 5);
            Assert.Equal(0, MiningInitiative.Run(null, map));
        }

        [Fact]
        public void The_settlements_own_walls_never_send_it_digging_for_stone()
        {
            // SettlementConstructionInitiative builds wood unless blocks are already in hand, so on its own
            // it has no stone shortfall, and this initiative has no appetite for stone of its own.
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 5, "Wall");

            Assert.Equal(0, MiningInitiative.Run(null, map));
        }

        // ---- the cap ----

        [Fact]
        public void However_much_stone_is_wanted_it_never_marks_more_than_the_cap_and_never_a_mountain()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 100); // five hundred blocks: twenty-five chunks: two hundred and fifty cells

            int first = MiningInitiative.Run(null, map);
            int second = MiningInitiative.Run(null, map);

            Assert.Equal(MiningTuning.MaxOutstandingDesignations, first);
            Assert.Equal(0, second);
            Assert.Equal(MiningTuning.MaxOutstandingDesignations, map.designationManager.AllDesignations.Count);
            Assert.True(map.designationManager.AllDesignations.Count * 100 < Mountain.Area * 10,
                "The most it will have marked at once must be a sliver of one mountain.");
        }

        [Fact]
        public void It_marks_more_only_as_the_marked_cells_are_mined_out_and_never_exceeds_the_cap_at_any_moment()
        {
            CoreMap map = MountainMap(out Pawn hand);
            PlaceWallBlueprints(map, 100);
            int cap = MiningTuning.MaxOutstandingDesignations;
            MiningInitiative.Run(null, map);

            for (int round = 0; round < 5; round++)
            {
                List<IntVec3> marks = Marks(map);
                for (int i = 0; i < 5; i++) MineableUtility.GetFirstMineable(marks[i], map)!.DestroyMined(hand);
                Assert.Equal(cap - 5, map.designationManager.AllDesignations.Count);

                int topped = MiningInitiative.Run(null, map);

                Assert.Equal(5, topped);
                Assert.Equal(cap, map.designationManager.AllDesignations.Count);
            }
        }

        [Fact]
        public void The_players_marks_count_against_the_cap_with_the_settlements_own()
        {
            CoreMap map = MountainMap(out _);
            PlaceWallBlueprints(map, 100);
            foreach (IntVec3 c in Mountain.Cells.Where(c => c.z <= Mountain.minZ + 1).Take(MiningTuning.MaxOutstandingDesignations))
            {
                map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine));
            }

            Assert.Equal(0, MiningInitiative.Run(null, map));
            Assert.Equal(MiningTuning.MaxOutstandingDesignations, map.designationManager.AllDesignations.Count);

            map.designationManager.DesignationAt(new IntVec3(Mountain.minX, 0, Mountain.minZ), DesignationDefOf.Mine)!.Delete();
            Assert.Equal(1, MiningInitiative.Run(null, map));
        }

        [Fact]
        public void The_cap_is_a_sliver_of_a_mountain_and_holds_the_longest_dig_the_generated_maps_need()
        {
            // Measured on generated 200x200 interiors (seeds 777 and roof-a): about thirteen thousand rock
            // cells, and the shortest dig from open ground to an ore vein 1 to 15 cells. The test over a real
            // map is SettlementMiningTests; this pins the relations the number is chosen against.
            const int longestDigMeasured = 15;
            const int rockCellsOnAGeneratedMap = 12000;
            Assert.True(MiningTuning.MaxOutstandingDesignations >= longestDigMeasured + 1);
            Assert.True(MiningTuning.MaxOutstandingDesignations * 100 < rockCellsOnAGeneratedMap);
        }

        // ---- what is a reserve ----

        [Fact]
        public void The_ores_a_building_is_made_of_and_the_trade_currency_are_reserved_and_nothing_else_is()
        {
            Assert.True(MiningInitiative.IsReservedOre(Def("Steel")), "steel is in a costList");
            Assert.True(MiningInitiative.IsReservedOre(EconomyThingDefOf.Silver), "silver is what every trade is paid in");
            Assert.False(MiningInitiative.IsReservedOre(Def("Flint")), "nothing is built from flint");
            Assert.False(MiningInitiative.IsReservedOre(Def("Salt")));
            Assert.False(MiningInitiative.IsReservedOre(Def("ChunkGranite")), "a chunk is stone for a build, never a reserve");
            Assert.False(MiningInitiative.IsReservedOre(Def("WoodLog")), "not an ore at all");

            // And it is read off content: everything it says is reserved really is a vein's yield that something costs.
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            foreach (ThingDef item in all.Where(d => d.mineable && d.mineableScatterCommonality > 0f).Select(d => d.mineableThing!).Distinct())
            {
                bool costed = all.Any(d => d.category == ThingCategory.Building && d.entityToBuild == null && d.CostListCountFor(item) > 0);
                Assert.Equal(costed || ReferenceEquals(item, EconomyThingDefOf.Silver), MiningInitiative.IsReservedOre(item));
            }
        }

        [Fact]
        public void The_reserve_covers_the_dearest_single_build_any_ore_is_spent_on()
        {
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            int dearest = 0;
            foreach (ThingDef item in all.Where(d => d.mineable && d.mineableScatterCommonality > 0f).Select(d => d.mineableThing!).Distinct())
            {
                if (!MiningInitiative.IsReservedOre(item)) continue;
                foreach (ThingDef building in all.Where(d => d.category == ThingCategory.Building && d.entityToBuild == null))
                {
                    dearest = Math.Max(dearest, building.CostListCountFor(item));
                }
            }

            Assert.True(dearest > 0, "Setup: content spends some ore on a building.");
            Assert.True(MiningTuning.OreReserve >= dearest,
                "A reserve of " + MiningTuning.OreReserve + " cannot afford the dearest single build (" + dearest + ").");
        }

        // ---- shape ----

        [Fact]
        public void The_same_state_always_gets_the_same_marks()
        {
            List<IntVec3> Run()
            {
                CoreMap map = MountainMap(out _);
                PlaceVein(map, new IntVec3(15, 0, 14));
                PlaceVein(map, new IntVec3(24, 0, 25));
                PlaceWallBlueprints(map, 3);
                MiningInitiative.Run(null, map);
                return Marks(map);
            }

            Pawn.ResetThingIdCounter();
            List<IntVec3> first = Run();
            Pawn.ResetThingIdCounter();
            List<IntVec3> second = Run();

            Assert.NotEmpty(first);
            Assert.Equal(first, second);
        }

        [Fact]
        public void The_map_tick_runs_it_on_the_rare_tick_and_not_between()
        {
            CoreMap map = MountainMap(out _);
            PlaceVein(map, new IntVec3(19, 0, 12));

            Find.TickManager.DebugSetTicksGame(1);
            map.MapTick();
            Assert.Empty(map.designationManager.AllDesignations);

            Find.TickManager.DebugSetTicksGame(MiningTuning.IntervalTicks);
            map.MapTick();
            Assert.NotEmpty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_vein_it_marked_is_mined_by_a_citizen_with_nothing_else_to_do_and_the_ore_lands_in_its_cell()
        {
            CoreMap map = MountainMap(out Pawn hand);
            hand.skills!.GetSkill(SimWorld.Work.SkillDefOf.Mining)!.Level = 20;
            var vein = new IntVec3(19, 0, 12);
            PlaceVein(map, vein);
            MiningInitiative.Run(null, map);
            Assert.Equal(3, Marks(map).Count);

            RunTicks(15000, hand);

            Assert.Empty(map.designationManager.AllDesignations);
            Assert.True(map.edificeGrid[vein] == null, "The vein should be mined out.");
            Assert.True(map.listerThings.ThingsOfDef(Def("Steel")).Sum(t => t.stackCount) > 0);
            // And it dug the three cells it was asked to and not a cell more.
            Assert.Equal(Mountain.Area - 3, Mountain.Cells.Count(c => map.edificeGrid[c] != null));
        }
    }
}
