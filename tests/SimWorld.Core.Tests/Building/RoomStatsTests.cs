using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// What a room is like to be in (<see cref="RoomStats"/>, RimWorld's room stats recalled). Pinned as orders, never
    /// as the recalled constants: each age's house, furnished in its age, makes a better room than the age before;
    /// wealth, beauty and floor space each raise a room and filth lowers it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RoomStatsTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public RoomStatsTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        private static (CoreMap Map, Dwelling House) Raise(string rung, string? furnishing)
        {
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung(rung), new IntVec3(10, 0, 10));
            ShelterKit.Roof(map, d);
            if (furnishing != null)
            {
                foreach (IntVec3 c in d.Spots(DwellingPartKind.TableSpot)) GenSpawn.Spawn(ThingMaker.MakeThing(FurnitureThingDefOf.DiningTable, StuffDefOf.WoodLog), c, map);
                foreach (IntVec3 c in d.Spots(DwellingPartKind.FurnishingSpot)) GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(furnishing)), c, map);
            }
            map.roomTracker.Notify_Dirty();
            map.roomTracker.RoomTrackerTick();
            return (map, d);
        }

        /// <summary>The house's best room: its parlour where it has one, else the room round its beds.</summary>
        private static float BestRoom(CoreMap map, Dwelling d) =>
            map.roomTracker.Rooms.Where(r => RoomStats.IsEnclosed(r) && r.Cells.Any(c => d.footprint.Contains(c))).Max(r => RoomStats.Impressiveness(r, map));

        [Fact]
        public void Each_age_furnishes_a_better_room_than_the_age_before()
        {
            var ladder = new (string Rung, string? Furnishing)[]
            {
                ("House_Meso", null),
                ("House_Neo", "Furnishing_Agrarian"),
                ("House_Chalco", "Furnishing_Bronze"),
                ("House_Classical", "Furnishing_Classical"),
                ("House_Medieval", "Furnishing_Medieval"),
                ("House_Industrial", "Furnishing_Industrial"),
                ("House_Information", "Furnishing_Information"),
                ("House_Exotic", "Furnishing_Exotic"),
            };
            var scores = new List<float>();
            foreach ((string rung, string? furnishing) in ladder)
            {
                (CoreMap map, Dwelling d) = Raise(rung, furnishing);
                float score = BestRoom(map, d);
                output.WriteLine($"{rung,-18} {furnishing ?? "-",-24} best room {score:0.0}, stage {RoomStats.StageOf(score)}");
                scores.Add(score);
            }
            for (int i = 1; i < scores.Count; i++)
            {
                Assert.True(scores[i] > scores[i - 1], $"{ladder[i].Rung} ({scores[i]:0.0}) is no better a room than {ladder[i - 1].Rung} ({scores[i - 1]:0.0})");
            }
            Assert.True(RoomStats.StageOf(scores[0]) < RoomStats.MediocreStage, "a pit house is not a mean room");
            Assert.True(RoomStats.StageOf(scores[^1]) > RoomStats.MediocreStage + 1, "the last age's house is not an impressive room");
        }

        [Fact]
        public void Wealth_beauty_and_space_raise_a_room_and_filth_lowers_it()
        {
            (CoreMap map, Dwelling d) = Raise("House_Medieval", null);
            Room room = map.roomTracker.RoomAt(d.Beds(map)[0].Position)!;
            float bare = RoomStats.Impressiveness(room, map);

            IntVec3 spot = d.Spots(DwellingPartKind.FurnishingSpot).Single();
            GenSpawn.Spawn(ThingMaker.MakeThing(FurnitureThingDefOf.Furnishing_Medieval), spot, map);
            float furnished = RoomStats.Impressiveness(room, map);
            Assert.True(RoomStats.Wealth(room, map) > 0f && RoomStats.Beauty(room, map) > 0f);
            Assert.True(furnished > bare, $"furnished {furnished} vs bare {bare}");

            ThingDef filth = DefDatabase<ThingDef>.AllDefsListForReading.First(t => t.category == ThingCategory.Filth);
            foreach (IntVec3 c in room.Cells.Where(c => GenGrid.Standable(c, map)).Take(8)) GenSpawn.Spawn(ThingMaker.MakeThing(filth), c, map);
            float dirty = RoomStats.Impressiveness(room, map);
            Assert.True(RoomStats.Cleanliness(room, map) < 0f);
            Assert.True(dirty < furnished, $"dirty {dirty} vs clean {furnished}");

            // The pit house's room is smaller than the stone town house's: less space, a lower score, all else bare.
            (CoreMap pitMap, Dwelling pit) = Raise("House_Meso", null);
            Room pitRoom = pitMap.roomTracker.RoomAt(pit.Beds(pitMap)[0].Position)!;
            Assert.True(RoomStats.Space(pitRoom, pitMap) < RoomStats.Space(room, map));
        }

        [Fact]
        public void The_outdoors_and_an_unroofed_house_are_not_rooms_to_score()
        {
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            Dwelling d = ShelterKit.Raise(map, ShelterKit.Rung("House_Medieval"), new IntVec3(10, 0, 10));
            map.roomTracker.Notify_Dirty();
            map.roomTracker.RoomTrackerTick();
            Assert.False(RoomStats.IsEnclosed(map.roomTracker.RoomAt(d.Beds(map)[0].Position)), "a house with no roof scored as a room");
            Assert.False(RoomStats.IsEnclosed(map.roomTracker.RoomAt(new IntVec3(2, 0, 2))));
            Assert.Equal(RoomStats.MediocreStage, SimWorld.AI.BedThoughtUtility.BedroomStage(d.Beds(map)[0]));
        }
    }
}
