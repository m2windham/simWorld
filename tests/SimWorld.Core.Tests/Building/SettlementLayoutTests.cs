using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.MapGen;
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
    /// Whether what a settlement builds reads as one settlement: its beds inside its ring, its houses on its
    /// street, nothing growing in its dormitory. Seen first in the Unity host's capture of its default game at
    /// tick 60,000, where 20 beds stood in the open, an empty ring had trees in it and most houses were off
    /// the street; <c>tools/bench --suite layout</c> reports the same thing headless. Each test below fails
    /// for one of the three causes.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SettlementLayoutTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public SettlementLayoutTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static ThingDef? BuiltDef(Thing t) => t switch
        {
            Blueprint bp => bp.EntityToBuild,
            Frame f => f.EntityToBuild,
            _ => t.def,
        };

        private static List<Thing> AllOf(CoreMap map, System.Func<ThingDef, bool> wanted) =>
            map.listerThings.AllThings.Where(t => BuiltDef(t) is ThingDef d && wanted(d)).ToList();

        private static bool IsTree(Thing t) => t.def.plant != null && t.def.plant.IsTree;

        /// <summary>
        /// The host's own new game (<c>SimHarness.StartGame</c>: seed <c>simworld-host</c>, a band of 20, three
        /// subdivisions, solo), ticked only by the tick loop to the tick the host was captured at.
        /// </summary>
        [Fact]
        public void The_hosts_default_game_beds_its_band_in_the_ring_and_houses_it_on_the_street()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "simworld-host", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;
            for (int i = 0; i < 60000; i++) game.TickManager.DoSingleTick();

            SettlementCompound compound = Assert.IsType<SettlementCompound>(map.settlementCompound);
            List<Thing> beds = AllOf(map, d => ReferenceEquals(d, ConstructionThingDefOf.Bed));
            List<Thing> dwellings = AllOf(map, DwellingUtility.IsDwelling);
            output.WriteLine("compound " + compound.Interior + ", " + beds.Count + " beds, " + dwellings.Count + " dwellings");

            Assert.NotEmpty(beds);
            Assert.All(beds, b => Assert.True(compound.Interior.Contains(b.Position), b.def.defName + " at " + b.Position + " is outside the ring's interior " + compound.Interior));

            var plots = compound.DwellingPlots(int.MaxValue, map.Size).ToList();
            Assert.NotEmpty(dwellings);
            Assert.All(dwellings, d =>
            {
                CellRect rect = GenAdj.OccupiedRect(d.Position, d.Rotation, BuiltDef(d)!.size);
                Assert.True(plots.Any(p => p.Plot == rect && p.Rot == d.Rotation), d.def.defName + " at " + d.Position + " " + d.Rotation + " is not on a street plot");
            });

            // The yard is the benches' (they may stand on it by now), never rock.
            Assert.All(compound.WorkYard.Cells, c => Assert.False(map.edificeGrid[c] is Thing e && e.def.mineable, "the work yard in front of the door is rock at " + c));
        }

        /// <summary>
        /// Cause 1. The farming pass runs inside the map's tick, before the construction pass, and its field
        /// marks home area round every cell. The compound was then planned inside that border, did not fit, and
        /// every bed was drawn at random round the field before it did.
        /// </summary>
        [Fact]
        public void The_village_is_laid_out_before_the_first_field_breaks_ground()
        {
            (Settlement settlement, CoreMap map) = FoundOnGeneratedMap("layout-field-first", 11);

            FarmingInitiative.Run(map); // the farming pass, first, as the map's tick runs it

            SettlementCompound compound = Assert.IsType<SettlementCompound>(map.settlementCompound);
            Zone_Growing? field = FarmingInitiative.FieldOf(map);
            Assert.NotNull(field);
            Assert.DoesNotContain(field!.Cells, c => compound.Reserves(CellRect.SingleCell(c)));

            for (int pass = 1; pass <= 20; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }
            List<Thing> beds = AllOf(map, d => ReferenceEquals(d, ConstructionThingDefOf.Bed));
            Assert.Equal(settlement.Citizens.Count, beds.Count);
            Assert.All(beds, b => Assert.True(compound.Interior.Contains(b.Position), "bed at " + b.Position + " is outside " + compound.Interior));
        }

        /// <summary>
        /// Cause 1, its other half. The field starts where the first citizen stands, and a founding band stands
        /// on the ground its village is laid out on; a field there is a field across the street and the plots.
        /// </summary>
        [Fact]
        public void A_field_begun_from_a_citizen_on_the_street_is_sown_off_the_villages_ground()
        {
            var map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil);
            map.settlementCompound = SettlementCompound.TryPlan(map, 20, 50);
            SettlementCompound compound = Assert.IsType<SettlementCompound>(map.settlementCompound);
            IntVec3 onTheStreet = compound.DoorCell + new IntVec3(3, 0, 0);
            for (int i = 0; i < 10; i++) GenSpawn.Spawn(NewHuman("Founder" + i), onTheStreet, map);

            FarmingInitiative.Run(map);

            Zone_Growing? field = FarmingInitiative.FieldOf(map);
            Assert.NotNull(field);
            Assert.NotEmpty(field!.Cells);
            Assert.DoesNotContain(field.Cells, c => compound.Reserves(CellRect.SingleCell(c)));
        }

        /// <summary>
        /// Cause 2. Round the hub there is room for a compound but its door looks onto deep water, so the
        /// work yard and every plot east of it are lost; open country lies further out. The plan takes the
        /// ground where the door opens onto the yard.
        /// </summary>
        [Fact]
        public void A_compound_is_not_planned_with_its_door_onto_ground_its_work_yard_cannot_stand_on()
        {
            var map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil);
            TerrainDef water = DefDatabase<TerrainDef>.GetNamed("WaterDeep");
            // Open only in a strip 16 wide round the hub (room for a compound's footprint, not its yard) and
            // east of x = 90.
            foreach (IntVec3 c in map.AllCells.Where(c => c.x < 52 || (c.x > 67 && c.x < 90)))
            {
                map.terrainGrid.SetTerrain(c, water);
                map.pathGrid.RecalculatePerceivedPathCostAt(c);
            }

            SettlementCompound compound = Assert.IsType<SettlementCompound>(SettlementCompound.TryPlan(map, 20, 50));

            output.WriteLine("planned " + compound.Interior + ", yard " + compound.WorkYard);
            Assert.All(compound.WorkYard.Cells, c => Assert.True(map.pathGrid.Walkable(c), "the work yard is under water at " + c));
        }

        /// <summary>
        /// Cause 3. Nothing ever felled a tree inside the ring unless a bed was planned on that very cell, so a
        /// ring whose beds stood elsewhere, or the cells between them, kept its copse.
        /// </summary>
        [Fact]
        public void A_tree_inside_the_ring_is_felled_and_one_outside_it_is_left()
        {
            var map = new CoreMap(120, 120, SimWorld.Map.TerrainDefOf.Soil);
            map.settlementCompound = SettlementCompound.TryPlan(map, 20, 50);
            SettlementCompound compound = Assert.IsType<SettlementCompound>(map.settlementCompound);

            var inside = new IntVec3(compound.Interior.minX + 1, 0, compound.Interior.maxZ - 1);
            Plant inRing = SpawnTree(map, inside, growth: 0.2f); // a sapling: no wood worth felling it for
            SpawnTree(map, compound.Footprint.ExpandedBy(6).Cells.First(c => GenGrid.InBounds(c, map)), growth: 0.2f);

            Pawn builder = NewHuman("Builder");
            builder.skills!.GetSkill(SkillDefOf.Plants)!.Level = 20;
            GenSpawn.Spawn(builder, compound.DoorCell + new IntVec3(2, 0, 0), map);

            var giver = (WorkGiver_ConstructChopWood)DefDatabase<WorkGiverDef>.GetNamed("ConstructChopWood").Worker;
            List<Thing> wanted = giver.PotentialWorkThingsGlobal(builder).Where(t => giver.HasJobOnThing(builder, t)).ToList();
            Assert.Equal(new Thing[] { inRing }, wanted);
        }

        private static Plant SpawnTree(CoreMap map, IntVec3 cell, float growth)
        {
            var tree = (Plant)ThingMaker.MakeThing(TreeDefOf.Plant_TreePoplar);
            tree.Growth = growth;
            GenSpawn.Spawn(tree, cell, map);
            return tree;
        }

        private static (Settlement, CoreMap) FoundOnGeneratedMap(string seed, int bandSeed)
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal,
                OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(bandSeed));
            Find.World = world;
            CoreMap map = settlement.EnterMap(world);
            return (settlement, map);
        }
    }
}
