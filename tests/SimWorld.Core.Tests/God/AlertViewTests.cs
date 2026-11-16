using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The alert readout through the god view: each alert appears while its condition holds and is gone the
    /// moment it stops, on a real opened settlement, with its culprits as thing ids.
    /// </summary>
    [Collection("GlobalDefs")]
    public class AlertViewTests : ContentTestBase
    {
        public AlertViewTests(CoreContentFixture content) : base(content)
        {
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static List<Pawn> Spawned(Settlement s) =>
            s.Citizens.Where(p => p.Spawned && !p.Dead && ReferenceEquals(p.Map, s.InteriorMap)).ToList();

        private static AlertView? Alert(string defName) =>
            GodViewSnapshot.Capture().Alerts.FirstOrDefault(a => a.DefName == defName);

        [Fact]
        public void No_alerts_and_no_exception_before_a_world_exists()
        {
            Find.World = null;
            Assert.Empty(GodViewSnapshot.Capture().Alerts);
        }

        [Fact]
        public void Colonist_needs_rescue_appears_when_one_is_downed_and_clears_when_they_get_up()
        {
            Settlement s = OpenedSettlement("alert-rescue");
            Pawn p = Spawned(s).First();
            Assert.DoesNotContain(Alert("Alert_ColonistNeedsRescuing")?.CulpritThingIds ?? new List<int>(), id => id == p.thingIDNumber);

            p.health.ForceDowned = true;
            AlertView? up = Alert("Alert_ColonistNeedsRescuing");
            Assert.NotNull(up);
            Assert.Contains(p.thingIDNumber, up!.CulpritThingIds);
            Assert.Equal(s.tile, up.SettlementTile);
            Assert.Equal(Alerts.AlertPriority.Critical, up.Priority);
            Assert.Contains(p.Label, up.Explanation);

            p.health.ForceDowned = false;
            Assert.Null(Alert("Alert_ColonistNeedsRescuing"));
        }

        [Fact]
        public void Starvation_appears_on_an_empty_stomach_and_clears_on_a_full_one()
        {
            Settlement s = OpenedSettlement("alert-starve");
            Pawn p = Spawned(s).First();

            p.needs.food!.CurLevelPercentage = 0f;
            Assert.Contains(p.thingIDNumber, Alert("Alert_StarvationColonists")!.CulpritThingIds);

            p.needs.food.CurLevelPercentage = 1f;
            Assert.Null(Alert("Alert_StarvationColonists"));
        }

        [Fact]
        public void Low_food_appears_when_the_map_is_emptied_of_food_and_clears_when_it_is_stocked()
        {
            Settlement s = OpenedSettlement("alert-lowfood");
            CoreMap map = s.InteriorMap!;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => t.def.IsNutritionGivingIngestible).ToList())
            {
                t.Destroy();
            }

            AlertView? low = Alert("Alert_LowFood");
            Assert.NotNull(low);
            Assert.Empty(low!.CulpritThingIds);

            int colonists = Spawned(s).Count;
            ThingDef food = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.IsNutritionGivingIngestible && d.stackLimit >= 75);
            int needed = (int)(Alerts.Alert_LowFood.NutritionThresholdPerColonist * colonists / food.ingestible!.nutrition) + 1;
            IntVec3 cell = Spawned(s).First().Position;
            while (needed > 0)
            {
                Thing stack = ThingMaker.MakeThing(food);
                stack.stackCount = System.Math.Min(needed, food.stackLimit);
                needed -= stack.stackCount;
                GenSpawn.Spawn(stack, cell, map);
            }

            Assert.Null(Alert("Alert_LowFood"));
        }

        [Fact]
        public void Fire_in_home_area_appears_for_a_fire_on_home_ground_and_clears_when_it_is_out()
        {
            Settlement s = OpenedSettlement("alert-fire");
            CoreMap map = s.InteriorMap!;
            IntVec3 cell = Spawned(s).First().Position;
            map.areaManager.Home[cell] = true;

            Thing fire = ThingMaker.MakeThing(FireThingDefOf.Fire);
            GenSpawn.Spawn(fire, cell, map);
            Assert.Contains(fire.thingIDNumber, Alert("Alert_FireInHomeArea")!.CulpritThingIds);

            fire.Destroy();
            Assert.Null(Alert("Alert_FireInHomeArea"));
        }

        [Fact]
        public void Need_doctor_follows_the_doctor_work_priority_the_player_sets()
        {
            Settlement s = OpenedSettlement("alert-doctor");
            List<Pawn> people = Spawned(s);
            Pawn patient = people.First();
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, 6f, hitPart: patient.RaceProps.body!.GetPartByLabel("left arm")), patient);
            Assert.True(HealthAIUtility.HasHediffsNeedingTend(patient));

            foreach (Pawn p in people) GodCommands.SetWorkPriority(p.thingIDNumber, "Doctor", 0);
            Assert.Contains(patient.thingIDNumber, Alert("Alert_NeedDoctor")!.CulpritThingIds);

            Pawn doctor = people.First(p => p != patient && !p.WorkTypeIsDisabled(global::SimWorld.Work.WorkTypeDefOf.Doctor));
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetWorkPriority(doctor.thingIDNumber, "Doctor", 1).Outcome);
            Assert.Null(Alert("Alert_NeedDoctor"));
        }

        [Fact]
        public void Every_alert_is_listed_once_in_a_fixed_order()
        {
            IReadOnlyList<Alerts.Alert> all = Alerts.AlertsReadout.AllAlerts;
            Assert.Equal(all.Count, all.Select(a => a.DefName).Distinct().Count());
            Assert.Contains(all, a => a.DefName == "Alert_LowFood");
            Assert.True(all.Count >= 10);
            Assert.Equal(all.OrderByDescending(a => a.Priority).Select(a => a.DefName), all.Select(a => a.DefName));
        }
    }
}
