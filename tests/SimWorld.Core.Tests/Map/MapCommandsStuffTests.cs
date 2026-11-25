using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The player chooses what a blueprint is built of (<see cref="MapCommands.PlaceBlueprint"/>'s stuff, as
    /// RimWorld's architect menu asks): any stuff in the def's <c>stuffCategories</c>, nothing else.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsStuffTests : ContentTestBase
    {
        public MapCommandsStuffTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static IntVec3 FreeCell(CoreMap map, ThingDef def, IntVec3 near, int skip = 0)
        {
            for (int r = 0; r < 30; r++)
            {
                foreach (IntVec3 c in CellRect.CenteredOn(near, r).EdgeCells)
                {
                    if (!GenGrid.InBounds(c, map) || !GenConstruct.CanPlaceBlueprintAt(def, c, map, out _)) continue;
                    if (map.thingGrid.ThingsListAt(c).Any(t => t is Pawn)) continue;
                    if (skip-- == 0) return c;
                }
            }
            throw new System.InvalidOperationException("no free cell");
        }

        [Fact]
        public void A_blueprint_is_built_of_the_stuff_the_player_names_and_only_a_legal_one()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "place-stuff", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            IntVec3 near = settlement.Citizens.First(p => p.Spawned && p.Map == map).Position;
            ThingDef bed = Def("Bed");

            // Granite blocks belong to the Bronze age (masonry): a stone-age town that holds none cannot build of
            // them (BuildGate), and one that holds some can, however it came by them.
            IntVec3 granite = FreeCell(map, bed, near);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.PlaceBlueprint("Bed", granite, Rot4.North, "BlocksGranite").Outcome);
            Thing blocks = ThingMaker.MakeThing(Def("BlocksGranite"));
            blocks.stackCount = bed.costStuffCount;
            GenSpawn.Spawn(blocks, FreeCell(map, Def("BlocksGranite"), near, skip: 18), map);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Bed", granite, Rot4.North, "BlocksGranite").Outcome);
            Blueprint placed = map.thingGrid.ThingsListAt(granite).OfType<Blueprint>().Single();
            Assert.Same(Def("BlocksGranite"), placed.stuffToUse);

            // None named: the default, wood.
            IntVec3 plain = FreeCell(map, bed, near, skip: 6);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Bed", plain).Outcome);
            Assert.Same(Def("WoodLog"), map.thingGrid.ThingsListAt(plain).OfType<Blueprint>().Single().stuffToUse);

            // Refused, and nothing placed: a stuff the bed cannot be made of, a stuff for a def not made from
            // stuff, and a stuff that is no def at all.
            IntVec3 other = FreeCell(map, bed, near, skip: 12);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.PlaceBlueprint("Bed", other, Rot4.North, "Cloth").Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.PlaceBlueprint("StorageHut", other, Rot4.North, "WoodLog").Outcome);
            Assert.Equal(MapCommandOutcome.UnknownDef, MapCommands.PlaceBlueprint("Bed", other, Rot4.North, "NoSuchStuff").Outcome);
            Assert.Empty(map.thingGrid.ThingsListAt(other).OfType<Blueprint>());
        }
    }
}
