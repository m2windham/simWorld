using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Designations
{
    /// <summary>
    /// The player's designations (<see cref="DesignatorUtility"/>, <see cref="WorkGiver_PlayerDesignated"/>):
    /// each kind is worked before the settlement's own work of that type, an invalid target is refused, a map
    /// with no player marks falls back to the settlement's initiative exactly as before, and the settlement
    /// initiative switch stops the fallback without stopping the player's marks.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PlayerDesignationTests : ContentTestBase
    {
        public PlayerDesignationTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int size = 30) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Thing Spawn(CoreMap map, string def, int x, int z) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Def(def)), new IntVec3(x, 0, z), map);

        private static Pawn Worker(CoreMap map, int x, int z, params WorkTypeDef[] work)
        {
            Pawn p = NewHuman("Worker");
            p.workSettings.EnableAndInitialize();
            p.workSettings.DisableAll();
            foreach (WorkTypeDef w in work) p.workSettings.SetPriority(w, 1);
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map);
            return p;
        }

        private static Pawn Animal(CoreMap map, string race, int x, int z)
        {
            var p = new Pawn(Def(race), race);
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map);
            return p;
        }

        private static Job? NextJob(Pawn p) =>
            WorkGiverScanUtility.TryGiveJobInGivers(p, p.workSettings.WorkGiversInOrderNormal);

        private static void PlayerMark(CoreMap map, DesignationDef def, Thing t) =>
            Assert.Null(DesignatorUtility.TryDesignateThing(def, t, map));

        private static Faction Settlers()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(f);
            return f;
        }

        // ---- Mine ----

        [Fact]
        public void A_player_mine_mark_is_dug_before_a_nearer_settlement_mark()
        {
            CoreMap map = NewMap();
            Pawn miner = Worker(map, 2, 2, WorkTypeDefOf.Mining);
            Thing near = Spawn(map, "Limestone", 4, 2);
            Thing far = Spawn(map, "Limestone", 20, 20);
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(near.Position), DesignationDefOf.Mine));
            Assert.Null(DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, far.Position, map));

            Job? job = NextJob(miner);

            Assert.NotNull(job);
            Assert.Equal(JobDefOf.Mine, job!.def);
            Assert.Same(far, job.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void With_no_player_marks_the_settlement_mark_is_worked_as_before()
        {
            CoreMap map = NewMap();
            Pawn miner = Worker(map, 2, 2, WorkTypeDefOf.Mining);
            Thing rock = Spawn(map, "Limestone", 4, 2);
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(rock.Position), DesignationDefOf.Mine));

            Assert.True(DefDatabase<WorkGiverDef>.GetNamed("MineDesignated").Worker is WorkGiver_MinerDesignated designated
                && designated.ShouldSkip(miner));
            Job? job = NextJob(miner);
            Assert.Same(rock, job!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void An_unreachable_player_mark_falls_back_to_the_settlement_mark()
        {
            CoreMap map = NewMap();
            Pawn miner = Worker(map, 2, 2, WorkTypeDefOf.Mining);
            Thing rock = Spawn(map, "Limestone", 4, 2);
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(rock.Position), DesignationDefOf.Mine));
            // A rock buried in a 3x3 block: designated, but no miner can stand beside it.
            for (int x = 19; x <= 21; x++) for (int z = 19; z <= 21; z++) Spawn(map, "Limestone", x, z);
            Assert.Null(DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, new IntVec3(20, 0, 20), map));

            Assert.Same(rock, NextJob(miner)!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Mine_refuses_a_cell_with_nothing_to_mine_and_claims_a_settlement_mark()
        {
            CoreMap map = NewMap();
            Assert.Equal("nothing here can be mined", DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, new IntVec3(5, 0, 5), map));
            Thing rock = Spawn(map, "Limestone", 6, 6);
            var settlementMark = new Designation(new LocalTargetInfo(rock.Position), DesignationDefOf.Mine);
            map.designationManager.AddDesignation(settlementMark);

            Assert.Null(DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, rock.Position, map));
            Assert.Equal(DesignationSource.Player, settlementMark.source);
            Assert.Single(map.designationManager.AllDesignations);
            Assert.Equal("already designated", DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, rock.Position, map));
        }

        // ---- CutPlant / HarvestPlant ----

        [Fact]
        public void A_marked_wild_plant_is_cut_and_an_unmarked_one_is_left_alone()
        {
            CoreMap map = NewMap();
            Pawn cutter = Worker(map, 2, 2, WorkTypeDefOf.PlantCutting);
            Thing tree = Spawn(map, "Plant_TreePoplar", 10, 10);
            Spawn(map, "Plant_TreePoplar", 4, 4);
            Assert.Null(NextJob(cutter));

            PlayerMark(map, PlayerDesignationDefOf.CutPlant, tree);
            Job? job = NextJob(cutter);

            Assert.Equal(PlantCuttingJobDefOf.CutPlant, job!.def);
            Assert.Same(tree, job.GetTarget(TargetIndex.A).Thing);

            RunTicks(3000, cutter);
            Assert.True(tree.Destroyed);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void Harvest_takes_a_ripe_plant_and_refuses_an_unripe_one()
        {
            CoreMap map = NewMap();
            Pawn grower = Worker(map, 2, 2, WorkTypeDefOf.PlantCutting);
            var ripe = (Plant)Spawn(map, "Plant_Potato", 8, 8);
            ripe.Growth = 1f;
            var green = (Plant)Spawn(map, "Plant_Potato", 9, 9);
            green.Growth = 0.05f;

            Assert.Contains("is not ready to harvest", DesignatorUtility.TryDesignateThing(PlayerDesignationDefOf.HarvestPlant, green, map));
            Assert.Contains("is not a plant", DesignatorUtility.TryDesignateThing(PlayerDesignationDefOf.CutPlant, grower, map));
            PlayerMark(map, PlayerDesignationDefOf.HarvestPlant, ripe);

            Job? job = NextJob(grower);
            Assert.Equal(BuildingJobDefOf.Harvest, job!.def);
            Assert.Same(ripe, job.GetTarget(TargetIndex.A).Thing);

            // Cut replaces harvest on one plant, as in RimWorld.
            PlayerMark(map, PlayerDesignationDefOf.CutPlant, ripe);
            Assert.Null(map.designationManager.DesignationOn(ripe, PlayerDesignationDefOf.HarvestPlant));
            Assert.NotNull(map.designationManager.DesignationOn(ripe, PlayerDesignationDefOf.CutPlant));
        }

        // ---- Hunt ----

        private static Pawn Hunter(CoreMap map, int x, int z)
        {
            Pawn p = Worker(map, x, z, WorkTypeDefOf.Hunting);
            p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Bow_Short")));
            return p;
        }

        [Fact]
        public void A_marked_animal_is_hunted_before_a_nearer_unmarked_one()
        {
            CoreMap map = NewMap();
            Pawn hunter = Hunter(map, 2, 2);
            Animal(map, "Chicken", 5, 2);
            Pawn marked = Animal(map, "Chicken", 22, 22);
            PlayerMark(map, PlayerDesignationDefOf.Hunt, marked);

            Job? job = NextJob(hunter);

            Assert.Equal(HuntingDefOf.Hunt, job!.def);
            Assert.Same(marked, job.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Hunt_refuses_a_person_and_anybodys_livestock()
        {
            CoreMap map = NewMap();
            Pawn person = Worker(map, 3, 3);
            Pawn tame = Animal(map, "Chicken", 5, 5);
            tame.faction = Settlers();

            Assert.Contains("is not an animal", DesignatorUtility.TryDesignateThing(PlayerDesignationDefOf.Hunt, person, map));
            Assert.Contains("is not wild", DesignatorUtility.TryDesignateThing(PlayerDesignationDefOf.Hunt, tame, map));
            Assert.Contains("is not wild", DesignatorUtility.TryDesignateThing(PlayerDesignationDefOf.Tame, tame, map));
            Assert.Empty(map.designationManager.AllDesignations);
        }

        // ---- Tame ----

        [Fact]
        public void A_marked_animal_is_tamed_though_the_settlement_wants_no_livestock()
        {
            CoreMap map = NewMap();
            Pawn handler = Worker(map, 2, 2, WorkTypeDefOf.Handling);
            Pawn chicken = Animal(map, "Chicken", 12, 12);
            // No food anywhere: the settlement's own taming gate is shut.
            Assert.False(TamingInitiative.WantsLivestock(map));
            Assert.Null(NextJob(handler));

            PlayerMark(map, PlayerDesignationDefOf.Tame, chicken);
            Job? job = NextJob(handler);

            Assert.Equal(JobDefOf.Tame, job!.def);
            Assert.Same(chicken, job.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Taming_an_animal_takes_its_mark_away()
        {
            CoreMap map = NewMap();
            Pawn handler = Worker(map, 2, 2, WorkTypeDefOf.Handling);
            handler.faction = Settlers();
            Pawn chicken = Animal(map, "Chicken", 3, 2);
            PlayerMark(map, PlayerDesignationDefOf.Tame, chicken);

            for (int i = 0; i < 50 && chicken.faction == null; i++) TameUtility.TryTame(chicken, handler);

            Assert.NotNull(chicken.faction);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        // ---- Slaughter ----

        [Fact]
        public void A_marked_head_of_livestock_is_slaughtered()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn handler = Worker(map, 2, 2, WorkTypeDefOf.Handling);
            handler.faction = settlers;
            Pawn chicken = Animal(map, "Chicken", 8, 8);
            chicken.faction = settlers;
            Pawn wild = Animal(map, "Chicken", 9, 9);
            Assert.Contains("not the settlement's own", DesignationsRefusal(PlayerDesignationDefOf.Slaughter, wild, map));

            PlayerMark(map, PlayerDesignationDefOf.Slaughter, chicken);
            Assert.Equal(DesignationJobDefOf.Slaughter, NextJob(handler)!.def);

            RunTicks(1500, handler);

            Assert.True(chicken.Dead);
            Assert.True(chicken.health.DiedViolently);
            Assert.False(wild.Dead);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        private static string DesignationsRefusal(DesignationDef def, Thing t, CoreMap map) =>
            DesignatorUtility.CanDesignateThing(def, t, map) ?? "";

        // ---- Deconstruct ----

        [Fact]
        public void A_marked_wall_is_taken_down_for_part_of_its_cost_and_rock_is_refused()
        {
            CoreMap map = NewMap();
            Pawn builder = Worker(map, 2, 2, WorkTypeDefOf.Construction);
            Thing wall = Spawn(map, "Wall", 10, 10);
            Thing rock = Spawn(map, "Limestone", 14, 14);
            List<global::SimWorld.Crafting.ThingDefCountClass> cost = wall.def.CostListAdjusted(wall.Stuff);
            Assert.NotEmpty(cost);

            Assert.Contains("mined, not deconstructed", DesignationsRefusal(PlayerDesignationDefOf.Deconstruct, rock, map));
            PlayerMark(map, PlayerDesignationDefOf.Deconstruct, wall);
            Assert.Equal(DesignationJobDefOf.Deconstruct, NextJob(builder)!.def);

            RunTicks(6000, builder);

            Assert.True(wall.Destroyed);
            Assert.False(rock.Destroyed);
            Assert.Empty(map.designationManager.AllDesignations);
            int refunded = map.listerThings.ThingsOfDef(cost[0].thingDef).Sum(t => t.stackCount);
            Assert.InRange(refunded, 1, cost[0].count - 1);
        }

        // ---- HaulUrgently ----

        [Fact]
        public void A_marked_item_is_hauled_before_a_nearer_unmarked_one()
        {
            CoreMap map = NewMap();
            Pawn hauler = Worker(map, 2, 2, WorkTypeDefOf.Hauling);
            var zone = new Zone_Stockpile();
            zone.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            map.zoneManager.RegisterZone(zone);
            for (int x = 25; x < 28; x++) map.zoneManager.AddCell(zone, new IntVec3(x, 0, 25));
            Spawn(map, "WoodLog", 3, 2);
            Thing marked = Spawn(map, "WoodLog", 15, 15);
            Assert.Contains("cannot be hauled", DesignationsRefusal(PlayerDesignationDefOf.HaulUrgently, Spawn(map, "Wall", 20, 5), map));

            PlayerMark(map, PlayerDesignationDefOf.HaulUrgently, marked);
            Job? job = NextJob(hauler);

            Assert.Equal(JobDefOf.HaulToCell, job!.def);
            Assert.Same(marked, job.GetTarget(TargetIndex.A).Thing);
        }

        // ---- the settlement initiative switch ----

        [Fact]
        public void Mining_initiative_off_withdraws_the_settlements_marks_and_keeps_the_players()
        {
            CoreMap map = NewMap();
            Pawn miner = Worker(map, 2, 2, WorkTypeDefOf.Mining);
            Thing settlementRock = Spawn(map, "Limestone", 4, 2);
            Thing playerRock = Spawn(map, "Limestone", 20, 20);
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(settlementRock.Position), DesignationDefOf.Mine));
            Assert.Null(DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, playerRock.Position, map));

            map.designationManager.SetSettlementInitiative(DesignationDefOf.Mine, false);
            var settlement = new SimWorld.World.Settlement(SimWorld.World.WorldObjectDefOf.Settlement, 0, null, "Quarry", 0);
            MiningInitiative.Run(settlement, map);

            Designation only = Assert.Single(map.designationManager.AllDesignations);
            Assert.Equal(DesignationSource.Player, only.source);
            Assert.True(DefDatabase<WorkGiverDef>.GetNamed("Mine").Worker is WorkGiver_Miner settlementMiner
                && settlementMiner.ShouldSkip(miner));
            Assert.Same(playerRock, NextJob(miner)!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Hunting_initiative_off_hunts_only_the_marked_animal()
        {
            CoreMap map = NewMap();
            Pawn hunter = Hunter(map, 2, 2);
            Pawn unmarked = Animal(map, "Chicken", 5, 2);
            Assert.Same(unmarked, NextJob(hunter)!.GetTarget(TargetIndex.A).Thing);

            map.designationManager.SetSettlementInitiative(PlayerDesignationDefOf.Hunt, false);
            Assert.Null(NextJob(hunter));

            Pawn marked = Animal(map, "Chicken", 22, 22);
            PlayerMark(map, PlayerDesignationDefOf.Hunt, marked);
            Assert.Same(marked, NextJob(hunter)!.GetTarget(TargetIndex.A).Thing);
        }

        [Fact]
        public void Taming_and_cutting_initiatives_off_stop_their_settlement_givers()
        {
            CoreMap map = NewMap();
            Pawn worker = Worker(map, 2, 2, WorkTypeDefOf.Handling, WorkTypeDefOf.PlantCutting);
            var tame = (WorkGiver_TameAnimals)DefDatabase<WorkGiverDef>.GetNamed("TameAnimals").Worker;
            var cut = (WorkGiver_PlantsCut)DefDatabase<WorkGiverDef>.GetNamed("PlantsCut").Worker;
            Assert.False(cut.ShouldSkip(worker));

            map.designationManager.SetSettlementInitiative(PlayerDesignationDefOf.Tame, false);
            map.designationManager.SetSettlementInitiative(PlayerDesignationDefOf.CutPlant, false);

            Assert.True(tame.ShouldSkip(worker));
            Assert.True(cut.ShouldSkip(worker));
            Thing tree = Spawn(map, "Plant_TreePoplar", 10, 10);
            PlayerMark(map, PlayerDesignationDefOf.CutPlant, tree);
            Assert.Same(tree, NextJob(worker)!.GetTarget(TargetIndex.A).Thing);
        }

        // ---- housekeeping and save ----

        [Fact]
        public void A_mark_leaves_with_the_thing_it_was_on()
        {
            CoreMap map = NewMap();
            Thing log = Spawn(map, "WoodLog", 5, 5);
            PlayerMark(map, PlayerDesignationDefOf.HaulUrgently, log);
            log.DeSpawn();
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void Player_marks_and_initiative_switches_survive_a_scribe_round_trip()
        {
            CoreMap map = NewMap();
            Thing rock = Spawn(map, "Limestone", 4, 4);
            Thing settlementRock = Spawn(map, "Limestone", 8, 4);
            Thing tree = Spawn(map, "Plant_TreePoplar", 10, 10);
            Pawn chicken = Animal(map, "Chicken", 12, 12);
            Assert.Null(DesignatorUtility.TryDesignateCell(DesignationDefOf.Mine, rock.Position, map));
            map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(settlementRock.Position), DesignationDefOf.Mine));
            PlayerMark(map, PlayerDesignationDefOf.CutPlant, tree);
            PlayerMark(map, PlayerDesignationDefOf.Hunt, chicken);
            map.designationManager.SetSettlementInitiative(PlayerDesignationDefOf.Hunt, false);
            map.designationManager.SetSettlementInitiative(DesignationDefOf.Mine, false);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            DesignationManager dm = loaded.designationManager;
            Assert.Equal(4, dm.AllDesignations.Count);
            Assert.Equal(DesignationSource.Player, dm.DesignationAt(rock.Position, DesignationDefOf.Mine)!.source);
            Assert.Equal(DesignationSource.Settlement, dm.DesignationAt(settlementRock.Position, DesignationDefOf.Mine)!.source);
            Designation cut = Assert.Single(dm.SpawnedDesignationsOfDef(PlayerDesignationDefOf.CutPlant));
            Assert.Equal(tree.thingIDNumber, cut.target.Thing!.thingIDNumber);
            Assert.Same(loaded, cut.target.Thing.Map);
            Designation hunt = Assert.Single(dm.SpawnedDesignationsOfDef(PlayerDesignationDefOf.Hunt));
            Assert.Equal(DesignationSource.Player, hunt.source);
            Assert.False(dm.SettlementInitiativeOn(PlayerDesignationDefOf.Hunt));
            Assert.False(dm.SettlementInitiativeOn(DesignationDefOf.Mine));
            Assert.True(dm.SettlementInitiativeOn(PlayerDesignationDefOf.Tame));

            // A map nobody designated saves without the switch element at all.
            Assert.DoesNotContain("settlementInitiativeOff", Scribe.SaveToString(NewMap(), "map"));
        }
    }
}
