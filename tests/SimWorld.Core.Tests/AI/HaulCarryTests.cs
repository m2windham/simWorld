using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Stockpile hauling carries what it picks up, and does not delete it
    /// (<see cref="JobDriver_HaulToCell"/> through <see cref="Pawn_CarryTracker"/>). The defect, from the
    /// player's side: a pile of logs bigger than one stockpile cell holds is hauled in two trips, the hauler
    /// takes a full cell's worth off the pile, is knocked down on the way, and that cell's worth is simply gone
    /// — 80 logs on the map become 5. Before the carry tracker the split-off count existed only as a number on
    /// the job, so nothing was left to put down.
    /// <para/>
    /// Every assertion here is conservation, not a coordinate: whatever a hauler picked up is either lying on
    /// the map, in a stockpile cell, or in the hauler's hands, and the total never drops. Where a test needs a
    /// hauler caught between pickup and delivery it finds that moment the way a player would see it — logs gone
    /// from the map and not yet delivered — rather than through anything only the fixed code has.
    /// </summary>
    public class HaulCarryTests : ContentTestBase
    {
        public HaulCarryTests(CoreContentFixture content) : base(content)
        {
        }

        public enum Interruption
        {
            /// <summary>Knocked down mid-carry — by a raid, a fall, anything (<see cref="Pawn.Notify_Downed"/>).</summary>
            Downed,

            /// <summary>Killed mid-carry (<see cref="Pawn.Notify_Died"/>).</summary>
            Killed,

            /// <summary>The player orders the hauler somewhere else (the call <c>MapCommands.OrderJob</c> makes:
            /// a player-forced job that pre-empts the one in hand).</summary>
            OrderedAway,

            /// <summary>The job is ended from outside — a threat, a tier change, anything that comes through
            /// <see cref="Pawn_JobTracker.EndCurrentJob"/> without going through the pawn's health.</summary>
            JobEnded,
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wood => Def("WoodLog");

        /// <summary>One stockpile cell holds this many logs; asked of the def so the test does not repeat it.</summary>
        private static int StackLimit => Wood.stackLimit;

        private static readonly IntVec3 HaulerStart = new IntVec3(0, 0, 0);
        private static readonly IntVec3 LogsCell = new IntVec3(9, 0, 9);
        private static readonly IntVec3 StockCell = new IntVec3(1, 0, 1);
        private static readonly IntVec3 SecondStockCell = new IntVec3(2, 0, 1);

        private static Thing SpawnLogs(CoreMap map, IntVec3 cell, int count)
        {
            Thing stack = ThingMaker.MakeThing(Wood);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, cell, map);
            return stack;
        }

        private static Zone_Stockpile NewStockpile(CoreMap map, ThingDef allowed, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetAllow(allowed, true);
            return zone;
        }

        /// <summary>A hauler, a pile of <paramref name="logs"/> across the map and a stockpile for logs — far
        /// enough apart that there is a walk between pickup and delivery for something to interrupt.</summary>
        private static (CoreMap map, Pawn hauler) HaulSite(int logs, params IntVec3[] stockpileCells)
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, HaulerStart, map);
            SpawnLogs(map, LogsCell, logs);
            NewStockpile(map, Wood, stockpileCells.Length > 0 ? stockpileCells : new[] { StockCell });
            return (map, hauler);
        }

        /// <summary>Every log lying on a map cell, wherever it is. Not what a hauler holds.</summary>
        private static int LogsOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Wood).Where(t => t.Spawned).Sum(t => t.stackCount);

        private static int LogsHeldBy(Pawn pawn) =>
            pawn.carryTracker.CarriedThing is { } held && held.def == Wood ? held.stackCount : 0;

        /// <summary>Ticks until some logs have left the map without a stockpile having them yet — the hauler
        /// has them in hand — and fails the test if that never happens. Returns how many are in hand, worked
        /// out from the map alone.</summary>
        private static int RunUntilLogsAreInHand(CoreMap map, Pawn hauler, int total, int maxTicks = 2000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                int inHand = total - LogsOnMap(map);
                if (inHand > 0)
                {
                    Assert.Equal(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
                    return inHand;
                }
                RunTicks(1, hauler);
            }
            Assert.Fail("No hauler ever picked the logs up within " + maxTicks + " ticks.");
            return 0;
        }

        private static void Interrupt(Pawn hauler, Interruption how)
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
                case Interruption.JobEnded:
                    hauler.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    break;
            }
        }

        // ---- the defect ----

        /// <summary>
        /// The headline. A pile that fits one cell is carried whole (the Thing itself leaves the map); a pile
        /// bigger than a cell is split (a new Thing of the count that fits). Either way, and however the hauler
        /// is pulled away, every log that left the ground is back on it — at the hauler's feet. Before the carry
        /// tracker the whole-pile case survived and the split case lost the entire load.
        /// </summary>
        [Theory]
        [InlineData(Interruption.Downed, 10)]
        [InlineData(Interruption.Downed, 80)]
        [InlineData(Interruption.Killed, 80)]
        [InlineData(Interruption.OrderedAway, 10)]
        [InlineData(Interruption.OrderedAway, 80)]
        [InlineData(Interruption.JobEnded, 10)]
        [InlineData(Interruption.JobEnded, 80)]
        public void An_interrupted_haul_leaves_every_log_it_picked_up_on_the_map(Interruption how, int logs)
        {
            (CoreMap map, Pawn hauler) = HaulSite(logs);

            int inHand = RunUntilLogsAreInHand(map, hauler, logs);
            IntVec3 where = hauler.Position;
            Assert.Equal(0, map.thingGrid.ThingsListAt(StockCell).Where(t => t.def == Wood).Sum(t => t.stackCount)); // not after delivery

            Interrupt(hauler, how);

            Assert.NotEqual(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
            Assert.Null(hauler.carryTracker.CarriedThing);
            Assert.Equal(logs, LogsOnMap(map));

            // Put down where the hauler stood, not somewhere else: "near the hauler" is the player's picture.
            Assert.Contains(map.thingGrid.ThingsListAt(where), t => t.def == Wood && t.stackCount == inHand);
        }

        /// <summary>
        /// The same loss, reached through the stack the load was meant for: the cell already holds most of a
        /// stack, so what fits is only the top-up. A hauler knocked down on the way with the top-up in hand
        /// must not shrink the pile it took the top-up from.
        /// </summary>
        [Fact]
        public void An_interrupted_top_up_haul_conserves_the_logs_it_took_off_the_pile()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, HaulerStart, map);
            NewStockpile(map, Wood, StockCell);
            int room = 5;
            SpawnLogs(map, StockCell, StackLimit - room);
            SpawnLogs(map, LogsCell, 20);

            int inHand = RunUntilLogsAreInHand(map, hauler, StackLimit - room + 20);
            Assert.Equal(room, inHand); // a pile of 20 is split, 5 of it carried

            hauler.health.ForceDowned = true;

            Assert.Null(hauler.carryTracker.CarriedThing);
            Assert.Equal(StackLimit - room + 20, LogsOnMap(map));
        }

        /// <summary>
        /// A body is not a stack of interchangeable units: it is carried whole, and an interrupted carry puts
        /// that same body down with the same person inside it (the property <c>CorpseTests</c> pins for an
        /// uninterrupted haul).
        /// </summary>
        [Fact]
        public void An_interrupted_corpse_haul_puts_the_same_body_down()
        {
            CoreMap map = NewMap(12, 12);
            CorpseDefGenerator.EnsureGenerated();
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, HaulerStart, map);
            var husky = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(husky, LogsCell, map);
            husky.health.Kill(null, null);
            CorpseThing corpse = husky.corpse!;
            NewStockpile(map, corpse.def, StockCell);

            for (int i = 0; i < 2000 && corpse.Spawned; i++) RunTicks(1, hauler);
            Assert.False(corpse.Spawned, "premise: the hauler picked the body up");
            Assert.Equal(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
            IntVec3 where = hauler.Position;

            hauler.health.ForceDowned = true;

            Assert.Null(hauler.carryTracker.CarriedThing);
            Assert.True(corpse.Spawned);
            Assert.False(corpse.Destroyed);
            Assert.Equal(where, corpse.Position);
            Assert.Same(husky, corpse.InnerPawn);
        }

        // ---- the ordinary case must not have changed ----

        /// <summary>
        /// A pile bigger than the cell it is hauled to goes in two trips when there are two cells: the first
        /// fills one, the second takes the rest. Nothing is lost, nothing is conjured, nobody is left holding
        /// anything, and no cell holds more than its limit.
        /// </summary>
        [Fact]
        public void An_uninterrupted_haul_of_a_pile_bigger_than_a_cell_fills_the_cells_and_loses_nothing()
        {
            (CoreMap map, Pawn hauler) = HaulSite(80, StockCell, SecondStockCell);

            RunTicks(6000, hauler);

            Assert.Equal(80, LogsOnMap(map));
            Assert.Null(hauler.carryTracker.CarriedThing);
            IEnumerable<Thing> piles = map.listerThings.ThingsOfDef(Wood).Where(t => t.Spawned).ToList();
            Assert.All(piles, t => Assert.True(t.stackCount <= StackLimit));
            Assert.All(piles, t => Assert.True(HaulAIUtility.IsInValidStorage(t), "everything should have ended up stored"));
        }

        /// <summary>With one cell, one full load goes into storage and the remainder stays where it was: the
        /// honest outcome of having nowhere else to put it.</summary>
        [Fact]
        public void An_uninterrupted_haul_into_one_cell_stores_a_full_cell_and_leaves_the_remainder_in_place()
        {
            (CoreMap map, Pawn hauler) = HaulSite(80);

            RunTicks(6000, hauler);

            Assert.Equal(80, LogsOnMap(map));
            Assert.Null(hauler.carryTracker.CarriedThing);
            Thing stored = Assert.Single(map.thingGrid.ThingsListAt(StockCell), t => t.def == Wood);
            Assert.Equal(StackLimit, stored.stackCount);
            Thing left = Assert.Single(map.listerThings.ThingsOfDef(Wood), t => t.Position == LogsCell);
            Assert.Equal(80 - StackLimit, left.stackCount);
        }

        /// <summary>A haul onto a part-filled stack merges the count up to the limit, and what is left of the
        /// pile stays out — previously the merge added the whole carried count whatever the stack held by then.</summary>
        [Fact]
        public void An_uninterrupted_top_up_haul_fills_the_stack_to_its_limit_and_no_further()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, HaulerStart, map);
            NewStockpile(map, Wood, StockCell);
            Thing stack = SpawnLogs(map, StockCell, StackLimit - 5);
            SpawnLogs(map, LogsCell, 20);

            RunTicks(6000, hauler);

            Assert.Equal(StackLimit, stack.stackCount);
            Assert.Equal(StackLimit - 5 + 20, LogsOnMap(map));
            Assert.Null(hauler.carryTracker.CarriedThing);
        }

        /// <summary>
        /// The stack at the destination grew while the hauler was walking there (somebody else's delivery, a
        /// player's drop), so less of the load fits than was planned at pickup. What fits goes in; the stack
        /// never passes its limit; the rest is put down at the hauler's feet rather than added on regardless.
        /// </summary>
        [Fact]
        public void A_stack_that_filled_up_on_the_way_takes_only_what_fits_and_the_rest_is_put_down()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, HaulerStart, map);
            NewStockpile(map, Wood, StockCell);
            Thing stack = SpawnLogs(map, StockCell, StackLimit - 5);
            SpawnLogs(map, LogsCell, 20);
            int total = StackLimit - 5 + 20;

            int inHand = RunUntilLogsAreInHand(map, hauler, total);
            Assert.Equal(5, inHand);
            stack.stackCount += 3; // somebody topped it up meanwhile: only 2 of the 5 still fit
            total += 3;

            RunTicks(2000, hauler);

            Assert.Equal(StackLimit, stack.stackCount);
            Assert.Equal(total, LogsOnMap(map));
            Assert.Null(hauler.carryTracker.CarriedThing);
        }

        // ---- Scribe ----

        /// <summary>
        /// A save taken mid-carry keeps the load, whether the pile was carried whole or split. The logs are off
        /// the map, so the hauler is the only place the save can find them: they come back in the loaded
        /// hauler's hands with the same count, the loaded job's target is that very Thing, and the hauler goes
        /// on to deliver them — nothing lost across the save, nothing counted twice.
        /// </summary>
        [Theory]
        [InlineData(10)]
        [InlineData(80)]
        public void A_hauler_saved_mid_carry_still_holds_the_logs_after_loading_and_delivers_them(int logs)
        {
            (CoreMap map, Pawn hauler) = HaulSite(logs);
            int inHand = RunUntilLogsAreInHand(map, hauler, logs);
            Assert.Equal(inHand, LogsHeldBy(hauler));

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedHauler = loaded.mapPawns.AllPawns.Single();
            Thing? held = loadedHauler.carryTracker.CarriedThing;
            Assert.NotNull(held);
            Assert.Same(Wood, held!.def);
            Assert.Equal(inHand, held.stackCount);
            Assert.False(held.Spawned);
            Assert.Equal(JobDefOf.HaulToCell, loadedHauler.jobs.curJob?.def);
            Assert.Same(held, loadedHauler.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(logs, LogsOnMap(loaded) + held.stackCount);

            RunTicks(3000, loadedHauler);

            Assert.Null(loadedHauler.carryTracker.CarriedThing);
            Assert.Equal(logs, LogsOnMap(loaded));
            Thing stored = Assert.Single(loaded.thingGrid.ThingsListAt(StockCell), t => t.def == Wood);
            Assert.Equal(inHand, stored.stackCount);
        }

        /// <summary>
        /// A hauler interrupted after loading is as safe as one that never was: the load that came back in its
        /// hands is put down by the same drop every job end performs.
        /// </summary>
        [Fact]
        public void A_hauler_saved_mid_carry_and_downed_after_loading_puts_the_logs_down()
        {
            (CoreMap map, Pawn hauler) = HaulSite(80);
            RunUntilLogsAreInHand(map, hauler, 80);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Pawn loadedHauler = loaded.mapPawns.AllPawns.Single();
            Assert.NotNull(loadedHauler.carryTracker.CarriedThing);

            loadedHauler.health.ForceDowned = true;

            Assert.Null(loadedHauler.carryTracker.CarriedThing);
            Assert.Equal(80, LogsOnMap(loaded));
        }
    }
}
