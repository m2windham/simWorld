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
    /// A quake hurts people only through a roof that falls on them (Doug's call, 2026-11-28). It breaks
    /// structures as before; a wall that goes takes its share of the roof's support with it, and RimWorld's
    /// roof collapse (<c>Building.RoofCollapseCellsFinder</c>, <c>Building.RoofCollapserImmediate</c>) decides
    /// what falls and hits what is under it. A bed that breaks is furniture breaking and hurts nobody.
    ///
    /// <para/>Every fixture holds roof up with <c>Sandstone</c> rock where it must stay up: natural rock is not
    /// a structure the quake knows, so it is the one holder sure to survive the shake. A map with one bed and
    /// one wall loses both, because the quake destroys at least one of every structure def standing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeRoofTests : ContentTestBase
    {
        public EarthquakeRoofTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static RoofDef Roof => SimWorld.Map.RoofDefOf.RoofConstructed;

        private Pawn Citizen(Settlement settlement, CoreMap map, IntVec3 cell, string name)
        {
            Pawn p = NewHuman(name);
            settlement.AddCitizen(p);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static bool Shake(Settlement settlement, CoreMap map)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            return DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target });
        }

        private static Settlement Town() => new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);

        /// <summary>Under a roof held up by rock, the bed breaks and the sleeper in it is not touched. Before,
        /// a broken bed dealt the roof's 15–30 hit itself, on every rotation and on both of its cells.</summary>
        [Theory]
        [InlineData(Rot4.NorthInt)]
        [InlineData(Rot4.EastInt)]
        [InlineData(Rot4.SouthInt)]
        [InlineData(Rot4.WestInt)]
        public void A_sleeper_whose_bed_breaks_under_a_standing_roof_is_unhurt(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            Settlement settlement = Town();
            var head = new IntVec3(10, 0, 10);
            Thing bed = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Bed")), head, map, rot);
            foreach (IntVec3 c in bed.OccupiedRect().ExpandedBy(1).Cells) map.roofGrid.SetRoof(c, Roof);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), head + new IntVec3(-2, 0, 0), map);

            Pawn onHead = Citizen(settlement, map, head, "OnTheHead");
            Pawn onFoot = Citizen(settlement, map, head + rot.FacingCell, "OnTheFoot");

            Assert.True(Shake(settlement, map));

            Assert.True(bed.Destroyed, "the fixture must bring the bed down, or this proves nothing");
            Assert.True(map.roofGrid.Roofed(head), "the rock holds the roof, so it must still stand");
            Assert.Empty(onHead.health.hediffSet.hediffs);
            Assert.Empty(onFoot.health.hediffSet.hediffs);
        }

        /// <summary>The roof's only holder is a wall the quake brings down: the whole roof is cut off from any
        /// support and falls, and the sleeper under it takes RimWorld's built-roof hit.</summary>
        [Fact]
        public void A_sleeper_under_a_roof_whose_wall_falls_is_hurt_by_the_roof()
        {
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            Settlement settlement = Town();
            var head = new IntVec3(10, 0, 10);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Bed")), head, map, Rot4.North);
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), head + new IntVec3(-1, 0, 0), map);
            foreach (IntVec3 c in new CellRect(head.x - 1, head.z - 1, 3, 4).Cells) map.roofGrid.SetRoof(c, Roof);
            Pawn sleeper = Citizen(settlement, map, head, "Sleeper");

            Assert.True(Shake(settlement, map));

            Assert.True(wall.Destroyed, "the fixture must bring the wall down, or this proves nothing");
            Assert.False(map.roofGrid.Roofed(head), "a roof with nothing left holding it must fall");
            Assert.NotEmpty(sleeper.health.hediffSet.hediffs);
        }

        /// <summary>Roof within RimWorld's support distance of a holder that survives stays up when a far wall
        /// falls; only the cells the fallen wall alone was holding come down, on whoever is under them. A strip
        /// of roof from a rock at x=0 to a wall at x=17: the cells near the rock are held, the far ones are
        /// more than <c>RoofMaxSupportDistance</c> (6.9) from any holder once the wall is gone.</summary>
        [Fact]
        public void Roof_still_held_by_a_standing_support_does_not_fall_when_a_far_wall_does()
        {
            CoreMap map = new CoreMap(20, 5, TerrainDefOf.Soil);
            Settlement settlement = Town();
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), new IntVec3(0, 0, 1), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sandstone")), new IntVec3(0, 0, 2), map);
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(17, 0, 1), map);
            for (int x = 1; x <= 16; x++)
            {
                map.roofGrid.SetRoof(new IntVec3(x, 0, 1), Roof);
                map.roofGrid.SetRoof(new IntVec3(x, 0, 2), Roof);
            }
            var near = new IntVec3(3, 0, 1);
            Thing bed = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Bed")), near, map, Rot4.East);
            Pawn sleeper = Citizen(settlement, map, near, "NearTheRock");
            Pawn farOne = Citizen(settlement, map, new IntVec3(14, 0, 2), "NearTheWall");

            Assert.True(Shake(settlement, map));

            Assert.True(wall.Destroyed && bed.Destroyed, "the fixture must bring the wall and the bed down");
            for (int x = 1; x <= 6; x++)
            {
                Assert.True(map.roofGrid.Roofed(new IntVec3(x, 0, 1)) && map.roofGrid.Roofed(new IntVec3(x, 0, 2)),
                    $"x={x} is within reach of the rock and must stay roofed");
            }
            for (int x = 11; x <= 16; x++)
            {
                Assert.False(map.roofGrid.Roofed(new IntVec3(x, 0, 1)) || map.roofGrid.Roofed(new IntVec3(x, 0, 2)),
                    $"x={x} was held only by the wall and must fall");
            }
            Assert.Empty(sleeper.health.hediffSet.hediffs);
            Assert.NotEmpty(farOne.health.hediffSet.hediffs);
        }
    }
}
