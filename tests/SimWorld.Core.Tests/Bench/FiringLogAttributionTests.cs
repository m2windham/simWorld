using SimWorld.Bench.Suites;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Bench
{
    /// <summary>
    /// The storyteller bench's <c>FiringLog</c> credits each firing with the harm its own worker did.
    /// <see cref="Storyteller.TryFire"/> raises <see cref="Storyteller.IncidentFired"/> after the worker has run,
    /// so an Earthquake's structures and deaths are already in the ledgers when the log hears of it. The log
    /// used to settle those ledgers before opening the new firing's row, which handed a quake's whole damage
    /// to the previous quake (or to the orphan count when there was none) and printed "no observable effect"
    /// against the quake that did it: on seed 2024 the day-13 quake read "razed 25, killed 1" in one run and
    /// nothing in the other, depending only on whether a later quake fired before day 60.
    /// </summary>
    [Collection("GlobalDefs")]
    public class FiringLogAttributionTests : ContentTestBase
    {
        public FiringLogAttributionTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        [Fact]
        public void Each_earthquake_is_credited_with_the_structures_it_razed_itself()
        {
            var map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
            SpawnWalls(map, 40);

            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);

            IncidentDef quake = DefDatabase<IncidentDef>.GetNamed("Earthquake");
            var log = new FiringLog();
            Find.Storyteller.IncidentFired += log.OnFired;

            int Ledger() => Find.Storyteller.resourceImpact.StructuresDestroyedBy("Earthquake");

            log.BeginInterval(1);
            Assert.True(Find.Storyteller.TryFire(new FiringIncident(quake, null, new IncidentParms { target = target })));
            int first = Ledger();

            Find.TickManager.DebugSetTicksGame(Storyteller.IncidentCycleLengthTicks * 10);
            log.BeginInterval(2);
            Assert.True(Find.Storyteller.TryFire(new FiringIncident(quake, null, new IncidentParms { target = target })));
            int second = Ledger() - first;
            log.SettleAttributed();

            Assert.True(first > 0 && second > 0, "fixture: both quakes razed something");
            Assert.Equal(2, log.Firings.Count);
            Assert.Equal(first, log.Firings[0].Razed);
            Assert.Equal(second, log.Firings[1].Razed);
            Assert.Equal(0, log.OrphanStructures);
        }

        private static void SpawnWalls(CoreMap map, int count)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            int placed = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (placed >= count) break;
                if (!GenGrid.Standable(c, map) || GenGrid.GetThingList(c, map).Count != 0) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(wall), c, map);
                placed++;
            }
            Assert.Equal(count, placed);
        }
    }
}
