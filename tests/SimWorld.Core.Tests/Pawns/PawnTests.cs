using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    public class PawnHolder : IExposable
    {
        public List<Pawn>? pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    public class PawnTests : ContentTestBase
    {
        public PawnTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Humans_get_every_need_and_animals_only_the_bodily_ones()
        {
            Pawn human = NewHuman();
            Assert.Equal(new[] { "Food", "Rest", "Joy", "Mood", "Beauty", "Comfort", "Outdoors", "RoomSize" }, human.needs.AllNeeds.Select(n => n.def.defName));
            Assert.NotNull(human.needs.food);
            Assert.NotNull(human.needs.mood);

            var dog = new Pawn(Husky, "Rex");
            Assert.Equal(new[] { "Food", "Rest" }, dog.needs.AllNeeds.Select(n => n.def.defName));
            Assert.Null(dog.needs.mood);
            Assert.Equal(0.86f, dog.needs.food!.MaxLevel);
        }

        [Fact]
        public void Ids_and_load_ids_are_stable_and_unique()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Assert.Equal(0, a.thingIDNumber);
            Assert.Equal(1, b.thingIDNumber);
            Assert.Equal("Thing_Human0", a.GetUniqueLoadID());
            Assert.Equal("Human1", b.ThingID);
        }

        [Fact]
        public void Hash_interval_spreads_pawns_across_ticks()
        {
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            var hitsA = new List<int>();
            var hitsB = new List<int>();
            for (int t = 1; t <= 300; t++)
            {
                Find.TickManager.DoSingleTick();
                if (a.IsHashIntervalTick(150)) hitsA.Add(t);
                if (b.IsHashIntervalTick(150)) hitsB.Add(t);
            }
            Assert.Equal(2, hitsA.Count);
            Assert.Equal(2, hitsB.Count);
            Assert.NotEqual(hitsA[0], hitsB[0]);
        }

        [Fact]
        public void Traits_conflict_and_degree_data_resolve()
        {
            TraitDef psychopath = Trait("Psychopath");
            TraitDef kind = Trait("Kind");
            TraitDef mood = Trait("NaturalMood");
            Assert.True(psychopath.ConflictsWith(kind));
            Assert.True(kind.ConflictsWith(psychopath));
            Assert.False(kind.ConflictsWith(mood));
            Assert.Equal("sanguine", mood.DataAtDegree(2).label);
            Assert.True(mood.HasDegree(-2));
            Assert.False(mood.HasDegree(3));

            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(mood, -1));
            Assert.True(p.story.traits.HasTrait(mood));
            Assert.True(p.story.traits.HasTrait(mood, -1));
            Assert.False(p.story.traits.HasTrait(mood, 1));
            Assert.Equal(-1, p.story.traits.DegreeOfTrait(mood));
            Assert.Equal("pessimist", p.story.traits.GetTrait(mood)!.Label);
        }

        [Fact]
        public void Pawns_round_trip_through_Scribe_with_needs_thoughts_traits_and_state()
        {
            Pawn a = NewHuman("Ada");
            Pawn b = NewHuman("Bo");
            a.story.traits.GainTrait(new Trait(Trait("NaturalMood"), 1));
            a.needs.food!.CurLevel = 0.42f;
            a.needs.joy!.GainJoy(0.3f, DefDatabase<JoyKindDef>.GetNamed("Social"));
            ThoughtDef insulted = DefDatabase<ThoughtDef>.GetNamed("Insulted");
            a.needs.mood!.thoughts.memories.TryGainMemory(insulted, b);
            a.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("AteWithoutTable"));
            a.needs.mood.thoughts.memories.Memories[0].age = 1500;
            Assert.True(a.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("Wander_Sad"), "test", causedByMood: true));
            a.mindState.mentalStateHandler.CurState!.age = 777;

            var holder = new PawnHolder { pawns = new List<Pawn> { a, b } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn la = loaded.pawns![0], lb = loaded.pawns[1];
            Assert.Equal("Ada", la.name);
            Assert.Same(Human, la.def);
            Assert.Equal(0, la.thingIDNumber);
            Assert.Equal(0.42f, la.needs.food!.CurLevel);
            Assert.InRange(la.needs.joy!.tolerances[DefDatabase<JoyKindDef>.GetNamed("Social")], 0.19f, 0.2f);
            Assert.Equal(1, la.story.traits.DegreeOfTrait(Trait("NaturalMood")));
            Assert.Equal(2, la.needs.mood!.thoughts.memories.Memories.Count);
            Thoughts.Thought_Memory memory = la.needs.mood.thoughts.memories.Memories[0];
            Assert.Same(insulted, memory.def);
            Assert.Same(lb, memory.otherPawn);
            Assert.Same(la, memory.pawn);
            Assert.Equal(1500, memory.age);
            Assert.Equal("Wander_Sad", la.MentalStateDef!.defName);
            Assert.Equal(777, la.mindState.mentalStateHandler.CurState!.age);
            Assert.True(la.mindState.mentalStateHandler.CurState.causedByMood);

            // Loaded objects are fully wired: a full day of ticking must not throw and must move needs.
            RunTicks(GenDate.TicksPerDay / 4, la, lb);
            Assert.True(la.needs.food.CurLevel < 0.42f);
            Assert.Equal(2, Pawn.AllocateThingId());
        }
    }
}
