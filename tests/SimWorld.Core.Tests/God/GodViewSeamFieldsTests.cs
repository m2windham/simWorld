using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

// SimWorld.Economy and SimWorld.AI share their leaf segment with test namespaces; alias the production types
// rather than relying on which one a bare name resolves to (CLAUDE.md).
using HuntingInitiative = global::SimWorld.AI.HuntingInitiative;
using SettlementLarder = global::SimWorld.Economy.SettlementLarder;
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The values the host's status bar, edict panel and inspector had been leaving out or working out for
    /// themselves, now on the snapshot (<c>docs/spec/simworld-spec.md</c> §12a): a settlement's own mood,
    /// health and food means and what it holds; how many edict slots there are and how many are taken; the
    /// date with its hour, and the hour itself; how long a condition has left, in words.
    ///
    /// <para/>Nothing here pins a tuned number. A mean lies in range and moves the way the citizen under it
    /// moved; stock is whatever the two books hold between them; a label reads back to the tick it was made
    /// from.
    /// </summary>
    public class GodViewSeamFieldsTests : ContentTestBase
    {
        public GodViewSeamFieldsTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        // ---- fixtures ----

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        /// <summary>A civilization with nothing generated behind it: enough for settlements and conditions,
        /// which is all a snapshot reads, without paying for a planet.</summary>
        private static CoreWorld BareWorld()
        {
            var world = new CoreWorld();
            Find.World = world;
            return world;
        }

        private static Settlement NewSettlement(CoreWorld world, int tile, string name, int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman(name + i));
            world.worldObjects.Add(settlement);
            return settlement;
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static SettlementSummary SummaryOf(Settlement settlement) =>
            GodViewSnapshot.Capture().Settlements.Single(s => s.Tile == settlement.tile);

        private static Dictionary<string, int> Counts(SettlementSummary summary) =>
            summary.Stock.ToDictionary(line => line.DefName, line => line.Count);

        private static int CountOf(SettlementSummary summary, ThingDef def) =>
            summary.Stock.FirstOrDefault(line => line.DefName == def.defName)?.Count ?? 0;

        private static void AssertIsAFraction(float value, string what) =>
            Assert.True(value >= 0f && value <= 1f, what + " was " + value.ToString(CultureInfo.InvariantCulture) + ", outside 0-1");

        private static IntVec3 FreeCell(CoreMap map) =>
            map.AllCells.First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0);

        private static Thing SpawnStack(CoreMap map, ThingDef def, int count)
        {
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, FreeCell(map), map);
            return stack;
        }

        private static void Wound(Pawn pawn)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            BodyPartRecord leg = pawn.RaceProps.body!.GetPartByLabel("left leg")!;
            cut.Worker.Apply(new DamageInfo(cut, 50f, hitPart: leg), pawn);
        }

        // ---- a settlement's own means ----

        [Fact]
        public void A_lone_settlements_means_are_the_civilizations()
        {
            // The bar has been printing the civilization's means beside the focused settlement's name. With
            // one settlement that was never wrong, and this is why: they are the same number.
            CoreWorld world = BareWorld();
            Settlement only = NewSettlement(world, 1, "Ashfell", citizens: 5);
            only.Citizens[0].needs.food!.CurLevelPercentage = 0.2f;
            only.Citizens[1].needs.mood!.CurLevelPercentage = 0.9f;

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            SettlementSummary summary = Assert.Single(snapshot.Settlements);

            Assert.Equal(snapshot.Civilization.MeanMood, summary.MeanMood, 5);
            Assert.Equal(snapshot.Civilization.MeanHealth, summary.MeanHealth, 5);
            Assert.Equal(snapshot.Civilization.MeanFoodNeed, summary.MeanFoodNeed, 5);
            AssertIsAFraction(summary.MeanMood, "mood");
            AssertIsAFraction(summary.MeanHealth, "health");
            AssertIsAFraction(summary.MeanFoodNeed, "food");
        }

        [Fact]
        public void A_starving_citizen_drags_her_own_settlements_food_down_and_nobody_elses()
        {
            // And with two settlements the civilization's number is no longer either town's. This is the
            // case the host could not draw: which town is hungry.
            CoreWorld world = BareWorld();
            Settlement hungry = NewSettlement(world, 1, "Ashfell", citizens: 4);
            Settlement fed = NewSettlement(world, 2, "Brackwater", citizens: 4);

            SettlementSummary hungryBefore = SummaryOf(hungry);
            SettlementSummary fedBefore = SummaryOf(fed);

            hungry.Citizens[0].needs.food!.CurLevelPercentage = 0f;

            GodViewSnapshot after = GodViewSnapshot.Capture();
            SettlementSummary hungryAfter = after.Settlements.Single(s => s.Tile == hungry.tile);
            SettlementSummary fedAfter = after.Settlements.Single(s => s.Tile == fed.tile);

            Assert.True(
                hungryAfter.MeanFoodNeed < hungryBefore.MeanFoodNeed,
                "A citizen with an empty stomach did not lower her settlement's food mean.");
            Assert.Equal(fedBefore.MeanFoodNeed, fedAfter.MeanFoodNeed);
            AssertIsAFraction(hungryAfter.MeanFoodNeed, "food");

            // The civilization's mean sits between its two towns', which is exactly why it described neither.
            Assert.True(after.Civilization.MeanFoodNeed > hungryAfter.MeanFoodNeed);
            Assert.True(after.Civilization.MeanFoodNeed < fedAfter.MeanFoodNeed);
        }

        [Fact]
        public void A_wounded_and_a_miserable_citizen_move_their_settlements_health_and_mood()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 1, "Ashfell", citizens: 4);
            Settlement elsewhere = NewSettlement(world, 2, "Brackwater", citizens: 4);

            SettlementSummary before = SummaryOf(settlement);
            SettlementSummary elsewhereBefore = SummaryOf(elsewhere);

            Wound(settlement.Citizens[0]);
            settlement.Citizens[1].needs.mood!.CurLevelPercentage = 0f;

            SettlementSummary after = SummaryOf(settlement);
            SettlementSummary elsewhereAfter = SummaryOf(elsewhere);

            Assert.True(after.MeanHealth < before.MeanHealth, "A wound did not lower the settlement's health mean.");
            Assert.True(after.MeanMood < before.MeanMood, "Misery did not lower the settlement's mood mean.");
            AssertIsAFraction(after.MeanHealth, "health");
            AssertIsAFraction(after.MeanMood, "mood");

            Assert.Equal(elsewhereBefore.MeanHealth, elsewhereAfter.MeanHealth);
            Assert.Equal(elsewhereBefore.MeanMood, elsewhereAfter.MeanMood);
        }

        [Fact]
        public void A_statistical_cohort_is_folded_into_a_settlements_means_without_being_walked()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 1, "Ashfell", citizens: 3);
            settlement.AddStatisticalPeople(40_000);

            SettlementSummary summary = SummaryOf(settlement);

            // Nobody was instantiated to average them — the tier's whole premise, one scale down.
            Assert.Equal(3, summary.CitizenCount);
            AssertIsAFraction(summary.MeanMood, "mood");
            AssertIsAFraction(summary.MeanHealth, "health");
            AssertIsAFraction(summary.MeanFoodNeed, "food");

            // The cohort outweighs three live citizens by four orders of magnitude, so emptying those three
            // stomachs cannot move the settlement's mean by more than their share of it.
            foreach (Pawn citizen in settlement.Citizens) citizen.needs.food!.CurLevelPercentage = 0f;
            Assert.True(Math.Abs(SummaryOf(settlement).MeanFoodNeed - summary.MeanFoodNeed) < 0.001f);
        }

        [Fact]
        public void A_settlement_with_nobody_in_it_still_reads_as_a_fraction()
        {
            // A mean over nobody. The host checks TotalPopulation before drawing one, and whatever it finds
            // here must at least not be a NaN that prints as one.
            CoreWorld world = BareWorld();
            Settlement empty = NewSettlement(world, 1, "Hollow", citizens: 0);

            SettlementSummary summary = SummaryOf(empty);

            Assert.Equal(0, summary.TotalPopulation);
            AssertIsAFraction(summary.MeanMood, "mood");
            AssertIsAFraction(summary.MeanHealth, "health");
            AssertIsAFraction(summary.MeanFoodNeed, "food");
            Assert.Empty(summary.Stock);
            Assert.Equal(0, summary.StoredUnits);
            Assert.Equal(0f, summary.StoredNutrition);
        }

        // ---- what a settlement holds ----

        [Fact]
        public void A_settlement_nobody_has_entered_holds_the_resources_in_its_ledger()
        {
            Game game = NewSoloGame("seam-ledger");
            Settlement settlement = PlayerSettlement(game);
            Assert.Null(settlement.InteriorMap);
            settlement.AddStore(Def("Silver"), 40);
            settlement.AddStore(Def("Pemmican"), 60);

            var resources = settlement.Stores
                .Where(kv => kv.Key.CountAsResource)
                .ToDictionary(kv => kv.Key.defName, kv => kv.Value);
            Assert.NotEmpty(resources);

            SettlementSummary summary = SummaryOf(settlement);

            Assert.Equal(resources, Counts(summary));
            Assert.Equal(resources.Values.Sum(), summary.StoredUnits);
            Assert.Equal(SettlementLarder.StoredNutrition(settlement), summary.StoredNutrition, 3);

            // Reading the stock never generated a map to look in.
            Assert.Null(settlement.InteriorMap);
            Assert.False(summary.HasInteriorMap);
        }

        [Fact]
        public void Stock_follows_the_ledger_and_reads_in_the_same_order_every_time()
        {
            Game game = NewSoloGame("seam-ledger-moves");
            Settlement settlement = PlayerSettlement(game);
            ThingDef blocks = Def("BlocksSandstone");
            ThingDef wood = Def("WoodLog");

            SettlementSummary before = SummaryOf(settlement);
            settlement.AddStore(blocks, 7);
            settlement.AddStore(wood, 30);
            settlement.AddStore(Def("Silver"), 5);
            settlement.AddStore(Def("RawRice"), 9);
            settlement.AddStore(Def("Cloth"), 3);
            SettlementSummary after = SummaryOf(settlement);

            Assert.Equal(CountOf(before, blocks) + 7, CountOf(after, blocks));
            Assert.Equal(CountOf(before, wood) + 30, CountOf(after, wood));
            Assert.Equal(before.StoredUnits + 54, after.StoredUnits);
            Assert.True(after.StoredNutrition > before.StoredNutrition, "Rice went into the ledger and the larder did not grow.");

            // Most important first, by name within a rank, and by nothing else: a second capture of the
            // same settlement cannot shuffle the bar.
            List<string> shown = after.Stock.Select(line => line.DefName).ToList();
            Assert.True(shown.Count >= 5);
            Assert.Equal(
                shown
                    .OrderByDescending(name => Def(name).resourceReadoutPriority)
                    .ThenBy(name => name, StringComparer.Ordinal),
                shown);
            Assert.Equal(shown, SummaryOf(settlement).Stock.Select(line => line.DefName));
            // The rule is exercised, not vacuous: more than one rank is on show, and more than one def in a rank.
            Assert.True(shown.Select(name => Def(name).resourceReadoutPriority).Distinct().Count() > 1);
            Assert.Contains(shown.GroupBy(name => Def(name).resourceReadoutPriority), rank => rank.Count() > 1);

            Assert.All(after.Stock, line =>
            {
                Assert.True(line.Count > 0);
                Assert.False(string.IsNullOrWhiteSpace(line.Label));
            });

            // Spent to nothing, a line goes away rather than standing at zero.
            settlement.SetStoreCount(blocks, 0);
            Assert.DoesNotContain(SummaryOf(settlement).Stock, line => line.DefName == blocks.defName);
        }

        [Fact]
        public void Gear_and_rubble_are_not_stock_in_either_book_and_silver_food_and_medicine_are()
        {
            Game game = NewSoloGame("seam-resources-only");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;

            ThingDef chunk = Def("ChunkSandstone");
            ThingDef club = Def("MeleeWeapon_Club");
            ThingDef knife = Def("MeleeWeapon_Knife");
            ThingDef silver = Def("Silver");
            ThingDef rice = Def("RawRice");
            ThingDef medicine = Def("MedicineHerbal");

            SettlementSummary before = SummaryOf(settlement);

            // The same six things in each book: three a settlement stores against need, three it merely has.
            settlement.AddStore(chunk, 9);
            settlement.AddStore(club, 2);
            settlement.AddStore(knife, 3);
            settlement.AddStore(silver, 50);
            settlement.AddStore(rice, 20);
            settlement.AddStore(medicine, 4);

            SpawnStack(map, chunk, 6);
            SpawnStack(map, club, 1);
            SpawnStack(map, knife, 1);
            SpawnStack(map, silver, 25);
            SpawnStack(map, rice, 10);
            SpawnStack(map, medicine, 2);

            SettlementSummary after = SummaryOf(settlement);

            Assert.DoesNotContain(after.Stock, line => line.DefName == chunk.defName);
            Assert.DoesNotContain(after.Stock, line => line.DefName == club.defName);
            Assert.DoesNotContain(after.Stock, line => line.DefName == knife.defName);

            Assert.Equal(CountOf(before, silver) + 75, CountOf(after, silver));
            Assert.Equal(CountOf(before, rice) + 30, CountOf(after, rice));
            Assert.Equal(CountOf(before, medicine) + 6, CountOf(after, medicine));

            // The totals follow the lines: the twenty-two units of gear and rubble moved neither.
            Assert.Equal(before.StoredUnits + 75 + 30 + 6, after.StoredUnits);
            Assert.Equal(after.Stock.Sum(line => line.Count), after.StoredUnits);
            Assert.Equal(before.StoredNutrition + rice.ingestible!.nutrition * 30, after.StoredNutrition, 2);
        }

        [Fact]
        public void A_settlement_opened_for_the_first_time_reports_resources_and_nothing_else()
        {
            // What the bar shows the moment a player first looks inside a town. A generated map is strewn
            // with rock chunks and the founding band's weapons lie where the scenario dropped them; before
            // the filter all of it read as stock.
            Game game = NewSoloGame("seam-both-books");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;

            List<Thing> lying = map.listerThings.ThingsInGroup(ThingRequestGroup.Item).Where(t => !(t is Corpse)).ToList();
            Assert.Contains(lying, t => !t.def.CountAsResource);   // otherwise this proves nothing
            Assert.Contains(lying, t => t.def.CountAsResource);

            SettlementSummary summary = SummaryOf(settlement);

            Assert.NotEmpty(summary.Stock);
            Assert.All(summary.Stock, line => Assert.True(Def(line.DefName).CountAsResource, line.DefName + " is not a resource"));
            Assert.All(
                lying.Where(t => !t.def.CountAsResource).Select(t => t.def.defName).Distinct(),
                name => Assert.DoesNotContain(summary.Stock, line => line.DefName == name));

            // Nothing countable was dropped along with the rubble.
            foreach (IGrouping<ThingDef, Thing> held in lying.Where(t => t.def.CountAsResource).GroupBy(t => t.def))
            {
                Assert.Equal(settlement.StoreCountOf(held.Key) + held.Sum(t => t.stackCount), CountOf(summary, held.Key));
            }
        }

        [Fact]
        public void What_counts_as_a_resource_is_what_a_settlement_stores_against_need()
        {
            // The counted set, as a rule rather than a list of priorities: content says which defs a
            // readout counts, and these are the properties that set has to have for the readout to be
            // believed. No def's own priority is pinned: those are recalled, and only order a list.
            List<ThingDef> items = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Item)
                .ToList();
            List<ThingDef> counted = items.Where(d => d.CountAsResource).ToList();

            Assert.NotEmpty(counted);
            Assert.All(DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.CountAsResource), d =>
                Assert.True(d.category == ThingCategory.Item, d.defName + " is counted and is not an item"));

            // Gear is never a resource, and a resource is something that stacks.
            Assert.All(items.Where(d => d.IsWeapon || d.IsApparel), d =>
                Assert.False(d.CountAsResource, d.defName + " is gear and is counted"));
            Assert.All(counted, d => Assert.True(d.stackLimit > 1, d.defName + " is counted and does not stack"));

            // Every food is counted, which is the only reason StoredNutrition is the settlement's whole larder.
            Assert.All(items.Where(d => d.IsNutritionGivingIngestible), d =>
                Assert.True(d.CountAsResource, d.defName + " is food and is not counted"));

            // No body is.
            Assert.All(items.Where(d => d.defName.StartsWith("Corpse_", StringComparison.Ordinal)), d =>
                Assert.False(d.CountAsResource, d.defName + " is a body and is counted"));

            foreach (string name in new[] { "Silver", "WoodLog", "Steel", "BlocksSandstone", "Cloth", "MedicineHerbal", "RawBerries", "MealSimple" })
            {
                Assert.True(Def(name).CountAsResource, name + " should be counted");
            }
            foreach (string name in new[] { "ChunkSandstone", "ChunkGranite", "ChunkLimestone", "MeleeWeapon_Club", "MeleeWeapon_Knife", "SimpleProstheticLeg" })
            {
                Assert.False(Def(name).CountAsResource, name + " should not be counted");
            }

            // A def that says nothing is left out: uncounted is the default, not a choice content has to make.
            Assert.False(new ThingDef().CountAsResource);
        }

        [Fact]
        public void Rations_issued_onto_an_opened_settlements_map_are_still_the_settlements()
        {
            // The reason stock is both books and not the ledger. Opening a settlement moves its larder out
            // of the ledger and onto the map, where people eat. A ledger-only readout shows the food of a
            // town that has just been fed dropping away.
            Game game = NewSoloGame("seam-both-books");
            Settlement settlement = PlayerSettlement(game);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            settlement.AddStore(Def("Pemmican"), 500);

            SettlementSummary before = SummaryOf(settlement);
            float ledgerNutritionBefore = SettlementLarder.StoredNutrition(settlement);
            Assert.True(before.StoredNutrition >= ledgerNutritionBefore);

            int issued = SettlementStockInitiative.IssueFromStores(settlement, map);
            Assert.True(issued > 0, "test setup: nothing was issued, so nothing crossed between the two books");
            Assert.True(SettlementLarder.StoredNutrition(settlement) < ledgerNutritionBefore);

            SettlementSummary after = SummaryOf(settlement);

            Assert.Equal(Counts(before), Counts(after));
            Assert.Equal(before.StoredUnits, after.StoredUnits);
            Assert.Equal(before.StoredNutrition, after.StoredNutrition, 2);

            // And it is the figure the settlement's own initiatives call "how much food does this town have".
            Assert.Equal(HuntingInitiative.NutritionAvailable(settlement, map), after.StoredNutrition, 2);

            // The whole pass — paint a granary, bank what rests in it, issue the shortfall — only ever moves
            // a unit from one book to the other, so it cannot change what the settlement holds either.
            SettlementStockInitiative.Run(settlement, map);
            Assert.Equal(Counts(after), Counts(SummaryOf(settlement)));
        }

        [Fact]
        public void Goods_lying_on_the_map_are_stock_and_a_body_is_not()
        {
            Game game = NewSoloGame("seam-map-goods");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;
            ThingDef blocks = Def("BlocksSandstone");

            SettlementSummary before = SummaryOf(settlement);

            SpawnStack(map, blocks, 12);
            SettlementSummary withBlocks = SummaryOf(settlement);

            Assert.Equal(CountOf(before, blocks) + 12, CountOf(withBlocks, blocks));
            Assert.Equal(before.StoredUnits + 12, withBlocks.StoredUnits);

            var stranger = new Pawn(Human, "Stranger");
            GenSpawn.Spawn(stranger, FreeCell(map), map);
            stranger.health.Kill(null, null);
            Corpse body = Assert.IsAssignableFrom<Corpse>(stranger.corpse);
            Assert.True(body.Spawned);

            SettlementSummary withBody = SummaryOf(settlement);

            // A settlement's dead are its people, not its stock.
            Assert.DoesNotContain(withBody.Stock, line => line.DefName == body.def.defName);
            Assert.Equal(withBlocks.StoredUnits, withBody.StoredUnits);
        }

        // ---- edict slots ----

        [Fact]
        public void Slots_used_rises_when_an_edict_is_issued_and_falls_when_it_is_rescinded()
        {
            NewSoloGame("seam-slots");

            GodViewSnapshot before = GodViewSnapshot.Capture();
            Assert.Equal(GodTuning.MaxActiveEdicts, before.EdictSlotLimit);
            Assert.Equal(0, before.EdictSlotsUsed);

            EdictOption target = before.Edicts.First(o => o.Availability == EdictAvailability.Available);

            Assert.True(GodCommands.IssueEdict(target.DefName).Changed);
            GodViewSnapshot issued = GodViewSnapshot.Capture();
            Assert.Equal(1, issued.EdictSlotsUsed);
            Assert.Equal(before.EdictSlotLimit, issued.EdictSlotLimit);

            // A refused or repeated command takes no slot.
            GodCommands.IssueEdict(target.DefName);
            Assert.Equal(1, GodViewSnapshot.Capture().EdictSlotsUsed);

            Assert.True(GodCommands.RescindEdict(target.DefName).Changed);
            Assert.Equal(0, GodViewSnapshot.Capture().EdictSlotsUsed);
        }

        [Fact]
        public void The_slot_count_and_the_edict_list_tell_one_story_all_the_way_to_a_full_slate()
        {
            NewSoloGame("seam-slots-full");

            // Shipped content gates one edict per era rung, so only a civilization some way up the ladder
            // has more edicts within reach than slots to put them in (GodViewTests.CompleteLadderThrough).
            EraDef industrial = DefDatabase<EraDef>.GetNamed("Industrial");
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order))
            {
                if (era.order > industrial.order) break;
                foreach (ResearchProjectDef project in era.Projects)
                {
                    if (!Find.ResearchManager.IsFinished(project)) Find.ResearchManager.SetProjectFinishedForSetup(project);
                }
            }

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            while (true)
            {
                Assert.Equal(snapshot.Edicts.Count(o => o.IsActive), snapshot.EdictSlotsUsed);
                Assert.InRange(snapshot.EdictSlotsUsed, 0, snapshot.EdictSlotLimit);

                // "No room" is said exactly when the count says there is none.
                if (snapshot.EdictSlotsUsed < snapshot.EdictSlotLimit)
                {
                    Assert.DoesNotContain(snapshot.Edicts, o => o.Availability == EdictAvailability.NoSlotFree);
                }
                else
                {
                    Assert.DoesNotContain(snapshot.Edicts, o => o.Availability == EdictAvailability.Available);
                }

                // One that rescinds nothing: a contradictory edict replaces another (EdictDef.Contradicts) and fills no slot.
                EdictOption? next = snapshot.Edicts.FirstOrDefault(o => o.Availability == EdictAvailability.Available
                    && !DefDatabase<EdictDef>.GetNamed(o.DefName).exclusiveWork && Find.God.ContradictedBy(DefDatabase<EdictDef>.GetNamed(o.DefName)).Count == 0);
                if (next == null) break;

                int usedBefore = snapshot.EdictSlotsUsed;
                Assert.True(GodCommands.IssueEdict(next.DefName).Changed);
                snapshot = GodViewSnapshot.Capture();
                Assert.Equal(usedBefore + 1, snapshot.EdictSlotsUsed);
            }

            Assert.Equal(snapshot.EdictSlotLimit, snapshot.EdictSlotsUsed);
            Assert.Contains(snapshot.Edicts, o => o.Availability == EdictAvailability.NoSlotFree);
        }

        // ---- the clock ----

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(GenDate.TicksPerHour - 1)]
        [InlineData(GenDate.TicksPerHour * 13 + 1250)]
        [InlineData(GenDate.TicksPerDay - 1)]
        [InlineData(GenDate.TicksPerDay * 17 + GenDate.TicksPerHour * 23)]
        [InlineData(GenDate.TicksPerYear + GenDate.TicksPerQuadrum * 2 + 4321)]
        public void The_date_time_label_and_the_hour_read_back_to_the_tick(int tick)
        {
            Find.TickManager.DebugSetTicksGame(tick);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Equal(tick, snapshot.TicksGame);
            Assert.Equal(GenDate.DateFullStringAt(tick, 0f), snapshot.DateTimeLabel);
            // The label a host swaps for DateLabel is that label with the hour on the end, not a second
            // calendar: the day it names is the day DateLabel names.
            Assert.StartsWith(snapshot.DateLabel, snapshot.DateTimeLabel);

            Assert.True(snapshot.HourOfDay >= 0f && snapshot.HourOfDay < 24f);
            Assert.Equal((float)(tick % GenDate.TicksPerDay) / GenDate.TicksPerHour, snapshot.HourOfDay, 4);
            Assert.EndsWith(
                ", " + ((int)snapshot.HourOfDay).ToString(CultureInfo.InvariantCulture) + "h",
                snapshot.DateTimeLabel);
        }

        [Fact]
        public void The_hour_moves_within_a_day_and_the_label_turns_over_on_the_hour()
        {
            const int start = GenDate.TicksPerDay * 3 + GenDate.TicksPerHour * 22;

            Find.TickManager.DebugSetTicksGame(start);
            GodViewSnapshot atTheHour = GodViewSnapshot.Capture();

            Find.TickManager.DebugSetTicksGame(start + GenDate.TicksPerHour / 2);
            GodViewSnapshot halfPast = GodViewSnapshot.Capture();

            Find.TickManager.DebugSetTicksGame(start + GenDate.TicksPerHour);
            GodViewSnapshot nextHour = GodViewSnapshot.Capture();

            Find.TickManager.DebugSetTicksGame(start + GenDate.TicksPerHour * 2);
            GodViewSnapshot midnight = GodViewSnapshot.Capture();

            // Half an hour on: the sun has moved, the printed hour has not.
            Assert.Equal(atTheHour.HourOfDay + 0.5f, halfPast.HourOfDay, 4);
            Assert.Equal(atTheHour.DateTimeLabel, halfPast.DateTimeLabel);

            Assert.Equal(atTheHour.HourOfDay + 1f, nextHour.HourOfDay, 4);
            Assert.NotEqual(atTheHour.DateTimeLabel, nextHour.DateTimeLabel);
            Assert.Equal(atTheHour.DateLabel, nextHour.DateLabel);

            // Over midnight the hour wraps and the day turns with it.
            Assert.Equal(0f, midnight.HourOfDay, 4);
            Assert.NotEqual(atTheHour.DateLabel, midnight.DateLabel);
            Assert.StartsWith(midnight.DateLabel, midnight.DateTimeLabel);
        }

        [Fact]
        public void The_hour_is_the_calendars_not_the_count_of_ticks_played()
        {
            // A game may start part-way through a day (TickManager's start offset). The simulation's own
            // daylight reads the calendar clock — Building.Plant passes TicksAbs — so the hour a host lights
            // its scene by has to be that clock's too, or the crops grow under a different sun from the one
            // on screen.
            Find.TickManager = new TickManager(GenDate.TicksPerHour * 9);
            Assert.Equal(0, Find.TickManager.TicksGame);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Equal(GenDate.HourFloat(Find.TickManager.TicksAbs, 0f), snapshot.HourOfDay, 4);
            Assert.Equal(9f, snapshot.HourOfDay, 4);
            Assert.Equal(GenDate.DateFullStringAt(Find.TickManager.TicksAbs, 0f), snapshot.DateTimeLabel);
        }

        // ---- how long a condition has left ----

        [Fact]
        public void A_conditions_time_left_is_its_ticks_left_in_words_and_counts_down_with_them()
        {
            CoreWorld world = BareWorld();
            world.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 2.5f));

            ConditionLine atStart = Assert.Single(GodViewSnapshot.Capture().Conditions);
            Assert.False(atStart.Permanent);
            Assert.Equal(GenDate.ToStringTicksToPeriod(atStart.TicksLeft), atStart.TimeLeftLabel);
            Assert.Contains("day", atStart.TimeLeftLabel);

            // Two days on there are hours left, and the label says hours.
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 2);
            ConditionLine later = Assert.Single(GodViewSnapshot.Capture().Conditions);
            Assert.Equal(atStart.TicksLeft - GenDate.TicksPerDay * 2, later.TicksLeft);
            Assert.Equal(GenDate.ToStringTicksToPeriod(later.TicksLeft), later.TimeLeftLabel);
            Assert.Contains("hour", later.TimeLeftLabel);
            Assert.NotEqual(atStart.TimeLeftLabel, later.TimeLeftLabel);

            // In its last hour it stops counting and says so.
            Find.TickManager.DebugSetTicksGame(atStart.TicksLeft - 1);
            ConditionLine ending = Assert.Single(GodViewSnapshot.Capture().Conditions);
            Assert.Equal(1, ending.TicksLeft);
            Assert.Equal(GenDate.ToStringTicksToPeriod(1), ending.TimeLeftLabel);
            Assert.DoesNotContain(ending.TimeLeftLabel, char.IsDigit);
        }

        [Fact]
        public void A_permanent_condition_says_so_instead_of_counting_down_from_the_largest_number_there_is()
        {
            CoreWorld world = BareWorld();
            world.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeCondition(GameConditionDefOf.ColdSnap, GameCondition.PermanentDuration));

            ConditionLine line = Assert.Single(GodViewSnapshot.Capture().Conditions);

            Assert.True(line.Permanent);
            Assert.False(string.IsNullOrWhiteSpace(line.TimeLeftLabel));
            // TicksLeft is int.MaxValue here by design; a label made from that would read as centuries.
            Assert.NotEqual(GenDate.ToStringTicksToPeriod(line.TicksLeft), line.TimeLeftLabel);
            Assert.DoesNotContain(line.TimeLeftLabel, char.IsDigit);
        }

        [Theory]
        [InlineData(GenDate.TicksPerHour)]
        [InlineData(GenDate.TicksPerHour + 1)]
        [InlineData(GenDate.TicksPerHour * 5 + 700)]
        [InlineData(GenDate.TicksPerDay - 1)]
        [InlineData(GenDate.TicksPerDay)]
        [InlineData(GenDate.TicksPerDay * 2 + GenDate.TicksPerHour * 7)]
        [InlineData(GenDate.TicksPerDay * 37 + 12345)]
        [InlineData(GenDate.TicksPerYear - 1)]
        [InlineData(GenDate.TicksPerYear)]
        [InlineData(GenDate.TicksPerYear * 3 + GenDate.TicksPerDay * 11)]
        public void A_period_reads_back_to_the_ticks_it_was_made_from(int ticks)
        {
            string label = GenDate.ToStringTicksToPeriod(ticks);

            string[] parts = label.Split(' ');
            Assert.Equal(2, parts.Length);
            float amount = float.Parse(parts[0], CultureInfo.InvariantCulture);
            (int unitTicks, int nextUnitTicks) = parts[1].TrimEnd('s') switch
            {
                "hour" => (GenDate.TicksPerHour, GenDate.TicksPerDay),
                "day" => (GenDate.TicksPerDay, GenDate.TicksPerYear),
                "year" => (GenDate.TicksPerYear, int.MaxValue),
                _ => throw new InvalidOperationException("unknown unit in '" + label + "'"),
            };

            // The largest unit that fits...
            Assert.True(ticks >= unitTicks && ticks < nextUnitTicks, "'" + label + "' is in the wrong unit for " + ticks + " ticks");
            // ...to one decimal place, so never more than a twentieth of that unit out.
            Assert.True(
                Math.Abs((double)amount * unitTicks - ticks) <= unitTicks * 0.05 + 1,
                "'" + label + "' reads back to " + (amount * unitTicks) + " ticks, not " + ticks);
            // One of something is not "1 days".
            Assert.Equal(parts[0] != "1", parts[1].EndsWith("s", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(GenDate.TicksPerHour, "1 hour")]
        [InlineData(GenDate.TicksPerHour * 2, "2 hours")]
        [InlineData(GenDate.TicksPerDay, "1 day")]
        [InlineData(GenDate.TicksPerDay + GenDate.TicksPerDay / 2, "1.5 days")]
        [InlineData(GenDate.TicksPerYear, "1 year")]
        public void A_period_is_worded_the_way_a_host_will_print_it(int ticks, string expected)
        {
            // The wording is the contract here — every host prints this string verbatim — so these few are
            // pinned as text. None is a tuned number: each is an exact multiple of its unit.
            Assert.Equal(expected, GenDate.ToStringTicksToPeriod(ticks));
        }

        [Theory]
        [InlineData(GenDate.TicksPerHour - 1)]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-500)]
        public void A_period_shorter_than_an_hour_does_not_pretend_to_count(int ticks)
        {
            string label = GenDate.ToStringTicksToPeriod(ticks);

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.DoesNotContain(label, char.IsDigit);
            Assert.Equal(GenDate.ToStringTicksToPeriod(GenDate.TicksPerHour - 1), label);
        }

        // ---- before a world exists ----

        [Fact]
        public void Before_any_world_exists_the_new_fields_are_empty_rather_than_missing()
        {
            // The main menu. The host's own CaptureIsSafeBeforeAWorldExists draws this state.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Empty(snapshot.Settlements);
            Assert.Empty(snapshot.Conditions);

            // The cap is a fact about the game, not about a world, so it is already true here.
            Assert.Equal(GodTuning.MaxActiveEdicts, snapshot.EdictSlotLimit);
            Assert.Equal(0, snapshot.EdictSlotsUsed);

            Assert.Equal(0f, snapshot.HourOfDay);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.DateTimeLabel));
            Assert.StartsWith(snapshot.DateLabel, snapshot.DateTimeLabel);
        }
    }
}
