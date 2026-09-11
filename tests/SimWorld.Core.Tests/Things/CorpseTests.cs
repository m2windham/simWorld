using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// Death leaves a body (system: corpses). Before this module a pawn that died simply stayed spawned on
    /// its map forever, and three separate lanes had to work around it: hunting butchered at the kill site,
    /// <c>HaulCorpses</c> was a work giver with nothing it could ever target, and a settlement's only way to
    /// stop counting its dead was to take them off the map and forget them.
    /// <para/>
    /// Everything unsourced here — the rot day counts, the temperature breakpoints — is asserted as an
    /// ordering or a trend, never as the literal (<c>RotUtility</c> and <c>CompProperties_Rottable</c> are
    /// explicit about which numbers could not be sourced).
    /// </summary>
    public class CorpseTests : ContentTestBase
    {
        public CorpseTests(CoreContentFixture content) : base(content)
        {
            // Generation is lazy by design (see CorpseDefGenerator's own doc on why it cannot run inside the
            // load pass), so every test here starts from the state a host reaches by calling this once after
            // loading content.
            CorpseDefGenerator.EnsureGenerated();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Chicken => Def("Chicken");

        private static ThingDef Muffalo => Def("Muffalo");

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Prey")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Citizen")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>Kills a spawned pawn and hands back the body death left behind.</summary>
        private static CorpseThing KillAndGetCorpse(Pawn pawn)
        {
            pawn.health.Kill(null, null);
            Assert.NotNull(pawn.corpse);
            return pawn.corpse!;
        }

        private static Thing SpawnButcherBench(CoreMap map, IntVec3 cell) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("TableButcher")), cell, map);

        private static Zone_Stockpile NewStockpile(CoreMap map, ThingDef allowed, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetAllow(allowed, true);
            return zone;
        }

        private static int MeatOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Def("Meat_Generic")).Sum(t => t.stackCount);

        private static IEnumerable<CorpseThing> CorpsesOn(CoreMap map) =>
            map.listerThings.AllThings.OfType<CorpseThing>();

        // ---- generated content ----

        [Fact]
        public void A_corpse_def_is_generated_for_every_race_and_generation_is_idempotent()
        {
            ThingDef husky = CorpseDefGenerator.CorpseDefFor(Husky);
            ThingDef human = CorpseDefGenerator.CorpseDefFor(Human);

            Assert.Equal("Corpse_Husky", husky.defName);
            Assert.Equal("Corpse_Human", human.defName);
            Assert.Equal(typeof(CorpseThing), husky.thingClass);
            Assert.True(CorpseDefGenerator.IsCorpseDef(husky));

            // A second pass adds nothing, and — the trap a generator that reads its own output would fall
            // into — never mints a corpse of a corpse.
            Assert.Equal(0, CorpseDefGenerator.EnsureGenerated());
            Assert.Null(DefDatabase<ThingDef>.GetNamedSilentFail("Corpse_Corpse_Husky"));
            Assert.Same(husky, CorpseDefGenerator.CorpseDefFor(Husky));
        }

        [Fact]
        public void A_generated_corpse_def_is_a_haulable_item_in_the_right_category()
        {
            ThingDef husky = CorpseDefGenerator.CorpseDefFor(Husky);
            ThingDef human = CorpseDefGenerator.CorpseDefFor(Human);

            Assert.Equal(ThingCategory.Item, husky.category);
            Assert.True(husky.EverHaulable, "A body has to be haulable or HaulCorpses has nothing to carry.");
            Assert.Equal(1, husky.stackLimit);
            Assert.Equal(TickerType.Rare, husky.tickerType);

            Assert.Contains(DefDatabase<ThingCategoryDef>.GetNamed("CorpsesAnimal"), husky.thingCategories!);
            Assert.Contains(DefDatabase<ThingCategoryDef>.GetNamed("CorpsesHumanlike"), human.thingCategories!);

            // The race's own properties come along, so a corpse def can answer "what was this" with no live
            // pawn — RimWorld shares the same RaceProperties object between race def and corpse def.
            Assert.Same(Husky.race, husky.race);

            // Deliberately not food: see CorpseDefGenerator's own note on why an ingestible corpse would make
            // every body on the map count as larder in HuntingInitiative.
            Assert.Null(husky.ingestible);
        }

        [Fact]
        public void Corpse_work_givers_are_wired_to_real_scanners()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.IsType<WorkGiver_HaulCorpses>(DefDatabase<WorkGiverDef>.GetNamed("HaulCorpses").Worker);
            Assert.IsType<WorkGiver_ButcherCorpse>(DefDatabase<WorkGiverDef>.GetNamed("ButcherCorpses").Worker);
            Assert.NotNull(CorpseWorkDefOf.ButcherCorpse);
            Assert.NotNull(CorpseWorkDefOf.ButcherAnimal);

            // Bodies before clutter: HaulCorpses outranks HaulGeneral inside the Hauling work type.
            Assert.True(DefDatabase<WorkGiverDef>.GetNamed("HaulCorpses").priorityInType
                > DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").priorityInType);
        }

        // ---- death leaves a body ----

        [Fact]
        public void A_pawn_that_dies_leaves_a_corpse_on_the_cell_it_died_on()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(4, 0, 5);
            Pawn citizen = SpawnHuman(map, cell, "Bob");

            CorpseThing corpse = KillAndGetCorpse(citizen);

            Assert.Equal(cell, corpse.Position);
            Assert.True(corpse.Spawned);
            Assert.False(citizen.Spawned, "The body is inside the corpse, not standing on the map.");
            Assert.False(citizen.Destroyed, "A dead pawn is still a person the chronicle and family tree name.");
            Assert.Empty(map.mapPawns.AllPawns);
            Assert.Same(corpse, Assert.Single(CorpsesOn(map)));
        }

        [Fact]
        public void A_corpse_remembers_who_it_was()
        {
            CoreMap map = NewMap(6, 6);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2), "Rex");
            int id = husky.thingIDNumber;

            CorpseThing corpse = KillAndGetCorpse(husky);

            Assert.Same(husky, corpse.InnerPawn);
            Assert.Same(corpse, husky.corpse);
            Assert.Equal(id, corpse.InnerPawn!.thingIDNumber);
            Assert.Equal(Husky.race, corpse.InnerPawn.RaceProps);
            Assert.Equal("dead husky", corpse.def.label);
        }

        [Fact]
        public void A_pawn_that_dies_unspawned_leaves_no_corpse()
        {
            // The ordinary case for a settlement's non-Full citizens: they have no map and no cell, so there
            // is nowhere to leave a body. They drop off the roster as they always did.
            Pawn citizen = NewHuman("Unspawned");
            citizen.health.Kill(null, null);

            Assert.True(citizen.Dead);
            Assert.Null(citizen.corpse);
        }

        [Fact]
        public void Making_a_corpse_twice_for_one_pawn_is_a_no_op()
        {
            CoreMap map = NewMap(6, 6);
            Pawn citizen = SpawnHuman(map, new IntVec3(2, 0, 2));
            CorpseThing corpse = KillAndGetCorpse(citizen);

            Assert.Same(corpse, CorpseMaker.MakeAndSpawnCorpseFor(citizen));
            Assert.Single(CorpsesOn(map));
        }

        [Fact]
        public void Destroying_a_corpse_destroys_the_body_it_holds()
        {
            CoreMap map = NewMap(6, 6);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2));
            CorpseThing corpse = KillAndGetCorpse(husky);

            corpse.Destroy(DestroyMode.Vanish);

            Assert.True(corpse.Destroyed);
            Assert.True(husky.Destroyed, "A destroyed corpse must not leave an orphaned pawn behind.");
            Assert.Empty(CorpsesOn(map));
        }

        [Fact]
        public void A_pawn_that_dies_mid_job_releases_what_it_had_claimed()
        {
            // Ordering, not decoration: Pawn_JobTracker.EndCurrentJob releases reservations through
            // pawn.Map, which is null the moment the body goes into a corpse. Ending the job after the
            // despawn would leave the claim standing forever, held by a dead man.
            CoreMap map = NewMap(10, 10);
            Pawn hauler = SpawnHuman(map, new IntVec3(0, 0, 0));
            NewStockpile(map, Def("WoodLog"), new IntVec3(1, 0, 1));
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 5;
            GenSpawn.Spawn(wood, new IntVec3(8, 0, 8), map);

            hauler.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
            Assert.True(map.reservationManager.IsReservedBy(hauler, wood));

            hauler.health.Kill(null, null);

            Assert.False(map.reservationManager.IsReserved(wood), "A dead hauler's claim must not outlive it.");
            Assert.Null(hauler.jobs.curJob);
        }

        // ---- rot ----

        [Fact]
        public void Rot_advances_through_its_stages_in_order_and_not_before()
        {
            CoreMap map = NewMap(6, 6);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2));
            CorpseThing corpse = KillAndGetCorpse(husky);

            CompRottable rot = corpse.RotComp!;
            Assert.Equal(RotStage.Fresh, corpse.RotStageNow);

            // One tick short of the threshold is still fresh; one past it is not. Asserted against the comp's
            // own thresholds rather than a day count this port cannot source.
            rot.RotProgress = rot.Props.TicksToRotStart - 1;
            Assert.Equal(RotStage.Fresh, corpse.RotStageNow);
            rot.RotProgress = rot.Props.TicksToRotStart;
            Assert.Equal(RotStage.Rotting, corpse.RotStageNow);

            rot.RotProgress = rot.Props.TicksToDessicated - 1;
            Assert.Equal(RotStage.Rotting, corpse.RotStageNow);
            rot.RotProgress = rot.Props.TicksToDessicated;
            Assert.Equal(RotStage.Dessicated, corpse.RotStageNow);

            // Rot is one-way and never destroys the body: a dessicated corpse is still a corpse, and the
            // pawn inside it is still there to be named.
            Assert.False(corpse.Destroyed);
            Assert.Same(husky, corpse.InnerPawn);
        }

        [Fact]
        public void A_corpse_left_out_rots_as_the_clock_runs()
        {
            CoreMap map = NewMap(6, 6);
            map.outdoorTemperature = 21f;
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(2, 0, 2));
            CorpseThing corpse = KillAndGetCorpse(husky);
            CompRottable rot = corpse.RotComp!;

            Assert.Equal(0f, rot.RotProgress);
            RunTicks(GenTicks.TickRareInterval * 4);

            Assert.True(rot.RotProgress > 0f, "A body on the ground rots on its own tick, with nobody driving it.");
            Assert.Equal(RotStage.Fresh, corpse.RotStageNow);
            Assert.True(rot.RotProgress < rot.Props.TicksToRotStart, "Days of rot cannot happen in seconds.");
        }

        [Fact]
        public void Cold_slows_rot_and_freezing_stops_it()
        {
            // Trend and ordering only — RotUtility is explicit that its two temperature breakpoints could not
            // be sourced, so what is pinned is "frozen keeps, cold is slower than warm, hotter than the
            // plateau is no faster", not the numbers that produce it.
            Assert.Equal(0f, RotUtility.RotRateAtTemperature(-20f));
            Assert.Equal(0f, RotUtility.RotRateAtTemperature(RotUtility.FreezingTemperature));

            float cool = RotUtility.RotRateAtTemperature(RotUtility.FullRotRateTemperature / 2f);
            float warm = RotUtility.RotRateAtTemperature(RotUtility.FullRotRateTemperature);
            float hot = RotUtility.RotRateAtTemperature(RotUtility.FullRotRateTemperature * 4f);
            Assert.True(cool > 0f && cool < warm);
            Assert.Equal(warm, hot);

            CoreMap frozen = NewMap(6, 6);
            frozen.outdoorTemperature = -20f;
            CorpseThing frozenCorpse = KillAndGetCorpse(SpawnAnimal(frozen, Husky, new IntVec3(2, 0, 2)));

            CoreMap warmMap = NewMap(6, 6);
            warmMap.outdoorTemperature = 21f;
            CorpseThing warmCorpse = KillAndGetCorpse(SpawnAnimal(warmMap, Husky, new IntVec3(2, 0, 2)));

            frozenCorpse.RotComp!.Tick(GenDate.TicksPerDay * 30);
            warmCorpse.RotComp!.Tick(GenDate.TicksPerDay * 30);

            Assert.Equal(0f, frozenCorpse.RotComp.RotProgress);
            Assert.Equal(RotStage.Fresh, frozenCorpse.RotStageNow);
            Assert.True(warmCorpse.RotComp.RotProgress > 0f);
            Assert.Equal(RotStage.Dessicated, warmCorpse.RotStageNow);
        }

        // ---- hauling ----

        [Fact]
        public void A_hauler_carries_a_corpse_to_storage_and_it_is_still_the_same_body()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hauler = SpawnHuman(map, new IntVec3(0, 0, 0), "Hauler");
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(9, 0, 9), "Rex");
            CorpseThing corpse = KillAndGetCorpse(husky);
            var storage = new IntVec3(1, 0, 1);
            NewStockpile(map, corpse.def, storage);

            RunTicks(3000, hauler);

            CorpseThing stored = Assert.Single(CorpsesOn(map));
            Assert.Same(corpse, stored);
            Assert.Equal(storage, stored.Position);
            Assert.Same(husky, stored.InnerPawn);
            Assert.True(HaulAIUtility.IsInValidStorage(stored));
        }

        [Fact]
        public void A_body_with_nowhere_to_go_is_left_where_it_fell()
        {
            CoreMap map = NewMap(8, 8);
            Pawn hauler = SpawnHuman(map, new IntVec3(0, 0, 0));
            var cell = new IntVec3(5, 0, 5);
            CorpseThing corpse = KillAndGetCorpse(SpawnAnimal(map, Husky, cell));

            var giver = (WorkGiver_HaulCorpses)DefDatabase<WorkGiverDef>.GetNamed("HaulCorpses").Worker;
            Assert.False(giver.HasJobOnThing(hauler, corpse), "No stockpile that takes corpses is an honest 'no job'.");
            Assert.Null(giver.JobOnThing(hauler, corpse));

            RunTicks(500, hauler);
            Assert.Equal(cell, corpse.Position);
        }

        /// <summary>
        /// The reason <see cref="JobDriver_HaulToCell"/> had to change for this module, stated as its own
        /// test: hauling used to destroy the source stack and build a fresh Thing of the same def at the
        /// destination, which is lossless only for goods whose whole identity is (def, stuff, count). A
        /// corpse is the loudest counterexample — it would have arrived empty — but a damaged item was
        /// already quietly being repaired by being carried.
        /// </summary>
        [Fact]
        public void Hauling_a_whole_stack_moves_the_thing_itself_rather_than_a_copy_of_its_def()
        {
            CoreMap map = NewMap(10, 10);
            Pawn hauler = SpawnHuman(map, new IntVec3(0, 0, 0));
            var storage = new IntVec3(1, 0, 1);
            NewStockpile(map, Def("WoodLog"), storage);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 4;
            wood.HitPoints = wood.MaxHitPoints / 2;
            GenSpawn.Spawn(wood, new IntVec3(8, 0, 8), map);

            RunTicks(3000, hauler);

            Thing delivered = Assert.Single(map.listerThings.ThingsOfDef(Def("WoodLog")));
            Assert.Same(wood, delivered);
            Assert.Equal(storage, delivered.Position);
            Assert.Equal(4, delivered.stackCount);
            Assert.Equal(wood.MaxHitPoints / 2, delivered.HitPoints);
        }

        // ---- butchering at a bench ----

        [Fact]
        public void A_butcher_fetches_a_carcass_to_the_bench_and_turns_it_into_meat()
        {
            CoreMap map = NewMap(12, 12);
            var benchCell = new IntVec3(2, 0, 2);
            Thing bench = SpawnButcherBench(map, benchCell);
            Pawn butcher = SpawnHuman(map, new IntVec3(1, 0, 1), "Butcher");
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(9, 0, 9), "Rex");
            CorpseThing corpse = KillAndGetCorpse(husky);

            Assert.True(WorkGiver_ButcherCorpse.IsButcherBench(bench), "A bench a butchery recipe names is a butcher bench.");
            Assert.Equal(0, MeatOnMap(map));

            RunTicks(6000, butcher);

            Assert.True(corpse.Destroyed, "The carcass is consumed by the butchery.");
            Assert.True(husky.Destroyed);
            Assert.True(MeatOnMap(map) > 0, "Butchering a carcass must yield what butchering that animal yields.");
            Assert.Contains(map.listerThings.ThingsOfDef(Def("Leather_Plain")), t => t.stackCount > 0);

            // Products land at the bench, the same place a bench recipe's products already land
            // (JobDriver_DoBill), not out where the body happened to fall.
            Assert.Equal(benchCell, map.listerThings.ThingsOfDef(Def("Meat_Generic"))[0].Position);
        }

        [Fact]
        public void Bench_butchery_yields_what_that_animal_yields_bigger_animal_more_meat()
        {
            // Trend, not a literal: the bench path runs the same ButcherUtility/HusbandryTuning formula the
            // hunt's kill-site path does, so a muffalo feeds a settlement for longer than a chicken either way.
            CoreMap smallMap = NewMap(10, 10);
            SpawnButcherBench(smallMap, new IntVec3(2, 0, 2));
            Pawn smallButcher = SpawnHuman(smallMap, new IntVec3(1, 0, 1));
            KillAndGetCorpse(SpawnAnimal(smallMap, Chicken, new IntVec3(7, 0, 7)));
            RunTicks(6000, smallButcher);

            CoreMap bigMap = NewMap(10, 10);
            SpawnButcherBench(bigMap, new IntVec3(2, 0, 2));
            Pawn bigButcher = SpawnHuman(bigMap, new IntVec3(1, 0, 1));
            KillAndGetCorpse(SpawnAnimal(bigMap, Muffalo, new IntVec3(7, 0, 7)));
            RunTicks(6000, bigButcher);

            Assert.True(MeatOnMap(smallMap) > 0);
            Assert.True(MeatOnMap(bigMap) > MeatOnMap(smallMap));
        }

        [Fact]
        public void No_bench_no_butchery_and_no_job_offered()
        {
            CoreMap map = NewMap(8, 8);
            Pawn butcher = SpawnHuman(map, new IntVec3(1, 0, 1));
            CorpseThing corpse = KillAndGetCorpse(SpawnAnimal(map, Husky, new IntVec3(5, 0, 5)));

            var giver = (WorkGiver_ButcherCorpse)DefDatabase<WorkGiverDef>.GetNamed("ButcherCorpses").Worker;
            Assert.True(giver.ShouldSkip(butcher));
            Assert.False(giver.HasJobOnThing(butcher, corpse));

            RunTicks(2000, butcher);
            Assert.False(corpse.Destroyed);
            Assert.Equal(0, MeatOnMap(map));
        }

        [Fact]
        public void A_dessicated_carcass_and_a_human_body_are_not_butchery_work()
        {
            CoreMap map = NewMap(10, 10);
            SpawnButcherBench(map, new IntVec3(2, 0, 2));
            Pawn butcher = SpawnHuman(map, new IntVec3(1, 0, 1), "Butcher");

            CorpseThing husk = KillAndGetCorpse(SpawnAnimal(map, Husky, new IntVec3(7, 0, 7)));
            husk.RotComp!.RotProgress = husk.RotComp.Props.TicksToDessicated;

            CorpseThing person = KillAndGetCorpse(SpawnHuman(map, new IntVec3(8, 0, 8), "Bob"));

            var giver = (WorkGiver_ButcherCorpse)DefDatabase<WorkGiverDef>.GetNamed("ButcherCorpses").Worker;
            Assert.False(husk.IsButcherable);
            Assert.False(giver.HasJobOnThing(butcher, husk), "Nobody should cross a map to butcher a husk.");
            Assert.False(person.IsButcherable);
            Assert.False(giver.HasJobOnThing(butcher, person));

            RunTicks(4000, butcher);
            Assert.Equal(0, MeatOnMap(map));
            Assert.False(husk.Destroyed);
            Assert.False(person.Destroyed);
        }

        // ---- Scribe ----

        [Fact]
        public void A_corpse_round_trips_through_Scribe_with_its_body_and_its_rot()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(3, 0, 4);
            Pawn husky = SpawnAnimal(map, Husky, cell, "Rex");
            CorpseThing corpse = KillAndGetCorpse(husky);
            corpse.RotComp!.RotProgress = corpse.RotComp.Props.TicksToRotStart + 1000f;
            Assert.Equal(RotStage.Rotting, corpse.RotStageNow);
            float savedProgress = corpse.RotComp.RotProgress;
            int savedDeathTick = corpse.timeOfDeath;

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            CorpseThing loadedCorpse = Assert.Single(CorpsesOn(loaded));
            Assert.Equal(cell, loadedCorpse.Position);
            Assert.Equal(savedDeathTick, loadedCorpse.timeOfDeath);

            Pawn loadedPawn = Assert.IsType<Pawn>(loadedCorpse.InnerPawn);
            Assert.NotSame(husky, loadedPawn);
            Assert.Equal("Rex", loadedPawn.name);
            Assert.Equal(Husky, loadedPawn.def);
            Assert.True(loadedPawn.Dead, "A body that came back alive would be the worst possible round trip.");
            Assert.Same(loadedCorpse, loadedPawn.corpse);

            Assert.Equal(savedProgress, loadedCorpse.RotComp!.RotProgress);
            Assert.Equal(RotStage.Rotting, loadedCorpse.RotStageNow);

            // The body is inside the corpse and nowhere else: a pawn saved twice would come back as two.
            Assert.Empty(loaded.mapPawns.AllPawns);
            Assert.Single(loaded.listerThings.AllThings);
        }
    }
}
