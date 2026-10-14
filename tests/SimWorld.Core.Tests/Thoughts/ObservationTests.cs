using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Thoughts
{
    /// <summary>
    /// Seeing the dead hurts (<see cref="PawnObserver"/>, <see cref="Thought_MemoryObservation"/>,
    /// <see cref="CorpseThing.GiveObservedThought"/>). Before this a body lay where it fell and nobody minded:
    /// a corpse was a Thing with no thought attached, so a settlement could walk past a raid's worth of its own
    /// dead for ever and never have a bad day over it.
    /// <para/>
    /// Everything here is behaviour, not the XML's literals (they are recalled, not sourced — see
    /// <c>Thoughts_Observation.xml</c>): a body is a bad moment, a rotting one a worse moment, the moment fades
    /// once the body is out of sight, and a crowd of bodies costs more than one without running away.
    /// </summary>
    public class ObservationTests : ContentTestBase
    {
        public ObservationTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
        }

        private static CoreMap NewMap() => new CoreMap(40, 40, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThoughtDef Fresh => ObservationThoughtDefOf.ObservedLayingCorpse;

        private static ThoughtDef Rotting => ObservationThoughtDefOf.ObservedLayingRottingCorpse;

        private static Pawn Spawn(CoreMap map, IntVec3 cell, string name = "Watcher")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>Somebody who dies where they stand and leaves a body on the cell.</summary>
        private static CorpseThing Body(CoreMap map, IntVec3 cell, string name = "Dead")
        {
            Pawn p = Spawn(map, cell, name);
            p.health.Kill(null, null);
            Assert.NotNull(p.corpse);
            return p.corpse!;
        }

        private static List<Thought_Memory> Memories(Pawn p) => p.needs.mood!.thoughts.memories.Memories.ToList();

        private static int Count(Pawn p, ThoughtDef def) => Memories(p).Count(m => ReferenceEquals(m.def, def));

        private static void Look(Pawn p) => p.needs.mood!.observer.ObserveSurroundingThings();

        private static void Rot(CorpseThing corpse)
        {
            CompRottable rot = corpse.RotComp!;
            rot.RotProgress = rot.Props.TicksToRotStart + 1f;
            Assert.NotEqual(RotStage.Fresh, corpse.RotStageNow);
        }

        // ---- who gets it, and from what ----

        [Fact]
        public void A_sighted_pawn_beside_a_fresh_human_body_gains_the_fresh_corpse_thought()
        {
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            Assert.Equal(RotStage.Fresh, body.RotStageNow);

            Look(watcher);

            Assert.Equal(1, Count(watcher, Fresh));
            Assert.Equal(0, Count(watcher, Rotting));
            Assert.True(watcher.needs.mood!.thoughts.TotalMoodOffset() < 0f, "seeing the dead has to cost the watcher something");
        }

        [Fact]
        public void A_rotting_body_gives_the_worse_thought_and_only_that_one()
        {
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10));
            Rot(body);
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));

            Look(watcher);

            Assert.Equal(0, Count(watcher, Fresh));
            Assert.Equal(1, Count(watcher, Rotting));
            Assert.True(Rotting.stages[0].baseMoodEffect < Fresh.stages[0].baseMoodEffect,
                "a rotting body has to be a worse sight than a fresh one");
        }

        [Fact]
        public void An_animals_body_gives_no_thought()
        {
            CoreMap map = NewMap();
            var dog = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(dog, new IntVec3(10, 0, 10), map);
            dog.health.Kill(null, null);
            Assert.NotNull(dog.corpse);
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));

            Look(watcher);

            Assert.Empty(Memories(watcher));
        }

        [Fact]
        public void A_body_out_of_line_of_sight_gives_no_thought()
        {
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(13, 0, 10));
            for (int z = 8; z <= 12; z++) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(11, 0, z), map);
            Assert.False(GenSight.LineOfSight(body.Position, watcher.Position, map, skipFirstCell: true));

            Look(watcher);

            Assert.Empty(Memories(watcher));
        }

        [Fact]
        public void A_blind_pawn_notices_nothing()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            foreach (string eye in new[] { "left eye", "right eye" })
            {
                cut.Worker.Apply(new DamageInfo(cut, 30f, hitPart: watcher.RaceProps.body!.GetPartByLabel(eye)!), watcher);
            }
            Assert.False(watcher.health.capacities.CapableOf(PawnCapacityDefOf.Sight), "the fixture has to have blinded the pawn");
            Assert.False(watcher.Dead);

            Look(watcher);

            Assert.Empty(Memories(watcher));
        }

        [Fact]
        public void A_body_beyond_the_sampled_cells_is_not_seen()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(30, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(10, 0, 10));

            Look(watcher);

            Assert.Empty(Memories(watcher));
        }

        [Theory]
        [InlineData("Psychopath")]
        [InlineData("Bloodlust")]
        public void The_people_death_does_not_disturb_are_not_disturbed(string trait)
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            watcher.story.traits.GainTrait(new Trait(Trait(trait)));

            Look(watcher);

            Assert.Equal(0f, watcher.needs.mood!.thoughts.TotalMoodOffset());
            Assert.Empty(Memories(watcher));
        }

        // ---- stacking, per target ----

        [Fact]
        public void Seeing_the_same_body_again_renews_one_memory_instead_of_adding_another()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));

            Look(watcher);
            Thought_Memory first = Memories(watcher).Single();
            first.age = 900;
            Look(watcher);

            Thought_Memory only = Memories(watcher).Single();
            Assert.Same(first, only);
            Assert.Equal(0, only.age);
        }

        [Fact]
        public void Two_different_bodies_are_two_memories_that_stack_in_the_mood_without_doubling_it()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10), "A");
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            Look(watcher);
            float one = watcher.needs.mood!.thoughts.TotalMoodOffset();

            Body(map, new IntVec3(10, 0, 12), "B");
            Look(watcher);
            float two = watcher.needs.mood!.thoughts.TotalMoodOffset();

            Assert.Equal(2, Count(watcher, Fresh));
            Assert.True(two < one, "a second body has to cost something more");
            Assert.True(two > 2f * one, "alike thoughts stack with diminishing weight, not at full price");
        }

        [Fact]
        public void A_crowd_of_bodies_is_remembered_one_by_one_and_the_cost_is_bounded()
        {
            CoreMap map = NewMap();
            Pawn watcher = Spawn(map, new IntVec3(20, 0, 20));
            for (int i = 0; i < 12; i++) Body(map, new IntVec3(18 + i % 4, 0, 17 + i / 4), "Dead" + i);

            Look(watcher);

            // The handler's own stack limit would have evicted the oldest of these if the def's were low: the
            // limit is inert by design (RimWorld never enforces one on an observation).
            Assert.Equal(12, Count(watcher, Fresh));
            float offset = watcher.needs.mood!.thoughts.TotalMoodOffset();
            float single = Fresh.stages[0].baseMoodEffect;
            Assert.True(offset < single);
            Assert.True(offset > single / (1f - Fresh.stackedEffectMultiplier) - 0.01f,
                "n alike thoughts can never cost more than 1/(1-m) times one of them");
        }

        // ---- the hit is worked off: it fades once the body is out of sight ----

        [Fact]
        public void The_memory_of_a_body_fades_once_it_is_out_of_sight()
        {
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            Look(watcher);
            Assert.Equal(1, Count(watcher, Fresh));

            body.Destroy();

            // A day is longer than the memory lasts (a quarter of one in RimWorld); asserting that it is
            // gone within a day and not within an hour pins the shape without the literal.
            watcher.needs.mood!.thoughts.ThoughtIntervalBulk(GenDate.TicksPerHour);
            Assert.Equal(1, Count(watcher, Fresh));
            watcher.needs.mood!.thoughts.ThoughtIntervalBulk(GenDate.TicksPerDay);
            Assert.Equal(0, Count(watcher, Fresh));
            Assert.Equal(0f, watcher.needs.mood!.thoughts.TotalMoodOffset());
        }

        [Fact]
        public void A_body_that_stays_in_view_keeps_the_memory_alive()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));

            for (int hour = 0; hour < 24; hour++)
            {
                Look(watcher);
                watcher.needs.mood!.thoughts.ThoughtIntervalBulk(GenDate.TicksPerHour);
            }

            Assert.Equal(1, Count(watcher, Fresh));
        }

        // ---- the measurement control ----

        [Fact]
        public void Ablating_the_effect_switches_the_thought_off_and_nothing_else()
        {
            CoreMap map = NewMap();
            CorpseThing body = Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            var counter = new GiverCounter { def = Def("WoodLog") };
            GenSpawn.Spawn(counter, new IntVec3(11, 0, 10), map);

            Ablation.Disable(CorpseThing.AblationName);
            try
            {
                Assert.Null(body.GiveObservedThought());
                Look(watcher);
                Assert.Empty(Memories(watcher));
                Assert.True(counter.Asked > 0, "the look still happens; only the effect is gone");
            }
            finally
            {
                Ablation.Clear();
            }

            Look(watcher);
            Assert.Equal(1, Count(watcher, Fresh));
        }

        // ---- when it runs ----

        [Fact]
        public void The_observer_runs_on_the_need_interval_and_not_every_tick()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            var counter = new GiverCounter { def = Def("WoodLog") };
            GenSpawn.Spawn(counter, new IntVec3(11, 0, 10), map);

            const int ticks = 3000;
            RunTicks(ticks, watcher);

            Assert.Equal(1, Count(watcher, Fresh));
            // Each look is one need interval in four or so (150 ticks each): about once per 600 ticks. A scan
            // per tick would be 3000; one per need interval would be 20.
            Assert.InRange(counter.Asked, 2, 12);
        }

        [Fact]
        public void A_pawn_that_is_not_on_a_map_looks_at_nothing()
        {
            CoreMap map = NewMap();
            var counter = new GiverCounter { def = Def("WoodLog") };
            GenSpawn.Spawn(counter, new IntVec3(11, 0, 10), map);
            Pawn watcher = NewHuman("Elsewhere");

            for (int i = 0; i < 40; i++) watcher.needs.mood!.observer.ObserverInterval();

            Assert.Equal(0, counter.Asked);
            Assert.Empty(Memories(watcher));
        }

        // ---- save and load ----

        [Fact]
        public void An_observation_memory_round_trips_and_still_merges_by_target()
        {
            CoreMap map = NewMap();
            Body(map, new IntVec3(10, 0, 10));
            Pawn watcher = Spawn(map, new IntVec3(12, 0, 10));
            Look(watcher);
            Memories(watcher).Single().age = 1234;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn reloaded = loaded.mapPawns.AllPawnsSpawned.Single(p => p.Label == "Watcher");
            var memory = Assert.IsType<Thought_MemoryObservation>(Memories(reloaded).Single());
            Assert.Equal(1234, memory.age);
            Assert.Same(Fresh, memory.def);
            CorpseThing body = loaded.listerThings.AllThings.OfType<CorpseThing>().Single();
            Assert.Equal(body.thingIDNumber, memory.TargetThingID);

            Look(reloaded);

            Assert.Single(Memories(reloaded));
            Assert.Equal(0, Memories(reloaded).Single().age);
        }

        // ---- test doubles ----

        /// <summary>A Thing that counts how often it is asked for a thought, to measure how often a pawn looks.</summary>
        private sealed class GiverCounter : Thing, IThoughtGiver
        {
            public int Asked;

            public Thought_Memory? GiveObservedThought()
            {
                Asked++;
                return null;
            }
        }
    }
}
