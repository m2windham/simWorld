using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreFilth = SimWorld.Filth.Filth;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Filth
{
    /// <summary>
    /// Filth: a Thing on a cell that accumulates by thickness, a handful of real sources that make it, a
    /// bounded cleaning job that takes it away again, and two systems that actually read how dirty a room is.
    /// <para/>
    /// Every tuned number in this module (cleaning work per layer, the per-cell generation chances, the
    /// cleanliness curve, the mood offsets) is SimWorld's own and unsourced, so these tests pin bands, trends
    /// and round trips rather than literals — CLAUDE.md's rule, and the reason there is not a single
    /// <c>Assert.Equal</c> against a tuning constant below.
    /// </summary>
    public class FilthTests : ContentTestBase
    {
        public FilthTests(CoreContentFixture content) : base(content)
        {
            FilthMaker.ResetCache();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A floored surface: in content it generates no filth, so a room floored with it only
        /// ever gets as dirty as somebody tracks it in.</summary>
        private static TerrainDef StoneRoad => DefDatabase<TerrainDef>.GetNamed("StreetStoneRoad");

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Cleaner")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>
        /// Walls plus a roof over <paramref name="rect"/>, then one map tick so <see cref="RoomTracker"/>
        /// actually floods. The tick matters: rooms are lazy, and without it every cell reads as unbounded
        /// open map (see <see cref="CleaningBounds"/>). The interior is <c>rect</c> shrunk by the wall ring.
        /// </summary>
        private static void BuildRoom(CoreMap map, CellRect rect)
        {
            foreach (IntVec3 c in rect.EdgeCells)
            {
                Thing wall = ThingMaker.MakeThing(Def("Wall"));
                GenSpawn.Spawn(wall, c, map);
            }
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            map.MapTick();
        }

        private static CoreFilth? FilthAt(CoreMap map, IntVec3 cell)
        {
            foreach (Thing t in map.thingGrid.ThingsListAt(cell))
            {
                if (t is CoreFilth f) return f;
            }
            return null;
        }

        private static int FilthCount(CoreMap map)
        {
            int n = 0;
            foreach (CoreFilth _ in FilthMaker.AllFilthOn(map)) n++;
            return n;
        }

        // ---- content ----

        [Fact]
        public void Filth_content_loads_and_every_binding_is_wired()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.NotNull(FilthDefOf.Filth_Dirt);
            Assert.NotNull(FilthDefOf.Filth_Blood);
            Assert.NotNull(FilthDefOf.Filth_AnimalFilth);
            Assert.NotNull(FilthStatDefOf.Cleanliness);
            Assert.NotNull(CleaningStatDefOf.CleaningSpeed);
            Assert.NotNull(CleaningJobDefOf.Clean);

            // Category and thingClass are what let filth coexist on an occupied cell and be found by the
            // map's existing Filth group; both existed before this module and neither needed widening.
            Assert.Equal(ThingCategory.Filth, FilthDefOf.Filth_Dirt.category);
            Assert.Equal(typeof(CoreFilth), FilthDefOf.Filth_Dirt.thingClass);
            Assert.NotNull(FilthDefOf.Filth_Dirt.filth);
        }

        [Fact]
        public void The_CleanFilth_work_giver_finally_has_a_worker()
        {
            // It shipped pointing at WorkGiver_Pending with no Filth class in the codebase for it to target:
            // a whole work type that could never produce a job. This is the assertion that it no longer is.
            var def = DefDatabase<global::SimWorld.Work.WorkGiverDef>.GetNamed("CleanFilth");
            Assert.IsType<WorkGiver_CleanFilth>(def.Worker);
            Assert.Same(global::SimWorld.Work.WorkTypeDefOf.Cleaning, def.workType);
        }

        // ---- thickness: accumulation with a ceiling, not unbounded stacking ----

        [Fact]
        public void Repeated_filth_in_one_cell_thickens_one_pile_instead_of_stacking_more_things()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(4, 0, 4);

            for (int i = 0; i < 20; i++)
            {
                FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            }

            Assert.Equal(1, FilthCount(map));

            CoreFilth filth = FilthAt(map, cell)!;
            Assert.Equal(FilthDefOf.Filth_Dirt.filth!.maxThickness, filth.thickness);
            Assert.False(filth.CanBeThickened);
        }

        [Fact]
        public void Thickness_rises_one_layer_at_a_time_and_stops_at_the_defs_ceiling()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(4, 0, 4);
            int max = FilthDefOf.Filth_Dirt.filth!.maxThickness;

            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            CoreFilth filth = FilthAt(map, cell)!;
            Assert.Equal(1, filth.thickness);

            for (int expected = 2; expected <= max; expected++)
            {
                Assert.True(FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test"),
                    "A pile below its ceiling should accept another layer.");
                Assert.Equal(expected, filth.thickness);
            }

            // At the ceiling the attempt reports failure rather than pretending a layer landed, and the pile
            // does not grow. That is what bounds filth in a cell somebody walks over all day.
            Assert.False(FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test"));
            Assert.Equal(max, filth.thickness);
            Assert.Equal(1, FilthCount(map));
        }

        [Fact]
        public void Two_different_filths_in_one_cell_are_two_piles()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(4, 0, 4);

            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Blood, "test");

            // Thickening only ever merges like with like — blood does not become dirt.
            Assert.Equal(2, FilthCount(map));
        }

        [Fact]
        public void Filth_never_lands_inside_a_wall_or_in_water()
        {
            CoreMap map = NewMap(8, 8);
            var wallCell = new IntVec3(2, 0, 2);
            var waterCell = new IntVec3(5, 0, 5);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), wallCell, map);
            map.terrainGrid.SetTerrain(waterCell, TerrainDefOf.WaterShallow);

            Assert.False(FilthMaker.TryMakeFilth(wallCell, map, FilthDefOf.Filth_Dirt, "test"));
            Assert.False(FilthMaker.TryMakeFilth(waterCell, map, FilthDefOf.Filth_Dirt, "test"));
            Assert.Equal(0, FilthCount(map));
        }

        [Fact]
        public void Filth_coexists_with_whatever_else_is_standing_in_its_cell()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(4, 0, 4);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 10;
            GenSpawn.Spawn(wood, cell, map);
            Pawn pawn = SpawnHuman(map, cell);

            Assert.True(FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test"));

            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            Assert.Contains(wood, here);
            Assert.Contains(pawn, here);
            Assert.NotNull(FilthAt(map, cell));
        }

        // ---- tick cost: only filth that can expire pays for a tick ----

        [Fact]
        public void Dirt_never_ticks_and_blood_does_because_only_one_of_them_can_expire()
        {
            CoreMap map = NewMap(8, 8);
            FilthMaker.TryMakeFilth(new IntVec3(1, 0, 1), map, FilthDefOf.Filth_Dirt, "test");
            FilthMaker.TryMakeFilth(new IntVec3(2, 0, 2), map, FilthDefOf.Filth_Blood, "test");

            CoreFilth dirt = FilthAt(map, new IntVec3(1, 0, 1))!;
            CoreFilth blood = FilthAt(map, new IntVec3(2, 0, 2))!;

            // Filth exists in large numbers, so the pile that can never go away must not be registered with
            // the tick manager at all — see Filth's own remarks on why this is decided per instance.
            Assert.False(dirt.DisappearsOnItsOwn);
            Assert.Equal(TickerType.Never, dirt.TickerType);
            Assert.True(blood.DisappearsOnItsOwn);
            Assert.Equal(TickerType.Rare, blood.TickerType);
            Assert.False(Find.TickManager.TickListFor(TickerType.Rare)!.Contains(dirt));
            Assert.True(Find.TickManager.TickListFor(TickerType.Rare)!.Contains(blood));
        }

        [Fact]
        public void Blood_dries_up_on_its_own_and_dirt_stays_until_someone_cleans_it()
        {
            CoreMap map = NewMap(8, 8);
            FilthMaker.TryMakeFilth(new IntVec3(1, 0, 1), map, FilthDefOf.Filth_Dirt, "test");
            FilthMaker.TryMakeFilth(new IntVec3(2, 0, 2), map, FilthDefOf.Filth_Blood, "test");

            CoreFilth dirt = FilthAt(map, new IntVec3(1, 0, 1))!;
            CoreFilth blood = FilthAt(map, new IntVec3(2, 0, 2))!;

            // Past the top of Filth_Blood's disappearsInDays band, whatever this particular pile rolled.
            int ticks = (FilthDefOf.Filth_Blood.filth!.disappearsInDays!.Value.max + 1) * GenDate.TicksPerDay;
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();

            Assert.True(blood.Destroyed, "Blood should have dried up on its own.");
            Assert.False(dirt.Destroyed, "Dirt has no disappear rate: only cleaning removes it.");
        }

        // ---- sources: things that actually make filth ----

        [Fact]
        public void A_pawn_walking_about_a_dirt_floored_room_dirties_it()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            Pawn pawn = SpawnHuman(map, new IntVec3(4, 0, 4));

            // Cleaning off, or this measures nothing: a citizen who dirties a floor and is also willing to
            // clean it tidies up behind itself inside the same run, and the count comes back to zero. That
            // the two cancel out is the feature working; here we want only the making half.
            pawn.workSettings.SetPriority(global::SimWorld.Work.WorkTypeDefOf.Cleaning, 0);

            Assert.Equal(0, FilthCount(map));

            // Walk the pawn back and forth across the room's bare soil floor.
            for (int lap = 0; lap < 6; lap++)
            {
                var target = new IntVec3(lap % 2 == 0 ? 12 : 4, 0, lap % 2 == 0 ? 12 : 4);
                pawn.jobs.StartJob(new global::SimWorld.AI.Job(global::SimWorld.AI.JobDefOf.GotoWander, target));
                RunTicks(400, pawn);
            }

            Assert.True(FilthCount(map) > 0, "Walking on a bare soil floor should have churned dirt up.");
            foreach (CoreFilth f in FilthMaker.AllFilthOn(map))
            {
                Assert.Same(FilthDefOf.Filth_Dirt, f.def);
                Assert.True(CleaningBounds.IsCleanable(f), "Filth should only ever be made where it can be cleaned.");
            }
        }

        [Fact]
        public void A_pawn_standing_still_dirties_nothing()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            Pawn pawn = SpawnHuman(map, new IntVec3(4, 0, 4));
            pawn.Asleep = true;

            RunTicks(1500, pawn);

            // Filth comes from moving, not from existing: a sleeping pawn leaves nothing behind.
            Assert.Equal(0, FilthCount(map));
        }

        [Fact]
        public void Walking_the_open_map_leaves_nothing_behind_at_all()
        {
            CoreMap map = NewMap(40, 40);
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 1));

            pawn.jobs.StartJob(new global::SimWorld.AI.Job(
                global::SimWorld.AI.JobDefOf.GotoWander, new IntVec3(38, 0, 38)));
            RunTicks(3000, pawn);

            Assert.NotEqual(new IntVec3(1, 0, 1), pawn.Position);
            // The wilderness is never cleaned, and this port has no weather to wash it, so filth made out
            // here could never be removed by anything. See Pawn_FilthTracker's remarks: it is not made.
            Assert.Equal(0, FilthCount(map));
        }

        [Fact]
        public void Butchering_an_animal_leaves_blood_on_the_floor()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);
            Pawn husky = new Pawn(Husky, "Dinner");
            GenSpawn.Spawn(husky, cell, map);
            husky.health.Kill(null, null);

            Pawn butcher = SpawnHuman(map, new IntVec3(4, 0, 5), "Butcher");
            Assert.True(global::SimWorld.Crafting.ButcherUtility.TryButcher(
                husky, butcher, DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed("ButcherAnimal")));

            CoreFilth? blood = FilthAt(map, cell);
            Assert.NotNull(blood);
            Assert.Same(FilthDefOf.Filth_Blood, blood!.def);
        }

        [Fact]
        public void A_bleeding_pawn_leaves_blood_and_a_whole_one_does_not()
        {
            int BloodPilesAfterBleeding(bool wound)
            {
                CoreMap map = NewMap(10, 10);
                Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5), "Patient");
                pawn.Asleep = true; // keep it in one cell so this measures bleeding, not walking
                if (wound)
                {
                    // A real Cut through the damage worker onto a named part — the same way the health
                    // tests make a bleeding wound. Thing.TakeDamage is the generic hit-points path and never
                    // produces an injury hediff, so it never bleeds.
                    var cut = DefDatabase<global::SimWorld.Health.DamageDef>.GetNamed("Cut");
                    cut.Worker.Apply(
                        new global::SimWorld.Health.DamageInfo(
                            cut, 12f, hitPart: pawn.RaceProps.body!.GetPartByLabel("left arm")),
                        pawn);
                    Assert.True(pawn.health.hediffSet.BleedRateTotal > 0f, "The wound should actually bleed.");
                }

                RunTicks(4000, pawn);
                return FilthCount(map);
            }

            Assert.True(BloodPilesAfterBleeding(wound: true) > 0, "An open wound should drip blood on the floor.");
            Assert.Equal(0, BloodPilesAfterBleeding(wound: false));
        }

        // ---- cleaning ----

        [Fact]
        public void A_citizen_walks_over_and_cleans_filth_away()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4));
            var dirty = new IntVec3(10, 0, 10);
            FilthMaker.TryMakeFilth(dirty, map, FilthDefOf.Filth_Dirt, "test");
            CoreFilth filth = FilthAt(map, dirty)!;

            Assert.True(CleaningBounds.IsCleanable(filth));

            RunTicks(4000, citizen);

            Assert.True(filth.Destroyed, "The citizen should have cleaned the filth away entirely.");
        }

        [Fact]
        public void Freshly_made_filth_is_left_alone_for_a_moment_and_becomes_work_once_it_settles()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4));
            FilthMaker.TryMakeFilth(new IntVec3(10, 0, 10), map, FilthDefOf.Filth_Dirt, "test");
            CoreFilth filth = FilthAt(map, new IntVec3(10, 0, 10))!;

            var giver = (WorkGiver_CleanFilth)DefDatabase<global::SimWorld.Work.WorkGiverDef>
                .GetNamed("CleanFilth").Worker;

            // Filth somebody is actively making is not work yet — otherwise a colonist would stand in a
            // doorway scrubbing the same cell forever.
            Assert.False(giver.HasJobOnThing(citizen, filth));

            for (int i = 0; i < WorkGiver_CleanFilth.MinTicksSinceThickened + 1; i++) Find.TickManager.DoSingleTick();

            Assert.True(giver.HasJobOnThing(citizen, filth), "Filth that has settled should become cleaning work.");
        }

        [Fact]
        public void A_thicker_pile_takes_proportionally_longer_to_clean()
        {
            int TicksToClean(int thickness)
            {
                CoreMap map = NewMap(14, 14);
                BuildRoom(map, new CellRect(1, 1, 11, 11));
                // Adjacent to the pawn, so walking time is the same negligible amount either way and what is
                // measured is the scrubbing itself.
                Pawn citizen = SpawnHuman(map, new IntVec3(5, 0, 5));
                var cell = new IntVec3(6, 0, 5);
                for (int i = 0; i < thickness; i++)
                {
                    FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
                }
                CoreFilth filth = FilthAt(map, cell)!;
                Assert.Equal(thickness, filth.thickness);

                for (int t = 0; t < 8000; t++)
                {
                    RunTicks(1, citizen);
                    if (filth.Destroyed) return t;
                }
                return int.MaxValue;
            }

            int thin = TicksToClean(1);
            int thick = TicksToClean(FilthDefOf.Filth_Dirt.filth!.maxThickness);

            Assert.True(thin < int.MaxValue, "A single layer should get cleaned well inside the budget.");
            Assert.True(thick < int.MaxValue, "A full-thickness pile should still get cleaned.");
            Assert.True(thick > thin,
                $"A thicker pile must cost more work: thickness 1 took {thin} ticks, full thickness took {thick}.");
        }

        [Fact]
        public void Two_citizens_do_not_both_claim_the_only_piece_of_filth()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 14, 14));
            Pawn a = SpawnHuman(map, new IntVec3(4, 0, 4), "A");
            Pawn b = SpawnHuman(map, new IntVec3(14, 0, 14), "B");
            var cell = new IntVec3(9, 0, 9);
            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            CoreFilth filth = FilthAt(map, cell)!;

            for (int i = 0; i < WorkGiver_CleanFilth.MinTicksSinceThickened + 1; i++) Find.TickManager.DoSingleTick();
            RunTicks(60, a, b);

            bool aHasIt = map.reservationManager.IsReservedBy(a, filth);
            bool bHasIt = map.reservationManager.IsReservedBy(b, filth);
            Assert.True(aHasIt ^ bHasIt, "Exactly one of the two should hold the only filth's reservation.");
        }

        // ---- the bound: what stops cleaning running forever ----

        [Fact]
        public void Filth_in_the_open_is_never_cleaning_work_however_long_anybody_waits()
        {
            CoreMap map = NewMap(20, 20);
            // No room, no home area: the whole map is open ground.
            Pawn citizen = SpawnHuman(map, new IntVec3(2, 0, 2));
            map.MapTick();
            var cell = new IntVec3(10, 0, 10);
            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            CoreFilth filth = FilthAt(map, cell)!;

            Assert.False(CleaningBounds.IsCleanable(filth));

            RunTicks(4000, citizen);

            // If this ever fails, cleaning has become an infinite job: a map's worth of soil is unbounded
            // work and a colonist would never do anything else again.
            Assert.False(filth.Destroyed, "Nobody should ever be sent to clean the open wilderness.");
        }

        [Fact]
        public void Cleaning_stops_when_the_room_is_clean_instead_of_running_forever()
        {
            CoreMap map = NewMap(16, 16);
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4));

            // Floor the room so walking around it cannot make new work while we watch it get finished.
            foreach (IntVec3 c in new CellRect(3, 3, 8, 8).Cells)
            {
                map.terrainGrid.SetTerrain(c, StoneRoad);
            }
            foreach (IntVec3 c in new[] { new IntVec3(5, 0, 5), new IntVec3(8, 0, 8), new IntVec3(6, 0, 9) })
            {
                FilthMaker.TryMakeFilth(c, map, FilthDefOf.Filth_Dirt, "test");
            }
            Assert.Equal(3, FilthCount(map));

            RunTicks(9000, citizen);

            Assert.Equal(0, FilthCount(map));
        }

        [Fact]
        public void A_painted_home_area_takes_over_from_the_enclosed_room_fallback()
        {
            CoreMap map = NewMap(20, 20);
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            var indoors = new IntVec3(5, 0, 5);
            var outdoors = new IntVec3(16, 0, 16);

            // With nothing painted, the fallback applies: indoors is cleanable, the open map is not.
            Assert.True(CleaningBounds.IsCleanable(map, indoors));
            Assert.False(CleaningBounds.IsCleanable(map, outdoors));

            // Paint a home area somewhere else entirely and it becomes the authority, exactly as in RimWorld
            // — including the part where it can exclude a room somebody does not want cleaned.
            map.areaManager.Home[outdoors] = true;

            Assert.True(CleaningBounds.IsCleanable(map, outdoors));
            Assert.False(CleaningBounds.IsCleanable(map, indoors));
        }

        // ---- what filth costs: room cleanliness, and the two systems that read it ----

        [Fact]
        public void Filth_makes_the_room_it_is_in_measurably_dirtier()
        {
            CoreMap map = NewMap(16, 16);
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            var cell = new IntVec3(5, 0, 5);

            float clean = RoomCleanlinessUtility.CleanlinessAt(map, cell);
            Assert.Equal(0f, clean);

            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            float oneePile = RoomCleanlinessUtility.CleanlinessAt(map, cell);
            Assert.True(oneePile < clean, "One pile of filth should make the room dirtier than none.");

            for (int x = 4; x <= 9; x++)
            {
                for (int z = 4; z <= 9; z++)
                {
                    FilthMaker.TryMakeFilth(new IntVec3(x, 0, z), map, FilthDefOf.Filth_Dirt, "test");
                }
            }
            Assert.True(RoomCleanlinessUtility.CleanlinessAt(map, cell) < oneePile,
                "More filth should keep making the room dirtier.");
        }

        [Fact]
        public void Surgery_in_a_filthy_room_is_likelier_to_go_wrong_than_in_a_clean_one()
        {
            float FactorWithFilthPiles(int piles)
            {
                CoreMap map = NewMap(14, 14);
                BuildRoom(map, new CellRect(2, 2, 8, 8));
                Pawn patient = SpawnHuman(map, new IntVec3(5, 0, 5), "Patient");
                int made = 0;
                for (int x = 3; x < 9 && made < piles; x++)
                {
                    for (int z = 3; z < 9 && made < piles; z++)
                    {
                        if (FilthMaker.TryMakeFilth(new IntVec3(x, 0, z), map, FilthDefOf.Filth_Blood, "test")) made++;
                    }
                }
                return RoomCleanlinessUtility.SurgerySuccessFactorFor(patient);
            }

            float spotless = FactorWithFilthPiles(0);
            float grubby = FactorWithFilthPiles(6);
            float squalid = FactorWithFilthPiles(30);

            // The surgery module rolls skill against SurgeryTuning's band; this is the room multiplying that
            // roll, which is RimWorld's own shape (RoomStatDefOf.SurgerySuccessChanceFactor).
            Assert.Equal(1f, spotless);
            Assert.True(grubby < spotless, "Operating in a grubby room should be worse than in a spotless one.");
            Assert.True(squalid < grubby, "Operating in squalor should be worse still.");
            Assert.True(squalid > 0f, "The penalty is a factor, never a guarantee of failure.");
        }

        [Fact]
        public void A_surgeon_really_does_fail_more_often_in_a_filthy_room()
        {
            int FailuresOutOf(int trials, bool filthy)
            {
                int failures = 0;
                for (int i = 0; i < trials; i++)
                {
                    CoreMap map = NewMap(14, 14);
                    BuildRoom(map, new CellRect(2, 2, 8, 8));
                    Pawn patient = SpawnHuman(map, new IntVec3(5, 0, 5), "Patient");
                    if (filthy)
                    {
                        foreach (IntVec3 c in new CellRect(3, 3, 6, 6).Cells)
                        {
                            for (int k = 0; k < 3; k++)
                            {
                                FilthMaker.TryMakeFilth(c, map, FilthDefOf.Filth_Blood, "test");
                            }
                        }
                    }

                    Pawn surgeon = NewHuman("Doctor");
                    surgeon.skills!.GetSkill(global::SimWorld.Work.SkillDefOf.Medicine)!.Level = 8;
                    var leg = patient.RaceProps.body!.GetPartByLabel("left leg")!;
                    patient.health.surgeryBills.AddBill(new global::SimWorld.Health.Bill_Medical(
                        DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed("RemoveBodyPart"), leg));

                    global::SimWorld.Health.SurgeryUtility.PerformNextSurgery(patient, surgeon);
                    if (!patient.health.hediffSet.PartIsMissing(leg)) failures++;
                }
                return failures;
            }

            // Same seeded stream, same surgeon skill, same operation: the only difference is the floor.
            int clean = FailuresOutOf(60, filthy: false);
            int dirty = FailuresOutOf(60, filthy: true);

            Assert.True(dirty > clean,
                $"Operating in filth should fail more often: {dirty} failures in squalor against {clean} in a clean room.");
        }

        // ---- filth reaching mood, through beauty ----
        //
        // These two were written against ThoughtWorker_FilthyRoom, a situational thought that read room
        // cleanliness directly because nothing implemented IEnvironmentSampler when this module landed. The
        // beauty module has since retired that worker — filth now carries a negative Beauty stat and reaches
        // mood the way RimWorld's does — so the same two properties are asserted here through the route that
        // actually runs. What is being pinned is unchanged: living in filth costs mood, more filth costs
        // more, and cleaning the room takes the cost away again.

        [Fact]
        public void Living_in_filth_is_a_mood_thought_and_a_clean_room_is_not()
        {
            CoreMap map = NewMap(16, 16);
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5), "Resident");
            var thought = DefDatabase<global::SimWorld.Thoughts.ThoughtDef>.GetNamed("Beauty");
            global::SimWorld.Thoughts.ThoughtWorker worker = thought.Worker!;
            var beauty = pawn.needs.TryGetNeed<global::SimWorld.Needs.Need_Beauty>()!;

            // Sample without ticking: CurInstantLevel is the target the surroundings ask for right now, so
            // the room can be dirtied between readings without the pawn wandering off in the meantime.
            beauty.CurLevel = beauty.CurInstantLevel;
            float clean = beauty.CurLevel;
            Assert.False(worker.CurrentState(pawn).Active, "A clean, bare room should carry no thought at all.");

            foreach (IntVec3 c in new CellRect(3, 3, 4, 4).Cells)
            {
                FilthMaker.TryMakeFilth(c, map, FilthDefOf.Filth_Dirt, "test");
            }
            beauty.CurLevel = beauty.CurInstantLevel;
            float dirtyLevel = beauty.CurLevel;
            global::SimWorld.Thoughts.ThoughtState dirty = worker.CurrentState(pawn);
            Assert.True(dirtyLevel < clean, "Dirt underfoot should read as uglier than a bare floor.");
            Assert.True(dirty.Active, "A dirty room should be a thought.");

            foreach (IntVec3 c in new CellRect(3, 3, 8, 8).Cells)
            {
                for (int k = 0; k < 3; k++) FilthMaker.TryMakeFilth(c, map, FilthDefOf.Filth_AnimalFilth, "test");
            }
            beauty.CurLevel = beauty.CurInstantLevel;
            global::SimWorld.Thoughts.ThoughtState squalid = worker.CurrentState(pawn);
            Assert.True(beauty.CurLevel < dirtyLevel, "More, and worse, filth should read uglier still.");
            Assert.True(squalid.Active);

            // Worse squalor is a worse mood offset. Asserted on the offset rather than the stage index:
            // this Def's stages run worst-first, the opposite of the retired FilthyRoom Def's, and the
            // ordering that matters is the one mood actually reads.
            Assert.True(thought.stages[squalid.StageIndex].baseMoodEffect < thought.stages[dirty.StageIndex].baseMoodEffect);
        }

        [Fact]
        public void Cleaning_a_room_lifts_the_mood_thought_back_off()
        {
            CoreMap map = NewMap(16, 16);
            BuildRoom(map, new CellRect(2, 2, 10, 10));
            // Floor the whole interior, not part of it: a bare soil cell anywhere in the room would have the
            // cleaner making fresh dirt as it walks, and "the room is finished" would never arrive.
            foreach (IntVec3 c in new CellRect(3, 3, 8, 8).Cells) map.terrainGrid.SetTerrain(c, StoneRoad);
            foreach (IntVec3 c in new CellRect(3, 3, 4, 4).Cells)
            {
                FilthMaker.TryMakeFilth(c, map, FilthDefOf.Filth_Dirt, "test");
            }
            Pawn citizen = SpawnHuman(map, new IntVec3(5, 0, 5), "Resident");
            global::SimWorld.Thoughts.ThoughtWorker worker = DefDatabase<global::SimWorld.Thoughts.ThoughtDef>
                .GetNamed("Beauty").Worker!;
            var beauty = citizen.needs.TryGetNeed<global::SimWorld.Needs.Need_Beauty>()!;

            beauty.CurLevel = beauty.CurInstantLevel;
            Assert.True(worker.CurrentState(citizen).Active);

            RunTicks(30000, citizen);

            Assert.Equal(0, FilthCount(map));
            Assert.False(worker.CurrentState(citizen).Active,
                "Once the room is clean, the beauty the filth was spoiling should have recovered with it.");
        }

        // ---- Scribe ----

        [Fact]
        public void Filth_survives_a_save_and_load_with_its_thickness_and_its_sources()
        {
            CoreMap map = NewMap(10, 10);
            var dirtCell = new IntVec3(3, 0, 3);
            var bloodCell = new IntVec3(6, 0, 6);

            FilthMaker.TryMakeFilth(dirtCell, map, FilthDefOf.Filth_Dirt, "Boots");
            FilthMaker.TryMakeFilth(dirtCell, map, FilthDefOf.Filth_Dirt, "Boots");
            FilthMaker.TryMakeFilth(bloodCell, map, FilthDefOf.Filth_Blood, "Husky");

            CoreFilth dirtBefore = FilthAt(map, dirtCell)!;
            CoreFilth bloodBefore = FilthAt(map, bloodCell)!;
            Assert.Equal(2, dirtBefore.thickness);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);

            CoreFilth dirtAfter = FilthAt(loaded, dirtCell)!;
            CoreFilth bloodAfter = FilthAt(loaded, bloodCell)!;

            Assert.NotNull(dirtAfter);
            Assert.NotNull(bloodAfter);
            Assert.Same(FilthDefOf.Filth_Dirt, dirtAfter.def);
            Assert.Same(FilthDefOf.Filth_Blood, bloodAfter.def);
            Assert.Equal(dirtBefore.thickness, dirtAfter.thickness);
            Assert.Equal(bloodBefore.thickness, bloodAfter.thickness);
            Assert.Contains("Boots", dirtAfter.Sources);
            Assert.Contains("Husky", bloodAfter.Sources);

            // A re-spawned pile keeps its own tick bucket: dirt still costs nothing, blood still expires.
            Assert.Equal(TickerType.Never, dirtAfter.TickerType);
            Assert.Equal(TickerType.Rare, bloodAfter.TickerType);
        }

        [Fact]
        public void A_loaded_pile_still_expires_when_it_was_going_to()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(4, 0, 4);
            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Blood, "test");

            CoreMap loaded = Scribe.Load<CoreMap>(Scribe.SaveToString(map, "map"), "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            CoreFilth blood = FilthAt(loaded, cell)!;
            Assert.False(blood.Destroyed);

            int ticks = (FilthDefOf.Filth_Blood.filth!.disappearsInDays!.Value.max + 1) * GenDate.TicksPerDay;
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();

            // The disappear tick is saved as an absolute tick, so loading does not reset the clock and a
            // pile cannot be made immortal by saving and loading repeatedly.
            Assert.True(blood.Destroyed);
        }
    }
}
