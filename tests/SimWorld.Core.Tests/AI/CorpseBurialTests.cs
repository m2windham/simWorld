using System.Collections.Generic;
using System.Linq;

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
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The dead go somewhere (<see cref="WorkGiver_HaulCorpses"/>, <see cref="JobDriver_HaulToContainer"/>,
    /// <see cref="StoreUtility.TryFindBestBetterStorageFor"/>). Before this a body had nowhere to go: no
    /// stockpile took one, so <c>HaulCorpses</c> had a destination for nothing and every body lay where it fell.
    /// <para/>
    /// Three promises, each its own test so one can fail without the others: <b>a citizen's body goes into a
    /// free grave and the grave is filled exactly once</b>; <b>a stranger's body is refused by a default grave
    /// and goes to the dumping stockpile</b>; and <b>where both would take a citizen, the grave wins</b> —
    /// storage priority, not distance, so the dump being nearer changes nothing.
    /// </summary>
    public class CorpseBurialTests : ContentTestBase
    {
        public CorpseBurialTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
        }

        private static CoreMap NewMap() => new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);

        private static Faction Ours { get; set; } = null!;

        private static Faction Theirs { get; set; } = null!;

        private void Factions()
        {
            Ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Theirs = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
        }

        private static Building_Grave NewGrave(CoreMap map, IntVec3 cell)
        {
            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDefOf.Grave);
            GenSpawn.Spawn(grave, cell, map);
            return grave;
        }

        private static Zone_Stockpile NewDump(CoreMap map, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetFromPreset(StorageSettingsPreset.DumpingStockpile);
            return zone;
        }

        private CorpseThing Body(CoreMap map, IntVec3 cell, Faction? faction, string name)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            p.health.Kill(null, null);
            Assert.NotNull(p.corpse);
            return p.corpse!;
        }

        private Pawn Hauler(CoreMap map, IntVec3 cell, string name = "Hauler")
        {
            Pawn p = NewHuman(name);
            p.faction = Ours;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static bool OnDump(CorpseThing body, Zone_Stockpile dump) =>
            body.Spawned && ReferenceEquals(body.Map!.zoneManager.ZoneAt(body.Position), dump);

        private static void Run(int ticks, params Pawn[] pawns) => RunTicks(ticks, pawns);

        // ---- the citizen goes into the grave ----

        [Fact]
        public void A_citizens_body_is_hauled_into_a_free_grave_and_the_grave_is_filled_once()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));

            Run(1500, hauler);

            Assert.True(grave.HasCorpse, "the hauler should have carried the body to the grave");
            Assert.Same(body, grave.Corpse);
            Assert.False(body.Spawned);
            Assert.Same(grave, body.StoringThing());
            Assert.Null(hauler.carryTracker.CarriedThing);
            Assert.Equal(1, grave.GetDirectlyHeldThings().Count);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));
        }

        [Fact]
        public void Two_bodies_and_one_grave_fill_it_once_and_leave_the_other_where_it_lay()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing first = Body(map, new IntVec3(20, 0, 20), Ours, "First");
            CorpseThing second = Body(map, new IntVec3(22, 0, 20), Ours, "Second");
            Pawn a = Hauler(map, new IntVec3(18, 0, 20), "A");
            Pawn b = Hauler(map, new IntVec3(18, 0, 22), "B");

            Run(2500, a, b);

            Assert.True(grave.HasCorpse);
            Assert.Equal(1, grave.GetDirectlyHeldThings().Count);
            CorpseThing buried = grave.Corpse!;
            CorpseThing left = ReferenceEquals(buried, first) ? second : first;
            Assert.True(left.Spawned, "the grave is full: the other body is still lying on the map");
            Assert.Null(a.carryTracker.CarriedThing);
            Assert.Null(b.carryTracker.CarriedThing);
        }

        [Fact]
        public void Each_body_gets_a_grave_of_its_own_when_there_are_graves_enough()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave g1 = NewGrave(map, new IntVec3(5, 0, 5));
            Building_Grave g2 = NewGrave(map, new IntVec3(8, 0, 5));
            Body(map, new IntVec3(20, 0, 20), Ours, "First");
            Body(map, new IntVec3(22, 0, 20), Ours, "Second");
            Pawn a = Hauler(map, new IntVec3(18, 0, 20), "A");
            Pawn b = Hauler(map, new IntVec3(18, 0, 22), "B");

            Run(2500, a, b);

            Assert.True(g1.HasCorpse);
            Assert.True(g2.HasCorpse);
            Assert.NotSame(g1.Corpse, g2.Corpse);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));
        }

        // ---- the stranger goes to the dump ----

        [Fact]
        public void A_strangers_body_is_refused_by_a_default_grave_and_goes_to_the_dumping_stockpile()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            Zone_Stockpile dump = NewDump(map, new IntVec3(30, 0, 30), new IntVec3(31, 0, 30));
            CorpseThing raider = Body(map, new IntVec3(20, 0, 20), Theirs, "Raider");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));
            Assert.False(grave.Accepts(raider));

            Run(2500, hauler);

            Assert.False(grave.HasCorpse, "a default grave is for the settlement's own");
            Assert.True(OnDump(raider, dump), "the raider should have been carried to the dumping stockpile");
        }

        [Fact]
        public void A_strangers_body_with_no_dump_stays_where_it_fell()
        {
            Factions();
            CoreMap map = NewMap();
            NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing raider = Body(map, new IntVec3(20, 0, 20), Theirs, "Raider");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));

            Run(1200, hauler);

            Assert.True(raider.Spawned);
            Assert.Equal(new IntVec3(20, 0, 20), raider.Position);
        }

        // ---- the grave beats the dump ----

        [Fact]
        public void Where_a_grave_and_a_dump_would_both_take_a_citizen_the_grave_wins()
        {
            Factions();
            CoreMap map = NewMap();
            // The dump is right beside the body and the grave is across the map: nearest-first would choose the
            // dump. Storage priority chooses the grave.
            Building_Grave grave = NewGrave(map, new IntVec3(4, 0, 4));
            Zone_Stockpile dump = NewDump(map, new IntVec3(21, 0, 20), new IntVec3(22, 0, 20));
            CorpseThing citizen = Body(map, new IntVec3(20, 0, 20), Ours, "Citizen");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));
            Assert.True(grave.Accepts(citizen));

            Run(2500, hauler);

            Assert.True(grave.HasCorpse, "the citizen belongs in the grave, however far");
            Assert.Same(citizen, grave.Corpse);
            Assert.False(OnDump(citizen, dump));
        }

        [Fact]
        public void The_choice_is_made_by_priority_not_by_distance()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(4, 0, 4));
            Zone_Stockpile dump = NewDump(map, new IntVec3(21, 0, 20));
            CorpseThing citizen = Body(map, new IntVec3(20, 0, 20), Ours, "Citizen");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));

            Assert.True(StoreUtility.TryFindBestBetterStorageFor(citizen, hauler, StoragePriority.Unstored,
                out IntVec3 cell, out IHaulDestination? destination));
            Assert.Same(grave, destination);
            Assert.False(cell.IsValid, "a destination wins outright: no stockpile cell is named alongside it");

            // Lower the grave below the dump and the dump wins: it is the priority doing the choosing.
            grave.GetStoreSettings().Priority = StoragePriority.Low;
            Assert.True(StoreUtility.TryFindBestBetterStorageFor(citizen, hauler, StoragePriority.Unstored,
                out cell, out destination));
            Assert.Null(destination);
            Assert.Equal(new IntVec3(21, 0, 20), cell);
            Assert.Same(dump, map.zoneManager.ZoneAt(cell));
        }

        [Fact]
        public void A_citizen_lying_in_the_dump_is_moved_to_a_grave_once_one_is_free()
        {
            Factions();
            CoreMap map = NewMap();
            Zone_Stockpile dump = NewDump(map, new IntVec3(20, 0, 20));
            CorpseThing citizen = Body(map, new IntVec3(20, 0, 20), Ours, "Citizen");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));
            Run(300, hauler);
            Assert.True(OnDump(citizen, dump), "with no grave the dump is as good as it gets: the citizen stays on it");
            Assert.Equal(StoragePriority.Normal, StoreUtility.CurrentStoragePriorityOf(citizen));

            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            Run(2500, hauler);

            Assert.Same(citizen, grave.Corpse);
            Assert.False(citizen.Spawned);
        }

        // ---- the carry is the same carry ----

        [Fact]
        public void A_hauler_stopped_with_a_body_in_hand_puts_it_down_and_the_grave_stays_empty()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Pawn hauler = Hauler(map, new IntVec3(19, 0, 20));

            for (int i = 0; i < 600 && hauler.carryTracker.CarriedThing == null; i++) Run(1, hauler);
            Assert.Same(body, hauler.carryTracker.CarriedThing);
            Assert.False(body.Spawned, "a body in hand is off the map");

            hauler.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);

            Assert.Null(hauler.carryTracker.CarriedThing);
            Assert.True(body.Spawned, "an interrupted haul puts the body down; it does not lose it");
            Assert.False(grave.HasCorpse);
        }

        [Fact]
        public void A_hauler_that_is_killed_with_a_body_in_hand_leaves_it_on_the_map()
        {
            Factions();
            CoreMap map = NewMap();
            NewGrave(map, new IntVec3(5, 0, 5));
            CorpseThing body = Body(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Pawn hauler = Hauler(map, new IntVec3(19, 0, 20));
            for (int i = 0; i < 600 && hauler.carryTracker.CarriedThing == null; i++) Run(1, hauler);
            Assert.Same(body, hauler.carryTracker.CarriedThing);

            hauler.health.Kill(null, null);

            Assert.True(body.Spawned);
            Assert.Null(hauler.carryTracker.CarriedThing);
        }

        // ---- the general hauler leaves bodies alone ----

        [Fact]
        public void The_general_hauler_does_not_take_a_body_to_a_stockpile()
        {
            Factions();
            CoreMap map = NewMap();
            Zone_Stockpile dump = NewDump(map, new IntVec3(30, 0, 30));
            CorpseThing citizen = Body(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));

            var general = new WorkGiver_Haul();

            Assert.False(general.HasJobOnThing(hauler, citizen));
            Assert.Null(general.JobOnThing(hauler, citizen));
            Assert.DoesNotContain(citizen, general.PotentialWorkThingsGlobal(hauler));
            Assert.True(new WorkGiver_HaulCorpses().HasJobOnThing(hauler, citizen));
            Assert.Same(dump, map.zoneManager.ZoneAt(new IntVec3(30, 0, 30)));
        }

        [Fact]
        public void The_corpse_hauler_offers_the_best_job_for_a_body_and_none_for_an_item()
        {
            Factions();
            CoreMap map = NewMap();
            Building_Grave grave = NewGrave(map, new IntVec3(5, 0, 5));
            NewDump(map, new IntVec3(30, 0, 30));
            CorpseThing citizen = Body(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Thing logs = GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog")), new IntVec3(10, 0, 10), map);
            Pawn hauler = Hauler(map, new IntVec3(18, 0, 20));
            var giver = new WorkGiver_HaulCorpses();

            Job? job = giver.JobOnThing(hauler, citizen);

            Assert.NotNull(job);
            Assert.Same(GraveJobDefOf.HaulToContainer, job!.def);
            Assert.Same(citizen, job.targetA.Thing);
            Assert.Same(grave, job.targetB.Thing);
            Assert.Equal(HaulMode.ToContainer, job.haulMode);
            Assert.False(giver.HasJobOnThing(hauler, logs));
            Assert.Null(giver.JobOnThing(hauler, logs));
        }
    }
}
