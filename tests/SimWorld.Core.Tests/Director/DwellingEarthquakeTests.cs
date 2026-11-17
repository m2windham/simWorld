using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A household's dwelling is one of the structures an earthquake brings down, and whoever sleeps in it is
    /// hurt by it, the same risk a dormitory bed carries (<see cref="EarthquakeFootprintTests"/>). Set
    /// up as that file sets up its sleeper; a file of its own rather than an addition to it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DwellingEarthquakeTests : ContentTestBase
    {
        public DwellingEarthquakeTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        [Fact]
        public void A_dwelling_that_comes_down_hurts_the_household_sleeping_in_it()
        {
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
            Thing house = GenSpawn.Spawn(ThingMaker.MakeThing(DwellingThingDefOf.House_Paleo), new IntVec3(10, 0, 10), map, Rot4.North);

            Pawn sleeper = NewHuman("Sleeper");
            settlement.AddCitizen(sleeper);
            IntVec3 slot = default;
            foreach (IntVec3 c in house.OccupiedRect().Cells) { slot = c; break; }
            GenSpawn.Spawn(sleeper, slot, map);

            // One dwelling on the map: the quake's DestroyCount is at least one, so it always comes down.
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            bool fired = DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);
            Assert.True(house.Destroyed, "the dwelling must come down");
            // Hurt, not killed: a fallen dwelling is RimWorld's built-roof hit (IncidentWorker_Earthquake).
            Assert.False(sleeper.Dead, "a healthy sleeper survives a dwelling falling on them");
            Assert.True(sleeper.health.hediffSet.hediffs.Count > 0, "sleeping in a dwelling that fell on them");
        }
    }
}
