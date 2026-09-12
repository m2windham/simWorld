using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A settlement clearing and sowing its own fields (<see cref="FarmingInitiative"/>).
    ///
    /// <para/><b>The hole these keep closed.</b> <see cref="Zone_Growing"/>, <see cref="WorkGiver_GrowerSow"/>,
    /// <see cref="JobDriver_Sow"/>, <see cref="Plant"/>'s growth model and two crop ThingDefs were all built,
    /// tested and green, and <b>nothing in <c>src/</c> had ever created a growing zone</b> — every one in the
    /// repository was made by a test, so no citizen in any game that has ever run has sown a seed. That is
    /// the same shape as the stockpile zone and the workbench bill before it: a finished mechanism nobody
    /// wanted anything from.
    ///
    /// <para/>Field size is derived from the mouths and from what content says a crop yields, so what the
    /// tests pin is the derivation (more eaters wants more field; a better crop wants less) rather than any
    /// cell count.
    /// </summary>
    public class FarmingInitiativeTests : ContentTestBase
    {
        public FarmingInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement SettlementWith(CoreMap map, int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Farmtown", 0);
            for (int i = 0; i < citizens; i++)
            {
                Pawn p = new Pawn(Human, "Farmer" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(i % map.Size.x, 0, 0), map);
            }
            return settlement;
        }

        [Fact]
        public void A_settlement_paints_a_field_and_names_a_crop_for_it()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);

            Assert.Null(FarmingInitiative.FieldOf(map));

            FarmingInitiative.Run(map);

            Zone_Growing? field = FarmingInitiative.FieldOf(map);
            Assert.NotNull(field);
            Assert.True(field!.CellCount > 0, "the settlement registered a field with no cells in it");
            Assert.NotNull(field.plantDefToGrow);
            Assert.True(field.allowSow);
        }

        [Fact]
        public void The_crop_it_chooses_is_one_that_actually_feeds_people()
        {
            CoreMap map = NewMap();
            ThingDef? crop = FarmingInitiative.CropFor(map);

            Assert.NotNull(crop);
            ThingDef? harvested = crop!.plant?.harvestedThingDef;
            Assert.NotNull(harvested);
            Assert.True(harvested!.IsNutritionGivingIngestible,
                "the settlement chose to grow " + crop.defName + ", which harvests into something nobody can eat");
        }

        [Fact]
        public void More_mouths_want_more_field()
        {
            // The derivation, asserted as a trend. The absolute cell count depends on the crop's own content
            // numbers, which are this port's and not RimWorld's.
            CoreMap small = NewMap();
            Settlement fewMouths = SettlementWith(small, 4);
            CoreMap large = NewMap();
            Settlement manyMouths = SettlementWith(large, 20);

            int forFew = FarmingInitiative.FieldCellsWantedFor(fewMouths, small);
            int forMany = FarmingInitiative.FieldCellsWantedFor(manyMouths, large);

            Assert.True(forFew > 0);
            Assert.True(forMany > forFew, "twenty mouths wanted no more field than four");
        }

        [Fact]
        public void A_settlement_with_nobody_in_it_does_not_farm()
        {
            CoreMap map = NewMap();
            var empty = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Ghosttown", 0);

            Assert.Equal(0, FarmingInitiative.FieldCellsWantedFor(empty, map));
        }

        [Fact]
        public void A_better_crop_needs_less_ground()
        {
            // Reads the ordering straight off content: whichever shipped crop yields more nutrition per cell
            // per day is the one a settlement needs fewer cells of. Written against every crop content ships
            // rather than against two named ones.
            List<ThingDef> crops = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Plant && FarmingInitiative.NutritionPerCellPerDay(d) > 0f)
                .OrderByDescending(FarmingInitiative.NutritionPerCellPerDay)
                .ToList();

            Assert.True(crops.Count >= 2, "content ships fewer than two food crops; this ordering has nothing to say");
            Assert.True(FarmingInitiative.NutritionPerCellPerDay(crops[0])
                >= FarmingInitiative.NutritionPerCellPerDay(crops[^1]));

            CoreMap map = NewMap();
            Assert.Same(crops[0], FarmingInitiative.CropFor(map));
        }

        [Fact]
        public void A_second_pass_grows_the_same_field_rather_than_starting_another()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);

            FarmingInitiative.Run(map);
            Zone_Growing field = FarmingInitiative.FieldOf(map)!;
            int after = field.CellCount;

            FarmingInitiative.Run(map);

            Assert.Single(map.zoneManager.AllZones.OfType<Zone_Growing>());
            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount >= after);
        }

        [Fact]
        public void It_stops_once_the_field_is_big_enough()
        {
            CoreMap map = NewMap();
            Settlement settlement = SettlementWith(map, 2);
            int wanted = FarmingInitiative.FieldCellsWantedFor(settlement, map);

            // Enough passes to reach the target several times over, if it were going to run away.
            for (int i = 0; i < 40; i++) FarmingInitiative.Run(map);

            Assert.True(FarmingInitiative.FieldOf(map)!.CellCount <= wanted,
                "the field grew past what the settlement wanted");
        }

        [Fact]
        public void A_citizen_sows_the_field_the_settlement_painted()
        {
            // The whole point: the zone exists so that the already-built sow giver has somewhere to point.
            CoreMap map = NewMap(20);
            SettlementWith(map, 1);
            FarmingInitiative.Run(map);

            Pawn sower = NewHuman("Sower");
            GenSpawn.Spawn(sower, new IntVec3(10, 0, 10), map);
            sower.workSettings.DisableAll();
            sower.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Growing"), 3);

            var giver = new WorkGiver_GrowerSow();
            bool anyCell = giver.PotentialWorkCellsGlobal(sower).Any(c => giver.HasJobOnCell(sower, c));

            Assert.True(anyCell, "the field the settlement painted offered no sowing work to anybody");
        }

        [Fact]
        public void The_field_survives_a_save_and_load()
        {
            CoreMap map = NewMap();
            SettlementWith(map, 5);
            FarmingInitiative.Run(map);

            Zone_Growing before = FarmingInitiative.FieldOf(map)!;
            int cells = before.CellCount;
            ThingDef crop = before.plantDefToGrow!;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Zone_Growing? after = FarmingInitiative.FieldOf(loaded);
            Assert.NotNull(after);
            Assert.Equal(cells, after!.CellCount);
            Assert.Same(crop, after.plantDefToGrow);

            // And it does not start a second field on the map it just loaded.
            FarmingInitiative.Run(loaded);
            Assert.Single(loaded.zoneManager.AllZones.OfType<Zone_Growing>());
        }
    }
}
