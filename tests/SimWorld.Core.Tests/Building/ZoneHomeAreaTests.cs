using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A zone's cells are home area, with a border (RimWorld: <c>AutoHomeAreaMaker.Notify_ZoneCellAdded</c>),
    /// so a fire in the settlement's own field is firefighting work.
    /// <para/>
    /// Found tracing a flashstorm over a threat-free founding band (<c>docs/perf/storyteller-pressure</c>):
    /// the fields were not home area, so a fire in them grew past a hundred cells with nobody sent to it.
    /// </summary>
    public class ZoneHomeAreaTests : ContentTestBase
    {
        public ZoneHomeAreaTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap() => new CoreMap(40, 40, TerrainDefOf.Soil);

        [Fact]
        public void A_zone_cell_marks_home_area_four_cells_round_it_and_no_further()
        {
            CoreMap map = NewMap();
            var field = new Zone_Growing { label = "field" };
            map.zoneManager.RegisterZone(field);
            var c = new IntVec3(20, 0, 20);
            Assert.True(map.zoneManager.AddCell(field, c));

            Area home = map.areaManager.Home;
            Assert.True(home[c]);
            Assert.True(home[new IntVec3(24, 0, 24)]);
            Assert.True(home[new IntVec3(16, 0, 20)]);
            Assert.False(home[new IntVec3(25, 0, 20)]);
            Assert.False(home[new IntVec3(20, 0, 15)]);
        }

        [Fact]
        public void A_fire_in_the_settlements_own_field_is_firefighting_work()
        {
            CoreMap map = NewMap();
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(faction);
            Pawn fighter = NewHuman("Fighter");
            fighter.faction = faction;
            GenSpawn.Spawn(fighter, new IntVec3(2, 0, 2), map);

            // Something else already set a home area, so the gate is live, and it is nowhere near the field.
            map.areaManager.Home[new IntVec3(0, 0, 0)] = true;

            var cell = new IntVec3(30, 0, 30);
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog")), cell, map);
            Assert.True(FireUtility.TryStartFireIn(cell, map, 0.5f));
            Fire fire = FireUtility.StaticFireIn(cell, map)!;
            Assert.False(WorkGiver_FightFires.IsFireToFight(fighter, fire));

            var field = new Zone_Growing { label = "field" };
            map.zoneManager.RegisterZone(field);
            map.zoneManager.AddCell(field, new IntVec3(28, 0, 28));
            Assert.True(WorkGiver_FightFires.IsFireToFight(fighter, fire));
        }
    }
}
