using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Where somebody slept sets a memory (<see cref="BedThoughtUtility"/>): their own household's dwelling is
    /// a bedroom, any other bed is barracks, and no bed at all is the ground, out in the open. Pinned as an
    /// order (home beats the dormitory, the dormitory beats the ground), never as the recalled literals.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SleepThoughtTests : ContentTestBase
    {
        public SleepThoughtTests(CoreContentFixture content) : base(content)
        {
        }

        private static readonly ThoughtDef[] SleepDefs =
        {
            SleepThoughtDefOf.SleptInBedroom, SleepThoughtDefOf.SleptInBarracks,
            SleepThoughtDefOf.SleptOnGround, SleepThoughtDefOf.SleptOutside,
        };

        private static ThoughtDef[] SleepMemories(Pawn p) =>
            p.needs.mood!.thoughts.memories.Memories.Where(m => SleepDefs.Contains(m.def)).Select(m => m.def).OrderBy(d => d.defName).ToArray();

        private static float SleepMood(Pawn p) =>
            p.needs.mood!.thoughts.memories.Memories.Where(m => SleepDefs.Contains(m.def)).Sum(m => m.MoodOffset());

        private static Pawn Tired(CoreMap map, string name, int familyId, IntVec3 at)
        {
            Pawn p = NewHuman(name);
            p.relations.familyId = familyId;
            GenSpawn.Spawn(p, at, map);
            p.needs.rest!.CurLevel = 0.05f;
            return p;
        }

        [Fact]
        public void A_household_sleeping_at_home_is_in_its_bedroom_and_a_dormitory_sleeper_in_barracks()
        {
            var map = new CoreMap(60, 60, TerrainDefOf.Soil);
            SimWorld.Tests.Building.ShelterKit.Raise(map, SimWorld.Tests.Building.ShelterKit.Rung("House_Paleo"), new IntVec3(30, 0, 30), householdId: 7);
            GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.Bed), new IntVec3(10, 0, 10), map);

            Pawn a = Tired(map, "A", 7, new IntVec3(20, 0, 20));
            Pawn b = Tired(map, "B", 7, new IntVec3(21, 0, 20));
            Pawn lodger = Tired(map, "Lodger", 9, new IntVec3(22, 0, 20));
            RunTicks(400, a, b, lodger);

            foreach (Pawn p in new[] { a, b, lodger }) Assert.True(p.Asleep, p + " is not asleep");
            Assert.Equal(new[] { SleepThoughtDefOf.SleptInBedroom }, SleepMemories(a));
            Assert.Equal(new[] { SleepThoughtDefOf.SleptInBedroom }, SleepMemories(b));
            Assert.Equal(new[] { SleepThoughtDefOf.SleptInBarracks }, SleepMemories(lodger));
            Assert.True(SleepMood(a) > SleepMood(lodger), "home " + SleepMood(a) + " vs dormitory " + SleepMood(lodger));
        }

        [Fact]
        public void Nobody_with_a_bed_sleeps_on_the_ground_and_out_in_the_open_worse_than_in_barracks()
        {
            var map = new CoreMap(40, 40, TerrainDefOf.Soil);
            Pawn grounded = Tired(map, "Grounded", Pawn_RelationsTracker.None, new IntVec3(20, 0, 20));
            RunTicks(400, grounded);

            Assert.True(grounded.Asleep);
            Assert.Equal(new[] { SleepThoughtDefOf.SleptOnGround, SleepThoughtDefOf.SleptOutside }, SleepMemories(grounded));

            var other = new CoreMap(40, 40, TerrainDefOf.Soil);
            Thing bed = GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.Bed), new IntVec3(10, 0, 10), other);
            Pawn bunked = NewHuman("Bunked");
            GenSpawn.Spawn(bunked, new IntVec3(10, 0, 10), other);
            BedThoughtUtility.ApplyBedThoughts(bunked, bed);
            Assert.True(SleepMood(bunked) > SleepMood(grounded), "barracks " + SleepMood(bunked) + " vs ground " + SleepMood(grounded));
        }

        [Fact]
        public void Another_households_dwelling_is_barracks_and_only_the_latest_sleep_is_remembered()
        {
            var map = new CoreMap(40, 40, TerrainDefOf.Soil);
            Dwelling house = SimWorld.Tests.Building.ShelterKit.Raise(map, SimWorld.Tests.Building.ShelterKit.Rung("House_Paleo"), new IntVec3(20, 0, 20), householdId: 3);
            Thing houseBed = house.Beds(map)[0];
            Pawn guest = NewHuman("Guest");
            guest.relations.familyId = 4;
            GenSpawn.Spawn(guest, new IntVec3(5, 0, 5), map);

            BedThoughtUtility.ApplyBedThoughts(guest, null);
            BedThoughtUtility.ApplyBedThoughts(guest, houseBed);
            Assert.Equal(new[] { SleepThoughtDefOf.SleptInBarracks }, SleepMemories(guest));

            guest.relations.familyId = 3;
            BedThoughtUtility.ApplyBedThoughts(guest, houseBed);
            Assert.Equal(new[] { SleepThoughtDefOf.SleptInBedroom }, SleepMemories(guest));
        }
    }
}
