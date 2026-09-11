using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// The chain from rock to usable material (system: stonework). Mining drops a chunk, a settlement raises
    /// a stonecutter's table where the chunks lie, a mason cuts them into blocks through the real bill path,
    /// and the settlement spends the blocks on the walls it already wanted.
    ///
    /// <para/>Three holes in that chain are what these tests exist to keep closed, and each had shipped: only
    /// sandstone had a recipe, so granite and limestone could be mined and never used; nothing in
    /// <c>src/</c> ever created a bill, so the stonecutter's table was a bench no citizen could ever be given
    /// work at; and no Def anywhere was built out of a stone block, so cutting one moved the hole along
    /// rather than closing it. The first three tests below are the audit of those three, written against
    /// content rather than against the three stones this port happens to ship — a fourth stone added in XML
    /// is caught by them with no test change.
    /// </summary>
    public class StonecuttingTests : ContentTestBase
    {
        public StonecuttingTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX = 16, int sizeZ = 16) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static IReadOnlyList<ThingDef> AllThingDefs => DefDatabase<ThingDef>.AllDefsListForReading;

        private static IReadOnlyList<CoreRecipeDef> AllRecipes => DefDatabase<CoreRecipeDef>.AllDefsListForReading;

        private static void KnowMasonry() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static Pawn Worker(CoreMap map, IntVec3 cell, WorkTypeDef workType, string name = "Mason")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            p.workSettings.DisableAll();
            p.workSettings.SetPriority(workType, 3);
            return p;
        }

        private static SimWorld.Things.CompBillGiver Comp(Thing bench) =>
            ((ThingWithComps)bench).GetComp<SimWorld.Things.CompBillGiver>()!;

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "TestSettlement" + tile, 0);

        private static IEnumerable<ThingDef> BlueprintEntities(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<SimWorld.Building.Blueprint>()
                .Select(bp => bp.EntityToBuild);

        /// <summary>Every stonecutting recipe the shipped content declares — the same question
        /// <see cref="SimWorld.Crafting.StonecutterInitiative.IsStonecutting"/> asks the content, asked here
        /// so these tests cannot pass by agreeing with a bug in that predicate about which recipes exist.</summary>
        private static List<CoreRecipeDef> StonecuttingRecipes() =>
            AllRecipes.Where(r => r.products != null && r.products.Count > 0
                && r.products.All(p => p.thingDef?.thingCategories?.Any(c => c.defName == "StoneBlocks") == true))
                .ToList();

        /// <summary>Every Def some rock drops when it is mined out, which after the mining lane is a real
        /// question about content rather than a list written here.</summary>
        private static List<ThingDef> MinedChunks() =>
            AllThingDefs.Where(d => d.mineableThing != null)
                .Select(d => d.mineableThing!)
                .Where(t => t.defName.StartsWith("Chunk", System.StringComparison.Ordinal))
                .Distinct()
                .ToList();

        // -------------------------------------------------------------------------------------------
        // The audit: the three holes this lane closed, asserted against content.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Every_chunk_the_rock_drops_can_be_cut_into_blocks()
        {
            List<ThingDef> chunks = MinedChunks();

            // Vacuity guard: if mineableThing ever stopped naming chunks this test would pass by finding
            // nothing to check, which is exactly how an audit rots.
            Assert.True(chunks.Count >= 3, "Expected the shipped rock types to drop chunks; found " + chunks.Count);

            List<CoreRecipeDef> recipes = StonecuttingRecipes();
            foreach (ThingDef chunk in chunks)
            {
                bool consumed = recipes.Any(r => r.ingredients != null
                    && r.ingredients.Any(i => i.filter.AllowedThingDefs.Contains(chunk)));
                Assert.True(consumed, chunk.defName + " is mined out of the ground and no recipe consumes it.");
            }
        }

        [Fact]
        public void Every_block_a_recipe_cuts_is_spent_by_something_that_can_be_built()
        {
            List<CoreRecipeDef> recipes = StonecuttingRecipes();
            Assert.True(recipes.Count >= 3, "Expected one stonecutting recipe per stone; found " + recipes.Count);

            foreach (CoreRecipeDef recipe in recipes)
            {
                foreach (SimWorld.Crafting.ThingDefCountClass product in recipe.products!)
                {
                    ThingDef block = product.thingDef;
                    bool spent = AllThingDefs.Any(d => d.costList != null
                        && d.costList.Any(c => ReferenceEquals(c.thingDef, block)));
                    Assert.True(spent, block.defName + " is cut by " + recipe.defName + " and nothing is built out of it.");

                    // And the settlement can actually reach it: a blueprint/frame pair has to exist or the
                    // construction pipeline has nothing to place.
                    ThingDef? wall = StoneWallMaterials.WallBuiltFrom(block);
                    Assert.NotNull(wall);
                    Assert.NotNull(GenConstruct.BlueprintDefFor(wall!));
                    Assert.NotNull(GenConstruct.FrameDefFor(wall!));
                }
            }
        }

        [Fact]
        public void Every_stonecutting_recipe_reaches_a_bench_that_a_work_giver_serves()
        {
            IReadOnlyList<WorkGiverDef> givers = DefDatabase<WorkGiverDef>.AllDefsListForReading;

            foreach (CoreRecipeDef recipe in StonecuttingRecipes())
            {
                Assert.NotNull(recipe.recipeUsers);
                ThingDef bench = Assert.Single(recipe.recipeUsers!,
                    u => u.comps?.OfType<SimWorld.Things.CompProperties_BillGiver>().Any() == true);

                WorkTypeDef workType = bench.comps!.OfType<SimWorld.Things.CompProperties_BillGiver>().Single().workType;

                // A bench nobody's trade covers is a recipe nothing runs, which is the class of defect this
                // whole lane is about.
                Assert.Contains(givers, g => g.workType == workType
                    && g.Worker is SimWorld.Crafting.WorkGiver_DoBill);

                // And something has to be able to raise the bench in the first place. Before this lane
                // TableStonecutter had no Blueprint/Frame pair at all, so it could only ever exist because a
                // test spawned it.
                Assert.NotNull(GenConstruct.BlueprintDefFor(bench));
                Assert.NotNull(GenConstruct.FrameDefFor(bench));
            }
        }

        // -------------------------------------------------------------------------------------------
        // The numbers: ordering and 1:1 claims, never literals.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Granite_outlasts_limestone_outlasts_sandstone_and_every_stone_wall_outlasts_wood()
        {
            int granite = StoneworkThingDefOf.WallGranite.BaseMaxHitPoints;
            int limestone = StoneworkThingDefOf.WallLimestone.BaseMaxHitPoints;
            int sandstone = StoneworkThingDefOf.WallSandstone.BaseMaxHitPoints;
            int wood = ConstructionThingDefOf.Wall.BaseMaxHitPoints;

            Assert.True(granite > limestone, "granite should outlast limestone");
            Assert.True(limestone > sandstone, "limestone should outlast sandstone");
            Assert.True(sandstone > wood, "even the softest stone wall should outlast a wooden one");
        }

        [Fact]
        public void Cutting_costs_the_same_work_and_yields_the_same_blocks_whatever_the_stone()
        {
            List<CoreRecipeDef> recipes = StonecuttingRecipes();

            // RimWorld prices every Make_Blocks_* identically and puts the whole difference between stones in
            // the finished structure; this is that 1:1 kept, and the reason the test above is about walls.
            Assert.Single(recipes.Select(r => r.workAmount).Distinct());
            Assert.Single(recipes.Select(r => r.products!.Single().count).Distinct());
            Assert.All(recipes, r => Assert.Equal(1, r.ingredients!.Single().GetBaseCount()));

            // No workSkill, matching RimWorld's stonecutting and the sandstone recipe that already shipped.
            Assert.All(recipes, r => Assert.Null(r.workSkill));
        }

        [Fact]
        public void A_bench_is_only_ever_planned_where_a_mason_could_reach_the_stone_from_it()
        {
            // The relation that matters, not the literal: a table further from the chunks than
            // WorkGiver_DoBill will look for ingredients is a table nobody can ever work.
            Assert.True(
                SimWorld.Crafting.StonecuttingTuning.BenchPlacementRadius
                    < SimWorld.Crafting.WorkGiver_DoBill.IngredientSearchRadius,
                "a bench placed further from the stone than the giver searches could never be worked");
        }

        // -------------------------------------------------------------------------------------------
        // The settlement deciding for itself: benches and bills.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_settlement_raises_a_stonecutter_where_its_chunks_lie()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Thing chunk = SpawnStack(map, new IntVec3(8, 0, 8), "ChunkGranite", 4);

            Assert.Empty(BlueprintEntities(map));

            SimWorld.Crafting.StonecutterInitiative.Run(map);

            SimWorld.Building.Blueprint blueprint = Assert.Single(
                map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<SimWorld.Building.Blueprint>());
            Assert.Same(Def("TableStonecutter"), blueprint.EntityToBuild);

            int distance = (blueprint.Position - chunk.Position).LengthHorizontalSquared;
            int reach = SimWorld.Crafting.WorkGiver_DoBill.IngredientSearchRadius;
            Assert.True(distance <= reach * reach,
                "the table was planned out of reach of the stone it exists to cut");

            // And it does not keep planning tables it has already planned.
            SimWorld.Crafting.StonecutterInitiative.Run(map);
            Assert.Single(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
        }

        [Fact]
        public void Nothing_is_planned_or_queued_before_the_civilization_knows_masonry()
        {
            CoreMap map = NewMap();
            SpawnStack(map, new IntVec3(8, 0, 8), "ChunkGranite", 4);
            Thing bench = Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");

            SimWorld.Crafting.StonecutterInitiative.Run(map);

            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.Empty(Comp(bench).BillStack.Bills);
        }

        [Fact]
        public void A_bench_gets_one_standing_bill_per_stone_on_the_map_and_never_a_second()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Thing bench = Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");
            SpawnStack(map, new IntVec3(5, 0, 6), "ChunkGranite", 2);
            SpawnStack(map, new IntVec3(6, 0, 5), "ChunkLimestone", 2);

            SimWorld.Crafting.StonecutterInitiative.Run(map);

            List<string> queued = Comp(bench).BillStack.Bills.Select(b => b.recipe.defName).ToList();
            Assert.Equal(new[] { "Make_Blocks_Granite", "Make_Blocks_Limestone" }, queued);

            // No sandstone chunk is on this map, so no sandstone bill: a bill whose ingredient is nowhere is
            // the same unreachable content this lane was opened over.
            Assert.DoesNotContain("Make_Blocks_Sandstone", queued);

            // Idempotent, which matters because this runs every rare tick for the life of the settlement.
            SimWorld.Crafting.StonecutterInitiative.Run(map);
            SimWorld.Crafting.StonecutterInitiative.Run(map);
            Assert.Equal(2, Comp(bench).BillStack.Bills.Count);
        }

        [Fact]
        public void A_standing_bill_wants_exactly_the_walls_the_settlement_could_ask_for()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Thing bench = Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");
            SpawnStack(map, new IntVec3(5, 0, 6), "ChunkGranite", 2);

            SimWorld.Crafting.StonecutterInitiative.Run(map);

            var bill = Assert.IsType<SimWorld.Crafting.Bill_Production>(Assert.Single(Comp(bench).BillStack.Bills));
            Assert.Equal(SimWorld.Crafting.BillRepeatMode.TargetCount, bill.repeatMode);

            // Derived, not chosen: every wall the construction initiative could ever want, at that wall's own
            // costList price. The assertion is the derivation, so changing either end moves both together.
            int blocksPerWall = StoneworkThingDefOf.WallGranite.costList!
                .Single(c => c.thingDef == Def("BlocksGranite")).count;
            Assert.Equal(ConstructionInitiativeTuning.MaxWallShelterCount * blocksPerWall, bill.targetCount);

            // Hysteresis is RimWorld's own mechanism: stop when stocked, start again once well below.
            Assert.True(bill.unpauseWhenYouHave < bill.targetCount);
            Assert.True(bill.unpauseWhenYouHave > 0);
        }

        [Fact]
        public void A_stocked_settlement_stops_cutting_and_starts_again_when_the_pile_runs_down()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Thing bench = Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");
            SpawnStack(map, new IntVec3(5, 0, 6), "ChunkGranite", 2);
            SimWorld.Crafting.StonecutterInitiative.Run(map);

            var bill = (SimWorld.Crafting.Bill_Production)Comp(bench).BillStack.Bills.Single();
            Assert.True(bill.ShouldDoNow(), "an empty settlement should want stone cut");

            Thing stock = SpawnStack(map, new IntVec3(7, 0, 7), "BlocksGranite", bill.targetCount);
            Assert.False(bill.ShouldDoNow(), "a settlement with a full stock of blocks should stop cutting");

            // Spending some of it is not enough to restart the bench — that is the whole point of the band.
            stock.stackCount = bill.targetCount - 1;
            Assert.False(bill.ShouldDoNow());

            stock.stackCount = 0;
            Assert.True(bill.ShouldDoNow(), "a settlement that has spent its stone should cut more");
        }

        // -------------------------------------------------------------------------------------------
        // End to end: a chunk becomes a block becomes a wall.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_mason_cuts_granite_into_blocks_through_the_real_bill_path()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");
            Thing chunks = SpawnStack(map, new IntVec3(5, 0, 6), "ChunkGranite", 3);
            Pawn mason = Worker(map, new IntVec3(2, 0, 2), WorkTypeDefOf.Crafting);

            // Nobody queued this bill: the settlement did, which is the seam that did not exist before.
            SimWorld.Crafting.StonecutterInitiative.Run(map);

            RunTicks(8000, mason);

            Assert.True(chunks.Destroyed || chunks.stackCount < 3, "the granite chunk was never consumed");
            Assert.Contains(map.listerThings.AllThings, t => t.def.defName == "BlocksGranite");
        }

        [Fact]
        public void The_settlement_walls_itself_in_the_best_stone_it_has_and_in_wood_when_it_has_none()
        {
            KnowMasonry();
            CoreMap map = NewMap();

            Assert.Same(ConstructionThingDefOf.Wall, StoneWallMaterials.PreferredWallDef(map));

            Thing sandstone = SpawnStack(map, new IntVec3(3, 0, 3), "BlocksSandstone", 50);
            Assert.Same(StoneworkThingDefOf.WallSandstone, StoneWallMaterials.PreferredWallDef(map));

            // The toughest stone it can afford a whole wall of, not the one it has most of.
            SpawnStack(map, new IntVec3(4, 0, 3), "BlocksGranite", 5);
            Assert.Same(StoneworkThingDefOf.WallGranite, StoneWallMaterials.PreferredWallDef(map));

            // Half a wall's worth is not a wall: a blueprint it could never finish would sit on the cell
            // forever counting toward a need nobody was filling.
            map.listerThings.ThingsOfDef(Def("BlocksGranite")).Single().Destroy();
            sandstone.Destroy();
            Assert.Same(ConstructionThingDefOf.Wall, StoneWallMaterials.PreferredWallDef(map));
        }

        [Fact]
        public void A_settlement_with_cut_stone_plans_stone_walls_and_builds_one_out_of_the_blocks()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            Pawn builder = NewHuman("Builder");
            settlement.AddCitizen(builder);
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20; // deterministic success
            GenSpawn.Spawn(builder, new IntVec3(0, 0, 0), map);
            SpawnStack(map, new IntVec3(1, 0, 1), "BlocksGranite", 40);

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            // The wall need is one need and the settlement now fills it in stone: not a wooden wall in sight.
            List<ThingDef> planned = BlueprintEntities(map).ToList();
            Assert.Contains(StoneworkThingDefOf.WallGranite, planned);
            Assert.DoesNotContain(ConstructionThingDefOf.Wall, planned);

            RunTicks(6000, builder);

            Assert.NotEmpty(map.listerThings.ThingsOfDef(StoneworkThingDefOf.WallGranite));
            int blocksLeft = map.listerThings.ThingsOfDef(Def("BlocksGranite")).Sum(t => t.stackCount);
            Assert.True(blocksLeft < 40, "the granite blocks were never spent on the wall");
        }

        [Fact]
        public void Stone_walls_already_standing_count_against_the_settlements_one_wall_need()
        {
            CoreMap map = NewMap();
            Settlement settlement = PlainSettlement();
            settlement.AddCitizen(NewHuman("Citizen"));

            // A citizen wants MinWallShelterCount walls at least; give it exactly that many, in granite.
            for (int i = 0; i < ConstructionInitiativeTuning.MinWallShelterCount; i++)
            {
                Spawn(map, new IntVec3(10 + i, 0, 12), "WallGranite");
            }

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            // Without EquivalentsOf, the settlement would count itself four wooden walls short and plan them
            // on top of the granite ones it already has.
            Assert.DoesNotContain(ConstructionThingDefOf.Wall, BlueprintEntities(map));
            Assert.DoesNotContain(StoneworkThingDefOf.WallGranite, BlueprintEntities(map));
        }

        [Fact]
        public void An_edict_that_prioritises_walls_still_prioritises_them_once_they_are_stone()
        {
            // The shipped GreatWorksMandate names the wooden Wall by defName, which is what makes this a real
            // hazard rather than a hypothetical one: a settlement that had cut enough stone to wall itself in
            // granite would silently stop being biased by the edict the moment it did.
            Assert.Contains(ConstructionThingDefOf.Wall,
                DefDatabase<SimWorld.God.EdictDef>.GetNamed("GreatWorksMandate").prioritizedConstruction);

            KnowMasonry();

            Settlement Scenario(int tile)
            {
                Settlement s = PlainSettlement(tile);
                s.AddCitizen(NewHuman("Citizen")); // bed target 1, wall target clamps to the 4-minimum
                return s;
            }

            CoreMap Stocked()
            {
                CoreMap m = NewMap(30, 30);
                SpawnStack(m, new IntVec3(1, 0, 1), "BlocksGranite", 200);
                return m;
            }

            // Control: no edict. Bed comes before walls, so the per-tick cap of three is spent on one bed and
            // two granite walls.
            CoreMap controlMap = Stocked();
            SettlementConstructionInitiative.TickSettlement(Scenario(1), controlMap);
            Assert.Contains(ConstructionThingDefOf.Bed, BlueprintEntities(controlMap));

            // Treatment: an edict naming the wooden Wall must still move the settlement's granite wall need to
            // the front — the edict is about walls, not about wood.
            var edict = new SimWorld.God.EdictDef
            {
                defName = "TestWallBiasOverStone",
                prioritizedConstruction = new List<ThingDef> { ConstructionThingDefOf.Wall },
            };
            Assert.True(Find.God.Activate(edict));

            CoreMap treatmentMap = Stocked();
            SettlementConstructionInitiative.TickSettlement(Scenario(2), treatmentMap);

            List<ThingDef> planned = BlueprintEntities(treatmentMap).ToList();
            Assert.NotEmpty(planned);
            Assert.All(planned, d => Assert.Same(StoneworkThingDefOf.WallGranite, d));
            Assert.DoesNotContain(ConstructionThingDefOf.Bed, planned);
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_standing_stonecutting_bill_survives_a_save_and_reload()
        {
            KnowMasonry();
            CoreMap map = NewMap();
            Thing bench = Spawn(map, new IntVec3(5, 0, 5), "TableStonecutter");
            SpawnStack(map, new IntVec3(5, 0, 6), "ChunkGranite", 2);
            SimWorld.Crafting.StonecutterInitiative.Run(map);

            var before = (SimWorld.Crafting.Bill_Production)Comp(bench).BillStack.Bills.Single();

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing loadedBench = Assert.Single(loaded.listerThings.ThingsOfDef(Def("TableStonecutter")));
            var after = Assert.IsType<SimWorld.Crafting.Bill_Production>(
                Assert.Single(Comp(loadedBench).BillStack.Bills));

            Assert.Same(before.recipe, after.recipe);
            Assert.Equal(before.repeatMode, after.repeatMode);
            Assert.Equal(before.targetCount, after.targetCount);
            Assert.Equal(before.unpauseWhenYouHave, after.unpauseWhenYouHave);

            // And the reloaded settlement does not queue the bill a second time on top of the one it saved.
            SimWorld.Crafting.StonecutterInitiative.Run(loaded);
            Assert.Single(Comp(loadedBench).BillStack.Bills);
        }

        [Fact]
        public void A_stone_wall_mid_construction_survives_a_save_and_reload()
        {
            CoreMap map = NewMap();
            Thing frame = Spawn(map, new IntVec3(6, 0, 6), "Frame_WallGranite");

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing? loadedFrame = loaded.edificeGrid[new IntVec3(6, 0, 6)];
            Assert.NotNull(loadedFrame);
            Assert.Same(StoneworkThingDefOf.WallGranite, ((SimWorld.Building.Frame)loadedFrame!).EntityToBuild);
            Assert.Same(frame.def, loadedFrame.def);
        }
    }
}
