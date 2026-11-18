using System.Collections.Generic;
using System.Linq;

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
    /// A quake's letter and <see cref="ResourceImpactLedger"/> count only the settlement's own buildings as its
    /// structures destroyed. A ruin's wall falls too, but it was never the settlement's; before, every fallen
    /// ruin wall was reported as a loss.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeOwnStructuresTests : ContentTestBase
    {
        public EarthquakeOwnStructuresTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        [Fact]
        public void Fallen_ruin_walls_are_not_counted_as_the_settlements_structures_lost()
        {
            CoreMap map = new CoreMap(30, 30, SimWorld.Map.TerrainDefOf.Soil);
            ThingDef wall = Def("Wall");
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var walls = new List<Thing>();
            foreach (int z in new[] { 6, 10, 14 })
            {
                for (int x = 2; x < 22; x++)
                {
                    bool owned = x % 2 == 0;
                    Thing w = ThingMaker.MakeThing(wall, owned ? Def("WoodLog") : Def("BlocksSandstone"));
                    if (owned) w.SetFaction(ours);
                    walls.Add(GenSpawn.Spawn(w, new IntVec3(x, 0, z), map));
                }
            }

            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, ours, "Quaketown", 0);
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            Assert.True(DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target }));

            int oursFallen = walls.Count(w => w.Destroyed && w.Faction == ours);
            int ruinsFallen = walls.Count(w => w.Destroyed && w.Faction == null);
            Assert.True(oursFallen > 0 && ruinsFallen > 0, "fixture: walls of both kinds fell");

            Assert.Equal(oursFallen, Find.Storyteller.resourceImpact.TotalStructuresDestroyed);
            Letter letter = Assert.Single(Find.LetterStack.LettersListForReading);
            Assert.Contains(oursFallen + " structures come down", letter.text);
        }
    }
}
