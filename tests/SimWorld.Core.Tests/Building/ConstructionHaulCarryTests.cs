using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
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

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Construction materials are carried, not deleted (<see cref="JobDriver_HaulToBuildingSite"/> through
    /// <see cref="Pawn_CarryTracker"/>). The defect, from the player's side: a wall costs five logs, the
    /// settlement owns five, a citizen picks them up and is pulled away before delivering them — and the logs
    /// are gone, so the wall can never be finished and nothing says why.
    /// <para/>
    /// Every assertion here is conservation, not a coordinate: whatever was picked up is either in the Frame,
    /// or lying on the map, or in a pawn's hands — the total never drops. Where a test needs a hauler caught
    /// between pickup and delivery it finds that moment the way a player would see it — logs gone from the
    /// ground and not yet at the site — rather than through anything only the fixed code has.
    /// </summary>
    public class ConstructionHaulCarryTests : ContentTestBase
    {
        public ConstructionHaulCarryTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        public enum Interruption
        {
            /// <summary>Knocked down mid-carry — by a raid, a fall, anything (<see cref="Pawn.Notify_Downed"/>).</summary>
            Downed,

            /// <summary>Killed mid-carry (<see cref="Pawn.Notify_Died"/>).</summary>
            Killed,

            /// <summary>The player orders the hauler somewhere else (the same call
            /// <c>MapCommands.OrderJob</c> makes: a player-forced job that pre-empts the one in hand).</summary>
            OrderedAway,

            /// <summary>The player cancels the wall while its logs are on their way (the haul's own
            /// fail-on-despawned site check ends it).</summary>
            SiteCancelled,
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wood => Def("WoodLog");

        private static readonly IntVec3 LogsCell = new IntVec3(1, 0, 1);
        private static readonly IntVec3 SiteCell = new IntVec3(9, 0, 9);

        /// <summary>A builder who never fails a construction attempt (see <see cref="JobDriver_ConstructFinishFrame"/>'s
        /// curve), a stack of logs beside them and a wall blueprint across the map — far enough that there is a
        /// walk between pickup and delivery for something to interrupt.</summary>
        private static (CoreMap map, Pawn builder, Thing blueprint) WallSite(int logs)
        {
            CoreMap map = NewMap(12, 12);
            Pawn builder = NewHuman("Builder");
            GenSpawn.Spawn(builder, new IntVec3(0, 0, 0), map);
            builder.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;

            Thing stack = ThingMaker.MakeThing(Wood);
            stack.stackCount = logs;
            GenSpawn.Spawn(stack, LogsCell, map);

            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, SiteCell, map);
            return (map, builder, blueprint);
        }

        private static int LooseWood(CoreMap map) =>
            map.listerThings.ThingsOfDef(Wood).Where(t => t.Spawned).Sum(t => t.stackCount);

        private static int WoodAt(CoreMap map, IntVec3 site)
        {
            Thing? here = map.edificeGrid[site];
            if (here is Frame frame) return frame.MaterialDelivered(Wood);
            if (here is global::SimWorld.Building.Building built && built.def.defName == "Wall") return WallCost;
            return 0;
        }

        private static int WallCost => Def("Wall").CostListCountFor(Def("WoodLog"));

        /// <summary>Ticks until some logs have left the ground without reaching the site yet — the hauler has
        /// them in hand — and fails the test if that never happens.</summary>
        private static void RunUntilLogsAreInHand(CoreMap map, IntVec3 site, int logsOnMap, int maxTicks = 2000)
        {
            TickManager tm = Find.TickManager;
            for (int i = 0; i < maxTicks; i++)
            {
                if (LooseWood(map) + WoodAt(map, site) < logsOnMap) return;
                tm.DoSingleTick();
            }
            Assert.Fail("No hauler ever picked the logs up within " + maxTicks + " ticks.");
        }

        private static void Interrupt(Pawn hauler, Interruption how, Thing blueprint)
        {
            switch (how)
            {
                case Interruption.Downed:
                    hauler.health.ForceDowned = true;
                    break;
                case Interruption.Killed:
                    hauler.health.Kill(null, null);
                    break;
                case Interruption.OrderedAway:
                    hauler.jobs.StartJob(new Job(DutyJobDefOf.Goto, new IntVec3(0, 0, 11)) { playerForced = true }, JobCondition.InterruptForced);
                    break;
                case Interruption.SiteCancelled:
                    blueprint.Destroy();
                    Find.TickManager.DoSingleTick(); // the haul notices its site is gone on its next tick
                    break;
            }
        }

        // ---- the defect ----

        /// <summary>
        /// The headline. A stack equal to the wall's cost is carried whole (the Thing itself leaves the map); a
        /// bigger one is split (a new Thing of the carried count). Either way, and however the hauler is pulled
        /// away, every log that left the ground is back on it. Before the carry tracker, each of these lost the
        /// whole load.
        /// </summary>
        [Theory]
        [InlineData(Interruption.Downed, 5)]
        [InlineData(Interruption.Downed, 8)]
        [InlineData(Interruption.Killed, 8)]
        [InlineData(Interruption.OrderedAway, 5)]
        [InlineData(Interruption.OrderedAway, 8)]
        [InlineData(Interruption.SiteCancelled, 8)]
        public void An_interrupted_haul_leaves_every_log_it_picked_up_on_the_map(Interruption how, int logs)
        {
            (CoreMap map, Pawn builder, Thing blueprint) = WallSite(logs);

            RunUntilLogsAreInHand(map, SiteCell, logs);
            Assert.Equal(JobDefOf.HaulToBuildingSite, builder.jobs.curJob?.def);
            Assert.Equal(0, WoodAt(map, SiteCell)); // caught between pickup and delivery, not after

            Interrupt(builder, how, blueprint);

            Assert.NotEqual(JobDefOf.HaulToBuildingSite, builder.jobs.curJob?.def);
            Assert.Null(builder.carryTracker.CarriedThing);
            Assert.Equal(logs, LooseWood(map) + WoodAt(map, SiteCell));
        }

        /// <summary>
        /// The ordinary case must not have changed: a haul nobody interrupts delivers exactly what the Frame
        /// needs, the wall is built, the rest of the stack stays where it was, and nobody is left holding
        /// anything.
        /// </summary>
        [Fact]
        public void An_uninterrupted_haul_delivers_exactly_what_the_wall_needs_and_leaves_the_rest()
        {
            (CoreMap map, Pawn builder, _) = WallSite(8);

            RunTicks(600, builder);

            Thing? built = map.edificeGrid[SiteCell];
            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.Equal("Wall", built!.def.defName);
            Assert.Equal(8 - WallCost, LooseWood(map));
            Assert.Null(builder.carryTracker.CarriedThing);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
        }

        // ---- the player's case ----

        /// <summary>
        /// The case from the player's side, through the real command surface and a real founded settlement: a
        /// wall designated with <see cref="MapCommands.PlaceBlueprint"/>, exactly its cost in logs and not one
        /// more on the whole map, and the player ordering the citizen who picked them up somewhere else before
        /// they are delivered. The wall still gets built.
        /// <para/>
        /// Two things are held fixed so this measures the carry and nothing else. Every citizen builds at skill
        /// 20, because a failed attempt refunds only half of what the Frame held (<see cref="Frame.FailConstruction"/>,
        /// RimWorld's own cost) and with no spare logs one failure makes the wall unfinishable whatever the
        /// hauling does. And the interruption is the player's own order, because left alone one is rare: over
        /// 201 tick streams of <c>MapCommandsTests</c>' own wall with 10 logs, no haul was interrupted
        /// mid-carry, and with 40 logs two were, both late in the run.
        /// </summary>
        [Fact]
        public void A_player_designated_wall_is_built_from_exactly_its_cost_though_its_hauler_is_ordered_away()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "player-designates-a-wall", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            Pawn[] citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToArray();
            foreach (Pawn citizen in citizens) citizen.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;

            foreach (Thing log in map.listerThings.ThingsOfDef(Wood).ToList()) log.Destroy();
            Pawn builder = citizens[0];
            ThingDef wall = Def("Wall");
            IntVec3 site = GenRadial.RadialPattern.Select(o => builder.Position + o)
                .First(c => GenGrid.InBounds(c, map) && GenConstruct.CanPlaceBlueprintAt(wall, c, map, out _));
            Thing logs = ThingMaker.MakeThing(Wood);
            logs.stackCount = WallCost;
            GenSpawn.Spawn(logs, builder.Position, map);
            Assert.Equal(WallCost, LooseWood(map));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Wall", site).Outcome);
            foreach (Pawn citizen in citizens) Find.TickManager.RegisterAllTickabilityFor(citizen);

            RunUntilLogsAreInHand(map, site, WallCost);
            Pawn hauler = citizens.Single(p => p.jobs.curJob?.def == JobDefOf.HaulToBuildingSite);
            IntVec3 elsewhere = GenRadial.RadialPattern.Skip(80).Select(o => hauler.Position + o)
                .First(c => GenGrid.InBounds(c, map) && GenGrid.Standable(c, map));
            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(hauler.thingIDNumber, "Goto", elsewhere).Outcome);
            Assert.Equal(WallCost, LooseWood(map) + WoodAt(map, site));

            RunTicks(6000, citizens);

            Thing? built = map.edificeGrid[site];
            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.Equal("Wall", built!.def.defName);
        }

        // ---- Scribe ----

        /// <summary>
        /// A save taken mid-carry keeps the load. The logs are off the map, so the hauler is the only place the
        /// save can find them: they come back in the loaded pawn's hands, the loaded job's target is that very
        /// Thing again, and the hauler goes on to deliver them — nothing lost across the save, and nothing
        /// counted twice.
        /// </summary>
        [Fact]
        public void A_hauler_saved_mid_carry_still_holds_the_logs_after_loading_and_delivers_them()
        {
            (CoreMap map, Pawn builder, _) = WallSite(8);
            RunUntilLogsAreInHand(map, SiteCell, 8);
            Thing carried = builder.carryTracker.CarriedThing!;
            Assert.NotNull(carried);
            Assert.Equal(WallCost, carried.stackCount);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedBuilder = loaded.mapPawns.AllPawns.Single();
            Thing? loadedCarried = loadedBuilder.carryTracker.CarriedThing;
            Assert.NotNull(loadedCarried);
            Assert.Same(Wood, loadedCarried!.def);
            Assert.Equal(WallCost, loadedCarried.stackCount);
            Assert.False(loadedCarried.Spawned);
            Assert.Equal(JobDefOf.HaulToBuildingSite, loadedBuilder.jobs.curJob?.def);
            Assert.Same(loadedCarried, loadedBuilder.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(8, LooseWood(loaded) + loadedCarried.stackCount);

            RunTicks(600, loadedBuilder);

            Assert.IsType<global::SimWorld.Building.Building>(loaded.edificeGrid[SiteCell]);
            Assert.Equal(8 - WallCost, LooseWood(loaded));
            Assert.Null(loadedBuilder.carryTracker.CarriedThing);
        }
    }
}
