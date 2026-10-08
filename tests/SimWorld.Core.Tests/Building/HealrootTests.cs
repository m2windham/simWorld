using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Healroot (<c>Plant_Healroot</c>), the plant herbal medicine comes from, and the sowing-skill rule it
    /// brought with it (<see cref="PlantProperties.sowMinSkill"/>).
    ///
    /// <para/><b>The hole this closes.</b> <c>MedicineHerbal</c> had no source: a founding band started with
    /// some and once it was spent no map could make more. What these pin is the chain from a seed to a tend --
    /// it is sown only by someone skilled enough, it grows into a plant a citizen harvests, the harvest is
    /// medicine, and tending can pick that medicine up -- as behaviour. RimWorld's figures for the plant are in
    /// the def's own comment; the tests deliberately do not restate them (CLAUDE.md).
    /// </summary>
    public class HealrootTests : ContentTestBase
    {
        public HealrootTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static and ContentTestBase does not reset it -- see MedicineTests.
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size = 14, TerrainDef? terrain = null) =>
            new CoreMap(size, size, terrain ?? TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Healroot => Def("Plant_Healroot");

        private static Pawn SpawnSower(CoreMap map, IntVec3 cell, int plantsLevel, string name = "Sower")
        {
            Pawn p = NewHuman(name);
            p.skills!.GetSkill(SkillDefOf.Plants)!.Level = plantsLevel;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Zone_Growing GrowingZone(CoreMap map, ThingDef crop, params IntVec3[] cells)
        {
            var zone = new Zone_Growing { plantDefToGrow = crop };
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            return zone;
        }

        // ---- content ----

        [Fact]
        public void Healroot_loads_and_harvests_herbal_medicine()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.NotNull(Healroot.plant);
            Assert.Equal(ThingCategory.Plant, Healroot.category);
            Assert.Same(MedicineDefOf.MedicineHerbal, Healroot.plant!.harvestedThingDef);
            Assert.True(Healroot.plant.harvestedThingDef!.IsMedicine);
            Assert.True(Healroot.plant.harvestYield > 0);
            Assert.True(Healroot.plant.growDays > 0f);
            Assert.False(Healroot.plant.IsTree, "healroot is a herb; a tree is felled for wood, not harvested in a field");
        }

        [Fact]
        public void Healroot_is_a_demanding_crop_the_food_crops_are_not()
        {
            // Not RimWorld's literals: that it asks for more of the ground and of the sower than the crops
            // a settlement lives on, which is what makes it a decision to grow rather than a free second field.
            foreach (ThingDef food in new[] { Def("Plant_Potato"), Def("Plant_Rice") })
            {
                Assert.True(Healroot.plant!.sowMinSkill > food.plant!.sowMinSkill,
                    "healroot asks no more skill to sow than " + food.defName);
                Assert.True(Healroot.plant.sowMinFertility > food.plant.sowMinFertility,
                    "healroot grows on ground " + food.defName + " would have to be sown in too");
            }
        }

        [Fact]
        public void Healroot_is_not_something_the_settlement_could_mistake_for_food()
        {
            // FarmingInitiative.CropFor picks the best food crop by nutrition per cell per day; herbal medicine
            // has no nutrition, so healroot must never be what a field is planted with for the mouths.
            Assert.Equal(0f, FarmingInitiative.NutritionPerCellPerDay(Healroot));
            Assert.NotSame(Healroot, FarmingInitiative.CropFor(NewMap()));
        }

        // ---- sowMinSkill ----

        [Fact]
        public void Below_the_minimum_skill_there_is_no_sow_job_and_at_it_there_is()
        {
            int minSkill = Healroot.plant!.sowMinSkill;
            Assert.True(minSkill > 0);

            CoreMap map = NewMap();
            var cell = new IntVec3(7, 0, 7);
            GrowingZone(map, Healroot, cell);
            Pawn clumsy = SpawnSower(map, new IntVec3(2, 0, 2), minSkill - 1, "Clumsy");
            Pawn capable = SpawnSower(map, new IntVec3(3, 0, 2), minSkill, "Capable");

            var giver = new WorkGiver_GrowerSow();

            Assert.False(giver.HasJobOnCell(clumsy, cell),
                "a citizen below the plant's minimum Plants skill was offered a sow job for it");
            Assert.True(giver.HasJobOnCell(capable, cell),
                "a citizen at the plant's minimum Plants skill was refused it");
            Job? job = giver.JobOnCell(capable, cell);
            Assert.NotNull(job);
            Assert.Same(BuildingJobDefOf.Sow, job!.def);
        }

        [Fact]
        public void The_skill_floor_belongs_to_the_plant_so_a_clumsy_sower_still_sows_a_plant_without_one()
        {
            Assert.Equal(0, Def("Plant_Potato").plant!.sowMinSkill);

            CoreMap map = NewMap();
            var cell = new IntVec3(7, 0, 7);
            GrowingZone(map, Def("Plant_Potato"), cell);
            Pawn clumsy = SpawnSower(map, new IntVec3(2, 0, 2), 0, "Clumsy");

            Assert.True(new WorkGiver_GrowerSow().HasJobOnCell(clumsy, cell));
        }

        [Fact]
        public void A_capable_citizen_sows_healroot_and_a_clumsy_one_leaves_it_alone()
        {
            int minSkill = Healroot.plant!.sowMinSkill;
            foreach ((int level, bool expectSown) in new[] { (minSkill - 1, false), (minSkill, true) })
            {
                Find.TickManager = new TickManager();
                CoreMap map = NewMap();
                var cell = new IntVec3(7, 0, 7);
                GrowingZone(map, Healroot, cell);
                Pawn sower = SpawnSower(map, new IntVec3(2, 0, 2), level);

                RunTicks(3000, sower);

                bool sown = map.listerThings.ThingsOfDef(Healroot).Count > 0;
                Assert.Equal(expectSown, sown);
            }
        }

        [Fact]
        public void Healroot_is_not_sown_on_ground_poorer_than_it_needs_though_the_food_crops_are()
        {
            Assert.True(TerrainDefOf.Sand.fertility < Healroot.plant!.sowMinFertility);
            Assert.True(TerrainDefOf.Sand.fertility >= Def("Plant_Potato").plant!.sowMinFertility);

            CoreMap map = NewMap(terrain: TerrainDefOf.Sand);
            var cell = new IntVec3(7, 0, 7);
            Zone_Growing zone = GrowingZone(map, Healroot, cell);
            Pawn sower = SpawnSower(map, new IntVec3(2, 0, 2), 20);

            var giver = new WorkGiver_GrowerSow();
            Assert.False(giver.HasJobOnCell(sower, cell));

            zone.plantDefToGrow = Def("Plant_Potato");
            Assert.True(giver.HasJobOnCell(sower, cell));
        }

        // ---- from a plant to a tend ----

        [Fact]
        public void A_grown_healroot_harvested_by_a_citizen_yields_medicine_that_tending_can_use()
        {
            CoreMap map = NewMap();
            var plantCell = new IntVec3(7, 0, 7);
            var plant = (Plant)ThingMaker.MakeThing(Healroot);
            plant.Growth = 1f;
            GenSpawn.Spawn(plant, plantCell, map);

            Pawn grower = SpawnSower(map, new IntVec3(2, 0, 2), 20, "Grower");
            RunTicks(3000, grower);

            Assert.True(plant.Destroyed, "nobody harvested the ripe healroot");
            List<Thing> medicine = map.listerThings.ThingsOfDef(MedicineDefOf.MedicineHerbal)
                .Where(t => t.Spawned).ToList();
            Assert.NotEmpty(medicine);
            int units = medicine.Sum(t => t.stackCount);
            Assert.True(units >= 1);

            // And a doctor looking for something to tend with finds it.
            Faction faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Tribe", "F_Tribe");
            Find.FactionManager.Add(faction);
            Pawn doctor = NewHuman("Doctor");
            doctor.faction = faction;
            GenSpawn.Spawn(doctor, new IntVec3(5, 0, 5), map);
            Pawn patient = NewHuman("Patient");
            patient.faction = faction;
            GenSpawn.Spawn(patient, new IntVec3(6, 0, 5), map);

            Thing? found = MedicineUtility.FindBestMedicine(doctor, patient);
            Assert.NotNull(found);
            Assert.Same(MedicineDefOf.MedicineHerbal, found!.def);

            // A tend with it beats the same tend without: the harvest was worth growing.
            float without = TendUtility.CalculateBaseTendQuality(doctor, patient, null);
            float with = TendUtility.CalculateBaseTendQuality(doctor, patient, found.def);
            Assert.True(with >= without);
        }

        [Fact]
        public void A_half_grown_healroot_is_not_harvested_yet()
        {
            CoreMap map = NewMap();
            var plant = (Plant)ThingMaker.MakeThing(Healroot);
            plant.Growth = 0.4f;
            GenSpawn.Spawn(plant, new IntVec3(7, 0, 7), map);
            Pawn grower = SpawnSower(map, new IntVec3(2, 0, 2), 20);

            Assert.False(plant.HarvestableNow);
            Assert.False(new WorkGiver_GrowerHarvest().HasJobOnThing(grower, plant));
        }

        [Fact]
        public void A_grown_healroot_survives_a_save_and_load()
        {
            CoreMap map = NewMap();
            var plant = (Plant)ThingMaker.MakeThing(Healroot);
            plant.Growth = 0.6f;
            GenSpawn.Spawn(plant, new IntVec3(4, 0, 4), map);
            GrowingZone(map, Healroot, new IntVec3(4, 0, 4), new IntVec3(5, 0, 4));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Plant reloaded = loaded.listerThings.ThingsOfDef(Healroot).OfType<Plant>().Single();
            Assert.Equal(0.6f, reloaded.Growth, 3);
            Zone_Growing zone = loaded.zoneManager.AllZones.OfType<Zone_Growing>().Single();
            Assert.Same(Healroot, zone.plantDefToGrow);
        }
    }
}
