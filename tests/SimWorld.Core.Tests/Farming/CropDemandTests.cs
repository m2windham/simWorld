using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Farming;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Farming
{
    /// <summary>
    /// crafting.billcreators, the supply half: <c>Plant_Cotton</c> grows into Cloth, and
    /// <see cref="CropDemandFields"/> keeps a cotton field sized by what the tailoring bench will need for the
    /// citizens who lack a shirt, beside the food field and never instead of it. Sizes are pinned against the
    /// worker's own figure and its trends (more people, more field; cloth in hand, less), not as literals.
    /// </summary>
    public class CropDemandTests : ContentTestBase
    {
        public CropDemandTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CropDemandDef Cotton => DefDatabase<CropDemandDef>.GetNamed("CottonForCloth");

        private static List<Pawn> Citizens(CoreMap map, int count)
        {
            var found = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                Pawn p = NewHuman("Weaver" + i);
                GenSpawn.Spawn(p, new IntVec3(i, 0, 0), map);
                found.Add(p);
            }
            return found;
        }

        private static void SpawnTailor(CoreMap map) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableTailor")), new IntVec3(20, 0, 30), map);

        [Fact]
        public void Cotton_ships_harvesting_cloth_and_the_food_field_never_picks_it()
        {
            Assert.Empty(Content.Result.Errors);
            ThingDef cotton = Def("Plant_Cotton");
            Assert.Same(Def("Cloth"), cotton.plant!.harvestedThingDef);
            Assert.True(cotton.plant.harvestYield > 0);
            Assert.Same(cotton, Cotton.crop);
            Assert.NotSame(cotton, FarmingInitiative.CropFor(NewMap()));
        }

        [Fact]
        public void A_ripe_cotton_plant_is_harvested_as_cloth()
        {
            CoreMap map = NewMap();
            var plant = (Plant)ThingMaker.MakeThing(Def("Plant_Cotton"));
            GenSpawn.Spawn(plant, new IntVec3(5, 0, 5), map);
            plant.Growth = 1f;

            plant.Harvest(null);

            Assert.True(map.listerThings.ThingsOfDef(Def("Cloth")).Sum(t => t.stackCount) > 0);
        }

        [Fact]
        public void With_a_tailoring_bench_and_bare_citizens_the_farm_pass_lays_out_a_cotton_field_of_the_wanted_size()
        {
            CoreMap map = NewMap();
            Citizens(map, 2);
            SpawnTailor(map);

            int wanted = Cotton.Worker.CellsWanted(map, null);
            Assert.True(wanted > 0);
            FarmingInitiative.Run(map);

            Zone_Growing field = CropDemandFields.FieldOf(map, Cotton.fieldLabel)!;
            Assert.NotNull(field);
            Assert.Same(Def("Plant_Cotton"), field.plantDefToGrow);
            Assert.True(field.allowSow);
            Assert.Equal(wanted, field.CellCount);
            Assert.NotSame(field, FarmingInitiative.FieldOf(map));
        }

        [Fact]
        public void More_bare_citizens_want_more_cotton_and_cloth_in_hand_wants_less()
        {
            CoreMap few = NewMap();
            Citizens(few, 1);
            SpawnTailor(few);
            CoreMap many = NewMap();
            Citizens(many, 4);
            SpawnTailor(many);
            int forFew = Cotton.Worker.CellsWanted(few, null);
            int forMany = Cotton.Worker.CellsWanted(many, null);
            Assert.True(forMany > forFew);

            Thing cloth = ThingMaker.MakeThing(Def("Cloth"));
            cloth.stackCount = 60;
            GenSpawn.Spawn(cloth, new IntVec3(30, 0, 30), many);
            Assert.True(Cotton.Worker.CellsWanted(many, null) < forMany);
        }

        [Fact]
        public void No_bench_no_field_and_a_closed_gap_stops_the_sowing()
        {
            CoreMap map = NewMap();
            List<Pawn> citizens = Citizens(map, 1);
            CropDemandFields.Run(map, null);
            Assert.Null(CropDemandFields.FieldOf(map, Cotton.fieldLabel));

            SpawnTailor(map);
            CropDemandFields.Run(map, null);
            Zone_Growing field = CropDemandFields.FieldOf(map, Cotton.fieldLabel)!;
            Assert.True(field.allowSow);

            // Dressed in every cloth garment the tailor makes (pants too since exposure4), so no gap is left.
            citizens[0].apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"), Def("Cloth")));
            citizens[0].apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Pants"), Def("Cloth")));
            CropDemandFields.Run(map, null);
            Assert.False(field.allowSow);
        }

        [Fact]
        public void The_cotton_field_survives_a_save_and_load_and_is_not_laid_out_twice()
        {
            CoreMap map = NewMap();
            Citizens(map, 2);
            SpawnTailor(map);
            CropDemandFields.Run(map, null);
            int cells = CropDemandFields.FieldOf(map, Cotton.fieldLabel)!.CellCount;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Zone_Growing after = CropDemandFields.FieldOf(loaded, Cotton.fieldLabel)!;
            Assert.NotNull(after);
            Assert.Equal(cells, after.CellCount);
            Assert.Same(Def("Plant_Cotton"), after.plantDefToGrow);
            CropDemandFields.Run(loaded, null);
            Assert.Single(loaded.zoneManager.AllZones.OfType<Zone_Growing>(), z => z.label == Cotton.fieldLabel);
        }
    }
}
