using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Combat;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The <c>Hunt</c> work type wired end to end (system 9: AI — hunting): <see cref="WorkGiver_Hunt"/>
    /// finding prey behind <see cref="HuntingInitiative"/>'s food gate (this port's replacement for
    /// RimWorld's player-painted <c>Designation.Hunt</c>), and <see cref="JobDriver_Hunt"/> driving a real
    /// <see cref="Verb"/> from a toil and butchering the kill where it falls (this port's replacement for
    /// hauling a <c>Corpse</c>, of which it has none).
    /// <para/>
    /// Everything unsourced here is asserted as a band, a trend or an ordering — never as the literal
    /// constant, which <see cref="HuntingTuning"/> is explicit about not being able to source.
    /// </summary>
    public class HuntingAITests : ContentTestBase
    {
        public HuntingAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Muffalo => Def("Muffalo");

        private static ThingDef Chicken => Def("Chicken");

        private static WorkGiver_Hunt Giver => (WorkGiver_Hunt)DefDatabase<WorkGiverDef>.GetNamed("Hunt").Worker;

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Prey")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>
        /// A hunter carrying <paramref name="weaponDefName"/>, or empty-handed when null, with
        /// <c>Handling</c> switched off in its work grid.
        /// <para/>
        /// That last part is not a fudge, it is the only way a hunt ever happens to a <i>live</i> animal:
        /// <c>TameAnimals</c> and <c>Hunt</c> accept exactly the same targets (a reachable, reservable, wild
        /// animal), and <c>Handling</c> outranks <c>Hunting</c> by naturalPriority, so a pawn allowed to do
        /// both always tames. See <see cref="Taming_outranks_hunting_for_a_pawn_allowed_to_do_both"/>, which
        /// pins that ordering deliberately; every other live-prey test here is about the hunt, so it assigns
        /// a pawn who is a hunter and not a handler — an ordinary work-priority setting.
        /// </summary>
        private static Pawn SpawnHunter(CoreMap map, IntVec3 cell, string? weaponDefName = "Bow_Short", string name = "Hunter")
        {
            Pawn p = NewHuman(name);
            if (weaponDefName != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weaponDefName)));
            p.workSettings.Disable(WorkTypeDefOf.Handling);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A bare <see cref="Settlement"/> with no world behind it — the same minimal shape
        /// <c>ConstructionInitiativeTests.PlainSettlement</c> uses, so these tests need no generated world.</summary>
        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "TestSettlement" + tile, 0);

        private static Thing SpawnItem(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            thing.stackCount = count;
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static int MeatOnMap(CoreMap map) =>
            map.listerThings.ThingsOfDef(Def("Meat_Generic")).Sum(t => t.stackCount);

        // ---- content ----

        [Fact]
        public void Hunting_content_loads_with_no_errors_and_Hunt_is_wired_to_a_real_scanner()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(HuntingDefOf.Hunt);
            Assert.NotNull(HuntingDefOf.ButcherAnimal);
            Assert.True(HuntingDefOf.ButcherAnimal.isButchery);

            WorkGiverDef hunt = DefDatabase<WorkGiverDef>.GetNamed("Hunt");
            Assert.IsType<WorkGiver_Hunt>(hunt.Worker);
            Assert.Equal(typeof(JobDriver_Hunt), HuntingDefOf.Hunt.driverClass);

            // The butchery recipe is the shipped one a TableButcher lists, reused rather than duplicated.
            Assert.Same(DefDatabase<RecipeDef>.GetNamed("ButcherAnimal"), HuntingDefOf.ButcherAnimal);
        }

        // ---- who may hunt (RimWorld: WorkGiver_HunterHunt.HasHuntingWeapon) ----

        [Fact]
        public void Only_a_pawn_wielding_a_ranged_weapon_counts_as_a_hunter()
        {
            CoreMap map = NewMap(8, 8);
            Pawn barehanded = SpawnHunter(map, new IntVec3(0, 0, 0), weaponDefName: null, name: "Barehanded");
            Pawn knifeman = SpawnHunter(map, new IntVec3(1, 0, 0), "MeleeWeapon_Knife", "Knifeman");
            Pawn archer = SpawnHunter(map, new IntVec3(2, 0, 0), "Bow_Short", "Archer");

            Assert.False(HuntUtility.HasHuntingWeapon(barehanded));
            Assert.False(HuntUtility.HasHuntingWeapon(knifeman));
            Assert.True(HuntUtility.HasHuntingWeapon(archer));

            // ... and that is exactly what gates the giver, before any scan happens.
            Assert.True(Giver.ShouldSkip(barehanded));
            Assert.True(Giver.ShouldSkip(knifeman));
            Assert.False(Giver.ShouldSkip(archer)); // an empty map larder: the settlement wants meat
        }

        [Fact]
        public void The_driver_shoots_with_a_bow_and_swings_with_a_knife_and_the_knife_must_get_close()
        {
            CoreMap map = NewMap(8, 8);
            Pawn archer = SpawnHunter(map, new IntVec3(0, 0, 0), "Bow_Short", "Archer");
            Pawn knifeman = SpawnHunter(map, new IntVec3(1, 0, 0), "MeleeWeapon_Knife", "Knifeman");
            Pawn barehanded = SpawnHunter(map, new IntVec3(2, 0, 0), weaponDefName: null, name: "Barehanded");

            Verb ranged = Assert.IsType<Verb_LaunchProjectile>(HuntUtility.MakeHuntVerb(archer));
            Verb melee = Assert.IsType<Verb_MeleeAttack>(HuntUtility.MakeHuntVerb(knifeman));

            // No race in this port defines natural fists/teeth Tools, so an empty-handed pawn has no attack
            // at all — see HuntUtility.MakeHuntVerb and MentalState_SocialFighting's own note on that gap.
            Assert.Null(HuntUtility.MakeHuntVerb(barehanded));

            // A melee VerbProperties keeps VerbProperties.range at its ranged default, which is why the
            // driver must not read range off the verb for a swing: the melee reach has to be far shorter
            // than the bow's, whatever either number happens to be.
            Assert.True(HuntUtility.EffectiveRange(melee) < HuntUtility.EffectiveRange(ranged));
            Assert.True(HuntUtility.EffectiveRange(melee) < melee.verbProps.range);
            Assert.Equal(ranged.verbProps.range, HuntUtility.EffectiveRange(ranged));

            // The edge the melee reach has to land on exactly: a diagonal neighbour counts as adjacent, and
            // one cell further does not. The driver only stops walking once it is inside this, so a reach
            // even a hair short of √2 would leave a melee hunter circling its prey forever.
            float diagonal = (new IntVec3(1, 0, 1) - new IntVec3(0, 0, 0)).LengthHorizontal;
            float twoCells = (new IntVec3(2, 0, 0) - new IntVec3(0, 0, 0)).LengthHorizontal;
            Assert.True(diagonal <= HuntUtility.EffectiveRange(melee));
            Assert.True(twoCells > HuntUtility.EffectiveRange(melee));
        }

        // ---- the food gate: this port's replacement for Designation.Hunt ----

        [Fact]
        public void A_settlement_hunts_while_short_of_food_and_stops_once_it_is_stocked()
        {
            CoreMap map = NewMap(8, 8);
            Settlement settlement = PlainSettlement();
            settlement.AddCitizen(NewHuman("Eater"));

            Assert.True(HuntingInitiative.WantsMeat(settlement, map), "An empty larder wants meat.");

            // Stock the abstract ledger well past what one citizen wants, and the gate closes.
            float wanted = HuntingInitiative.NutritionWanted(settlement, map);
            int meals = (int)(wanted / Def("MealSimple").ingestible!.nutrition) + 5;
            settlement.SetStoreCount(Def("MealSimple"), meals);

            Assert.False(HuntingInitiative.WantsMeat(settlement, map));
        }

        [Fact]
        public void Food_lying_on_the_map_closes_the_gate_that_the_stores_ledger_alone_never_would()
        {
            // The trap this test exists for: Settlement.Stores has no writer that credits a spawned Thing, so
            // a gate reading only that ledger would keep hunting forever while the map filled up with meat.
            CoreMap map = NewMap(10, 10);
            Settlement settlement = PlainSettlement();
            settlement.AddCitizen(NewHuman("Eater"));

            Assert.True(HuntingInitiative.WantsMeat(settlement, map));

            float wanted = HuntingInitiative.NutritionWanted(settlement, map);
            int meat = (int)(wanted / Def("Meat_Generic").ingestible!.nutrition) + 10;
            SpawnItem(map, new IntVec3(5, 0, 5), "Meat_Generic", meat);

            Assert.False(HuntingInitiative.WantsMeat(settlement, map), "Meat on the map feeds people and must count.");
            Assert.Empty(settlement.Stores); // ... and it never touched the ledger, which is the whole point.
        }

        [Fact]
        public void More_mouths_want_more_food_and_only_real_citizens_count()
        {
            CoreMap map = NewMap(8, 8);
            Settlement one = PlainSettlement(1);
            one.AddCitizen(NewHuman("A"));
            Settlement three = PlainSettlement(2);
            for (int i = 0; i < 3; i++) three.AddCitizen(NewHuman("B" + i));

            Assert.True(HuntingInitiative.NutritionWanted(three, map) > HuntingInitiative.NutritionWanted(one, map));

            // A Statistical cohort has no Pawn object to feed (spec §11.3) — the same line the construction
            // initiative draws. Adding 500 of them must not move the target by a crumb.
            float before = HuntingInitiative.NutritionWanted(one, map);
            one.AddStatisticalPeople(500);
            Assert.Equal(before, HuntingInitiative.NutritionWanted(one, map), 3);
        }

        [Fact]
        public void With_no_settlement_behind_the_map_the_pawns_standing_on_it_are_the_mouths()
        {
            CoreMap empty = NewMap(8, 8);
            Assert.Equal(0f, HuntingInitiative.NutritionWanted(null, empty), 3);
            Assert.False(HuntingInitiative.WantsMeat(null, empty), "Nobody there to feed: nothing to hunt for.");

            CoreMap peopled = NewMap(8, 8);
            SpawnHunter(peopled, new IntVec3(0, 0, 0));
            Assert.True(HuntingInitiative.NutritionWanted(null, peopled) > 0f);
            Assert.True(HuntingInitiative.WantsMeat(null, peopled));
        }

        [Fact]
        public void A_stocked_settlement_leaves_the_wild_animals_alone()
        {
            CoreMap map = NewMap(10, 10);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6));

            Assert.False(Giver.ShouldSkip(hunter));
            Assert.True(Giver.HasJobOnThing(hunter, chicken));

            // Feed the map far past what its one humanlike wants; the giver stops offering hunts entirely.
            float wanted = HuntingInitiative.NutritionWanted(null, map);
            int meals = (int)(wanted / Def("MealSimple").ingestible!.nutrition) + 5;
            SpawnItem(map, new IntVec3(2, 0, 2), "MealSimple", meals);

            Assert.True(Giver.ShouldSkip(hunter));
        }

        // ---- what may be hunted ----

        [Fact]
        public void Someone_elses_animal_is_never_prey()
        {
            CoreMap map = NewMap(10, 10);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn wild = SpawnAnimal(map, Chicken, new IntVec3(5, 0, 5), "Wild");
            Pawn tame = SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6), "Tame");
            tame.faction = new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), "Owner", "F_Owner");

            Assert.True(Giver.HasJobOnThing(hunter, wild));
            Assert.False(Giver.HasJobOnThing(hunter, tame));
            Assert.False(Giver.HasJobOnThing(hunter, hunter), "A hunter is not its own prey.");
        }

        [Fact]
        public void A_hunter_refuses_prey_that_outweighs_it_too_far_and_takes_prey_that_does_not()
        {
            CoreMap map = NewMap(12, 12);
            Pawn grown = SpawnHunter(map, new IntVec3(0, 0, 0), "Bow_Short", "Grown");
            Pawn small = SpawnHunter(map, new IntVec3(1, 0, 0), "Bow_Short", "Small");
            small.ageTracker.DebugSetAge(8f); // a child: a genuinely smaller hunter, not a doctored constant
            Assert.True(small.BodySize < grown.BodySize);

            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(6, 0, 6), "Muffalo");
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(7, 0, 7), "Chicken");

            // The rule is a ratio to the hunter's own size, so the same animal is prey for one and not the
            // other — that ordering is the assertion, not any particular body size.
            Assert.True(HuntUtility.IsSafeToHunt(grown, muffalo));
            Assert.False(HuntUtility.IsSafeToHunt(small, muffalo));
            Assert.True(HuntUtility.IsSafeToHunt(small, chicken));

            Assert.True(Giver.HasJobOnThing(grown, muffalo));
            Assert.False(Giver.HasJobOnThing(small, muffalo));
        }

        [Fact]
        public void An_animal_that_has_already_turned_manhunter_is_not_offered_as_prey()
        {
            CoreMap map = NewMap(10, 10);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(5, 0, 5));

            Assert.True(Giver.HasJobOnThing(hunter, chicken));

            chicken.mindState.angryAt = hunter;
            chicken.mindState.angryUntilTick = Find.TickManager.TicksGame + 5000;
            Assert.False(Giver.HasJobOnThing(hunter, chicken), "Provoked prey is a manhunter, not a target.");

            // The anger is a window, not a permanent mark: once it lapses the animal is prey again.
            Find.TickManager.DebugSetTicksGame(chicken.mindState.angryUntilTick + 1);
            Assert.True(Giver.HasJobOnThing(hunter, chicken));
        }

        [Fact]
        public void Taming_outranks_hunting_for_a_pawn_allowed_to_do_both()
        {
            // TameAnimals and Hunt accept the same targets, and content orders Handling (naturalPriority 950)
            // above Hunting (850) — so a settlement short of food still tries to tame a wild animal before it
            // shoots one, and only a pawn who is not a handler hunts it. That ordering needed no special case
            // in WorkGiver_Hunt; it falls straight out of JobGiver_Work walking the priority list.
            CoreMap map = NewMap(10, 10);
            Pawn generalist = NewHuman("Generalist");
            generalist.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Bow_Short")));
            GenSpawn.Spawn(generalist, new IntVec3(1, 0, 1), map);
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(5, 0, 5));

            Assert.True(Giver.HasJobOnThing(generalist, chicken), "Hunting is on the table...");

            generalist.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.Tame, generalist.jobs.curJob?.def); // ... but taming is tried first.
        }

        // ---- revenge (RimWorld's manhunter-on-damage response) ----

        [Fact]
        public void Wounding_a_wilder_animal_provokes_it_more_often_than_a_tamer_one()
        {
            CoreMap map = NewMap(8, 8);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn wild = SpawnAnimal(map, Muffalo, new IntVec3(3, 0, 3), "Wild");   // wildness 0.6
            Pawn tamer = SpawnAnimal(map, Husky, new IntVec3(4, 0, 4), "Placid");  // wildness 0.3
            Assert.True(wild.RaceProps.wildness > tamer.RaceProps.wildness);

            Assert.True(ProvokeCount(wild, hunter, 1500) > ProvokeCount(tamer, hunter, 1500));
        }

        /// <summary>Rolls the revenge consequence <paramref name="trials"/> times against one animal, clearing
        /// the mark between rolls so each trial is independent.</summary>
        private static int ProvokeCount(Pawn animal, Pawn hunter, int trials)
        {
            int provoked = 0;
            for (int i = 0; i < trials; i++)
            {
                animal.mindState.angryAt = null;
                animal.mindState.angryUntilTick = -1;
                if (HuntUtility.TryProvokeRevenge(animal, hunter)) provoked++;
            }
            animal.mindState.angryAt = null;
            animal.mindState.angryUntilTick = -1;
            return provoked;
        }

        [Fact]
        public void A_provoked_animal_is_marked_angry_at_the_hunter_for_a_bounded_window()
        {
            CoreMap map = NewMap(8, 8);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(3, 0, 3));

            // Roll until it takes, rather than assuming any particular chance.
            bool provoked = false;
            for (int i = 0; i < 500 && !provoked; i++) provoked = HuntUtility.TryProvokeRevenge(muffalo, hunter);
            Assert.True(provoked);

            Assert.Same(hunter, muffalo.mindState.angryAt);
            Assert.True(muffalo.mindState.angryUntilTick > Find.TickManager.TicksGame);
            Assert.True(HuntUtility.IsAngry(muffalo));

            Find.TickManager.DebugSetTicksGame(muffalo.mindState.angryUntilTick + 1);
            Assert.False(HuntUtility.IsAngry(muffalo));
        }

        [Fact]
        public void A_dead_animal_is_never_provoked()
        {
            CoreMap map = NewMap(8, 8);
            Pawn hunter = SpawnHunter(map, new IntVec3(0, 0, 0));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(3, 0, 3));
            chicken.health.Kill(null, null);

            for (int i = 0; i < 200; i++) Assert.False(HuntUtility.TryProvokeRevenge(chicken, hunter));
            Assert.Null(chicken.mindState.angryAt);
        }

        // ---- end to end, through the real work-giver path ----

        [Fact]
        public void A_hungry_settlements_hunter_kills_wild_animals_and_butchers_them_where_they_fall()
        {
            // A small flock rather than a single bird: prey that turns on its hunter takes itself off the
            // menu for a while (see the revenge tests), so a one-animal map makes this test a coin toss on
            // that roll rather than a check that the loop runs. Find prey → close → shoot → butcher → meat.
            CoreMap map = NewMap(14, 14);
            Pawn hunter = SpawnHunter(map, new IntVec3(1, 0, 1));
            var flock = new List<Pawn>();
            for (int i = 0; i < 3; i++) flock.Add(SpawnAnimal(map, Chicken, new IntVec3(8 + i, 0, 8), "Chicken" + i));

            Assert.Equal(0, MeatOnMap(map));

            var ticking = new List<Pawn> { hunter };
            ticking.AddRange(flock);
            RunTicks(12000, ticking.ToArray());

            // Destroyed, not merely Dead: butchering is the only thing that removes the carcass, so this is
            // the whole chain — the giver found prey, the driver drove a real Verb until it died, and the
            // last toil turned it into food.
            Assert.Contains(flock, c => c.Destroyed);
            Assert.True(MeatOnMap(map) > 0, "Butchering a kill must yield meat — a hunt that drops nothing is not a hunt.");
        }

        [Fact]
        public void A_bigger_animal_feeds_the_settlement_for_longer()
        {
            // Trend, not a yield literal: HusbandryTuning scales meat by body size, and this pins that the
            // hunt inherits that scaling rather than inventing a second formula of its own. Both animals are
            // already dead so the comparison is about the butchery the hunt ends with, not about how many
            // arrows each takes.
            CoreMap smallMap = NewMap(10, 10);
            Pawn smallHunter = SpawnHunter(smallMap, new IntVec3(1, 0, 1));
            Pawn chicken = SpawnAnimal(smallMap, Chicken, new IntVec3(5, 0, 5));
            chicken.health.Kill(null, null);
            RunTicks(3000, smallHunter);

            CoreMap bigMap = NewMap(10, 10);
            Pawn bigHunter = SpawnHunter(bigMap, new IntVec3(1, 0, 1));
            Pawn muffalo = SpawnAnimal(bigMap, Muffalo, new IntVec3(5, 0, 5));
            muffalo.health.Kill(null, null);
            RunTicks(3000, bigHunter);

            Assert.True(chicken.Destroyed && muffalo.Destroyed);
            Assert.True(MeatOnMap(smallMap) > 0);
            Assert.True(MeatOnMap(bigMap) > MeatOnMap(smallMap));
        }

        [Fact]
        public void A_carcass_nobody_butchered_is_still_valid_work()
        {
            // With no Corpse Thing, an interrupted hunt would otherwise strand a dead animal on the map
            // forever; the giver accepts one and the driver skips straight to the butchery toil.
            CoreMap map = NewMap(10, 10);
            Pawn hunter = SpawnHunter(map, new IntVec3(1, 0, 1));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(5, 0, 5));
            chicken.health.Kill(null, null);

            Assert.True(Giver.HasJobOnThing(hunter, chicken));

            RunTicks(3000, hunter);

            Assert.True(chicken.Destroyed);
            Assert.True(MeatOnMap(map) > 0);
        }

        [Fact]
        public void Hunting_stops_once_the_kills_have_filled_the_larder()
        {
            // The gate is self-closing: butchered meat lands on the map, which is half of what the gate
            // reads, so the same hunter that was sent out eventually has no reason to go again.
            CoreMap map = NewMap(12, 12);
            Pawn hunter = SpawnHunter(map, new IntVec3(1, 0, 1));
            Assert.False(Giver.ShouldSkip(hunter));

            float wanted = HuntingInitiative.NutritionWanted(null, map);
            var prey = new List<Pawn>();
            for (int i = 0; i < 6; i++)
            {
                Pawn m = SpawnAnimal(map, Muffalo, new IntVec3(4 + i, 0, 9), "Muffalo" + i);
                m.health.Kill(null, null); // pre-killed: this test is about the gate closing, not about aim
                prey.Add(m);
            }

            RunTicks(20000, hunter);

            Assert.True(HuntingInitiative.NutritionAvailable(null, map) >= wanted, "Enough meat to stop hunting.");
            Assert.True(Giver.ShouldSkip(hunter), "A stocked larder ends the hunt.");
            Assert.Contains(prey, p => p.Destroyed);
        }

        [Fact]
        public void A_hunt_that_cannot_be_finished_gives_up_rather_than_running_forever()
        {
            // Prey walled off mid-hunt: the job must end, not spin. (The expiry ceiling is the backstop; an
            // unreachable target trips the driver's own approach failure long before it.)
            CoreMap map = NewMap(12, 12);
            Pawn hunter = SpawnHunter(map, new IntVec3(1, 0, 1));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(10, 0, 10));

            hunter.jobs.TryFindAndStartJob();
            Assert.Equal(HuntingDefOf.Hunt, hunter.jobs.curJob?.def);
            Assert.True(hunter.jobs.curJob!.expiryInterval > 0, "A hunt carries a give-up ceiling.");

            chicken.DeSpawn();
            RunTicks(5, hunter);

            Assert.NotEqual(HuntingDefOf.Hunt, hunter.jobs.curJob?.def);
        }

        // ---- scribe round trip ----

        [Fact]
        public void A_pawn_mid_hunt_round_trips_through_Scribe_and_finishes_after_loading()
        {
            CoreMap map = NewMap(12, 12);
            Pawn hunter = SpawnHunter(map, new IntVec3(1, 0, 1));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(8, 0, 8));

            hunter.jobs.TryFindAndStartJob();
            Assert.Equal(HuntingDefOf.Hunt, hunter.jobs.curJob?.def);
            Assert.Same(chicken, hunter.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.True(map.reservationManager.IsReservedBy(hunter, chicken));

            RunTicks(120, hunter, chicken); // a warmup's worth of hunting, then save mid-aim

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedHunter = loaded.mapPawns.AllPawns.First(p => p.RaceProps.Humanlike);
            Pawn loadedPrey = loaded.mapPawns.AllPawns.First(p => p.RaceProps.Animal);

            Assert.Equal(HuntingDefOf.Hunt, loadedHunter.jobs.curJob?.def);
            Assert.Same(loadedPrey, loadedHunter.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedHunter, loadedPrey));

            // The loaded job resumes through a freshly rebuilt driver (the Verb is deliberately not saved —
            // the hunter simply re-aims) rather than sitting inert.
            RunTicks(20000, loadedHunter, loadedPrey);
            Assert.True(loadedPrey.Destroyed);
            Assert.True(MeatOnMap(loaded) > 0);
        }
    }
}
