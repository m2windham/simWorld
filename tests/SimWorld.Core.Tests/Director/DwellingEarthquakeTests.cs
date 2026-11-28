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
    /// A household's house is walls and beds an earthquake brings down like any other. Whoever sleeps in it is
    /// hurt only if its roof falls on them (<see cref="EarthquakeRoofTests"/>): a pit house is six cells across,
    /// so the banks the quake leaves standing are well inside RimWorld's support distance and hold the roof.
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
        public void A_house_the_quake_breaks_under_a_standing_roof_hurts_nobody_in_it()
        {
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
            Dwelling house = SimWorld.Tests.Building.ShelterKit.Raise(map, SimWorld.Tests.Building.ShelterKit.Rung("House_Meso"), new IntVec3(8, 0, 8));
            SimWorld.Tests.Building.ShelterKit.Roof(map, house);
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
            foreach (Pawn p in sleepers)
            {
                Assert.True(map.roofGrid.Roofed(p.Position), "the standing banks hold the roof over every bed");
                Assert.Empty(p.health.hediffSet.hediffs);
            }
        }
    }
}
