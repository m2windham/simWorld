using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Letters;
using SimWorld.Quests;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Quests
{
    public class QuestTests : ContentTestBase
    {
        public QuestTests(CoreContentFixture content) : base(content)
        {
            // Find's letters/quest/storyteller services are thread-static and not covered by
            // ContentTestBase's Find.Reset() (see Find.cs); give every test a clean slate explicitly.
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            QuestGen.ResetIdCounterForTests();
        }

        private static QuestScriptDef Script(string defName) => DefDatabase<QuestScriptDef>.GetNamed(defName);

        // ---- content ----

        [Fact]
        public void Letters_and_quests_content_loads_with_expected_counts_and_defOfs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(8, DefDatabase<LetterDef>.DefCount);
            Assert.Equal(3, DefDatabase<QuestScriptDef>.DefCount);

            Assert.NotNull(LetterDefOf.NeutralEvent);
            Assert.NotNull(LetterDefOf.PositiveEvent);
            Assert.NotNull(LetterDefOf.NegativeEvent);
            Assert.NotNull(LetterDefOf.ThreatBig);
            Assert.NotNull(LetterDefOf.AcceptQuest);
            Assert.NotNull(LetterDefOf.EraReached);
            Assert.Equal(typeof(ChoiceLetter), LetterDefOf.AcceptQuest.letterClass);
            Assert.NotNull(QuestScriptDefOf.Quest_LostCaravan);
        }

        // ---- LetterStack ----

        [Fact]
        public void LetterStack_receive_adds_to_stack_and_raises_event()
        {
            var stack = new LetterStack();
            Letter? received = null;
            stack.LetterReceived += l => received = l;
            Find.TickManager.DebugSetTicksGame(500);

            Letter let = stack.ReceiveLetter("Hello", "World", LetterDefOf.NeutralEvent);

            Assert.Same(let, received);
            Assert.Single(stack.LettersListForReading);
            Assert.Equal(500, let.arrivalTick);
            Assert.Equal("Hello", let.label);
        }

        [Fact]
        public void LetterStack_remove_removes_it()
        {
            var stack = new LetterStack();
            Letter let = stack.ReceiveLetter("A", "B", LetterDefOf.NeutralEvent);

            Assert.True(stack.RemoveLetter(let));
            Assert.Empty(stack.LettersListForReading);
            Assert.False(stack.RemoveLetter(let));
        }

        [Fact]
        public void LetterStack_tick_runs_choice_letter_timeout_action_and_removes_it()
        {
            var stack = new LetterStack();
            Find.TickManager.DebugSetTicksGame(0);
            var choice = new ChoiceLetter { def = LetterDefOf.AcceptQuest, label = "Offer" };
            bool timedOut = false;
            choice.SetTimeout(10, () => timedOut = true);
            stack.ReceiveLetter(choice);

            Find.TickManager.DebugSetTicksGame(5);
            stack.LetterStackTick();
            Assert.False(timedOut);
            Assert.Single(stack.LettersListForReading);

            Find.TickManager.DebugSetTicksGame(10);
            stack.LetterStackTick();
            Assert.True(timedOut);
            Assert.Empty(stack.LettersListForReading);
        }

        [Fact]
        public void ChoiceLetter_without_timeout_is_not_removed_by_tick()
        {
            var stack = new LetterStack();
            var choice = new ChoiceLetter { def = LetterDefOf.AcceptQuest, label = "Offer" };
            stack.ReceiveLetter(choice);

            Find.TickManager.DebugSetTicksGame(999999);
            stack.LetterStackTick();

            Assert.Single(stack.LettersListForReading);
        }

        // ---- Slate ----

        [Fact]
        public void Slate_get_set_prefix_and_exists()
        {
            var slate = new Slate();
            Assert.False(slate.Exists("points"));
            slate.Set("points", 42f);
            Assert.True(slate.Exists("points"));
            Assert.Equal(42f, slate.Get<float>("points"));
            Assert.True(slate.TryGet("points", out float points));
            Assert.Equal(42f, points);
            Assert.Equal(0f, slate.Get<float>("missing"));
            Assert.Equal(-1f, slate.Get("missing", -1f));

            slate.PushPrefix("inner");
            slate.Set("points", 7f);
            Assert.Equal(7f, slate.Get<float>("points"));
            slate.PopPrefix();
            Assert.Equal(42f, slate.Get<float>("points")); // outer value untouched by the prefixed write
        }

        // ---- QuestGen ----

        [Fact]
        public void QuestGen_builds_Quest_LostCaravan_with_expected_parts_and_unique_signals()
        {
            var slate = new Slate();
            slate.Set("points", 50f);
            Quest quest = QuestGen.Generate(Script("Quest_LostCaravan"), slate);

            Assert.Equal(1, quest.id);
            Assert.Same(Script("Quest_LostCaravan"), quest.root);
            Assert.Equal("quest1.initiate", quest.initiateSignal);
            Assert.Equal(3, quest.PartsListForReading.Count);

            var delay = Assert.IsType<QuestPart_Delay>(quest.PartsListForReading[0]);
            var reward = Assert.IsType<QuestPart_Reward>(quest.PartsListForReading[1]);
            var end = Assert.IsType<QuestPart_QuestEnd>(quest.PartsListForReading[2]);

            Assert.Equal(quest.initiateSignal, delay.inSignal);
            Assert.NotEqual(quest.initiateSignal, delay.outSignalCompleted);
            Assert.Equal(delay.outSignalCompleted, reward.inSignal);
            Assert.Equal(delay.outSignalCompleted, end.inSignal);
            Assert.Equal(QuestEndOutcome.Success, end.outcome);

            // A second quest generated in the same test gets a disjoint id and signal namespace.
            Quest second = QuestGen.Generate(Script("Quest_LostCaravan"), new Slate());
            Assert.Equal(2, second.id);
            Assert.NotEqual(quest.initiateSignal, second.initiateSignal);
        }

        [Fact]
        public void Quest_LostCaravan_accept_starts_delay_then_gives_reward_and_ends_success()
        {
            Quest quest = QuestGen.Generate(Script("Quest_LostCaravan"), new Slate());
            Find.QuestManager.Add(quest);
            Assert.Equal(QuestState.NotYetAccepted, quest.State);

            quest.Accept();
            Assert.Equal(QuestState.Ongoing, quest.State);
            var delay = Assert.IsType<QuestPart_Delay>(quest.PartsListForReading[0]);
            Assert.True(delay.TicksLeft > 0);

            int ticks = delay.TicksLeft;
            for (int i = 0; i < ticks; i++) Find.QuestManager.QuestManagerTick();

            Assert.Equal(QuestState.EndedSuccess, quest.State);
            Assert.Equal(200f, quest.RewardsGiven.First(r => r.kind == "silver").amount);
            Assert.Equal(5f, quest.RewardsGiven.First(r => r.kind == "goodwill").amount);
            Assert.Contains("silver", quest.RewardsSummary);
        }

        [Fact]
        public void Quest_Tribute_expires_to_EndedOfferExpired_when_not_accepted_within_expireDays()
        {
            Find.TickManager.DebugSetTicksGame(0);
            Quest quest = QuestGen.Generate(Script("Quest_Tribute"), new Slate());
            Find.QuestManager.Add(quest);
            Assert.True(quest.ticksUntilAcceptanceExpiry > 0);

            Find.TickManager.DebugSetTicksGame(quest.appearanceTick + quest.ticksUntilAcceptanceExpiry - 1);
            quest.QuestTick();
            Assert.Equal(QuestState.NotYetAccepted, quest.State);

            Find.TickManager.DebugSetTicksGame(quest.appearanceTick + quest.ticksUntilAcceptanceExpiry);
            quest.QuestTick();
            Assert.Equal(QuestState.EndedOfferExpired, quest.State);
        }

        [Fact]
        public void QuestManager_SendSignal_reaches_only_ongoing_quests()
        {
            Quest ongoing = QuestGen.Generate(Script("Quest_Omen"), new Slate());
            ongoing.Accept();
            Quest notAccepted = QuestGen.Generate(Script("Quest_Omen"), new Slate());
            Find.QuestManager.Add(ongoing);
            Find.QuestManager.Add(notAccepted);

            var probe = new ProbeQuestPart { inSignal = "probe" };
            ongoing.AddPart(probe);
            var probeNotAccepted = new ProbeQuestPart { inSignal = "probe" };
            notAccepted.AddPart(probeNotAccepted);

            Find.QuestManager.SendSignal(new Signal("probe"));

            Assert.Equal(1, probe.EnabledCount);
            Assert.Equal(0, probeNotAccepted.EnabledCount);
        }

        private sealed class ProbeQuestPart : QuestPart
        {
            public int EnabledCount;

            public override void Enable(Signal signal)
            {
                base.Enable(signal);
                EnabledCount++;
            }
        }

        [Fact]
        public void QuestManager_Scribe_round_trip_preserves_states_parts_and_ticks_left()
        {
            Quest quest = QuestGen.Generate(Script("Quest_LostCaravan"), new Slate());
            Find.QuestManager.Add(quest);
            quest.Accept();
            var delay = (QuestPart_Delay)quest.PartsListForReading[0];
            for (int i = 0; i < 3; i++) Find.QuestManager.QuestManagerTick();
            int ticksLeftBeforeSave = delay.TicksLeft;
            Assert.True(ticksLeftBeforeSave > 0);

            Find.QuestManager.Notify_QuestScriptFired(Script("Quest_LostCaravan"), Find.TickManager.TicksGame);

            string xml = Scribe.SaveToString(Find.QuestManager, "questManager");
            QuestManager loaded = Scribe.Load<QuestManager>(xml, "questManager", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Quest loadedQuest = Assert.Single(loaded.QuestsListForReading);
            Assert.Equal(QuestState.Ongoing, loadedQuest.State);
            Assert.Equal(3, loadedQuest.PartsListForReading.Count);
            var loadedDelay = Assert.IsType<QuestPart_Delay>(loadedQuest.PartsListForReading[0]);
            Assert.Equal(ticksLeftBeforeSave, loadedDelay.TicksLeft);
            Assert.Same(loadedQuest, loadedDelay.quest);
            Assert.False(loaded.CanFire(Script("Quest_LostCaravan"), Find.TickManager.TicksGame));
        }

        // ---- IncidentWorker_GiveQuest ----

        [Fact]
        public void IncidentWorker_GiveQuest_respects_points_bounds_and_refire_days()
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("GiveQuest_Random");

            // No script's [rootMinPoints, rootMaxPoints] covers this: nothing can fire.
            var tooHigh = new IncidentParms { target = new CivilizationTarget(), points = 200000f };
            Assert.False(def.Worker.CanFireNow(tooHigh));

            // Quest_Omen's rootMaxPoints (50) excludes it at high points; the wider scripts still qualify.
            Find.TickManager.DebugSetTicksGame(0);
            var highPoints = new IncidentParms { target = new CivilizationTarget(), points = 1000f };
            Assert.True(def.Worker.TryExecute(highPoints));
            Quest fired = Find.QuestManager.QuestsListForReading.Single();
            Assert.NotSame(Script("Quest_Omen"), fired.root);

            // Refire spacing: the script that just fired cannot fire again immediately.
            Assert.False(Find.QuestManager.CanFire(fired.root, Find.TickManager.TicksGame));
        }

        [Fact]
        public void IncidentWorker_GiveQuest_adds_a_quest_letter_and_chronicle_entry()
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("GiveQuest_Random");
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 10f };

            Assert.True(def.Worker.TryExecute(parms));

            Assert.Single(Find.QuestManager.QuestsListForReading);
            Assert.NotEmpty(Find.LetterStack.LettersListForReading);
            Assert.Single(Find.Storyteller.Chronicle);
            Assert.Contains("Quest offered", Find.Storyteller.Chronicle[0].incidentDefName);
        }

        [Fact]
        public void IncidentWorker_GiveQuest_weights_favour_the_higher_weighted_script()
        {
            int tributeOrCaravanPicks = 0;
            int omenPicks = 0;
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("GiveQuest_Random");

            for (int i = 0; i < 200; i++)
            {
                Find.QuestManager = new QuestManager();
                Find.TickManager.DebugSetTicksGame(i * 10_000_000);
                var parms = new IncidentParms { target = new CivilizationTarget(), points = 10f };
                Assert.True(def.Worker.TryExecute(parms));
                Quest fired = Find.QuestManager.QuestsListForReading.Single();
                if (fired.root == Script("Quest_Omen")) omenPicks++;
                else tributeOrCaravanPicks++;
            }

            Assert.True(omenPicks < tributeOrCaravanPicks, $"expected the weight-1 scripts ({tributeOrCaravanPicks}) to dominate Quest_Omen ({omenPicks})");
        }

        // ---- reward sink ----

        private sealed class FakeRewardSink : IQuestRewardSink
        {
            public List<RewardRecord> Received { get; } = new List<RewardRecord>();
            public void GiveRewards(IReadOnlyList<RewardRecord> rewards) => Received.AddRange(rewards);
        }

        [Fact]
        public void QuestPart_Reward_notifies_the_reward_sink_when_one_is_set()
        {
            var sink = new FakeRewardSink();
            Find.QuestManager.RewardSink = sink;

            Quest quest = QuestGen.Generate(Script("Quest_LostCaravan"), new Slate());
            Find.QuestManager.Add(quest);
            quest.Accept();
            var delay = (QuestPart_Delay)quest.PartsListForReading[0];
            int ticks = delay.TicksLeft;
            for (int i = 0; i < ticks; i++) Find.QuestManager.QuestManagerTick();

            Assert.Contains(sink.Received, r => r.kind == "silver" && r.amount == 200f);
        }

        // ---- QuestNode_IsSet / QuestNode_IsTrue ----

        [Fact]
        public void QuestNode_IsSet_and_IsTrue_branch_on_slate_state()
        {
            var slate = new Slate();
            bool setBranch = false, elseBranch = false;
            var isSet = new QuestNode_IsSet
            {
                name = "flag",
                node = new RunAction(() => setBranch = true),
                elseNode = new RunAction(() => elseBranch = true),
            };

            RunOutsideGeneration(isSet, slate);
            Assert.False(setBranch);
            Assert.True(elseBranch);

            setBranch = false;
            elseBranch = false;
            slate.Set("flag", true);
            RunOutsideGeneration(isSet, slate);
            Assert.True(setBranch);
            Assert.False(elseBranch);
        }

        /// <summary>Runs a node with a throwaway quest/slate so a unit test doesn't need a full <see cref="QuestScriptDef"/>.</summary>
        private static void RunOutsideGeneration(QuestNode node, Slate slate)
        {
            var script = new QuestScriptDef { defName = "Test_" + System.Guid.NewGuid().ToString("N"), root = node };
            QuestGen.Generate(script, slate);
        }

        private sealed class RunAction : QuestNode
        {
            private readonly System.Action action;
            public RunAction(System.Action action) => this.action = action;
            protected override void RunInt() => action();
        }
    }
}
