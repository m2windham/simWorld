using System;
using System.Collections.Generic;
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
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// <see cref="MapCommands.SetZonePlant"/> -- the player telling a growing zone that already stands what to
    /// grow (RimWorld: <c>Command_SetPlantToGrow</c>).
    ///
    /// <para/><b>What is pinned.</b> The plant is set; the question RimWorld's own command asks about a plant
    /// with a minimum sowing skill (can anyone here sow it?) is answered as RimWorld answers it -- the choice
    /// is applied anyway and the result's <see cref="MapCommandResult.Reason"/> warns; and a zone the
    /// settlement itself paints is handed to the player rather than put back on the next pass -- the one way a
    /// player's choice of crop could otherwise be silently undone.
    /// </summary>
    public class MapCommandsPlantTests : ContentTestBase
    {
        public MapCommandsPlantTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Healroot => Def("Plant_Healroot");

        private static int MinSkill => Healroot.plant!.sowMinSkill;

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        /// <summary>Every living citizen's Plants skill, set to one level -- the knob that decides whether
        /// anybody can sow healroot.</summary>
        private static void SetEveryonesPlantsSkill(Settlement settlement, int level)
        {
            foreach (Pawn p in settlement.Citizens) p.skills!.GetSkill(SkillDefOf.Plants)!.Level = level;
        }

        private static IntVec3 FreeCellNear(CoreMap map, IntVec3 near)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 c = near + pattern[i];
                if (!GenGrid.InBounds(c, map) || !GenGrid.Standable(c, map)) continue;
                if (map.zoneManager.ZoneAt(c) != null || map.edificeGrid[c] != null) continue;
                return c;
            }
            throw new InvalidOperationException("No free cell near " + near);
        }

        private static (CoreMap map, IntVec3 cell, Zone_Growing zone, Settlement settlement) PotatoZone(string seed)
        {
            Settlement settlement = OpenedSettlement(seed);
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            IntVec3 cell = FreeCellNear(map, citizen.Position);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", new[] { cell }).Outcome);
            return (map, cell, (Zone_Growing)map.zoneManager.ZoneAt(cell)!, settlement);
        }

        // ---- it sets the plant ----

        [Fact]
        public void The_player_changes_what_a_standing_zone_grows()
        {
            var (_, cell, zone, _) = PotatoZone("zone-plant-set");

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Rice");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Same(Def("Plant_Rice"), zone.plantDefToGrow);
        }

        [Fact]
        public void Naming_the_plant_it_already_grows_changes_nothing_and_says_so()
        {
            var (_, cell, zone, _) = PotatoZone("zone-plant-same");

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Potato");

            Assert.Equal(MapCommandOutcome.NoChange, result.Outcome);
            Assert.Same(Def("Plant_Potato"), zone.plantDefToGrow);
        }

        // ---- a plant nobody can sow yet is applied, with a warning (RimWorld's WarnAsAppropriate) ----

        [Fact]
        public void Healroot_is_applied_with_a_warning_when_nobody_in_the_settlement_can_sow_it_yet()
        {
            var (_, cell, zone, settlement) = PotatoZone("zone-plant-nobody");
            SetEveryonesPlantsSkill(settlement, MinSkill - 1);

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Healroot");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Same(Healroot, zone.plantDefToGrow);
            Assert.Contains("Warning", result.Reason);
            Assert.Contains(MinSkill.ToString(), result.Reason);
        }

        [Fact]
        public void The_zone_sits_unsown_until_a_capable_sower_arrives_and_then_is_sown()
        {
            var (_, cell, zone, settlement) = PotatoZone("zone-plant-later");
            SetEveryonesPlantsSkill(settlement, MinSkill - 1);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetZonePlant(cell, "Plant_Healroot").Outcome);

            var sow = new WorkGiver_GrowerSow();
            Pawn citizen = settlement.Citizens[0];
            Assert.False(sow.HasJobOnCell(citizen, cell), "nobody can sow it yet, so the zone offers no work");

            citizen.skills!.GetSkill(SkillDefOf.Plants)!.Level = MinSkill;

            Assert.True(sow.HasJobOnCell(citizen, cell), "the planned-ahead zone should be worked once somebody can");
            Assert.Same(Healroot, zone.plantDefToGrow);
        }

        [Fact]
        public void One_capable_citizen_is_enough_to_set_healroot_and_nothing_warns()
        {
            var (_, cell, zone, settlement) = PotatoZone("zone-plant-one");
            SetEveryonesPlantsSkill(settlement, MinSkill - 1);
            settlement.Citizens[0].skills!.GetSkill(SkillDefOf.Plants)!.Level = MinSkill;

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Healroot");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Same(Healroot, zone.plantDefToGrow);
            Assert.DoesNotContain("Warning", result.Reason);
        }

        [Fact]
        public void A_skilled_citizen_who_is_not_working_the_land_does_not_count_so_the_result_warns()
        {
            var (_, cell, zone, settlement) = PotatoZone("zone-plant-idle");
            SetEveryonesPlantsSkill(settlement, MinSkill);
            foreach (Pawn p in settlement.Citizens) p.workSettings.SetPriority(WorkTypeDefOf.Growing, 0);

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Healroot");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Same(Healroot, zone.plantDefToGrow);
            Assert.Contains("Warning", result.Reason);
        }

        [Fact]
        public void A_plant_with_no_skill_floor_never_warns_for_want_of_a_sower()
        {
            var (_, cell, zone, settlement) = PotatoZone("zone-plant-floor");
            SetEveryonesPlantsSkill(settlement, 0);

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Rice");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Same(Def("Plant_Rice"), zone.plantDefToGrow);
            Assert.DoesNotContain("Warning", result.Reason);
        }

        // ---- it still refuses what cannot be done ----

        [Fact]
        public void It_refuses_a_cell_with_no_growing_zone_an_unknown_def_and_a_def_that_is_not_a_plant()
        {
            var (map, cell, zone, _) = PotatoZone("zone-plant-bad");

            IntVec3 bare = FreeCellNear(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2));
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.SetZonePlant(bare, "Plant_Rice").Outcome);
            Assert.Equal(MapCommandOutcome.UnknownDef, MapCommands.SetZonePlant(cell, "NoSuchCropAtAll").Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.SetZonePlant(cell, "WoodLog").Outcome);
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.SetZonePlant(new IntVec3(-1, 0, -1), "Plant_Rice").Outcome);
            Assert.Same(Def("Plant_Potato"), zone.plantDefToGrow);
        }

        [Fact]
        public void With_no_game_running_there_is_no_map_to_change()
        {
            Assert.Null(Find.CurrentGame);

            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.SetZonePlant(new IntVec3(1, 0, 1), "Plant_Rice").Outcome);
        }

        // ---- the settlement's own zones are handed over ----

        [Fact]
        public void A_zone_the_player_painted_keeps_its_label()
        {
            var (_, cell, zone, _) = PotatoZone("zone-plant-own");
            string label = zone.label;

            MapCommands.SetZonePlant(cell, "Plant_Rice");

            Assert.Equal(label, zone.label);
        }

        [Fact]
        public void A_plant_set_on_the_settlements_own_field_is_not_put_back_on_the_next_pass()
        {
            Settlement settlement = OpenedSettlement("zone-plant-field");
            CoreMap map = settlement.InteriorMap!;
            FarmingInitiative.Run(map);
            Zone_Growing field = FarmingInitiative.FieldOf(map)!;
            IntVec3 cell = field.Cells[0];
            ThingDef settlementsCrop = field.plantDefToGrow!;
            ThingDef other = new[] { Def("Plant_Potato"), Def("Plant_Rice") }.First(c => c != settlementsCrop);

            MapCommandResult result = MapCommands.SetZonePlant(cell, other.defName);
            Assert.Equal(MapCommandOutcome.Done, result.Outcome);

            for (int i = 0; i < 5; i++) FarmingInitiative.Run(map);

            Assert.Same(other, field.plantDefToGrow);
            Assert.True(field.allowSow);
            Assert.NotEqual(FarmingInitiative.FieldLabel, field.label);
            Assert.NotSame(field, FarmingInitiative.FieldOf(map));
            Assert.NotNull(FarmingInitiative.FieldOf(map));
        }

        [Fact]
        public void A_plant_set_on_the_settlements_herb_garden_is_handed_over_and_sowing_resumes()
        {
            Settlement settlement = OpenedSettlement("zone-plant-garden");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = settlement.Citizens.First(p => p.Spawned && p.Map == map);
            IntVec3 cell = FreeCellNear(map, citizen.Position);
            var garden = new Zone_Growing { label = FarmingInitiative.HerbGardenLabel, plantDefToGrow = Healroot, allowSow = false };
            map.zoneManager.RegisterZone(garden);
            map.zoneManager.AddCell(garden, cell);
            Assert.Same(garden, FarmingInitiative.HerbGardenOf(map));

            MapCommandResult result = MapCommands.SetZonePlant(cell, "Plant_Potato");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Same(Def("Plant_Potato"), garden.plantDefToGrow);
            Assert.Null(FarmingInitiative.HerbGardenOf(map));
            Assert.True(garden.allowSow, "the settlement had paused it for food; a zone handed to the player should be sowing");
        }

        // ---- save and load ----

        [Fact]
        public void A_plant_the_player_set_and_the_label_it_was_handed_over_with_survive_a_save_and_load()
        {
            Settlement settlement = OpenedSettlement("zone-plant-save");
            CoreMap map = settlement.InteriorMap!;
            FarmingInitiative.Run(map);
            Zone_Growing field = FarmingInitiative.FieldOf(map)!;
            IntVec3 cell = field.Cells[0];
            ThingDef other = new[] { Def("Plant_Potato"), Def("Plant_Rice") }.First(c => c != field.plantDefToGrow);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetZonePlant(cell, other.defName).Outcome);

            string xml = Scribe.SaveToString(field, "zone");
            Zone_Growing loaded = Scribe.Load<Zone_Growing>(xml, "zone", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Same(other, loaded.plantDefToGrow);
            Assert.Equal(field.label, loaded.label);
            Assert.NotEqual(FarmingInitiative.FieldLabel, loaded.label);
            Assert.Equal(field.CellCount, loaded.CellCount);
        }
    }
}
