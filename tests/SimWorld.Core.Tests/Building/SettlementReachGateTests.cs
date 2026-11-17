using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Each planner that sites something for a settlement keeps to ground the band can walk to
    /// (<see cref="SettlementReach"/>). <c>BandReachTests</c> pins the whole loop on seed 2024, but there
    /// the compound absorbs nearly every placement, so it still passes with the random-draw gate
    /// (<c>SettlementConstructionInitiative.Fits</c>) or the works gate
    /// (<see cref="SettlementWorksInitiative.TryPlaceBlueprint"/>) removed. These seal the band into a
    /// pocket too small for a compound and drive each planner directly, so each gate is the only thing
    /// keeping its plans inside.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SettlementReachGateTests : ContentTestBase
    {
        public SettlementReachGateTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private const int PocketRadius = 6;

        private static ThingDef Wall => DefDatabase<ThingDef>.GetNamed("Wall");

        /// <summary>A founded band on its generated map, with the first citizen <see cref="SettlementReach.BandCell"/>
        /// measures from walled into a square pocket: everything outside it is out of the band's reach.</summary>
        private static (Settlement Settlement, CoreMap Map, IntVec3 Band) BandInAPocket(string seed)
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal,
                OverallTemperature.Normal, OverallPopulation.Normal, "Test", 3, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(29), "Pocket");
            CoreMap map = settlement.EnterMap(world);
            Find.World = world;

            IntVec3 band = SettlementReach.BandCell(map)!.Value;
            foreach (IntVec3 c in CellRect.CenteredOn(band, PocketRadius).ClipInsideMap(map).EdgeCells)
            {
                foreach (Thing t in GenGrid.GetThingList(c, map).ToList())
                {
                    if (t is Pawn) t.DeSpawn();
                    else t.Destroy();
                }
                if (map.pathGrid.Walkable(c)) GenSpawn.Spawn(ThingMaker.MakeThing(Wall), c, map);
            }

            Assert.Equal(band, SettlementReach.BandCell(map));
            int outside = map.AllCells.Count(c => map.pathGrid.Walkable(c) && !map.reachability.CanReachCells(band, c));
            Assert.True(outside > 1000, $"fixture: the pocket should seal the band off from most of the map; {outside} cells are out of reach");
            return (settlement, map, band);
        }

        private static List<Thing> Blueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).ToList();

        [Fact]
        public void The_construction_draw_places_nothing_outside_the_bands_pocket()
        {
            (Settlement settlement, CoreMap map, IntVec3 band) = BandInAPocket("reach-gate-fits");

            for (int pass = 0; pass < 10; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }

            Assert.Null(map.settlementCompound); // no compound fits the pocket, so every bed comes from the draw
            List<Thing> planned = Blueprints(map);
            Assert.NotEmpty(planned);
            Assert.All(planned, bp => Assert.True(map.reachability.CanReachCells(band, bp.Position),
                $"{bp} at {bp.Position} is outside the band's pocket round {band}"));
        }

        [Fact]
        public void A_works_blueprint_is_placed_inside_the_bands_pocket_or_not_at_all()
        {
            (_, CoreMap map, IntVec3 band) = BandInAPocket("reach-gate-works");
            ThingDef bench = ResearchWorkDefOf.ResearchBench;

            // Anchored on the band, the scan runs in cell order from the corner of a square wider than the
            // pocket, so the first cell that would take the bench lies outside the wall.
            Assert.True(SettlementWorksInitiative.TryPlaceBlueprint(map, bench, band, PocketRadius + 4),
                "the pocket has room for a bench, so an anchored placement should find it");

            // Unanchored, it samples the whole map, almost all of it out of reach.
            for (int i = 0; i < 20; i++) SettlementWorksInitiative.TryPlaceBlueprint(map, bench, anchor: null, radius: 0);

            List<Thing> planned = Blueprints(map);
            Assert.NotEmpty(planned);
            Assert.All(planned, bp => Assert.True(map.reachability.CanReachCells(band, bp.Position),
                $"{bp} at {bp.Position} is outside the band's pocket round {band}"));
        }
    }
}
