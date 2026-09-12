using System.Collections.Generic;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Content
{
    /// <summary>Loads the shipped core content once for every test in the "GlobalDefs" collection.</summary>
    public class CoreContentFixture
    {
        public DefDatabase Database { get; }
        public DefLoadResult Result { get; }

        public CoreContentFixture()
        {
            Database = new DefDatabase();
            Result = CoreContent.Load(Database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true });
        }
    }

    /// <summary>
    /// Tests that read <see cref="DefDatabase.Global"/> share this collection so they never run in parallel
    /// with each other; each test class re-points Global at the fixture's database in its constructor.
    /// </summary>
    [CollectionDefinition("GlobalDefs")]
    public class GlobalDefsCollection : ICollectionFixture<CoreContentFixture>
    {
    }

    [Collection("GlobalDefs")]
    public abstract class ContentTestBase
    {
        protected CoreContentFixture Content { get; }

        protected ContentTestBase(CoreContentFixture content, int seed = 12345)
        {
            Content = content;
            DefDatabase.Global = content.Database;

            // Drop every thread-static service before rebuilding the ones each test needs. Without this,
            // anything a previous test left behind leaks into the next one on the same thread — finished
            // research being the one that bit: a test asserting a thing is NOT yet researchable passed alone
            // and failed in a subset, purely on ordering. Find.Reset() exists for exactly this, and using it
            // means a new service can never be forgotten here the way ResearchManager was.
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();

            // And map ids, for exactly the reason thing ids are reset: SimWorld.Map.Map.ResetMapIdCounter's
            // own doc spells it out. Building.WildPlantSpawner seeds its rolls from the map's id, so without
            // this a map-using test's outcome depended on how many maps every earlier test in the run built,
            // and one of them passed alone and failed in the suite the moment another class started making
            // maps.
            SimWorld.Map.Map.ResetMapIdCounter();
        }

        protected static ThingDef Human => DefDatabase<ThingDef>.GetNamed("Human");

        protected static ThingDef Husky => DefDatabase<ThingDef>.GetNamed("Husky");

        protected static Pawn NewHuman(string name = "Test") => new Pawn(Human, name);

        protected static TraitDef Trait(string defName) => DefDatabase<TraitDef>.GetNamed(defName);

        /// <summary>Registers the pawns and advances the shared clock; pawns tick through the tick lists as in-game.</summary>
        protected static void RunTicks(int ticks, params Pawn[] pawns)
        {
            TickManager tm = Find.TickManager;
            foreach (Pawn p in pawns)
            {
                if (!tm.TickListFor(TickerType.Normal)!.Contains(p)) tm.RegisterAllTickabilityFor(p);
            }
            for (int i = 0; i < ticks; i++) tm.DoSingleTick();
        }
    }

    public class CoreContentTests : ContentTestBase
    {
        public CoreContentTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Core_content_loads_with_no_errors()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(Content.Result.Defs.Count > 30);
        }

        [Fact]
        public void Expected_def_families_are_present()
        {
            Assert.Equal(8, DefDatabase<global::SimWorld.Needs.NeedDef>.DefCount);
            Assert.Equal(11, DefDatabase<global::SimWorld.MindState.MentalBreakDef>.DefCount);
            Assert.Equal(12, DefDatabase<global::SimWorld.MindState.MentalStateDef>.DefCount);
            Assert.True(DefDatabase<global::SimWorld.Thoughts.ThoughtDef>.DefCount >= 15);
            Assert.NotNull(global::SimWorld.Needs.NeedDefOf.Mood);
            Assert.Equal("Mood", global::SimWorld.Needs.NeedDefOf.Mood.defName);
        }

        [Fact]
        public void Content_directory_is_located_relative_to_the_repository()
        {
            Assert.NotNull(CoreContent.DataDirectory);
            Assert.EndsWith("Data", CoreContent.DataDirectory!);
        }
    }
}
