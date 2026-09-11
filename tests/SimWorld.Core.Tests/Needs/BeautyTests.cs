using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Needs
{
    /// <summary>
    /// Beauty end to end: a sampler that reads the cells a citizen is standing among, a need that chases what
    /// it finds there, and a mood thought that reads the need. Before this module the chain was built and
    /// unconnected — <c>IEnvironmentSampler</c> had no implementation anywhere in <c>src/</c>, so every
    /// citizen's beauty sat at its Def's base level forever.
    /// <para/>
    /// Every tuned number here is SimWorld's own and unsourced (the beauty-to-level curve, the category
    /// thresholds, the Beauty stat values on filth and sculptures, the mood offsets), so these tests pin
    /// orderings, bands and round trips rather than literals — CLAUDE.md's rule. The one number asserted
    /// exactly is <c>0.5</c>, and only because two independent things are defined to agree there: the curve
    /// answers the need's own <c>baseLevel</c> for an unremarkable environment.
    /// </summary>
    public class BeautyTests : ContentTestBase
    {
        public BeautyTests(CoreContentFixture content) : base(content)
        {
            FilthMaker.ResetCache();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A floored surface, which generates no filth: without it a walking citizen churns dirt up
        /// out of bare soil and quietly makes its own room ugly mid-test.</summary>
        private static TerrainDef StoneRoad => DefDatabase<TerrainDef>.GetNamed("StreetStoneRoad");

        /// <summary>Walls and a roof around <paramref name="rect"/>, a paved interior, then one map tick so
        /// <see cref="Building.RoomTracker"/> actually floods — rooms are lazy, and without the tick every
        /// cell reads as open outdoors.</summary>
        private static void BuildRoom(CoreMap map, CellRect rect)
        {
            foreach (IntVec3 c in rect.EdgeCells)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            }
            foreach (IntVec3 c in rect.Cells)
            {
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                map.terrainGrid.SetTerrain(c, StoneRoad);
            }
            map.MapTick();
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static global::SimWorld.Needs.Need_Beauty BeautyOf(Pawn p) =>
            p.needs.TryGetNeed<global::SimWorld.Needs.Need_Beauty>()!;

        /// <summary>Stops a citizen cleaning away the very filth a test is measuring them against. Beauty
        /// recovering once the room is clean is a real behaviour and is asserted in <c>FilthTests</c>; here it
        /// would just be a race between the cleaning job and the need's own fall rate.</summary>
        private static void NeverCleans(Pawn pawn) =>
            pawn.workSettings.SetPriority(global::SimWorld.Work.WorkTypeDefOf.Cleaning, 0);

        private static void Dirty(CoreMap map, CellRect rect, ThingDef filth)
        {
            foreach (IntVec3 c in rect.Cells) FilthMaker.TryMakeFilth(c, map, filth, "test");
        }

        // ---- the end-to-end proof ----

        [Fact]
        public void A_citizen_living_in_filth_ends_up_with_less_beauty_than_one_in_a_clean_room()
        {
            // Two identical rooms on one map, one of them filthy, one citizen in each. This is the test the
            // module exists for: before a sampler existed both citizens sat at 0.5 forever.
            CoreMap map = NewMap(28, 14);
            var cleanRoom = new CellRect(1, 1, 12, 12);
            var filthyRoom = new CellRect(15, 1, 12, 12);
            BuildRoom(map, cleanRoom);
            BuildRoom(map, filthyRoom);
            Dirty(map, filthyRoom.ContractedBy(1), FilthDefOf.Filth_AnimalFilth);

            Pawn inClean = SpawnHuman(map, new IntVec3(7, 0, 7), "Clean");
            Pawn inFilth = SpawnHuman(map, new IntVec3(21, 0, 7), "Squalid");
            NeverCleans(inClean);
            NeverCleans(inFilth);

            RunTicks(GenDate.TicksPerHour, inClean, inFilth);

            float clean = BeautyOf(inClean).CurLevel;
            float filthy = BeautyOf(inFilth).CurLevel;

            Assert.True(filthy < clean,
                $"Living in filth should read as uglier than a clean room: {filthy} against {clean}.");
            Assert.Equal(0.5f, clean, 3);
            Assert.InRange(filthy, 0f, 0.4f);
        }

        [Fact]
        public void Beauty_answers_to_ugly_things_and_to_beautiful_ones()
        {
            // Read CurInstantLevel rather than ticking: it is the target the surroundings ask for right now,
            // so the room can be changed between readings with nothing else moving.
            CoreMap map = NewMap(16, 16);
            var room = new CellRect(2, 2, 8, 8);
            BuildRoom(map, room);
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5), "Resident");
            global::SimWorld.Needs.Need_Beauty beauty = BeautyOf(pawn);

            float bare = beauty.CurInstantLevel;
            Assert.Equal(0.5f, bare, 3);

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sculpture")), new IntVec3(3, 0, 3), map);
            float oneSculpture = beauty.CurInstantLevel;
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sculpture")), new IntVec3(8, 0, 8), map);
            float twoSculptures = beauty.CurInstantLevel;

            Assert.True(oneSculpture > bare, "Art in the room should read as prettier than a bare one.");
            Assert.True(twoSculptures > oneSculpture, "More art should read prettier still.");

            Dirty(map, new CellRect(4, 4, 4, 4), FilthDefOf.Filth_AnimalFilth);
            float withFilth = beauty.CurInstantLevel;
            Assert.True(withFilth < twoSculptures, "Filth should spoil what the art was doing.");
            Assert.True(withFilth < bare, "Enough filth should read as worse than an empty room.");
        }

        [Fact]
        public void Worse_filth_is_uglier_than_milder_filth()
        {
            CoreMap map = NewMap(16, 16);
            var room = new CellRect(2, 2, 8, 8);
            BuildRoom(map, room);
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5), "Resident");
            global::SimWorld.Needs.Need_Beauty beauty = BeautyOf(pawn);

            var patch = new CellRect(4, 4, 3, 3);
            Dirty(map, patch, FilthDefOf.Filth_Dirt);
            float dirt = beauty.CurInstantLevel;

            Dirty(map, patch, FilthDefOf.Filth_AnimalFilth);
            float worse = beauty.CurInstantLevel;

            Assert.True(dirt < 0.5f, "Dirt should be an ugliness, not a neutral.");
            Assert.True(worse < dirt, "Animal filth on top of dirt should be uglier than the dirt alone.");
        }

        // ---- what the need does to mood ----

        [Fact]
        public void The_beauty_need_moves_mood_in_both_directions()
        {
            CoreMap map = NewMap(16, 16);
            var room = new CellRect(2, 2, 6, 6);
            BuildRoom(map, room);
            Pawn pawn = SpawnHuman(map, new IntVec3(4, 0, 4), "Resident");
            global::SimWorld.Needs.Need_Beauty beauty = BeautyOf(pawn);

            float neutralMood = MoodOffsetWithBeautyAt(pawn, beauty.CurInstantLevel);
            Assert.Equal(0f, neutralMood);

            Dirty(map, room.ContractedBy(1), FilthDefOf.Filth_AnimalFilth);
            float squalidMood = MoodOffsetWithBeautyAt(pawn, beauty.CurInstantLevel);
            Assert.True(squalidMood < neutralMood, "Squalor should cost mood.");

            foreach (IntVec3 c in room.ContractedBy(1).Cells)
            {
                foreach (Thing t in new List<Thing>(map.thingGrid.ThingsListAt(c)))
                {
                    if (t.def.category == ThingCategory.Filth) t.DeSpawn();
                }
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sculpture")), new IntVec3(3, 0, 3), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Sculpture")), new IntVec3(6, 0, 6), map);
            float prettyMood = MoodOffsetWithBeautyAt(pawn, beauty.CurInstantLevel);

            Assert.True(prettyMood > neutralMood, "A room somebody made beautiful should be worth mood.");
        }

        /// <summary>Puts the beauty need at <paramref name="level"/> and returns the pawn's total situational
        /// mood offset — the number <see cref="Need_Mood"/> itself chases.</summary>
        private static float MoodOffsetWithBeautyAt(Pawn pawn, float level)
        {
            BeautyOf(pawn).CurLevel = level;
            pawn.needs.mood!.thoughts.situational.Recalculate();
            return pawn.needs.mood.thoughts.TotalMoodOffset();
        }

        [Fact]
        public void Categories_and_the_curve_never_run_backwards()
        {
            float previous = float.NegativeInfinity;
            var previousCategory = BeautyCategory.Hideous;
            for (float b = -20f; b <= 20f; b += 0.5f)
            {
                float level = global::SimWorld.Needs.Need_Beauty.LevelFromBeauty(b);
                Assert.InRange(level, 0f, 1f);
                Assert.True(level >= previous, $"Beauty {b} read as less pleasant than the beauty below it.");
                previous = level;

                BeautyCategory category = CategoryAtLevel(level);
                Assert.True(category >= previousCategory, $"Beauty {b} banded worse than the beauty below it.");
                previousCategory = category;
            }

            Assert.Equal(0.5f, global::SimWorld.Needs.Need_Beauty.LevelFromBeauty(0f), 3);
        }

        private static BeautyCategory CategoryAtLevel(float level)
        {
            Pawn p = NewHuman();
            BeautyOf(p).CurLevel = level;
            return BeautyOf(p).CurCategory;
        }

        // ---- where the sampler does and does not reach ----

        [Fact]
        public void Outdoors_the_sample_is_a_radius_so_distant_filth_is_not_perceived()
        {
            // No walls: the whole map is one Room that touches outside, so the room-wide average would be
            // meaningless (and enormous) and the radial fallback is what runs.
            CoreMap map = NewMap(40, 40);
            map.MapTick();
            Pawn pawn = SpawnHuman(map, new IntVec3(20, 0, 20), "Wanderer");
            global::SimWorld.Needs.Need_Beauty beauty = BeautyOf(pawn);

            Assert.Equal(0.5f, beauty.CurInstantLevel, 3);

            Dirty(map, new CellRect(2, 2, 4, 4), FilthDefOf.Filth_AnimalFilth);
            Assert.Equal(0.5f, beauty.CurInstantLevel, 3);

            Dirty(map, new CellRect(18, 18, 5, 5), FilthDefOf.Filth_AnimalFilth);
            Assert.True(beauty.CurInstantLevel < 0.5f, "Filth underfoot should be perceived.");
        }

        [Fact]
        public void An_unspawned_citizen_and_a_substituted_sampler_both_still_work()
        {
            Pawn offMap = NewHuman();
            global::SimWorld.Needs.Need_Beauty beauty = BeautyOf(offMap);

            // Nowhere to look: the need's own default, exactly as before a sampler existed.
            Assert.Equal(beauty.def.baseLevel, beauty.CurInstantLevel);

            // The seam still belongs to whoever sets it — a host's own sampler wins over the map's.
            offMap.environment = new FixedBeauty(0.93f);
            Assert.Equal(0.93f, beauty.CurInstantLevel, 3);
        }

        private sealed class FixedBeauty : IEnvironmentSampler
        {
            private readonly float level;
            public FixedBeauty(float level) => this.level = level;
            public float? InstantLevelFor(NeedDef need, Pawn pawn) => level;
        }

        // ---- tiering ----

        [Fact]
        public void A_citizen_below_Full_tier_is_never_sampled()
        {
            CoreMap map = NewMap(16, 16);
            var room = new CellRect(2, 2, 10, 10);
            BuildRoom(map, room);
            Dirty(map, room.ContractedBy(1), FilthDefOf.Filth_AnimalFilth);

            Pawn full = SpawnHuman(map, new IntVec3(4, 0, 4), "Attended");
            Pawn coarse = SpawnHuman(map, new IntVec3(8, 0, 8), "Unattended");
            NeverCleans(full);
            NeverCleans(coarse);

            // The same demotion a real attention change makes (God.AttentionBudget's own route).
            coarse.tier.Notify_AttentionChanged(true);
            coarse.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, coarse.tier.Tier);

            float before = BeautyOf(coarse).CurLevel;

            // Long enough for several of the Long bucket's coarse ticks, which is where an Interval citizen's
            // needs advance in bulk.
            RunTicks(GenDate.TicksPerHour * 4, full, coarse);

            Assert.True(BeautyOf(full).CurLevel < 0.4f, "The attended citizen should have noticed the squalor.");
            Assert.Equal(before, BeautyOf(coarse).CurLevel);

            // And the reason it did not move is that it was never asked: the target is whatever the need
            // already holds, so no map sample is paid for at this tier.
            Assert.Equal(BeautyOf(coarse).CurLevel, BeautyOf(coarse).CurInstantLevel);
        }

        // ---- Scribe ----

        [Fact]
        public void Beauty_survives_a_save_and_load_and_goes_on_sampling_afterwards()
        {
            CoreMap map = NewMap(16, 16);
            var room = new CellRect(2, 2, 10, 10);
            BuildRoom(map, room);
            Dirty(map, room.ContractedBy(1), FilthDefOf.Filth_Dirt);
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5), "Resident");
            NeverCleans(pawn);

            RunTicks(GenDate.TicksPerHour, pawn);
            float before = BeautyOf(pawn).CurLevel;
            Assert.True(before < 0.5f, "The citizen should have noticed the dirt before the save.");

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            var loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            global::SimWorld.Needs.Need_Beauty loadedBeauty = BeautyOf(loadedPawn);

            Assert.Equal(before, loadedBeauty.CurLevel, 4);

            // The sampler holds no state of its own, so a loaded citizen keeps reading the loaded map — the
            // property that would break if this had been wired per-pawn on spawn instead.
            loaded.MapTick();
            Assert.True(loadedBeauty.CurInstantLevel < 0.5f);
        }
    }
}
