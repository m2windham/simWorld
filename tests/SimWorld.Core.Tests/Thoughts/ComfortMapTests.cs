using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Building;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Thoughts
{
    /// <summary>
    /// What a richer life gives back (comfort lane), on real generated settlements. The era sets what a citizen
    /// expects, falling to nothing by the Industrial age; before this lane nothing answered it, and the ten-year
    /// arcs' late towns sat at 0.38-0.51 mood with "Low spirits" standing. A better house is a better night, a meal
    /// is better at a table and better still in a fine room, a hearty meal is worth walking for, and a late-era town
    /// that has furnished its houses and cooks well lives above the line its expectations would otherwise drag it to.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ComfortMapTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public ComfortMapTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ResearchProjectDef Project(string name) => DefDatabase<ResearchProjectDef>.GetNamed(name);

        private sealed class Town
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map => Settlement.InteriorMap!;
            public Faction Faction = null!;
            public List<int> Households = new List<int>();
        }

        /// <summary>A tribal band of <paramref name="band"/> on a generated map with its compound planned, that has
        /// learned every project of the eras before <paramref name="era"/> and the <paramref name="learned"/> ones.</summary>
        private static Town Founded(string seed, string era, int band = 20, params string[] learned)
        {
            CoreMap.ResetMapIdCounter();
            var t = new Town
            {
                Game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("TribalStart").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: band),
            };
            t.Settlement = t.Game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(t.Settlement.tile).Outcome);
            t.Faction = t.Settlement.Citizens.First(p => p.Spawned).Faction!;
            for (int i = 0; i < 2000 && t.Map.settlementCompound == null; i++) t.Game.TickManager.DoSingleTick();
            Assert.NotNull(t.Map.settlementCompound);

            ResearchManager research = Find.ResearchManager;
            int order = DefDatabase<EraDef>.GetNamed(era).order;
            foreach (EraDef e in DefDatabase<EraDef>.AllDefsListForReading.Where(e => e.order < order))
            {
                foreach (ResearchProjectDef p in e.Projects) research.SetProjectFinishedForSetup(p);
            }
            foreach (string p in learned) research.SetProjectFinishedForSetup(Project(p));
            Assert.Equal(era, research.CurrentEra!.defName);
            t.Households = DwellingUtility.Households(t.Settlement).Keys.ToList();
            return t;
        }

        /// <summary>A finished, roofed house of <paramref name="rung"/> for <paramref name="household"/>, with a table
        /// and <paramref name="furnishing"/> on its spots when given.</summary>
        private static Dwelling House(Town t, string rung, int household, string? furnishing = null)
        {
            Dwelling? d = ShelterKit.RaiseOnPlot(t.Map, ShelterKit.Rung(rung), household, t.Faction);
            Assert.NotNull(d);
            if (furnishing != null)
            {
                foreach (IntVec3 c in d!.Spots(DwellingPartKind.TableSpot)) Spawn(t, FurnitureThingDefOf.DiningTable, c, StuffDefOf.WoodLog);
                foreach (IntVec3 c in d.Spots(DwellingPartKind.FurnishingSpot)) Spawn(t, Def(furnishing), c);
            }
            Rooms(t.Map);
            return d!;
        }

        private static Thing Spawn(Town t, ThingDef def, IntVec3 cell, ThingDef? stuff = null)
        {
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFaction(t.Faction);
            return GenSpawn.Spawn(thing, cell, t.Map);
        }

        private static void Rooms(CoreMap map)
        {
            map.roomTracker.Notify_Dirty();
            map.roomTracker.RoomTrackerTick();
        }

        private static Pawn MemberOf(Town t, int household) =>
            t.Settlement.Citizens.First(p => !p.Dead && p.Spawned && p.relations.familyId == household && p.needs?.mood != null);

        private static float MemoryMood(Pawn p, ThoughtDef def) =>
            p.needs.mood!.thoughts.memories.Memories.Where(m => m.def == def).Sum(m => m.MoodOffset());

        private static bool Holds(Pawn p, ThoughtDef def) => p.needs.mood!.thoughts.memories.Memories.Any(m => m.def == def);

        // ---- rooms ----

        [Fact]
        public void A_furnished_stone_town_house_is_a_better_night_than_a_pit_house_or_the_same_house_bare()
        {
            Town t = Founded("comfort-rooms", "Medieval", 20, "GuildHalls", "GuildSystem");
            Dwelling pit = House(t, "House_Meso", t.Households[0]);
            Dwelling stone = House(t, "House_Medieval", t.Households[1], "Furnishing_Medieval");
            Pawn poor = MemberOf(t, t.Households[0]);
            Pawn well = MemberOf(t, t.Households[1]);

            BedThoughtUtility.ApplyBedThoughts(poor, pit.Beds(t.Map)[0]);
            BedThoughtUtility.ApplyBedThoughts(well, stone.Beds(t.Map)[0]);
            float pitNight = MemoryMood(poor, SleepThoughtDefOf.SleptInBedroom);
            float stoneNight = MemoryMood(well, SleepThoughtDefOf.SleptInBedroom);

            foreach (IntVec3 c in stone.Spots(DwellingPartKind.TableSpot).Concat(stone.Spots(DwellingPartKind.FurnishingSpot)))
            {
                foreach (Thing f in t.Map.thingGrid.ThingsListAt(c).Where(x => x.def.category == ThingCategory.Building).ToList()) f.Destroy();
            }
            Rooms(t.Map);
            BedThoughtUtility.ApplyBedThoughts(well, stone.Beds(t.Map)[0]);
            float bareNight = MemoryMood(well, SleepThoughtDefOf.SleptInBedroom);

            output.WriteLine($"pit house {pitNight}, bare stone house {bareNight}, furnished stone house {stoneNight}");
            Assert.True(stoneNight > bareNight, $"furnished {stoneNight} vs bare {bareNight}");
            Assert.True(stoneNight > pitNight, $"furnished stone {stoneNight} vs pit {pitNight}");
            Assert.True(pitNight < 0f, "a pit house is a mean room: " + pitNight);
        }

        // ---- meals ----

        /// <summary>Hands <paramref name="p"/> a hungry stomach and a meal at <paramref name="at"/>, and runs the
        /// game until it has eaten it. Returns whether it did.</summary>
        private static bool Eat(Town t, Pawn p, ThingDef meal, IntVec3 at)
        {
            Thing food = Spawn(t, meal, at);
            p.needs.food!.CurLevel = 0.1f;
            p.jobs.StartJob(new Job(JobDefOf.Ingest, food));
            for (int i = 0; i < 3000 && p.jobs.curJob?.def == JobDefOf.Ingest; i++) t.Game.TickManager.DoSingleTick();
            return p.needs.food!.CurLevel > 0.5f;
        }

        private static IntVec3 Centre(Dwelling d) =>
            new IntVec3(d.footprint.minX + d.footprint.width / 2, 0, d.footprint.minZ + d.footprint.height / 2);

        private static IntVec3 OpenCellNear(Town t, IntVec3 near) =>
            GenRadial.RadialCellsAround(near, 6f, true).First(c => GenGrid.InBounds(c, t.Map) && GenGrid.Standable(c, t.Map)
                && t.Map.thingGrid.ThingsListAt(c).Count == 0 && DwellingUtility.DwellingAt(t.Map, c) == null);

        [Fact]
        public void A_meal_is_carried_to_a_table_and_nobody_misses_a_table_before_the_town_has_one()
        {
            Town t = Founded("comfort-table", "Agrarian", 20);
            ThoughtDef noTable = IngestThoughtDefOf.AteWithoutTable;
            Dwelling home = House(t, "House_Neo", t.Households[0]);
            Pawn p = MemberOf(t, t.Households[0]);
            IntVec3 near = OpenCellNear(t, Centre(home));

            // Before Shelter, no table is known: a meal eaten on the ground is only a meal.
            Assert.True(Eat(t, p, Def("MealSimple"), near));
            Assert.False(Holds(p, noTable), "a band that has never had a table missed one");

            // Once tables are known, a meal with none in reach is a complaint.
            Find.ResearchManager.SetProjectFinishedForSetup(Project("Shelter"));
            Assert.True(Eat(t, p, Def("MealSimple"), near));
            Assert.True(Holds(p, noTable), "no complaint about eating without a table once tables are known");
            Assert.True(MemoryMood(p, noTable) < 0f);

            // With the household's table built, the meal is carried to it and eaten there.
            p.needs.mood!.thoughts.memories.RemoveMemoriesOfDef(noTable);
            Thing table = Spawn(t, FurnitureThingDefOf.DiningTable, home.Spots(DwellingPartKind.TableSpot).Single(), StuffDefOf.WoodLog);
            Rooms(t.Map);
            Assert.True(Eat(t, p, Def("MealSimple"), near));
            Assert.False(Holds(p, noTable), "ate at a table and still complained of having none");
            Assert.True((p.Position - table.Position).LengthHorizontalSquared <= 2, "ate at " + p.Position + ", the table is at " + table.Position);
        }

        [Fact]
        public void A_hungry_citizen_walks_past_raw_meat_for_a_hearty_meal_and_remembers_each()
        {
            Town t = Founded("comfort-meal", "Agrarian", 20);
            // Somebody who minds what they eat: an ascetic feels neither meal.
            Pawn p = t.Settlement.Citizens.First(c => !c.Dead && c.Spawned && c.needs?.mood != null
                && !ThoughtUtility.ThoughtNullified(c, IngestThoughtDefOf.AteFineMeal) && !ThoughtUtility.ThoughtNullified(c, IngestThoughtDefOf.AteRawFood));
            // Everything edible already lying about is put out of the question.
            foreach (Thing f in t.Map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(x => x.def.IsNutritionGivingIngestible).ToList()) f.Destroy();

            IntVec3 rawAt = OpenCellNear(t, p.Position);
            IntVec3 mealAt = GenRadial.RadialCellsAround(p.Position, 12f, false)
                .Where(c => (c - p.Position).LengthHorizontal >= 8f)
                .First(c => GenGrid.InBounds(c, t.Map) && GenGrid.Standable(c, t.Map) && t.Map.thingGrid.ThingsListAt(c).Count == 0
                    && Reachability.CanReach(p, c, PathEndMode.OnCell));
            Thing raw = Spawn(t, Def("Meat_Generic"), rawAt);
            raw.stackCount = 30;
            Thing meal = Spawn(t, Def("MealFine"), mealAt);

            p.needs.food!.CurLevel = 0.1f;
            ThinkResult result = new JobGiver_GetFood().TryIssueJobPackage(p);
            Assert.Same(meal, result.Job?.GetTarget(TargetIndex.A).Thing);

            Assert.True(Eat(t, p, Def("MealFine"), mealAt));
            Assert.True(MemoryMood(p, IngestThoughtDefOf.AteFineMeal) > 0f, "a hearty meal left no good memory");
            Assert.True(Eat(t, p, Def("Meat_Generic"), rawAt));
            Assert.True(MemoryMood(p, IngestThoughtDefOf.AteRawFood) < 0f, "raw meat left no bad memory");
        }

        [Fact]
        public void A_meal_in_a_furnished_parlour_is_remembered_and_one_in_a_pit_house_is_not()
        {
            Town t = Founded("comfort-dining", "Industrial", 20, "UrbanSanitation", "IndustrialCapitalism");
            Dwelling terrace = House(t, "House_Industrial", t.Households[0], "Furnishing_Industrial");
            Pawn p = MemberOf(t, t.Households[0]);
            Thing table = t.Map.listerThings.ThingsOfDef(FurnitureThingDefOf.DiningTable).Single();

            IngestThoughtUtility.ApplyThoughts(p, Def("MealSimple"), table);
            Assert.True(MemoryMood(p, IngestThoughtDefOf.AteInImpressiveDiningRoom) > 0f, "a furnished parlour left no memory");

            // A bare table out of doors is a table, and nothing more.
            p.needs.mood!.thoughts.memories.RemoveMemoriesOfDef(IngestThoughtDefOf.AteInImpressiveDiningRoom);
            Thing outdoors = Spawn(t, FurnitureThingDefOf.DiningTable, OpenCellNear(t, Centre(terrace)), StuffDefOf.WoodLog);
            Rooms(t.Map);
            IngestThoughtUtility.ApplyThoughts(p, Def("MealSimple"), outdoors);
            Assert.False(Holds(p, IngestThoughtDefOf.AteInImpressiveDiningRoom));
            Assert.False(Holds(p, IngestThoughtDefOf.AteWithoutTable));
        }

        // ---- the floor ----

        [Fact]
        public void A_late_era_town_that_has_furnished_its_houses_and_cooks_well_lives_above_its_expectations()
        {
            // Information: expectations stand at nothing. The town has its houses, furnished in its age, and its
            // cooks' feast platters in the larder by the houses.
            Town t = Founded("comfort-floor", "Information", 20, "Suburbs", "Plastics", "FoodScience");
            var homes = new List<Dwelling>();
            foreach (int h in t.Households)
            {
                Dwelling? d = ShelterKit.RaiseOnPlot(t.Map, ShelterKit.Rung("House_Information"), h, t.Faction);
                if (d == null) break;
                foreach (IntVec3 c in d.Spots(DwellingPartKind.TableSpot)) Spawn(t, FurnitureThingDefOf.DiningTable, c, StuffDefOf.WoodLog);
                foreach (IntVec3 c in d.Spots(DwellingPartKind.FurnishingSpot)) Spawn(t, FurnitureThingDefOf.Furnishing_Information, c);
                homes.Add(d);
            }
            Rooms(t.Map);
            DwellingUtility.AssignHomes(t.Settlement, t.Map);
            foreach (Dwelling d in homes)
            {
                Thing platter = Spawn(t, Def("MealLavish"), OpenCellNear(t, Centre(d)));
                platter.stackCount = 10;
            }

            for (int i = 0; i < 2 * GenDate.TicksPerDay; i++) t.Game.TickManager.DoSingleTick();

            List<Pawn> housed = t.Settlement.Citizens.Where(p => !p.Dead && p.needs?.mood != null && homes.Any(d => d.householdId == p.relations.familyId)).ToList();
            Assert.NotEmpty(housed);
            float mean = housed.Average(p => p.needs.mood!.CurLevel);
            int comforted = housed.Count(p => p.needs.mood!.thoughts.memories.Memories.Any(m =>
                m.def == IngestThoughtDefOf.AteLavishMeal || m.def == IngestThoughtDefOf.AteInImpressiveDiningRoom
                || (m.def == SleepThoughtDefOf.SleptInBedroom && m.MoodOffset() > 0f)));
            output.WriteLine($"{housed.Count} housed citizens, mean mood {mean:0.00}, {comforted} holding a comfort memory");
            foreach (Pawn p in housed.Take(3))
            {
                output.WriteLine(p + ": " + string.Join(", ", p.needs.mood!.thoughts.MoodThoughtGroupsAsComputed()
                    .Select(g => g[0].def.defName + " " + ThoughtHandler.StackedMoodOffset(g).ToString("0.0"))));
            }
            Assert.True(comforted * 2 >= housed.Count, $"only {comforted} of {housed.Count} housed citizens felt any comfort");
            Assert.True(mean >= 0.55f, $"mean mood {mean:0.00} of the housed");
        }
    }
}
