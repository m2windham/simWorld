using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// Food the ledger issues onto a map lands where the band can walk to it. The second playtest starved
    /// beside stores the view called full; issued food was placed on any standable cell around the granary or
    /// the map's middle, and on a map split into sealed rock pockets the middle can be in a pocket nobody
    /// reaches. That food still counted as on the map, so nothing more was issued.
    /// </summary>
    [Collection("GlobalDefs")]
    public class IssuedFoodReachTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public IssuedFoodReachTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        [Theory]
        [InlineData("hunters-mandate")]
        [InlineData("mandate-idle")]
        [InlineData("issue-reach-a")]
        [InlineData("issue-reach-b")]
        public void Issued_food_lands_only_where_the_band_can_walk(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            IntVec3 band = settlement.Citizens.First(p => p.Spawned && p.Map == map).Position;
            var middle = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            output.WriteLine(seed + ": middle reachable " + map.reachability.CanReachCells(band, middle));

            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList())
            {
                t.Destroy();
            }
            ThingDef ration = SettlementLarder.BestStoredFood(settlement) ?? DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.IsNutritionGivingIngestible && d.stackLimit >= 75);
            settlement.AddStore(ration, 500);

            Assert.True(SettlementStockInitiative.IssueFromStores(settlement, map) > 0);
            var issued = map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList();
            Assert.NotEmpty(issued);
            Assert.All(issued, t => Assert.True(map.reachability.CanReachCells(band, t.Position), t.Label + " at " + t.Position + " is out of the band's reach"));
        }

        /// <summary>A sealed pocket right beside the band: the cells nearest the drop point that nobody can walk
        /// into get no food, however close they are.</summary>
        [Theory]
        [InlineData("hunters-mandate")]
        [InlineData("mandate-idle")]
        public void Issued_food_skips_a_sealed_pocket_beside_the_drop_point(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            IntVec3 band = SettlementReach.BandCell(map)!.Value;
            foreach (var zone in map.zoneManager.AllZones.ToList()) map.zoneManager.DeregisterZone(zone);
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList())
            {
                t.Destroy();
            }

            // A ring of granite three cells out from a centre four cells from the band: its inside is the
            // nearest ground to the band on that side, and sealed.
            int dx = band.x + 10 < map.Size.x ? 1 : -1;
            var centre = new IntVec3(band.x + 4 * dx, 0, band.z);
            ThingDef granite = DefDatabase<ThingDef>.GetNamed("Granite");
            var inside = new System.Collections.Generic.List<IntVec3>();
            for (int x = -3; x <= 3; x++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    var cell = new IntVec3(centre.x + x, 0, centre.z + z);
                    if (!GenGrid.InBounds(cell, map)) continue;
                    foreach (Thing t in map.thingGrid.ThingsAt(cell).ToList())
                    {
                        if (t is Pawn p)
                        {
                            p.DeSpawn();
                            GenSpawn.Spawn(p, band, map); // out of the pocket, beside the one measured from
                        }
                        else
                        {
                            t.Destroy();
                        }
                    }
                    if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(z)) == 3) GenSpawn.Spawn(ThingMaker.MakeThing(granite), cell, map);
                    else inside.Add(cell);
                }
            }
            Assert.Contains(inside, c => GenGrid.Standable(c, map) && !map.reachability.CanReachCells(band, c));

            ThingDef ration = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.IsNutritionGivingIngestible && d.stackLimit >= 75);
            settlement.AddStore(ration, 5000);
            Assert.True(SettlementStockInitiative.IssueFromStores(settlement, map) > 0);

            var issued = map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList();
            output.WriteLine(seed + ": " + issued.Count + " stacks, nearest-first out to " + issued.Max(t => (t.Position - band).LengthHorizontal));
            Assert.All(issued, t => Assert.True(map.reachability.CanReachCells(band, t.Position), t.Label + " at " + t.Position + " is out of the band's reach"));
        }
    }
}
