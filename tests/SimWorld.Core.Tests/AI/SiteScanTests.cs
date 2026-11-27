using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using Xunit;
using Xunit.Abstractions;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <see cref="WorkGiver_PlantsCut"/> and <see cref="WorkGiver_ConstructChopWood"/> look for their plants on the
    /// cells that can hold one (growing zones, blueprints, frames, the compound) and walk the plant list only when two
    /// or more turn up (tick-cost lane). Each test holds what they offer to the walk they used to make, element for
    /// element and in the list's order, through none, one and several such plants; the last does it all through a
    /// played settlement's first days. It also holds the premise of <see cref="MiningInitiative.MaterialsNeeded"/>
    /// walking three groups instead of every thing: every blueprint, frame and bench is in its group.
    /// </summary>
    public class SiteScanTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public SiteScanTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static T Giver<T>(string defName) where T : WorkGiver_Scanner =>
            (T)DefDatabase<WorkGiverDef>.GetNamed(defName).Worker;

        private static WorkGiver_PlantsCut Cut => Giver<WorkGiver_PlantsCut>("PlantsCut");

        private static WorkGiver_ConstructChopWood Fell =>
            (WorkGiver_ConstructChopWood)DefDatabase<WorkGiverDef>.AllDefsListForReading.First(d => d.Worker is WorkGiver_ConstructChopWood).Worker;

        /// <summary>The walk <see cref="WorkGiver_PlantsCut"/> made before: every plant, in list order.</summary>
        private static List<Thing> CutByWalk(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).Where(t => t is Plant p && WorkGiver_PlantsCut.ShouldBeCut(p)).ToList();

        /// <summary>The walk <see cref="WorkGiver_ConstructChopWood"/> made before: every tree, in list order.</summary>
        private static List<Thing> FellByWalk(CoreMap map)
        {
            var result = new List<Thing>();
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant))
            {
                if (!(t is Plant plant) || !plant.Spawned || plant.def.plant == null || !plant.def.plant.IsTree) continue;
                bool onSite = map.thingGrid.CellContains(plant.Position, ThingCategory.Blueprint)
                    || map.thingGrid.CellContains(plant.Position, ThingCategory.Frame)
                    || (map.settlementCompound != null && map.settlementCompound.Interior.Contains(plant.Position));
                if (onSite)
                {
                    result.Add(plant);
                    continue;
                }
                ThingDef? wood = plant.def.plant.harvestedThingDef;
                if (plant.HarvestableNow && wood != null && WorkGiver_ConstructChopWood.WoodShortfall(map, wood) > 0) result.Add(plant);
            }
            return result;
        }

        private static Plant SpawnPlant(CoreMap map, string defName, IntVec3 cell, float growth = 1f)
        {
            var plant = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(Def(defName)), cell, map);
            plant.Growth = growth;
            return plant;
        }

        private static Thing SpawnWallBlueprint(CoreMap map, IntVec3 cell) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(Def("Wall"))!), cell, map);

        private static Pawn SpawnPawn(CoreMap map)
        {
            Pawn p = NewHuman("Worker");
            GenSpawn.Spawn(p, new IntVec3(1, 0, 1), map);
            return p;
        }

        [Fact]
        public void Cutting_offers_what_the_plant_walk_offers_in_its_order()
        {
            var map = new CoreMap(20, 20, SimWorld.Map.TerrainDefOf.Soil);
            Pawn pawn = SpawnPawn(map);
            // A plant listed first, away from everything, that a blueprint will later cover.
            Plant underSite = SpawnPlant(map, "Plant_Potato", new IntVec3(15, 0, 15));
            SpawnPlant(map, "Plant_Potato", new IntVec3(3, 0, 15));
            var field = new Zone_Growing { label = "field", plantDefToGrow = Def("Plant_Rice") };
            map.zoneManager.RegisterZone(field);
            map.zoneManager.AddCell(field, new IntVec3(5, 0, 5));
            map.zoneManager.AddCell(field, new IntVec3(6, 0, 5));
            SpawnPlant(map, "Plant_Rice", new IntVec3(6, 0, 5));
            Assert.Empty(Cut.PotentialWorkThingsGlobal(pawn));
            Assert.Empty(CutByWalk(map));

            Plant weed = SpawnPlant(map, "Plant_Potato", new IntVec3(5, 0, 5));
            Assert.Equal(new Thing[] { weed }, CutByWalk(map));
            Assert.Equal(CutByWalk(map), Cut.PotentialWorkThingsGlobal(pawn).ToList());

            // The zone's cells are looked at before the blueprints', but the plant list has the covered plant first.
            SpawnWallBlueprint(map, underSite.Position);
            Assert.Equal(new Thing[] { underSite, weed }, CutByWalk(map));
            Assert.Equal(CutByWalk(map), Cut.PotentialWorkThingsGlobal(pawn).ToList());

            // A zone that sows nothing makes no weed of its potato.
            field.allowSow = false;
            Assert.Equal(new Thing[] { underSite }, CutByWalk(map));
            Assert.Equal(CutByWalk(map), Cut.PotentialWorkThingsGlobal(pawn).ToList());
        }

        [Fact]
        public void Felling_offers_what_the_tree_walk_offers_in_its_order()
        {
            var map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            Pawn pawn = SpawnPawn(map);
            Plant first = SpawnPlant(map, "Plant_TreePoplar", new IntVec3(18, 0, 18));
            Plant second = SpawnPlant(map, "Plant_TreePoplar", new IntVec3(4, 0, 18));
            Plant sapling = SpawnPlant(map, "Plant_TreePoplar", new IntVec3(10, 0, 4), 0.05f);
            Assert.Empty(Fell.PotentialWorkThingsGlobal(pawn));
            Assert.Empty(FellByWalk(map));

            // Enough wood lying about for the walls to come, so no tree is felled for its wood alone.
            ThingDef wood = Def("WoodLog");
            for (int i = 0; i < 4; i++)
            {
                Thing stack = ThingMaker.MakeThing(wood);
                stack.stackCount = wood.stackLimit;
                GenSpawn.Spawn(stack, new IntVec3(1 + i, 0, 10), map);
            }

            SpawnWallBlueprint(map, second.Position);
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, wood) <= 0);
            Assert.Equal(new Thing[] { second }, FellByWalk(map));
            Assert.Equal(FellByWalk(map), Fell.PotentialWorkThingsGlobal(pawn).ToList());

            // The second blueprint covers the tree listed first: offered in the list's order, not the sites'.
            SpawnWallBlueprint(map, first.Position);
            Assert.Equal(new Thing[] { first, second }, FellByWalk(map));
            Assert.Equal(FellByWalk(map), Fell.PotentialWorkThingsGlobal(pawn).ToList());

            // Short of wood once the stacks are gone: every grown tree, the sapling still not.
            foreach (Thing stack in map.listerThings.ThingsOfDef(wood).ToList()) stack.Destroy();
            Assert.True(WorkGiver_ConstructChopWood.WoodShortfall(map, wood) > 0);
            Assert.Equal(new Thing[] { first, second }, FellByWalk(map));
            Assert.DoesNotContain(sapling, FellByWalk(map));
            Assert.Equal(FellByWalk(map), Fell.PotentialWorkThingsGlobal(pawn).ToList());
        }

        [Fact]
        public void A_played_settlement_offers_what_the_walks_offer()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "site-scans", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement home = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(home.tile).Outcome);
            Pawn pawn = home.Citizens.First(p => p.Spawned && p.RaceProps.Humanlike);
            CoreMap map = pawn.Map!;

            int samples = 0, cutOffers = 0, fellOffers = 0;
            for (int i = 1; i <= GenDate.TicksPerDay * 2; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % 500 != 0) continue;
                samples++;
                List<Thing> cut = CutByWalk(map);
                Assert.Equal(cut, Cut.PotentialWorkThingsGlobal(pawn).ToList());
                List<Thing> fell = FellByWalk(map);
                Assert.Equal(fell, Fell.PotentialWorkThingsGlobal(pawn).ToList());
                if (cut.Count > 0) cutOffers++;
                if (fell.Count > 0) fellOffers++;

                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (t is Blueprint) Assert.Contains(t, map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
                    else if (t is Frame) Assert.Contains(t, map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
                    else if ((t as ThingWithComps)?.GetComp<CompBillGiver>() != null) Assert.Contains(t, map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver));
                }
            }
            output.WriteLine($"{samples} samples: cutting offered something in {cutOffers}, felling in {fellOffers}");
        }
    }
}
