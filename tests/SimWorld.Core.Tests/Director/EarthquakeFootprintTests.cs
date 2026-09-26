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
    /// A structure the earthquake destroys falls on its whole footprint. <c>Bed</c> is 1x2, and the crush
    /// used to look only at a destroyed structure's <c>Position</c> — its head — so somebody standing on the
    /// foot of a bed that came down walked away. Set up as <see cref="EarthquakeTests"/> sets up its sleeper;
    /// kept in a file of its own rather than added to that one.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EarthquakeFootprintTests : ContentTestBase
    {
        public EarthquakeFootprintTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new global::SimWorld.God.GodManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        [Theory]
        [InlineData(Rot4.NorthInt)]
        [InlineData(Rot4.EastInt)]
        [InlineData(Rot4.SouthInt)]
        [InlineData(Rot4.WestInt)]
        public void Somebody_on_the_foot_of_a_bed_that_comes_down_is_crushed_with_it(int facing)
        {
            var rot = new Rot4(facing);
            CoreMap map = new CoreMap(24, 24, TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Quaketown", 0);
            var head = new IntVec3(10, 0, 10);
            Thing bed = GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed")), head, map, rot);
            IntVec3 foot = head + rot.FacingCell;
            Assert.Contains(foot, bed.OccupiedRect().Cells);

            Pawn onFoot = NewHuman("OnTheFoot");
            settlement.AddCitizen(onFoot);
            GenSpawn.Spawn(onFoot, foot, map);

            // One bed on the map: the quake's DestroyCount is at least one, so it always comes down.
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            bool fired = DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = target });

            Assert.True(fired);
            Assert.True(bed.Destroyed, "the fixture must bring the bed down, or this proves nothing");
            Assert.True(onFoot.Dead, "standing on the foot of a bed that fell on them");
            Assert.Equal(1, Find.Storyteller.deaths.AttributedTo("Earthquake"));
        }
    }
}
