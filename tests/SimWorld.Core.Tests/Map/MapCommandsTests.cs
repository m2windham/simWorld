using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
    /// The settlement-scale command surface: the player's own door into the same blueprint/zone machinery
    /// <c>Building.SettlementConstructionInitiative</c>, <c>Building.FarmingInitiative</c> and
    /// <c>Economy.SettlementStockInitiative</c> already drive. Before <see cref="MapCommands"/> existed, the
    /// settlement view had no way to write at all — <c>Map/View</c> was read-only, and every Blueprint, zone
    /// and stockpile in the game was an AI decision. <see cref="A_citizen_builds_what_the_player_designated"/>
    /// is the test that proves the gap is closed: it fails against the code this lane started from (the
    /// command does not exist) and passes once a citizen, driven through the real per-tick job loop, finishes
    /// a wall the player — not the settlement's own initiative — asked for.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsTests : ContentTestBase
    {
        public MapCommandsTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        /// <summary>Founds a settlement and opens it — attention <b>and</b> a real, generated interior — the
        /// same two-step every other test in this codebase that needs a live map goes through
        /// (<c>OpenSettlementTests</c>, <c>MapViewIntegrationTests</c>). <see cref="MapCommands"/> has no
        /// other way to reach a map: it resolves the same way <see cref="MapViewSnapshot.Capture()"/> does,
        /// off <c>Find.God.Attention.FocusedSettlement</c>, so a hand-built map with no owning
        /// <see cref="Settlement"/> is not a map this class can ever see.</summary>
        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static Pawn AnyCitizenOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.First(p => p.Spawned && p.Map == map);

        private static List<Pawn> AllCitizensOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();

        /// <summary>The first cell spiralling out from <paramref name="near"/> that could actually take
        /// <paramref name="entityDef"/> — the same physical check <c>GenConstruct</c> itself applies, walked
        /// deterministically (nearest first) rather than by the initiative's own random sampling, since a
        /// test wants one predictable answer, not "eventually, probably".</summary>
        private static IntVec3 FindPlaceableCell(CoreMap map, ThingDef entityDef, IntVec3 near)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = near + pattern[i];
                if (GenGrid.InBounds(candidate, map) && GenConstruct.CanPlaceBlueprintAt(entityDef, candidate, map, out _))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("No cell near " + near + " could take " + entityDef.defName + ".");
        }

        private static IntVec3 FindFreeCellFarFrom(CoreMap map, IntVec3 far)
        {
            var target = new IntVec3(
                Math.Clamp(far.x, 0, map.Size.x - 1),
                0,
                Math.Clamp(far.z, 0, map.Size.z - 1));
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = target + pattern[i];
                if (!GenGrid.InBounds(candidate, map)) continue;
                if (!GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue;
                if (map.edificeGrid[candidate] != null) continue;
                return candidate;
            }
            throw new InvalidOperationException("No free cell found near " + target + ".");
        }

        // -------------------------------------------------------------------------------------------
        // Placing a designation creates what the AI initiative would have created.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Placing_a_blueprint_creates_the_same_kind_of_thing_the_settlement_initiative_would()
        {
            Settlement settlement = OpenedSettlement("place-wall-blueprint");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            ThingDef wall = Def("Wall");
            IntVec3 site = FindPlaceableCell(map, wall, citizen.Position);

            MapCommandResult result = MapCommands.PlaceBlueprint("Wall", site);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);

            // Same defName GenConstruct.BlueprintDefFor(Wall) names, same cell — exactly what
            // SettlementConstructionInitiative.PlaceBlueprint would have spawned for this def and cell.
            Blueprint? spawned = map.thingGrid.ThingsListAt(site).OfType<Blueprint>().FirstOrDefault();
            Assert.NotNull(spawned);
            Assert.Same(wall, spawned!.EntityToBuild);
            Assert.Equal(site, spawned.Position);
            Assert.Equal(GenConstruct.BlueprintDefFor(wall), spawned.def);
        }

        // -------------------------------------------------------------------------------------------
        // The headline: a citizen actually builds what the player designated.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The proof that a player's decision reaches the world. Nothing here calls a worker or a job driver
        /// directly — it places a Blueprint the way a host would (defName + cell, through
        /// <see cref="MapCommands"/> alone) and then drives the real tick loop
        /// (<c>Game.WireTickHooks</c>'s own pipeline, via <see cref="ContentTestBase.RunTicks"/>) until a
        /// citizen has hauled materials to it and finished it. Before <see cref="MapCommands"/> existed this
        /// failed at the first line: there was no way to place a player Blueprint at all.
        /// </summary>
        [Fact]
        public void A_citizen_builds_what_the_player_designated()
        {
            Settlement settlement = OpenedSettlement("player-designates-a-wall");
            CoreMap map = settlement.InteriorMap!;

            Pawn builder = AnyCitizenOn(settlement, map);
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20; // deterministic success, as BuildingTests does

            ThingDef wall = Def("Wall");
            IntVec3 site = FindPlaceableCell(map, wall, builder.Position);

            // Materials right where a builder already stands, so nothing here depends on how far the
            // generated map happens to put wood from the site — only on the command and the job loop.
            Thing logs = ThingMaker.MakeThing(Def("WoodLog"));
            logs.stackCount = 10;
            GenSpawn.Spawn(logs, builder.Position, map);

            Assert.Null(map.edificeGrid[site]);

            MapCommandResult placed = MapCommands.PlaceBlueprint("Wall", site);
            Assert.Equal(MapCommandOutcome.Done, placed.Outcome);

            RunTicks(6000, AllCitizensOn(settlement, map).ToArray());

            Thing? built = map.edificeGrid[site];
            Assert.NotNull(built);
            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.Equal("Wall", built!.def.defName);
        }

        // -------------------------------------------------------------------------------------------
        // A growing zone the player marks gets sown.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_growing_zone_the_player_marks_gets_sown()
        {
            Settlement settlement = OpenedSettlement("player-marks-a-field");
            CoreMap map = settlement.InteriorMap!;

            Pawn farmer = AnyCitizenOn(settlement, map);
            farmer.skills!.GetSkill(SkillDefOf.Plants)!.Level = 20;
            // Isolate this test from the rest of the settlement's think tree (hunting, hauling, whatever a
            // freshly founded band's other 19 citizens happen to prioritize): this one citizen wants nothing
            // but Growing, so if the command wired the zone correctly, sowing is the only job left to do.
            farmer.workSettings!.DisableAll();
            farmer.workSettings!.SetPriority(WorkTypeDefOf.Growing, 1);

            ThingDef potato = Def("Plant_Potato");
            // Right where the farmer already stands: trivially reachable, so the test proves the command
            // wires into WorkGiver_GrowerSow rather than proving anything about pathing.
            IntVec3 cell = farmer.Position;
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);

            MapCommandResult marked = MapCommands.MarkGrowingZone("Plant_Potato", new[] { cell });
            Assert.Equal(MapCommandOutcome.Done, marked.Outcome);
            Assert.IsType<Zone_Growing>(map.zoneManager.ZoneAt(cell));

            // Growing (the work type) covers both sowing and harvesting, and WorkGiver_GrowerHarvest looks at
            // every mature Plant on the whole map, zone or not. A generated interior ships hundreds of wild
            // ones, so an isolated Growing-only pawn keeps finding a wild harvest before it ever reaches the
            // one cell this test cares about. Clearing the map's existing wild growth removes that
            // competition without touching anything about how the command itself is tested.
            foreach (Thing wild in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).ToList()) wild.Destroy();

            RunTicks(3000, farmer);

            Thing? plant = map.thingGrid.ThingsListAt(cell).FirstOrDefault(t => t.def == potato);
            Assert.NotNull(plant);
            Assert.IsType<Plant>(plant);
        }

        // -------------------------------------------------------------------------------------------
        // A stockpile the player marks is immediately real storage, on equal footing with the granary.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_stockpile_the_player_marks_accepts_goods_like_any_other()
        {
            Settlement settlement = OpenedSettlement("player-marks-a-stockpile");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 cell = FindFreeCellFarFrom(map, citizen.Position);

            MapCommandResult marked = MapCommands.MarkStockpile(new[] { cell });

            Assert.Equal(MapCommandOutcome.Done, marked.Outcome);
            var stockpile = Assert.IsType<Zone_Stockpile>(map.zoneManager.ZoneAt(cell));
            // Accepts goods, with the same default EnsureGranary gives the AI's own granary — a player
            // stockpile is not a narrower kind of storage. (Neither takes bodies: Economy/SettlementDeadTests.)
            Assert.True(stockpile.filter.Allows(Def("WoodLog")));
        }

        // -------------------------------------------------------------------------------------------
        // The design rule: a bad decision must be allowed to be bad.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Pins the rule the whole class doc is built around. Sand carries real fertility (0.6) — enough for
        /// rice's 0.4 floor, so this is not "cannot be grown at all" — but far below ordinary Soil (1.4) or
        /// SoilRich (2.8): a field sown here grows more slowly and yields worse for the whole time it stands,
        /// exactly the "sown and yields badly" case CLAUDE's own brief names. Nothing about that makes it
        /// refused.
        /// </summary>
        [Fact]
        public void A_field_on_poor_but_sowable_soil_is_accepted_not_refused()
        {
            Settlement settlement = OpenedSettlement("player-sows-poor-ground");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 cell = FindFreeCellFarFrom(map, citizen.Position);
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Sand);

            ThingDef rice = Def("Plant_Rice");
            Assert.True(TerrainDefOf.Sand.fertility >= rice.plant!.sowMinFertility,
                "test setup assumption broken: Sand should still meet rice's sowMinFertility");
            Assert.True(TerrainDefOf.Sand.fertility < TerrainDefOf.Soil.fertility,
                "test setup assumption broken: Sand should read as poorer than ordinary Soil");

            MapCommandResult result = MapCommands.MarkGrowingZone("Plant_Rice", new[] { cell });

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
        }

        /// <summary>A wall far from every citizen, with nothing around it, is a useless placement by any
        /// player-judgement standard — and it is legal, so it is built exactly like any other.</summary>
        [Fact]
        public void A_wall_placed_nowhere_useful_is_accepted_not_refused()
        {
            Settlement settlement = OpenedSettlement("player-builds-a-folly");
            CoreMap map = settlement.InteriorMap!;
            var farCorner = new IntVec3(map.Size.x - 3, 0, map.Size.z - 3);
            ThingDef wall = Def("Wall");
            IntVec3 site = FindPlaceableCell(map, wall, farCorner);

            MapCommandResult result = MapCommands.PlaceBlueprint("Wall", site);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
        }

        // -------------------------------------------------------------------------------------------
        // The physically impossible, refused with the right named outcome.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void No_game_running_is_refused_as_no_map()
        {
            // ContentTestBase leaves Find.CurrentGame null; nothing here starts a game at all.
            MapCommandResult result = MapCommands.PlaceBlueprint("Wall", new IntVec3(1, 0, 1));

            Assert.Equal(MapCommandOutcome.NoMap, result.Outcome);
            Assert.False(result.Changed);
        }

        [Fact]
        public void An_unopened_settlement_is_refused_as_no_map()
        {
            // Game.NewGame focuses the founding settlement but never generates its interior — the same state
            // OpenSettlementTests.FocusingAloneLeavesTheSettlementWithNoInterior pins.
            Game game = NewSoloGame("focused-but-unopened");
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Null(settlement.InteriorMap);

            MapCommandResult result = MapCommands.MarkStockpile(new[] { new IntVec3(1, 0, 1) });

            Assert.Equal(MapCommandOutcome.NoMap, result.Outcome);
        }

        [Fact]
        public void An_off_map_cell_is_refused()
        {
            Settlement settlement = OpenedSettlement("off-map-cell");
            CoreMap map = settlement.InteriorMap!;
            var offMap = new IntVec3(map.Size.x + 5, 0, map.Size.z + 5);

            MapCommandResult result = MapCommands.PlaceBlueprint("Wall", offMap);

            Assert.Equal(MapCommandOutcome.OffMap, result.Outcome);
        }

        [Fact]
        public void An_already_occupied_cell_is_refused()
        {
            Settlement settlement = OpenedSettlement("occupied-cell");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            ThingDef wall = Def("Wall");
            IntVec3 site = FindPlaceableCell(map, wall, citizen.Position);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Wall", site).Outcome);
            MapCommandResult second = MapCommands.PlaceBlueprint("Wall", site);

            Assert.Equal(MapCommandOutcome.Occupied, second.Outcome);
        }

        [Fact]
        public void An_unknown_def_is_refused_for_a_blueprint_and_for_a_crop()
        {
            Settlement settlement = OpenedSettlement("unknown-defs");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            MapCommandResult blueprintResult = MapCommands.PlaceBlueprint("NoSuchThingDefAtAll", citizen.Position);
            Assert.Equal(MapCommandOutcome.UnknownDef, blueprintResult.Outcome);

            MapCommandResult zoneResult = MapCommands.MarkGrowingZone("NoSuchCropAtAll", new[] { citizen.Position });
            Assert.Equal(MapCommandOutcome.UnknownDef, zoneResult.Outcome);
        }

        [Fact]
        public void A_def_with_no_plant_properties_cannot_be_marked_as_a_crop()
        {
            Settlement settlement = OpenedSettlement("not-a-plant");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);

            // WoodLog is a real, loaded ThingDef with no PlantProperties at all — physically impossible to
            // grow, which is the one crop-side refusal this class makes (see the class doc).
            MapCommandResult result = MapCommands.MarkGrowingZone("WoodLog", new[] { citizen.Position });

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void Zone_commands_refuse_a_cell_that_already_belongs_to_another_zone()
        {
            Settlement settlement = OpenedSettlement("zone-collision");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 cell = FindFreeCellFarFrom(map, citizen.Position);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { cell }).Outcome);

            MapCommandResult result = MapCommands.MarkGrowingZone("Plant_Potato", new[] { cell });

            Assert.Equal(MapCommandOutcome.Occupied, result.Outcome);
            // All-or-nothing: the failed cell must not have been half-claimed by a new zone either.
            Assert.IsType<Zone_Stockpile>(map.zoneManager.ZoneAt(cell));
        }

        // -------------------------------------------------------------------------------------------
        // Cancelling a designation.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Cancelling_a_designation_removes_the_blueprint()
        {
            Settlement settlement = OpenedSettlement("cancel-a-blueprint");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            ThingDef wall = Def("Wall");
            IntVec3 site = FindPlaceableCell(map, wall, citizen.Position);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Wall", site).Outcome);

            MapCommandResult result = MapCommands.CancelDesignation(site);

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.Empty(map.thingGrid.ThingsListAt(site).OfType<Blueprint>());
        }

        [Fact]
        public void Cancelling_an_empty_cell_is_a_no_op()
        {
            Settlement settlement = OpenedSettlement("cancel-nothing-there");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 cell = FindFreeCellFarFrom(map, citizen.Position);

            MapCommandResult result = MapCommands.CancelDesignation(cell);

            Assert.Equal(MapCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
        }

        // -------------------------------------------------------------------------------------------
        // The seam: no Def, no live object, ever — mirrors God.GodViewSeamIntegrityTests for this surface.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// <c>Tests.Map.MapViewTests</c> already walks every public type in the whole <c>SimWorld.Map.View</c>
        /// namespace (so it covers <see cref="MapCommandResult"/>/<see cref="MapCommandOutcome"/> too, without
        /// this test file changing it), but it does not check for a live <see cref="Settlement"/> or
        /// <see cref="global::SimWorld.World.World"/> the way <c>God.GodViewSeamIntegrityTests</c> does for the god seam. This
        /// closes that one gap for the command surface specifically, as its own file rather than an edit to
        /// that shared one.
        /// </summary>
        [Fact]
        public void MapCommandResult_hands_out_no_def_and_no_live_object()
        {
            var forbidden = new (Type Type, string Why)[]
            {
                (typeof(Def), "a Def — the host could reach its workers through it"),
                (typeof(Thing), "a live Thing — that is a write surface into the simulation"),
                (typeof(Pawn), "a live Pawn"),
                (typeof(CoreMap), "the Map itself"),
                (typeof(Settlement), "a live Settlement"),
                (typeof(global::SimWorld.World.World), "the World itself"),
            };

            Type[] types = { typeof(MapCommandResult), typeof(MapCommandOutcome) };
            foreach (Type type in types)
            {
                if (type.IsEnum) continue;
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    foreach ((Type bad, string why) in forbidden)
                    {
                        Assert.False(bad.IsAssignableFrom(property.PropertyType),
                            type.Name + "." + property.Name + " exposes " + why);
                    }
                }
            }
        }
    }
}
