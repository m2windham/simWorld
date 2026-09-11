using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

// SimWorld.Building shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using ArtThingDefOf = global::SimWorld.Building.ArtThingDefOf;
using Blueprint = global::SimWorld.Building.Blueprint;
using ConstructionThingDefOf = global::SimWorld.Building.ConstructionThingDefOf;
using Frame = global::SimWorld.Building.Frame;
using GenConstruct = global::SimWorld.Building.GenConstruct;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchWorkDefOf = global::SimWorld.Research.ResearchWorkDefOf;
using SculptureMaterials = global::SimWorld.Building.SculptureMaterials;
using SettlementWorksInitiative = global::SimWorld.Building.SettlementWorksInitiative;
using WorksInitiativeTuning = global::SimWorld.Building.WorksInitiativeTuning;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The public works a settlement raises for itself (systems: research, mining, needs.beauty).
    ///
    /// <para/>Three holes these exist to keep closed, and every one of them had shipped. <c>ResearchBench</c>
    /// had a Blueprint/Frame pair, a costList and a work giver that refuses to produce a job without one —
    /// and nothing in <c>src/</c> ever placed one, so no civilization in this port could research at all.
    /// <c>Sculpture</c> was in the same state one system over: a full pair, a Beauty stat the sampler reads,
    /// and not one line of code that named it. And ten minerals came out of the ground — flint, salt, coal,
    /// copper, tin, silver, gold, jade, uranium, plasteel — without a single recipe ingredient or
    /// <c>costList</c> anywhere in content naming any of them. The first tests below are the audit of the
    /// third, written against content rather than against the two minerals this lane happened to wire.
    /// </summary>
    public class SettlementWorksTests : ContentTestBase
    {
        public SettlementWorksTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private static CoreMap NewMap(int sizeX = 24, int sizeZ = 24) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static IReadOnlyList<ThingDef> AllThingDefs => DefDatabase<ThingDef>.AllDefsListForReading;

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "TestSettlement" + tile, 0);

        private static Settlement PeopledSettlement(int citizens = 3)
        {
            Settlement settlement = PlainSettlement();
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
        }

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

        private static List<Blueprint> Blueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList();

        private static int BlueprintCount(CoreMap map, ThingDef entityDef) =>
            Blueprints(map).Count(bp => ReferenceEquals(bp.EntityToBuild, entityDef));

        /// <summary>Everything the rock drops when it is mined out — a question about content, not a list
        /// written here, so a mineral added in XML is audited with no test change.</summary>
        private static List<ThingDef> MinedMinerals() =>
            AllThingDefs.Where(d => d.mineableThing != null)
                .Select(d => d.mineableThing!)
                .Distinct()
                .ToList();

        /// <summary>Every ThingDef some other Def is built out of.</summary>
        private static HashSet<ThingDef> EverythingSomeCostListNames()
        {
            var named = new HashSet<ThingDef>();
            foreach (ThingDef def in AllThingDefs)
            {
                if (def.costList == null) continue;
                foreach (global::SimWorld.Crafting.ThingDefCountClass entry in def.costList) named.Add(entry.thingDef);
            }
            return named;
        }

        // -------------------------------------------------------------------------------------------
        // The audit: minerals that something is made out of.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Content_loads_clean_and_the_new_art_binds()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ArtThingDefOf.Sculpture);
            Assert.NotNull(ArtThingDefOf.SculptureGold);
            Assert.NotNull(ArtThingDefOf.SculptureJade);

            // A Def with no Blueprint/Frame pair is a Def nothing on a map can ever raise — the precise defect
            // that left the stonecutter's table, and four benches still, unbuildable.
            foreach (ThingDef art in SculptureMaterials.AllSculptureDefs)
            {
                Assert.True(GenConstruct.BlueprintDefFor(art) != null, art.defName + " has no Blueprint.");
                Assert.True(GenConstruct.FrameDefFor(art) != null, art.defName + " has no Frame.");
            }
            Assert.NotNull(GenConstruct.BlueprintDefFor(ResearchWorkDefOf.ResearchBench));
            Assert.NotNull(GenConstruct.FrameDefFor(ResearchWorkDefOf.ResearchBench));
        }

        [Fact]
        public void Something_a_settlement_can_build_is_made_out_of_a_mineral_it_mines()
        {
            List<ThingDef> minerals = MinedMinerals();

            // Vacuity guard: an audit that passes by finding nothing to check is how an audit rots.
            Assert.True(minerals.Count >= 10, "Expected the shipped veins to drop minerals; found " + minerals.Count);

            HashSet<ThingDef> spent = EverythingSomeCostListNames();
            List<ThingDef> wired = minerals.Where(spent.Contains).ToList();

            // Steel is the one that was already spent on something (Buildings_Power and Buildings_Security
            // cost it) and it predates the ore ladder entirely — it comes out of Buildings_Natural.xml. The
            // ten veins Buildings_Mineable.xml added had nothing between them: not one was named by a recipe
            // ingredient or a costList anywhere in content.
            Assert.Contains(Def("Steel"), wired);
            Assert.True(wired.Count >= 3,
                "Only " + wired.Count + " of " + minerals.Count
                    + " mined minerals is named by any costList in content, and Steel is one of them. The "
                    + "ore ladder's own ten are recorded as open in this lane's report where they are still "
                    + "unwired; that is not fixed by loosening this test.");
        }

        [Fact]
        public void Gold_and_jade_are_the_two_that_are_wired()
        {
            HashSet<ThingDef> spent = EverythingSomeCostListNames();
            Assert.Contains(Def("Gold"), spent);
            Assert.Contains(Def("Jade"), spent);
        }

        // -------------------------------------------------------------------------------------------
        // The research bench: the hole that blocked a whole subsystem.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlement_raises_a_research_bench_because_nothing_else_ever_would()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            Assert.Equal(0, BlueprintCount(map, ResearchWorkDefOf.ResearchBench));

            SettlementWorksInitiative.Run(settlement, map);

            Assert.Equal(1, BlueprintCount(map, ResearchWorkDefOf.ResearchBench));
        }

        [Fact]
        public void It_stops_at_the_number_the_tech_tree_was_balanced_for()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            for (int i = 0; i < WorksInitiativeTuning.ResearchBenchesWanted + 5; i++)
            {
                SettlementWorksInitiative.Run(settlement, map);
            }

            Assert.Equal(WorksInitiativeTuning.ResearchBenchesWanted, BlueprintCount(map, ResearchWorkDefOf.ResearchBench));
        }

        [Fact]
        public void A_bench_already_standing_counts_against_the_want()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            for (int i = 0; i < WorksInitiativeTuning.ResearchBenchesWanted; i++)
            {
                Spawn(map, new IntVec3(i * 2, 0, 0), "ResearchBench");
            }

            SettlementWorksInitiative.Run(settlement, map);

            Assert.Equal(0, BlueprintCount(map, ResearchWorkDefOf.ResearchBench));
        }

        [Fact]
        public void A_settlement_with_nobody_living_in_it_raises_nothing()
        {
            Settlement empty = PlainSettlement();
            CoreMap map = NewMap();

            SettlementWorksInitiative.Run(empty, map);

            Assert.Empty(Blueprints(map));
        }

        /// <summary>
        /// The whole point of the lane, end to end and through the real pipelines: a settlement decides it
        /// wants a bench, the construction pipeline raises the thing the blueprint named, the agenda picks
        /// something to study, and an idle citizen who was never told what to do goes and studies it. Every
        /// one of those four steps existed before this lane and the chain was broken at the first.
        /// </summary>
        [Fact]
        public void A_settlement_that_could_not_research_at_all_now_can()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap(12, 12);

            // No bench, no project: the state every game in this port started and stayed in.
            Pawn scholar = NewHuman("Scholar");
            GenSpawn.Spawn(scholar, new IntVec3(0, 0, 0), map);
            Assert.Null(WorkGiverScanUtility.TryGiveJobInGivers(scholar, new List<WorkGiverDef> { ResearchGiverDef }));

            // The settlement wants one and says where.
            SettlementWorksInitiative.Run(settlement, map);
            Blueprint planned = Blueprints(map).Single(bp => ReferenceEquals(bp.EntityToBuild, ResearchWorkDefOf.ResearchBench));

            // The existing construction pipeline raises it: blueprint, frame, materials, finished building.
            Frame frame = planned.ReplaceWithFrame();
            foreach (global::SimWorld.Crafting.ThingDefCountClass cost in ResearchWorkDefOf.ResearchBench.costList!)
            {
                frame.AddMaterial(cost.thingDef, cost.count);
            }
            Assert.True(frame.MaterialsFullySatisfied());
            Thing bench = frame.CompleteConstruction(NewHuman("Builder"));
            Assert.Equal(ResearchWorkDefOf.ResearchBench, bench.def);

            // The agenda supplies what to study, without any edict having been issued.
            Assert.Null(Find.ResearchManager.CurrentProj);
            global::SimWorld.Research.ResearchProjectDef project =
                global::SimWorld.Research.ResearchAgenda.EnsureProject()!;

            // And an idle citizen finds the work through their own think tree.
            RunTicks(3000, scholar);

            Assert.True(Find.ResearchManager.GetProgress(project) > 0f,
                "A citizen with a bench the settlement built itself and a project the civilization chose "
                    + "itself should have advanced " + project.defName + " by now.");
        }

        private static WorkGiverDef ResearchGiverDef => DefDatabase<WorkGiverDef>.GetNamed("Research");

        // -------------------------------------------------------------------------------------------
        // Art: what the gold and jade are for.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_mineral_sculptures_outshine_the_wooden_one_and_jade_leads()
        {
            // The ordering is the sourced part; 60 and 70 are not, and are deliberately not asserted.
            float wood = ArtThingDefOf.Sculpture.GetStatValueAbstract(BeautyStatDefOf.Beauty);
            float gold = ArtThingDefOf.SculptureGold.GetStatValueAbstract(BeautyStatDefOf.Beauty);
            float jade = ArtThingDefOf.SculptureJade.GetStatValueAbstract(BeautyStatDefOf.Beauty);

            Assert.True(wood > 0f, "A sculpture with no beauty is a sculpture nothing would ever want.");
            Assert.True(gold > wood, "A gold sculpture should outshine a wooden one.");
            Assert.True(jade > gold, "Items_Minerals.xml prices jade above gold; the art should agree.");

            Assert.Equal(
                new[] { ArtThingDefOf.SculptureJade, ArtThingDefOf.SculptureGold, ArtThingDefOf.Sculpture },
                SculptureMaterials.AllSculptureDefs);
        }

        [Fact]
        public void A_settlement_carves_wood_until_it_has_a_mineral_to_carve()
        {
            CoreMap map = NewMap();
            Assert.Same(ArtThingDefOf.Sculpture, SculptureMaterials.PreferredSculptureDef(map));

            SpawnStack(map, new IntVec3(5, 0, 5), "Gold", CostOf(ArtThingDefOf.SculptureGold, "Gold"));
            Assert.Same(ArtThingDefOf.SculptureGold, SculptureMaterials.PreferredSculptureDef(map));

            SpawnStack(map, new IntVec3(6, 0, 5), "Jade", CostOf(ArtThingDefOf.SculptureJade, "Jade"));
            Assert.Same(ArtThingDefOf.SculptureJade, SculptureMaterials.PreferredSculptureDef(map));
        }

        [Fact]
        public void Half_a_sculptures_worth_of_gold_is_not_enough_to_start_one()
        {
            // A blueprint the settlement cannot finish would sit on a cell forever, counting toward a want
            // nobody is filling — the reason PreferredWallDef checks the whole costList and this does too.
            CoreMap map = NewMap();
            int price = CostOf(ArtThingDefOf.SculptureGold, "Gold");
            SpawnStack(map, new IntVec3(5, 0, 5), "Gold", price - 1);

            Assert.Same(ArtThingDefOf.Sculpture, SculptureMaterials.PreferredSculptureDef(map));
        }

        private static int CostOf(ThingDef def, string material) =>
            def.costList!.First(c => c.thingDef.defName == material).count;

        [Fact]
        public void The_settlement_wants_enough_art_for_one_of_them_to_be_worth_looking_at()
        {
            // The derivation, asserted as the relation it is rather than as 12 and 5: enough beauty inside one
            // perceptible sample to lift it a band above "nothing here either way".
            int cells = GenRadial.NumCellsInRadius(BeautyUtility.SampleRadius);
            float step = SculptureMaterials.FirstStepAboveNeutral();
            Assert.True(step > 0f, "The beauty curve should mark a step above neutral for this to derive from.");

            foreach (ThingDef art in SculptureMaterials.AllSculptureDefs)
            {
                int wanted = SculptureMaterials.SculpturesWantedOf(art);
                float beautyOfOne = art.GetStatValueAbstract(BeautyStatDefOf.Beauty);

                Assert.True(wanted >= 1, art.defName + " is worth carving at all.");
                Assert.True(wanted * beautyOfOne >= step * cells,
                    art.defName + ": " + wanted + " of them do not lift one sample above neutral.");
                Assert.True((wanted - 1) * beautyOfOne < step * cells,
                    art.defName + ": " + wanted + " is more than it takes, so the target is padded.");
            }

            // The prettier the material, the fewer of them it takes. Falls out of the derivation; asserted
            // because it is the property a reader would otherwise have to recompute to believe.
            Assert.True(
                SculptureMaterials.SculpturesWantedOf(ArtThingDefOf.SculptureJade)
                    < SculptureMaterials.SculpturesWantedOf(ArtThingDefOf.Sculpture));
        }

        [Fact]
        public void The_want_for_art_does_not_grow_with_the_population()
        {
            // Beauty is a property of a place, not of a headcount — the average over the cells a citizen sees
            // does not divide by the people looking. A civilization-scale settlement therefore wants exactly
            // what a hamlet wants, and this pins that no later lane quietly adds a per-citizen multiplier.
            CoreMap smallMap = NewMap();
            CoreMap bigMap = NewMap();
            Settlement hamlet = PeopledSettlement(2);
            Settlement city = PeopledSettlement(500);

            for (int i = 0; i < 30; i++)
            {
                SettlementWorksInitiative.Run(hamlet, smallMap);
                SettlementWorksInitiative.Run(city, bigMap);
            }

            Assert.Equal(
                BlueprintCount(smallMap, ArtThingDefOf.Sculpture),
                BlueprintCount(bigMap, ArtThingDefOf.Sculpture));
        }

        [Fact]
        public void Art_gathers_where_the_art_already_is()
        {
            // Scattered sculptures move a citizen's average by almost nothing however many there are, so the
            // count only means what it says if the pieces land inside one sample. See ArtClusterRadius.
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap(40, 40);
            var first = new IntVec3(20, 0, 20);
            Spawn(map, first, "Sculpture");

            SettlementWorksInitiative.Run(settlement, map);

            Blueprint planned = Blueprints(map).Single(bp => SculptureMaterials.IsSculpture(bp.EntityToBuild));
            int dx = planned.Position.x - first.x;
            int dz = planned.Position.z - first.z;
            Assert.True(System.Math.Abs(dx) <= SettlementWorksInitiative.ArtClusterRadius
                    && System.Math.Abs(dz) <= SettlementWorksInitiative.ArtClusterRadius,
                "A new piece landed at " + planned.Position + ", outside the cluster around " + first + ".");
        }

        [Fact]
        public void A_gold_sculpture_already_carved_counts_against_the_want_for_a_wooden_one()
        {
            // One want, two materials. Without SculptureMaterials.EquivalentsOf a settlement that had carved
            // its gold would go straight on wanting the same number of wooden ones.
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            int target = SculptureMaterials.SculpturesWantedOf(ArtThingDefOf.Sculpture);
            for (int i = 0; i < target; i++) Spawn(map, new IntVec3(i, 0, 0), "SculptureGold");

            SettlementWorksInitiative.Run(settlement, map);

            Assert.DoesNotContain(Blueprints(map), bp => SculptureMaterials.IsSculpture(bp.EntityToBuild));
        }

        [Fact]
        public void Gold_lying_on_the_map_becomes_a_gold_sculpture_and_the_gold_is_actually_spent()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            int price = CostOf(ArtThingDefOf.SculptureGold, "Gold");
            SpawnStack(map, new IntVec3(10, 0, 10), "Gold", price);

            SettlementWorksInitiative.Run(settlement, map);

            Blueprint planned = Blueprints(map).Single(bp => SculptureMaterials.IsSculpture(bp.EntityToBuild));
            Assert.Same(ArtThingDefOf.SculptureGold, planned.EntityToBuild);

            // And the construction pipeline really does consume the mineral: the frame is not satisfied until
            // the gold is in it, which is what makes a mined vein finally mean something.
            Frame frame = planned.ReplaceWithFrame();
            Assert.False(frame.MaterialsFullySatisfied());
            Assert.Equal(price, frame.MaterialStillNeeded(Def("Gold")));
            frame.AddMaterial(Def("Gold"), price);
            Assert.True(frame.MaterialsFullySatisfied());

            Thing carved = frame.CompleteConstruction(NewHuman("Carver"));
            Assert.Same(ArtThingDefOf.SculptureGold, carved.def);
            Assert.True(carved.def.GetStatValueAbstract(BeautyStatDefOf.Beauty) > 0f);
        }

        // -------------------------------------------------------------------------------------------
        // Gating, and the state this initiative deliberately does not keep.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_gated_entry_point_only_fires_on_its_own_interval()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            Find.TickManager.DebugSetTicksGame(WorksInitiativeTuning.IntervalTicks + 1);
            SettlementWorksInitiative.TickSettlement(settlement, map);
            Assert.Empty(Blueprints(map));

            Find.TickManager.DebugSetTicksGame(WorksInitiativeTuning.IntervalTicks * 2);
            SettlementWorksInitiative.TickSettlement(settlement, map);
            Assert.NotEmpty(Blueprints(map));
        }

        [Fact]
        public void A_settlement_nobody_has_entered_has_no_map_and_that_is_not_an_error()
        {
            Settlement settlement = PeopledSettlement();
            Assert.Null(settlement.InteriorMap);

            SettlementWorksInitiative.TickSettlement(settlement); // must not throw
        }

        /// <summary>
        /// The module's round trip. It keeps no state of its own — every decision is re-derived from what is
        /// standing on the map — so what a save and load has to preserve is the map, and what this has to
        /// prove is that a reloaded settlement neither duplicates the works it already has nor forgets it
        /// wanted them. Running the same pass twice over unchanged state is the same question.
        /// </summary>
        [Fact]
        public void Re_running_over_unchanged_state_never_duplicates_and_never_forgets()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            for (int i = 0; i < 40; i++) SettlementWorksInitiative.Run(settlement, map);
            int benches = BlueprintCount(map, ResearchWorkDefOf.ResearchBench);
            int art = Blueprints(map).Count(bp => SculptureMaterials.IsSculpture(bp.EntityToBuild));

            Assert.Equal(WorksInitiativeTuning.ResearchBenchesWanted, benches);
            Assert.Equal(SculptureMaterials.SculpturesWantedOf(ArtThingDefOf.Sculpture), art);

            for (int i = 0; i < 10; i++) SettlementWorksInitiative.Run(settlement, map);

            Assert.Equal(benches, BlueprintCount(map, ResearchWorkDefOf.ResearchBench));
            Assert.Equal(art, Blueprints(map).Count(bp => SculptureMaterials.IsSculpture(bp.EntityToBuild)));
        }
    }
}
