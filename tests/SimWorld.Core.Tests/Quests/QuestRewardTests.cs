using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Quests;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Quests
{
    /// <summary>
    /// Quest rewards actually reaching the civilization (<see cref="QuestRewardSink"/>, the implementation
    /// <see cref="IQuestRewardSink"/> never had).
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> Quests generated, ran and resolved for
    /// three batches, and <see cref="QuestPart_Reward.Enable"/> handed its rewards to an interface no type in
    /// <c>src/</c> implemented, so <c>Find.QuestManager.RewardSink?.GiveRewards(...)</c> was a null-conditional
    /// over a null. Measured on exactly the fixture
    /// <see cref="A_completed_quest_used_to_enrich_nobody"/> builds — a fresh game, the shipped
    /// <c>Quest_LostCaravan</c> script accepted and run to completion — the old behaviour produced: quest
    /// state <c>EndedSuccess</c>; <c>Quest.RewardsGiven</c> reading "200 silver, 5 goodwill";
    /// <c>RewardSink</c> null; the seat's stores <c>MeleeWeapon_Knife:2</c> before and
    /// <c>MeleeWeapon_Knife:2</c> after, with no Silver entry ever created; the civilization's wealth 60
    /// before and 60 after; goodwill with the faction the reward named 0 before and 0 after; and a chronicle
    /// holding nothing but the founding line. The quest said it had paid and nothing anywhere had been paid.
    /// </summary>
    public class QuestRewardTests : ContentTestBase
    {
        public QuestRewardTests(CoreContentFixture content) : base(content)
        {
            // Find's letters/quest/storyteller/faction services are thread-static and not covered by
            // ContentTestBase's Find.Reset(); give every test a clean slate explicitly, as QuestTests does.
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            QuestGen.ResetIdCounterForTests();
        }

        // ---- fixtures ----

        private static QuestScriptDef Script(string defName) => DefDatabase<QuestScriptDef>.GetNamed(defName);

        private static DifficultyDef Difficulty(string defName) => DefDatabase<DifficultyDef>.GetNamed(defName);

        private static ThingDef Silver => EconomyThingDefOf.Silver;

        private static ThingDef Knife => DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife");

        private static Settlement Town(string name, int tile, int foundingTick) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, name, foundingTick);

        private static CivilizationTarget TargetOf(params Settlement[] settlements)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(settlements);
            return target;
        }

        private static RewardRecord[] Silvers(float amount) =>
            new[] { new RewardRecord(QuestRewardSink.SilverKind, amount) };

        private static global::SimWorld.Sim.Game NewGame(string seed) =>
            global::SimWorld.Sim.Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: false, bandSize: 20);

        /// <summary>Accepts the quest and ticks the quest manager until it has ended, which is what a running
        /// game does to it — rather than reaching in and enabling the reward part by hand.</summary>
        private static Quest RunToCompletion(QuestScriptDef script)
        {
            Quest quest = QuestGen.Generate(script, new Slate());
            Find.QuestManager.Add(quest);
            quest.Accept();

            var delay = (QuestPart_Delay)quest.PartsListForReading[0];
            int ticks = delay.TicksLeft;
            Assert.True(ticks > 0, "the script's delay did not start, so nothing would ever have paid out");
            for (int i = 0; i < ticks; i++) Find.QuestManager.QuestManagerTick();
            return quest;
        }

        private static IEnumerable<string> ChronicleHeadlines() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName);

        // ---- the headline ----

        [Fact]
        public void A_completed_quest_used_to_enrich_nobody()
        {
            // The exact fixture the class doc quotes the old numbers from.
            global::SimWorld.Sim.Game game = NewGame("quest-reward");
            Settlement seat = game.CivilizationTarget.Seat!;
            Faction player = Find.FactionManager.OfPlayer!;
            Faction rewardingFaction = Find.FactionManager.FirstFactionOfDef(
                DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"))!;

            Assert.Equal(0, seat.StoreCountOf(Silver));
            int goodwillBefore = player.GoodwillWith(rewardingFaction);
            float wealthBefore = game.CivilizationTarget.PlayerWealthForStoryteller;

            Quest quest = RunToCompletion(Script("Quest_LostCaravan"));

            // The quest itself always believed it had paid — that half was never broken.
            Assert.Equal(QuestState.EndedSuccess, quest.State);
            Assert.Contains(quest.RewardsGiven, r => r.kind == QuestRewardSink.SilverKind && r.amount > 0f);
            Assert.Contains(quest.RewardsGiven, r => r.kind == QuestRewardSink.GoodwillKind && r.amount != 0f);

            // ...and now the world agrees. Silver exists in the seat's ledger where there was no entry at all,
            // the civilization is measurably richer for it, and the faction that paid thinks better of us.
            Assert.True(seat.StoreCountOf(Silver) > 0,
                "the quest awarded silver and the civilization's seat holds none of it");
            Assert.True(game.CivilizationTarget.PlayerWealthForStoryteller > wealthBefore,
                "the civilization's own wealth readout did not move, so nothing of value arrived");
            Assert.True(player.GoodwillWith(rewardingFaction) > goodwillBefore,
                "the quest awarded goodwill and the relation did not move");

            // Untouched stores stay untouched: a payout adds, it does not rewrite the ledger.
            Assert.Equal(2, seat.StoreCountOf(Knife));

            // And the civilization's record says it happened, the same way a raid's does.
            Assert.Contains(ChronicleHeadlines(), h => h.StartsWith("Quest reward:", StringComparison.Ordinal));
        }

        [Fact]
        public void Every_quest_manager_ships_with_a_sink_so_no_path_pays_into_nothing()
        {
            // The defect was not "the sink is wrong", it was "there is no sink" — on a new game, on a load,
            // and in every bare fixture. All three build a QuestManager the same way, so all three get one.
            Assert.IsType<QuestRewardSink>(new QuestManager().RewardSink);
            Assert.IsType<QuestRewardSink>(Find.QuestManager.RewardSink);

            // Still replaceable, and still silenceable, for a host or a test that wants neither.
            Find.QuestManager.RewardSink = null;
            Assert.Null(Find.QuestManager.RewardSink);
        }

        // ---- difficulty ----

        [Fact]
        public void The_silver_a_quest_pays_scales_with_the_difficultys_quest_reward_value_factor()
        {
            // DifficultyDef.questRewardValueFactor was dormant for want of anything that paid a reward.
            // It is applied where RimWorld applies it — when the reward is *generated* — so this measures
            // the whole path a shipped script takes: generate under a difficulty, then pay what came out.
            DifficultyDef medium = Difficulty("Medium");
            DifficultyDef extreme = Difficulty("Extreme");
            Assert.True(extreme.questRewardValueFactor > medium.questRewardValueFactor,
                "the two shipped difficulties do not differ on this knob, so this test would prove nothing");

            const float Award = 200f;
            int onMedium = SilverPaidUnder(medium, Award);
            int onExtreme = SilverPaidUnder(extreme, Award);

            Assert.True(onExtreme > onMedium,
                "the harder difficulty pays " + onExtreme + ", the easier one " + onMedium);

            // The ratio is the two defs' own factors, not a number this test invented.
            double expectedRatio = extreme.questRewardValueFactor / (double)medium.questRewardValueFactor;
            Assert.Equal(expectedRatio, onExtreme / (double)onMedium, 2);

            // And a factor of exactly one leaves the award alone, so the scaling cannot be silently always-on.
            Assert.Equal(1f, medium.questRewardValueFactor);
            Assert.Equal((int)Award, onMedium);
        }

        [Fact]
        public void Goodwill_is_deliberately_not_scaled_by_difficulty()
        {
            // The translation this lane recorded: questRewardValueFactor is a factor on reward *value*, and
            // goodwill is a diplomatic reading clamped to [-100, 100] whose thresholds decide war and
            // alliance. Scaling it would move factions across those thresholds as a side effect of a
            // difficulty setting, which is a different kind of change from "the pot of silver is bigger".
            var rewards = new[] { new RewardRecord(QuestRewardSink.GoodwillKind, 5f, "OutlanderCivilization") };

            int onMedium = GoodwillAppliedUnder(Difficulty("Medium"), rewards);
            int onExtreme = GoodwillAppliedUnder(Difficulty("Extreme"), rewards);

            Assert.Equal(5, onMedium);
            Assert.Equal(onMedium, onExtreme);
        }

        [Fact]
        public void A_positive_reward_is_never_rounded_out_of_existence()
        {
            // The same floor SettlementRaidResolver puts under a loser's casualties, for the same reason: the
            // one outcome this class must never produce is "the quest paid nothing". A shallow factor makes
            // generation hand the sink a fractional award — 1 silver becomes 0.01, 20 becomes 0.2 — and both
            // still have to arrive as something.
            var shallow = new DifficultyDef { defName = "TestShallow", questRewardValueFactor = 0.01f };
            Assert.Equal(1, SilverPaidUnder(shallow, 1f));
            Assert.Equal(1, SilverPaidUnder(shallow, 20f));
        }

        /// <summary>
        /// What a quest awarding <paramref name="award"/> silver actually banks under
        /// <paramref name="difficulty"/>: generated first, then paid.
        ///
        /// <para/>The two steps are not decoration. <see cref="DifficultyDef.questRewardValueFactor"/> is
        /// applied at generation (<see cref="QuestNode_GiveReward"/>), so a record reaching the sink already
        /// carries it — handing the sink a hand-built record would measure a layer the factor does not live
        /// in, and would keep passing if generation stopped applying it at all.
        /// </summary>
        private static int SilverPaidUnder(DifficultyDef difficulty, float award)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller { difficulty = difficulty };
            Settlement seat = Town("Seat_" + difficulty.defName, 1, foundingTick: 0);
            var sink = new QuestRewardSink(TargetOf(seat));

            QuestRewardPayment payment = sink.Pay(GeneratedSilverReward(award));

            // What the payment reports and what the ledger holds are the same number, always.
            Assert.Equal(payment.SilverPaid, seat.StoreCountOf(Silver));
            Assert.Equal(0, payment.SilverUnstored);
            return payment.SilverPaid;
        }

        /// <summary>The reward records a one-node script awarding <paramref name="award"/> silver produces
        /// under the storyteller's current difficulty — the same <see cref="QuestNode_GiveReward"/> path every
        /// shipped script goes through, with nothing else in the tree to distract the measurement.</summary>
        private static IReadOnlyList<RewardRecord> GeneratedSilverReward(float award)
        {
            var script = new QuestScriptDef
            {
                defName = "Quest_TestSilverAward",
                root = new QuestNode_GiveReward { silverAmount = award },
            };

            Quest quest = QuestGen.Generate(script, new Slate());
            return quest.PartsListForReading.OfType<QuestPart_Reward>().Single().rewards;
        }

        private static int GoodwillAppliedUnder(DifficultyDef difficulty, RewardRecord[] rewards)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller { difficulty = difficulty };
            Find.FactionManager = new FactionManager();
            Faction player = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Us", "F_Us");
            Faction other = new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), "Them", "F_Them");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(other);

            var sink = new QuestRewardSink(TargetOf(Town("Seat", 1, foundingTick: 0)));
            QuestRewardPayment payment = sink.Pay(rewards);

            Assert.Equal(payment.GoodwillApplied, player.GoodwillWith(other));
            return payment.GoodwillApplied;
        }

        // ---- where it lands ----

        [Fact]
        public void The_reward_goes_to_the_seat_and_not_to_the_settlement_the_player_is_watching()
        {
            // The obvious alternative — pay whichever town the player has open — is the one this lane
            // rejected, for the reason SettlementRaidResolver already gives about raids: a civilization that
            // only changes where the camera points is a stage set. A quest is offered to the civilization.
            Settlement seat = Town("Elder", 10, foundingTick: 0);
            Settlement watched = Town("Younger", 11, foundingTick: 5000);
            CivilizationTarget civilization = TargetOf(seat, watched);
            Find.God.Attention.Focus(watched);
            Assert.True(Find.God.Attention.HasFocus);

            var sink = new QuestRewardSink(civilization);
            QuestRewardPayment payment = sink.Pay(Silvers(120f));

            Assert.Same(seat, civilization.Seat);
            Assert.Equal(payment.SilverPaid, seat.StoreCountOf(Silver));
            Assert.True(payment.SilverPaid > 0);
            Assert.Equal(0, watched.StoreCountOf(Silver));
            Assert.Equal(seat.name, payment.RecipientName);
        }

        [Fact]
        public void Paying_a_reward_takes_no_roll_at_all()
        {
            // Determinism, and the concrete reason the seat was chosen over a population-weighted pick: this
            // runs inside QuestManager.QuestManagerTick, so a roll here would shift every subsequent roll in
            // the game by the accident of when a quest's delay happened to elapse.
            var sink = new QuestRewardSink(TargetOf(Town("Seat", 1, foundingTick: 0), Town("Other", 2, foundingTick: 9)));
            uint before = Rand.Current.Iterations;

            sink.Pay(new[]
            {
                new RewardRecord(QuestRewardSink.SilverKind, 200f),
                new RewardRecord(QuestRewardSink.GoodwillKind, 5f, "OutlanderCivilization"),
            });

            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void A_civilization_with_nowhere_to_store_goods_says_so_rather_than_dropping_them()
        {
            // The failure this whole class exists to remove is a reward silently becoming nothing. A
            // civilization that has lost every settlement genuinely has no ledger to credit — so the silver is
            // counted and said out loud instead of vanishing the way it used to.
            var sink = new QuestRewardSink(TargetOf());
            QuestRewardPayment payment = sink.Pay(Silvers(200f));

            Assert.Null(sink.Treasury);
            Assert.Equal(0, payment.SilverPaid);
            Assert.True(payment.SilverUnstored > 0);
            Assert.False(payment.ChangedTheWorld);
            Assert.Contains(ChronicleHeadlines(), h => h.StartsWith("Quest reward:", StringComparison.Ordinal));
        }

        [Fact]
        public void A_goodwill_reward_naming_a_faction_this_world_does_not_have_is_counted_not_lost()
        {
            // A solo start has no rivals at all, and Quest_LostCaravan still names OutlanderCivilization. The
            // relation cannot move; what must not happen is the sink pretending it did.
            var sink = new QuestRewardSink(TargetOf(Town("Alone", 1, foundingTick: 0)));
            QuestRewardPayment payment = sink.Pay(
                new[] { new RewardRecord(QuestRewardSink.GoodwillKind, 5f, "OutlanderCivilization") });

            Assert.Equal(0, payment.GoodwillApplied);
            Assert.Equal(1, payment.GoodwillUndeliverable);
        }

        [Fact]
        public void A_reward_kind_the_sink_does_not_know_is_counted_rather_than_ignored()
        {
            // Shipped content produces exactly two kinds. A third one arriving means content grew and this
            // class did not, which is a thing a caller should be able to see rather than guess at.
            var sink = new QuestRewardSink(TargetOf(Town("Seat", 1, foundingTick: 0)));
            QuestRewardPayment payment = sink.Pay(new[] { new RewardRecord("psylinkLevel", 1f) });

            Assert.Equal(1, payment.UnrecognisedKinds);
            Assert.False(payment.ChangedTheWorld);
        }

        // ---- persistence ----

        [Fact]
        public void What_a_quest_paid_survives_a_save_and_a_load()
        {
            // The sink keeps no state of its own — it spends the reward on the world and on the chronicle, so
            // those are what has to round-trip. A reward the save forgets is a reward that did nothing, one
            // reload later.
            global::SimWorld.Sim.Game game = NewGame("quest-reward-scribe");
            Settlement seat = game.CivilizationTarget.Seat!;
            Faction rewardingFaction = Find.FactionManager.FirstFactionOfDef(
                DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"))!;

            Faction player = Find.FactionManager.OfPlayer!;
            int goodwillBefore = player.GoodwillWith(rewardingFaction);

            Quest quest = RunToCompletion(Script("Quest_LostCaravan"));
            Assert.Equal(QuestState.EndedSuccess, quest.State);

            int silverAfter = seat.StoreCountOf(Silver);
            int goodwillAfter = player.GoodwillWith(rewardingFaction);
            Assert.True(silverAfter > 0, "nothing was paid, so there is nothing for the save to remember");
            Assert.True(goodwillAfter > goodwillBefore,
                "the relation did not move (from " + goodwillBefore + "), so there is nothing for the save to remember");
            string rewardLine = Find.Storyteller.Chronicle
                .First(e => e.incidentDefName.StartsWith("Quest reward:", StringComparison.Ordinal)).incidentDefName;

            string worldXml = Scribe.SaveToString(game.World!, "world");
            global::SimWorld.World.World loadedWorld = Scribe.Load<global::SimWorld.World.World>(
                worldXml, "world", out IReadOnlyList<string> worldErrors, Content.Database);
            Assert.Empty(worldErrors);

            string storytellerXml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            global::SimWorld.Director.Storyteller loadedStoryteller = Scribe.Load<global::SimWorld.Director.Storyteller>(
                storytellerXml, "storyteller", out IReadOnlyList<string> storytellerErrors, Content.Database);
            Assert.Empty(storytellerErrors);

            string questXml = Scribe.SaveToString(Find.QuestManager, "questManager");
            QuestManager loadedQuests = Scribe.Load<QuestManager>(
                questXml, "questManager", out IReadOnlyList<string> questErrors, Content.Database);
            Assert.Empty(questErrors);

            Settlement loadedSeat = loadedWorld.worldObjects.OfType<Settlement>().First(s => s.name == seat.name);
            Assert.Equal(silverAfter, loadedSeat.StoreCountOf(Silver));

            Faction loadedPlayer = loadedWorld.factions.First(f => f.def.isPlayer);
            Faction loadedOther = loadedWorld.factions.First(f => f.loadID == rewardingFaction.loadID);
            Assert.Equal(goodwillAfter, loadedPlayer.GoodwillWith(loadedOther));

            Assert.Contains(loadedStoryteller.Chronicle, e => e.incidentDefName == rewardLine);

            Quest loadedQuest = loadedQuests.QuestsListForReading.Single();
            Assert.Equal(QuestState.EndedSuccess, loadedQuest.State);
            Assert.Equal(quest.RewardsSummary, loadedQuest.RewardsSummary);

            // And the loaded game can still pay: the sink is not save data, it is what a QuestManager is born
            // with, so a reload does not quietly go back to the old do-nothing behaviour.
            Assert.IsType<QuestRewardSink>(loadedQuests.RewardSink);
        }
    }
}
