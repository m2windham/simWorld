using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A household furnishes its own house as the town learns how (<see cref="HouseFurnishing"/>), on a real generated
    /// settlement: no table before the town knows tables, one once it does, the furnishings of its age on the
    /// furnishing spot, and the next age's put in place of the last.
    /// </summary>
    [Collection("GlobalDefs")]
    public class HouseFurnishingTests : ContentTestBase
    {
        public HouseFurnishingTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ResearchProjectDef Project(string name) => DefDatabase<ResearchProjectDef>.GetNamed(name);

        private static Blueprint? PlannedAt(CoreMap map, IntVec3 c) =>
            map.thingGrid.ThingsListAt(c).OfType<Blueprint>().FirstOrDefault();

        [Fact]
        public void A_household_gets_a_table_once_tables_are_known_and_each_age_furnishes_over_the_last()
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("TribalStart").scenario, "comfort-furnish", subdivisionOverride: 3, soloStart: true, bandSize: 12);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            Faction faction = settlement.Citizens.First(p => p.Spawned).Faction!;
            for (int i = 0; i < 2000 && map.settlementCompound == null; i++) game.TickManager.DoSingleTick();
            Assert.NotNull(map.settlementCompound);

            ResearchManager research = Find.ResearchManager;
            foreach (ResearchProjectDef p in DefDatabase<EraDef>.GetNamed("SticksAndStones").Projects) research.SetProjectFinishedForSetup(p);
            int household = DwellingUtility.Households(settlement).Keys.First();
            Dwelling home = ShelterKit.RaiseOnPlot(map, ShelterKit.Rung("House_Neo"), household, faction)!;
            Assert.NotNull(home);
            // Every citizen has a bed and the larder is full, so nothing more urgent holds the furnishing back.
            foreach (var p in settlement.Citizens.Where(p => !p.Dead))
            {
                Thing bed = ThingMaker.MakeThing(ConstructionThingDefOf.Bed, StuffDefOf.WoodLog);
                IntVec3 c = map.AllCells.First(x => x.x > 2 && x.z > 2 && GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, x, map, out _)
                    && GenAdj.OccupiedRect(x, Rot4.North, ConstructionThingDefOf.Bed.size).ExpandedBy(1).Cells.All(n => GenGrid.InBounds(n, map) && GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0));
                GenSpawn.Spawn(bed, c, map);
            }
            settlement.AddStore(DefDatabase<ThingDef>.GetNamed("MealSimple"), 600);
            Assert.Null(RenewalInitiative.WaitReason(settlement, map));

            IntVec3 tableSpot = home.Spots(DwellingPartKind.TableSpot).Single();
            IntVec3 furnishingSpot = home.Spots(DwellingPartKind.FurnishingSpot).Single();

            // Sticks and stones: nobody has a table yet, nor anything to furnish with.
            HouseFurnishing.Tick(settlement, map);
            Assert.Null(PlannedAt(map, tableSpot));
            Assert.Null(PlannedAt(map, furnishingSpot));

            // Shelter brings the table.
            research.SetProjectFinishedForSetup(Project("Shelter"));
            HouseFurnishing.Tick(settlement, map);
            Assert.Same(FurnitureThingDefOf.DiningTable, PlannedAt(map, tableSpot)?.EntityToBuild);
            Assert.Null(PlannedAt(map, furnishingSpot));
            PlannedAt(map, tableSpot)!.ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));
            Assert.Contains(map.thingGrid.ThingsListAt(tableSpot), x => x.def == FurnitureThingDefOf.DiningTable);

            // Weaving brings the Agrarian age's furnishings.
            research.SetProjectFinishedForSetup(Project("Weaving"));
            HouseFurnishing.Tick(settlement, map);
            Blueprint? first = PlannedAt(map, furnishingSpot);
            Assert.Same(FurnitureThingDefOf.Furnishing_Agrarian, first?.EntityToBuild);

            // Built, and then outgrown: Dyeing's furnishings are planned over them, and stand once finished.
            first!.ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));
            Assert.Contains(map.thingGrid.ThingsListAt(furnishingSpot), x => x.def == FurnitureThingDefOf.Furnishing_Agrarian);
            HouseFurnishing.Tick(settlement, map);
            Assert.Null(PlannedAt(map, furnishingSpot));
            research.SetProjectFinishedForSetup(Project("Dyeing"));
            HouseFurnishing.Tick(settlement, map);
            Blueprint? next = PlannedAt(map, furnishingSpot);
            Assert.Same(FurnitureThingDefOf.Furnishing_Bronze, next?.EntityToBuild);
            next!.ReplaceWithFrame().CompleteConstruction(NewHuman("Builder"));
            Assert.Contains(map.thingGrid.ThingsListAt(furnishingSpot), x => x.def == FurnitureThingDefOf.Furnishing_Bronze);
            Assert.DoesNotContain(map.thingGrid.ThingsListAt(furnishingSpot), x => x.def == FurnitureThingDefOf.Furnishing_Agrarian);
        }
    }
}
