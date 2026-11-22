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
    /// A household's house is walls and beds an earthquake brings down like any other, and whoever sleeps in it is
    /// hurt by it, the same risk a dormitory bed carries (<see cref="EarthquakeFootprintTests"/>). Set up as that
    /// file sets up its sleeper; a file of its own rather than an addition to it.
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
        public void A_house_that_comes_down_hurts_the_household_sleeping_in_it()
        {
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
            Dwelling house = SimWorld.Tests.Building.ShelterKit.Raise(map, SimWorld.Tests.Building.ShelterKit.Rung("House_Meso"), new IntVec3(8, 0, 8));
            var beds = house.Beds(map);
            Assert.Equal(3, beds.Count);
            int earthBefore = map.listerThings.ThingsOfDef(ShelterThingDefOf.WallEarth).Count;

            var sleepers = new System.Collections.Generic.List<Pawn>();
            foreach (Thing bed in beds)
            {
                Pawn sleeper = NewHuman("Sleeper" + sleepers.Count);
                settlement.AddCitizen(sleeper);
                GenSpawn.Spawn(sleeper, bed.Position, map);
                sleepers.Add(sleeper);
            }

            // The quake brings down at least one of every structure def standing: a bed, and a bank of earth.
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            bool fired = DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);
            Assert.True(house.Beds(map).Count < 3, "a bed of the house must come down");
            Assert.True(map.listerThings.ThingsOfDef(ShelterThingDefOf.WallEarth).Count < earthBefore, "the dug bank is a structure the quake knows");
            Assert.False(house.IsComplete(map));
            // Hurt, not killed: a fallen bed is RimWorld's built-roof hit (IncidentWorker_Earthquake).
            Assert.Contains(sleepers, p => !p.Dead && p.health.hediffSet.hediffs.Count > 0);
        }
    }
}
