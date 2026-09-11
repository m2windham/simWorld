using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Needs
{
    /// <summary>
    /// What actually moves a need, and what does not move at all.
    ///
    /// <para/><b>The defect these tests were written after.</b> <c>Need.NeedInterval</c> carried a generic
    /// decay guarded by <c>if (!IsFrozen &amp;&amp; def.fallPerDay &gt; 0f)</c>. No shipped <c>NeedDef</c> sets
    /// <c>fallPerDay</c> and no shipped <c>needClass</c> reaches the base method anyway, so the path was dead
    /// twice over — and because the guard was a silent early-out, a need with no rate looked exactly like a
    /// need deliberately without one. RimWorld has no such path at all (<c>Need.NeedInterval</c> is abstract
    /// there, and <c>fallPerDay</c> is read only by the unported <c>Need_Chemical</c>), so the fix was to
    /// delete it rather than fill it in. These tests pin what that leaves: which of the eight shipped needs
    /// moves, by what mechanism, and — for the ones that hold still — that the reason is a missing driver and
    /// not a missing number.
    ///
    /// <para/>Rates asserted here are the ones the classes declare and source from RimWorld
    /// (<see cref="Need_Outdoors"/>'s deltas, <c>Need_Food.BaseFoodFallPerTick</c>,
    /// <c>Need_Joy.BaseFallPerInterval</c>), so a day's decay is checked against the declared rate rather than
    /// against a literal nobody can source.
    /// </summary>
    public class NeedDecayTests : ContentTestBase
    {
        public NeedDecayTests(CoreContentFixture content) : base(content)
        {
        }

        private static NeedDef NeedDefNamed(string defName) => DefDatabase<NeedDef>.GetNamed(defName);

        private static Need NeedOf(Pawn p, string defName) => p.needs.TryGetNeed(NeedDefNamed(defName))!;

        private static Need_Outdoors OutdoorsOf(Pawn p) => (Need_Outdoors)NeedOf(p, "Outdoors");

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        /// <summary>Walls and a roof around <paramref name="rect"/>, then one map tick so the room actually
        /// floods — rooms are lazy, and without the tick every cell reads as open outdoors.</summary>
        private static void BuildRoom(CoreMap map, CellRect rect, RoofDef? roof = null)
        {
            foreach (IntVec3 c in rect.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, roof ?? RoofDefOf.RoofConstructed);
            map.MapTick();
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // -----------------------------------------------------------------------------------------------
        // The inventory: which needs move, and on what.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole answer in one place, for all eight needs. Three carry their own rate and move for a
        /// citizen doing nothing anywhere. Mood moves because those three did — it is a seeker chasing the
        /// thought total, and a day spent hungry, sleepless and bored is three situational thoughts. Two more
        /// move only once the citizen is somewhere: Beauty wants a map, Outdoors wants a roof. Two cannot
        /// move at all today and say so in their own Def. No ninth behaviour, and nothing that moves
        /// "generically".
        /// </summary>
        [Fact]
        public void Three_needs_move_unprompted_mood_follows_them_two_want_a_situation_and_two_cannot_move()
        {
            Assert.Equal(8, DefDatabase<NeedDef>.DefCount);

            Pawn alone = NewHuman();
            Dictionary<string, float> before = alone.needs.AllNeeds.ToDictionary(n => n.def.defName, n => n.CurLevel);
            RunTicks(GenDate.TicksPerDay, alone);

            // Moves on its own: each of these carries its own rate inside its own class.
            Assert.True(NeedOf(alone, "Food").CurLevel < before["Food"], "Food should fall for anybody.");
            Assert.True(NeedOf(alone, "Rest").CurLevel < before["Rest"], "Rest should fall for anybody awake.");
            Assert.True(NeedOf(alone, "Joy").CurLevel < before["Joy"], "Joy should fall for anybody.");

            // Moves because something else did: mood chases the thought total, and a day of going hungry,
            // sleepless and bored is three situational thoughts. Mood needs no rate of its own and has none —
            // it is a seeker, and what it seeks is the other needs' consequences.
            Assert.True(NeedOf(alone, "Mood").CurLevel < before["Mood"], "Mood should follow the day the citizen had.");
            Assert.True(alone.needs.mood!.thoughts.TotalMoodOffset() < 0f);
            Assert.Equal(alone.needs.mood.CurInstantLevel, NeedOf(alone, "Mood").CurLevel, 1);

            // Needs a situation, and this citizen is in none: they are on no map (Beauty), and off-map counts
            // as open air, where the outdoors need is already full (Outdoors).
            Assert.Equal(before["Beauty"], NeedOf(alone, "Beauty").CurLevel, 4);
            Assert.Equal(1f, OutdoorsOf(alone).CurLevel);
            Assert.True(OutdoorsOf(alone).DeltaPerDay > 0f,
                "Outdoors is not stuck at 1 — it is being pushed up against its ceiling by open air.");

            // ...and both move once they have one.
            CoreMap map = NewMap();
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            Pawn resident = SpawnHuman(map, new IntVec3(6, 0, 6), "Resident");
            // The map now answers for beauty, where for an off-map citizen it answered null. (A bare room is
            // "no opinion" and reads as exactly the need's base level by design, so what is asserted here is
            // that somebody answered at all — BeautyTests covers the need moving once the room is filthy.)
            Assert.NotNull(MapEnvironmentSampler.Instance.InstantLevelFor(NeedDefNamed("Beauty"), resident));
            Assert.Null(MapEnvironmentSampler.Instance.InstantLevelFor(NeedDefNamed("Beauty"), alone));
            Assert.True(OutdoorsOf(resident).DeltaPerDay < 0f,
                "A roof over the citizen's head should be draining the outdoors need.");

            // Cannot move at all — its own test below says why; listed here so the inventory is complete.
            Assert.Equal(before["Comfort"], NeedOf(alone, "Comfort").CurLevel, 4);
            Assert.Equal(before["RoomSize"], NeedOf(alone, "RoomSize").CurLevel, 4);
        }

        /// <summary>Food, Rest and Joy over a quarter day — short enough that none of the three crosses a band
        /// and changes its own rate — each against the rate its own class declares, with a tolerance of the
        /// one interval a hash-staggered citizen can gain or lose at either end.</summary>
        [Fact]
        public void The_three_self_moving_needs_each_cover_the_rate_they_declare()
        {
            Pawn p = NewHuman();
            Need_Food food = p.needs.food!;
            Need_Rest rest = p.needs.rest!;
            Need_Joy joy = p.needs.joy!;

            float foodStart = food.CurLevel;
            float restStart = rest.CurLevel;
            float joyStart = joy.CurLevel;

            const int Span = GenDate.TicksPerDay / 4;
            RunTicks(Span, p);

            AssertFellBy(food.CurLevel, foodStart, Need_Food.BaseFoodFallPerTick * p.HungerRate * Span,
                Need_Food.BaseFoodFallPerTick * Need.IntervalTicks);
            AssertFellBy(rest.CurLevel, restStart, Need_Rest.BaseRestFallPerTick * Span,
                Need_Rest.BaseRestFallPerTick * Need.IntervalTicks);
            AssertFellBy(joy.CurLevel, joyStart, Need_Joy.BaseFallPerInterval * (Span / Need.IntervalTicks),
                Need_Joy.BaseFallPerInterval);

            Assert.Equal(HungerCategory.Fed, food.CurCategory);
            Assert.Equal(RestCategory.Rested, rest.CurCategory);
            Assert.Equal(JoyCategory.Satisfied, joy.CurCategory);
        }

        private static void AssertFellBy(float now, float start, float expectedFall, float tolerance)
        {
            Assert.InRange(start - now, expectedFall - tolerance, expectedFall + tolerance);
        }

        /// <summary>The end state each need is supposed to produce, for a citizen nobody looks after. This is
        /// the test that would have failed loudest had the generic path ever been the thing moving them.</summary>
        [Fact]
        public void A_citizen_left_alone_long_enough_starves_collapses_and_runs_out_of_recreation()
        {
            Pawn p = NewHuman();
            RunTicks(GenDate.TicksPerDay * 3, p);

            Assert.Equal(HungerCategory.Starving, p.needs.food!.CurCategory);
            Assert.True(p.needs.food.TicksStarving > 0);
            Assert.Equal(RestCategory.Exhausted, p.needs.rest!.CurCategory);
            Assert.True(p.needs.rest.TicksAtZero > 0);
            Assert.Equal(JoyCategory.Empty, p.needs.joy!.CurCategory);
        }

        // -----------------------------------------------------------------------------------------------
        // Outdoors: the need this lane unfroze.
        // -----------------------------------------------------------------------------------------------

        [Fact]
        public void A_citizen_sealed_under_a_roof_loses_the_outdoors_need_and_one_left_outside_does_not()
        {
            CoreMap map = NewMap(24);
            BuildRoom(map, new CellRect(2, 2, 10, 10));

            Pawn inside = SpawnHuman(map, new IntVec3(6, 0, 6), "Inside");
            Pawn outside = SpawnHuman(map, new IntVec3(20, 0, 20), "Outside");
            Need_Outdoors indoors = OutdoorsOf(inside);
            Need_Outdoors openAir = OutdoorsOf(outside);

            Assert.Equal(OutdoorsCategory.Free, indoors.CurCategory);
            Assert.Equal(Need_Outdoors.Delta_IndoorsThinRoof, indoors.DeltaPerDay);
            Assert.Equal(Need_Outdoors.Delta_OutdoorsNoRoof, openAir.DeltaPerDay);

            RunTicks(GenDate.TicksPerDay * 2, inside, outside);

            Assert.True(indoors.CurLevel < 0.5f, "Two days indoors should be most of the way to cabin fever.");
            Assert.True(indoors.CurCategory < OutdoorsCategory.Free);
            Assert.Equal(1f, openAir.CurLevel);

            // ...and an ordinary roof never entombs anybody: the fall stops at the floor and stays there.
            RunTicks(GenDate.TicksPerDay * 3, inside, outside);
            Assert.Equal(Need_Outdoors.Minimum_IndoorsThinRoof, indoors.CurLevel, 4);
            Assert.Equal(OutdoorsCategory.CabinFeverSevere, indoors.CurCategory);
        }

        /// <summary>
        /// The reachable situations, ranked. Open sky is best, a mountain is worst, and only thick rock takes
        /// the floor away — which is what makes a mountain base different in kind rather than in degree.
        /// (RimWorld's sixth case, indoors with no roof, is unreachable in both games: a room with an unroofed
        /// cell is by definition open to the weather, so that cell is an outdoor one.)
        /// </summary>
        [Fact]
        public void The_outdoors_situations_rank_open_sky_best_and_a_mountain_worst()
        {
            CoreMap map = NewMap(40);
            BuildRoom(map, new CellRect(2, 2, 8, 8));
            BuildRoom(map, new CellRect(14, 2, 8, 8), RoofDefOf.RoofRockThick);

            map.roofGrid.SetRoof(new IntVec3(30, 0, 6), RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(new IntVec3(34, 0, 6), RoofDefOf.RoofRockThick);
            map.MapTick();

            float openSky = OutdoorsOf(SpawnHuman(map, new IntVec3(30, 0, 12), "Sky")).DeltaPerDay;
            float shelter = OutdoorsOf(SpawnHuman(map, new IntVec3(30, 0, 6), "Shelter")).DeltaPerDay;
            float overhang = OutdoorsOf(SpawnHuman(map, new IntVec3(34, 0, 6), "Overhang")).DeltaPerDay;
            float indoors = OutdoorsOf(SpawnHuman(map, new IntVec3(6, 0, 6), "Indoors")).DeltaPerDay;
            float mountain = OutdoorsOf(SpawnHuman(map, new IntVec3(18, 0, 6), "Mountain")).DeltaPerDay;

            Assert.True(openSky > shelter, "Open sky should beat standing under a roofed shelter.");
            Assert.True(shelter > 0f, "A roofed cell in the open still counts as being out of doors.");
            Assert.True(overhang < 0f, "A thick overhang stops counting as being outdoors at all.");
            Assert.True(indoors < 0f, "A roofed room should drain the need.");
            Assert.True(mountain < indoors, "A mountain should drain faster than an ordinary room.");
        }

        // -----------------------------------------------------------------------------------------------
        // The two that hold still, and why.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Comfort and RoomSize never move. Pinned here with the reason beside it, because the reason is the
        /// point: it is not a rate nobody filled in — both carry real seeker rates — it is that nothing
        /// answers what their target should be, so the target is always exactly where the need already sits.
        /// The day somebody drives one of them, this is the test that fails.
        /// </summary>
        [Fact]
        public void Comfort_and_room_size_hold_because_nothing_answers_for_them_not_because_a_rate_is_missing()
        {
            CoreMap map = NewMap();
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            Pawn p = SpawnHuman(map, new IntVec3(6, 0, 6), "Resident");

            foreach (string defName in new[] { "Comfort", "RoomSize" })
            {
                NeedDef def = NeedDefNamed(defName);
                Need need = NeedOf(p, defName);

                Assert.True(def.seekerRisePerHour > 0f && def.seekerFallPerHour > 0f,
                    defName + " has rates; they simply have nothing to carry it toward.");
                Assert.Null(MapEnvironmentSampler.Instance.InstantLevelFor(def, p));
                Assert.Equal(def.baseLevel, need.CurLevel);
                Assert.Equal(need.CurLevel, need.CurInstantLevel);
            }

            float comfort = NeedOf(p, "Comfort").CurLevel;
            float roomSize = NeedOf(p, "RoomSize").CurLevel;
            RunTicks(GenDate.TicksPerDay, p);
            Assert.Equal(comfort, NeedOf(p, "Comfort").CurLevel, 4);
            Assert.Equal(roomSize, NeedOf(p, "RoomSize").CurLevel, 4);
        }

        // -----------------------------------------------------------------------------------------------
        // The generic path itself.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// No need inherits a decay any more, and none is allowed to ask for one. The structural half
        /// (<c>NeedInterval</c> is abstract) is what stops the silent fallback growing back; the content half
        /// is that <c>fallPerDay</c> is untouched by all eight Defs, which is also RimWorld's arrangement.
        /// </summary>
        [Fact]
        public void Nothing_inherits_a_generic_decay_and_content_that_asks_for_one_is_refused()
        {
            Assert.True(typeof(Need).GetMethod(nameof(Need.NeedInterval))!.IsAbstract,
                "A base NeedInterval that quietly does nothing is the bug this test exists for.");
            Assert.True(typeof(Need).GetMethod(nameof(Need.NeedIntervalBulk))!.IsAbstract);

            foreach (NeedDef def in DefDatabase<NeedDef>.AllDefsListForReading)
            {
                Assert.Equal(NeedDef.DefaultFallPerDay, def.fallPerDay);
                Assert.Empty(def.ConfigErrors());
            }

            var asking = new NeedDef
            {
                defName = "Test_FallPerDay",
                label = "test",
                description = "test",
                needClass = typeof(Need_Food),
                fallPerDay = 1.2f,
            };
            Assert.Contains(asking.ConfigErrors(), e => e.Contains("fallPerDay"));
        }

        /// <summary>
        /// The other half of the same ambiguity: a seeker need whose rates nobody filled in does not decay
        /// slowly, it does not move at all — indistinguishable, from the outside, from Comfort and RoomSize
        /// above. RimWorld has this check but asks <c>needClass == typeof(Need_Seeker)</c> exactly, which no
        /// real Def ever names; widening it to subclasses is what makes it able to fire.
        /// </summary>
        [Fact]
        public void A_seeker_need_that_never_had_its_rates_filled_in_is_a_load_error()
        {
            var silent = new NeedDef
            {
                defName = "Test_SilentSeeker",
                label = "test",
                description = "test",
                needClass = typeof(Need_Environment),
            };
            Assert.Contains(silent.ConfigErrors(), e => e.Contains("seeker rise/fall"));

            silent.seekerRisePerHour = 0.3f;
            silent.seekerFallPerHour = 0.3f;
            Assert.Empty(silent.ConfigErrors());

            var abstractClass = new NeedDef
            {
                defName = "Test_AbstractNeed",
                label = "test",
                description = "test",
                needClass = typeof(Need_Seeker),
            };
            Assert.Contains(abstractClass.ConfigErrors(), e => e.Contains("abstract"));
        }

        // -----------------------------------------------------------------------------------------------
        // Bulk (Interval tier) versus per-interval (Full tier).
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Every need, advanced both ways over the same elapsed time, has to land in the same place. A citizen
        /// the player looks away from drops to <see cref="PawnTier.Interval"/> and stops running
        /// <see cref="Need.NeedInterval"/> in favour of one bulk call per coarse tick, so a disagreement here
        /// is a citizen whose needs depend on whether anybody is watching.
        /// <para/>
        /// Spans stay inside one rate band, which is the only claim the bulk contract makes — it holds the
        /// current rate constant, so crossing a band mid-span is a documented approximation rather than a bug.
        /// </summary>
        [Fact]
        public void The_bulk_path_lands_where_the_per_interval_path_does_over_the_same_span()
        {
            AssertPathsAgree(40, p => p.needs.food!);
            AssertPathsAgree(40, p => p.needs.rest!);

            // Joy from full: every band above Low falls at the same rate, so 40 intervals cross none.
            AssertPathsAgree(40, p => p.needs.joy!, p => p.needs.joy!.CurLevel = 1f);

            // A seeker, through the substitution seam so the target is a fixed number rather than a map. Ten
            // intervals is well short of the 0.4 the need would have to cover to reach that target, so this
            // compares two rates rather than two arrivals at the same clamp.
            AssertPathsAgree(10, p => NeedOf(p, "Beauty"), p => p.environment = new FixedEnvironment(0.1f));

            // Mood, whose target is the thought total. Nothing expires inside the span, so both paths chase
            // the same number. (They age that memory by different amounts — the bulk path runs one thought
            // interval for the whole span, not one per slice — which shows in the memory's age, not here.)
            AssertPathsAgree(2, p => p.needs.mood!,
                p => p.needs.mood!.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("AteWithoutTable")));
        }

        [Fact]
        public void Outdoors_agrees_with_itself_in_bulk_too()
        {
            CoreMap map = NewMap(24);
            BuildRoom(map, new CellRect(2, 2, 10, 10));

            Need_Outdoors stepped = OutdoorsOf(SpawnHuman(map, new IntVec3(5, 0, 5), "Stepped"));
            Need_Outdoors bulked = OutdoorsOf(SpawnHuman(map, new IntVec3(8, 0, 8), "Bulked"));

            const int Slices = 40;
            for (int i = 0; i < Slices; i++) stepped.NeedInterval();
            bulked.NeedIntervalBulk(Slices * Need.IntervalTicks);

            Assert.Equal(stepped.CurLevel, bulked.CurLevel, 5);
            Assert.True(stepped.CurLevel < 1f, "Both citizens are indoors; the need should have moved at all.");
        }

        /// <summary>
        /// A coarse tick is 2,000 ticks and a need interval is 150, so a bulk span is almost never a whole
        /// number of intervals. The caller advances its clock by the whole span regardless, so whatever an
        /// implementation rounds away is gone for good: <see cref="Need_Joy"/> used to take
        /// <c>elapsed / 150</c> as an integer and drop 50 ticks of decay on every coarse tick, making
        /// recreation fall 2.5% slower for every citizen nobody was watching. Thirty coarse ticks against one
        /// call for the same whole day catches exactly that.
        /// </summary>
        [Fact]
        public void A_bulk_span_that_is_not_a_whole_number_of_intervals_loses_none_of_it()
        {
            const int CoarseTick = 2000;
            const int CoarseTicksPerDay = GenDate.TicksPerDay / CoarseTick;

            Pawn chunked = NewHuman("Chunked");
            Pawn whole = NewHuman("Whole");
            chunked.needs.joy!.CurLevel = 1f;
            whole.needs.joy!.CurLevel = 1f;

            for (int i = 0; i < CoarseTicksPerDay; i++) chunked.needs.joy.NeedIntervalBulk(CoarseTick);
            whole.needs.joy.NeedIntervalBulk(GenDate.TicksPerDay);

            Assert.Equal(whole.needs.joy.CurLevel, chunked.needs.joy.CurLevel, 5);
            Assert.True(chunked.needs.joy.CurLevel < 1f);
        }

        private static void AssertPathsAgree(int slices, Func<Pawn, Need> pick, Action<Pawn>? setup = null)
        {
            Pawn stepped = NewHuman("Stepped");
            Pawn bulked = NewHuman("Bulked");
            if (setup != null)
            {
                setup(stepped);
                setup(bulked);
            }

            Need a = pick(stepped);
            Need b = pick(bulked);
            b.CurLevel = a.CurLevel;
            float start = a.CurLevel;

            for (int i = 0; i < slices; i++) a.NeedInterval();
            b.NeedIntervalBulk(slices * Need.IntervalTicks);

            Assert.Equal(a.CurLevel, b.CurLevel, 5);
            Assert.NotEqual(start, a.CurLevel);   // and it moved at all, or the comparison says nothing
        }

        // -----------------------------------------------------------------------------------------------
        // Scribe.
        // -----------------------------------------------------------------------------------------------

        [Fact]
        public void Every_need_survives_a_save_and_load_at_the_level_it_had()
        {
            Pawn p = NewHuman();
            var levels = new Dictionary<string, float>();
            float level = 0.11f;
            foreach (Need need in p.needs.AllNeeds)
            {
                need.CurLevelPercentage = level;
                levels[need.def.defName] = need.CurLevel;
                level += 0.09f;
            }

            JoyKindDef social = DefDatabase<JoyKindDef>.GetNamed("Social");
            p.needs.joy!.tolerances.Notify_JoyGained(0.4f, social);
            float tolerance = p.needs.joy.tolerances[social];

            string xml = Scribe.SaveToString(p, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(levels.Count, loaded.needs.AllNeeds.Count);
            foreach (Need need in loaded.needs.AllNeeds)
            {
                Assert.Equal(levels[need.def.defName], need.CurLevel, 4);
            }

            Assert.IsType<Need_Outdoors>(loaded.needs.TryGetNeed(NeedDefNamed("Outdoors")));
            Assert.Equal(tolerance, loaded.needs.joy!.tolerances[social], 4);

            // And it goes on decaying afterwards rather than arriving frozen.
            float foodBefore = loaded.needs.food!.CurLevel;
            RunTicks(GenDate.TicksPerHour, loaded);
            Assert.True(loaded.needs.food.CurLevel < foodBefore);
        }

        private sealed class FixedEnvironment : IEnvironmentSampler
        {
            private readonly float level;

            public FixedEnvironment(float level) => this.level = level;

            public float? InstantLevelFor(NeedDef need, Pawn pawn) => level;
        }
    }
}
