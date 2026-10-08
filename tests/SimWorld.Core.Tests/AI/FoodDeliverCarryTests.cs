using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A meal being delivered is carried, not deleted (<see cref="JobDriver_FoodDeliver"/> through
    /// <see cref="Pawn_CarryTracker"/>). The defect, from the player's side: a warden takes a meal from the
    /// larder to a hungry prisoner, is knocked down or called away on the way, and the meal is gone — the larder
    /// is a pile, so the meal was split off it, and a split-off count existed only as a number on the job. The
    /// prisoner stays hungry and nothing in the game says where the food went.
    /// <para/>
    /// Every assertion is conservation, not a coordinate: the meal is in the warden's hands, on the map, or in
    /// the prisoner's room, and the total never drops. The shape is <c>ConstructionHaulCarryTests</c>'.
    /// Its own file rather than more cases in <c>WardenDeliverFoodTests</c> (CLAUDE.md: add a file rather than
    /// edit a shared one); the fixtures are that class's, kept small.
    /// </summary>
    public class FoodDeliverCarryTests : ContentTestBase
    {
        public FoodDeliverCarryTests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager but ContentTestBase does not reset it — see
            // WardenDeliverFoodTests' identical constructor comment.
            Find.FactionManager = new FactionManager();
        }

        public enum Interruption
        {
            /// <summary>Knocked down mid-carry (<see cref="Pawn.Notify_Downed"/>).</summary>
            Downed,

            /// <summary>Killed mid-carry (<see cref="Pawn.Notify_Died"/>).</summary>
            Killed,

            /// <summary>The player orders the warden somewhere else (a player-forced job that pre-empts the
            /// one in hand, the call <c>MapCommands.OrderJob</c> makes).</summary>
            OrderedAway,

            /// <summary>The job is ended from outside, without going through the pawn's health.</summary>
            JobEnded,
        }

        // ---- fixtures (WardenDeliverFoodTests', trimmed) ----

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static ThingDef Potatoes => DefDatabase<ThingDef>.GetNamed("RawPotatoes");

        private static readonly IntVec3 LarderCell = new IntVec3(9, 0, 8);

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, int count)
        {
            Thing food = ThingMaker.MakeThing(Potatoes);
            food.stackCount = count;
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        private static void SpawnBuilding(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);

        /// <summary>A four-walled cell with one door at (5,2) enclosing (3..4, 2..3): a prison cell in the
        /// ordinary sense. Returns an interior cell.</summary>
        private static IntVec3 BuildPrisonCell(CoreMap map)
        {
            var doorCell = new IntVec3(5, 0, 2);
            var rect = new CellRect(2, 1, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells)
            {
                if (c != doorCell) SpawnBuilding(map, c, "Wall");
            }
            SpawnBuilding(map, doorCell, "Door");
            return new IntVec3(3, 0, 2);
        }

        /// <summary>A warden, a hungry ambulatory prisoner in a walled cell, and a larder pile of
        /// <paramref name="meals"/> outside it. The prisoner is left out of the tick lists (its own
        /// <c>JobGiver_GetFood</c> would race the delivery, which is the wrong thing to be measuring); a test
        /// that wants it acting names it in <c>RunTicks</c>.</summary>
        private static (CoreMap map, Pawn warden, Pawn prisoner, Thing larder, Job delivery) DeliverySite(int meals)
        {
            CoreMap map = NewMap(12, 12);
            Faction captor = NewFaction("Captors");
            Faction other = NewFaction("Raiders");
            captor.SetRelationDirect(other, FactionRelationKind.Hostile, -100);

            Pawn warden = NewHuman("Warden");
            GenSpawn.Spawn(warden, new IntVec3(9, 0, 9), map);
            warden.faction = captor;

            IntVec3 inside = BuildPrisonCell(map);
            Pawn prisoner = NewHuman("Prisoner");
            GenSpawn.Spawn(prisoner, inside, map);
            prisoner.faction = other;
            prisoner.health.ForceDowned = true;
            CaptureUtility.Capture(warden, prisoner);
            prisoner.health.ForceDowned = false;
            prisoner.needs.food!.CurLevelPercentage = 0.1f;
            Find.TickManager.DeRegisterAllTickabilityFor(prisoner);

            Thing larder = SpawnFood(map, LarderCell, meals);
            map.MapTick();

            Job? job = new WorkGiver_Warden_DeliverFood().JobOnThing(warden, prisoner);
            Assert.NotNull(job);
            return (map, warden, prisoner, larder, job!);
        }

        /// <summary>Potatoes lying on a map cell, wherever they are. Not what a warden holds.</summary>
        private static int MealsOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Potatoes).Where(t => t.Spawned).Sum(t => t.stackCount);

        /// <summary>Ticks until a meal has left the map without having arrived anywhere — the warden has it in
        /// hand — and fails the test if that never happens. The moment is read off the map alone: a meal gone
        /// from it while the job runs, and not yet lying in the prisoner's room.</summary>
        private static int RunUntilMealIsInHand(CoreMap map, Pawn warden, Pawn prisoner, int total, int maxTicks = 1000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                int inHand = total - MealsOnMap(map);
                if (inHand > 0)
                {
                    Assert.Equal(WardenDeliverFoodDefOf.DeliverFood, warden.jobs.curJob?.def);
                    return inHand;
                }
                RunTicks(1, warden);
            }
            Assert.Fail("The warden never picked a meal up within " + maxTicks + " ticks.");
            return 0;
        }

        private static void Interrupt(Pawn warden, Interruption how)
        {
            switch (how)
            {
                case Interruption.Downed:
                    warden.health.ForceDowned = true;
                    break;
                case Interruption.Killed:
                    warden.health.Kill(null, null);
                    break;
                case Interruption.OrderedAway:
                    warden.jobs.StartJob(new Job(DutyJobDefOf.Goto, new IntVec3(11, 0, 11)) { playerForced = true }, JobCondition.InterruptForced);
                    break;
                case Interruption.JobEnded:
                    warden.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    break;
            }
        }

        // ---- the defect ----

        /// <summary>
        /// The headline. A larder pile of four is split (one meal carried, three left behind); a single meal is
        /// carried whole (the Thing itself leaves the map). However the warden is pulled away, the meal that
        /// left the larder is back on the ground at the warden's feet. Before the carry tracker, the split case
        /// lost the meal.
        /// </summary>
        [Theory]
        [InlineData(Interruption.Downed, 4)]
        [InlineData(Interruption.Downed, 1)]
        [InlineData(Interruption.Killed, 4)]
        [InlineData(Interruption.OrderedAway, 4)]
        [InlineData(Interruption.OrderedAway, 1)]
        [InlineData(Interruption.JobEnded, 4)]
        [InlineData(Interruption.JobEnded, 1)]
        public void An_interrupted_delivery_leaves_the_meal_on_the_map(Interruption how, int meals)
        {
            (CoreMap map, Pawn warden, Pawn prisoner, _, Job delivery) = DeliverySite(meals);
            warden.jobs.StartJob(delivery);

            int inHand = RunUntilMealIsInHand(map, warden, prisoner, meals);
            Assert.Equal(JobDriver_FoodDeliver.MealsPerDelivery, inHand);
            IntVec3 where = warden.Position;

            Interrupt(warden, how);

            Assert.NotEqual(WardenDeliverFoodDefOf.DeliverFood, warden.jobs.curJob?.def);
            Assert.Null(warden.carryTracker.CarriedThing);
            Assert.Equal(meals, MealsOnMap(map));
            Assert.Contains(map.thingGrid.ThingsListAt(where), t => t.def == Potatoes && t.stackCount >= inHand);
        }

        // ---- the ordinary case must not have changed ----

        /// <summary>
        /// An uninterrupted delivery from a pile still puts exactly one meal in the prisoner's room, leaves the
        /// rest of the pile where it was, and leaves the warden empty-handed.
        /// </summary>
        [Fact]
        public void An_uninterrupted_delivery_from_a_pile_puts_one_meal_in_the_prisoners_room_and_leaves_the_rest()
        {
            (CoreMap map, Pawn warden, Pawn prisoner, Thing larder, Job delivery) = DeliverySite(4);
            warden.jobs.StartJob(delivery);

            RunTicks(600, warden);
            map.MapTick();

            Assert.Null(warden.carryTracker.CarriedThing);
            Assert.Equal(4 - JobDriver_FoodDeliver.MealsPerDelivery, larder.stackCount);
            Assert.True(larder.Spawned);
            SimWorld.Building.Room cell = map.roomTracker.RoomAt(prisoner.Position)!;
            Thing delivered = Assert.Single(
                map.listerThings.ThingsOfDef(Potatoes),
                t => ReferenceEquals(map.roomTracker.RoomAt(t.Position), cell));
            Assert.Equal(JobDriver_FoodDeliver.MealsPerDelivery, delivered.stackCount);
            Assert.Equal(4, MealsOnMap(map));
        }

        /// <summary>A lone meal travels as the real Thing — the same object arrives in the prisoner's room,
        /// not a copy of its def — which is what lets a damaged or masterwork item keep being itself.</summary>
        [Fact]
        public void An_uninterrupted_delivery_of_a_lone_meal_brings_the_meal_itself()
        {
            (CoreMap map, Pawn warden, Pawn prisoner, Thing larder, Job delivery) = DeliverySite(1);
            larder.HitPoints = larder.MaxHitPoints / 2;
            warden.jobs.StartJob(delivery);

            RunTicks(600, warden);
            map.MapTick();

            Assert.Null(warden.carryTracker.CarriedThing);
            Assert.True(larder.Spawned);
            Assert.Equal(1, larder.stackCount);
            Assert.Equal(larder.MaxHitPoints / 2, larder.HitPoints);
            Assert.NotEqual(LarderCell, larder.Position);
            Assert.Same(map.roomTracker.RoomAt(prisoner.Position), map.roomTracker.RoomAt(larder.Position));
        }

        /// <summary>A meal delivered onto a stack of the same food already standing there merges into it
        /// rather than leaving two piles on one cell.</summary>
        [Fact]
        public void A_delivery_onto_the_same_food_already_on_the_cell_merges_into_it()
        {
            (CoreMap map, Pawn warden, Pawn prisoner, _, Job delivery) = DeliverySite(4);
            Thing already = SpawnFood(map, prisoner.Position, 3);
            warden.jobs.StartJob(delivery);

            RunTicks(600, warden);

            Assert.Null(warden.carryTracker.CarriedThing);
            Assert.Equal(3 + JobDriver_FoodDeliver.MealsPerDelivery, already.stackCount);
            Assert.Equal(4 + 3, MealsOnMap(map));
            Assert.Single(map.thingGrid.ThingsListAt(prisoner.Position), t => t.def == Potatoes);
        }

        // ---- Scribe ----

        /// <summary>
        /// A save taken mid-carry keeps the meal, whether it was split off a pile or carried whole. The meal is
        /// off the map, so the warden is the only place the save can find it: it comes back in the loaded
        /// warden's hands with the same count, the loaded job's target A is that very Thing, and the warden goes
        /// on to put it down in the prisoner's room. Nothing is lost across the save and nothing counted twice.
        /// </summary>
        [Theory]
        [InlineData(4)]
        [InlineData(1)]
        public void A_warden_saved_mid_carry_still_holds_the_meal_after_loading_and_delivers_it(int meals)
        {
            (CoreMap map, Pawn warden, Pawn prisoner, _, Job delivery) = DeliverySite(meals);
            warden.jobs.StartJob(delivery);
            int inHand = RunUntilMealIsInHand(map, warden, prisoner, meals);
            Assert.Equal(inHand, warden.carryTracker.CarriedThing!.stackCount);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            // A Faction round-trips through the World/FactionManager save, never through a lone Map's — the
            // same constraint WardenDeliverFoodTests' own Scribe test records, and the only thing unresolved.
            Assert.All(errors, e => Assert.Contains("Pawn.faction", e));

            Pawn loadedWarden = loaded.mapPawns.AllPawns.Single(p => p.thingIDNumber == warden.thingIDNumber);
            Pawn loadedPrisoner = loaded.mapPawns.AllPawns.Single(p => p.thingIDNumber == prisoner.thingIDNumber);
            Find.TickManager.DeRegisterAllTickabilityFor(loadedPrisoner);

            Thing? held = loadedWarden.carryTracker.CarriedThing;
            Assert.NotNull(held);
            Assert.Same(Potatoes, held!.def);
            Assert.Equal(inHand, held.stackCount);
            Assert.False(held.Spawned);
            Assert.Equal(WardenDeliverFoodDefOf.DeliverFood, loadedWarden.jobs.curJob?.def);
            Assert.Same(held, loadedWarden.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(meals, MealsOnMap(loaded) + held.stackCount);

            loaded.MapTick();
            RunTicks(600, loadedWarden);
            loaded.MapTick();

            Assert.Null(loadedWarden.carryTracker.CarriedThing);
            Assert.Equal(meals, MealsOnMap(loaded));
            SimWorld.Building.Room cell = loaded.roomTracker.RoomAt(loadedPrisoner.Position)!;
            Assert.Contains(loaded.listerThings.ThingsOfDef(Potatoes), t => ReferenceEquals(loaded.roomTracker.RoomAt(t.Position), cell));
        }
    }
}
