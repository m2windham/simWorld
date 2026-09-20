using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Letters;
using SimWorld.Quests;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The letters/quests seam (<c>GodViewSnapshot.Letters.cs</c>, <c>GodCommands.Letters.cs</c>): the read
    /// model surfaces a pending <see cref="ChoiceLetter"/> and <see cref="GodCommands.ResolveLetterChoice"/> is
    /// the only way a host ever answers one.
    ///
    /// <para/>Before this lane, the audit behind <c>docs/design/player-first.md</c> found the sharpest instance
    /// of "the player has no hands" this codebase had: <see cref="LetterStack.LetterStackTick"/> only ever ran
    /// a timeout, nothing resolved a <see cref="LetterChoice"/>, and <see cref="Quest.Accept"/> — a fully built,
    /// fully tested method — had no caller anywhere in <c>src/</c>, not because quests were unfinished but
    /// because the one letter that offers one could not be answered. <see cref="The_real_tick_loop_raises_a_ChoiceLetter_from_IncidentWorker_GiveQuest_and_ResolveLetterChoice_accepts_the_quest"/>
    /// is that exact chain, end to end, through a real incident rather than a hand-built letter.
    /// </summary>
    public class LetterChoiceTests : ContentTestBase
    {
        public LetterChoiceTests(CoreContentFixture content) : base(content)
        {
            // Find's letters/quest/storyteller services are thread-static and, absent a real Game, only ever
            // lazily auto-created — give every test a guaranteed-fresh instance explicitly, the same as
            // QuestTests' own constructor does for the same reason.
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new Storyteller();
            QuestGen.ResetIdCounterForTests();
        }

        private static ChoiceLetter NewChoiceLetter(string label = "Offer", string text = "Decide.") =>
            new ChoiceLetter { def = LetterDefOf.AcceptQuest, label = label, text = text };

        // ---- the end-to-end path this lane exists to build ----

        [Fact]
        public void The_real_tick_loop_raises_a_ChoiceLetter_from_IncidentWorker_GiveQuest_and_ResolveLetterChoice_accepts_the_quest()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "letters-give-quest",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);

            IncidentDef giveQuest = DefDatabase<IncidentDef>.GetNamed("GiveQuest_Random");
            // Above every autoAccept script's rootMaxPoints (Quest_Omen tops out at 50) and within both
            // manual-accept scripts' range (Quest_Tribute, Quest_LostCaravan both run to 99999) — so a
            // ChoiceLetter is guaranteed regardless of which of the two the weighted pick lands on.
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 100f };
            Find.Storyteller.incidentQueue.Add(new FiringIncident(giveQuest, null, parms), Find.TickManager.TicksGame);

            // Not asserting Find.LetterStack.LettersListForReading is empty here: Game.NewGame itself sends a
            // "game start dialog" StandardLetter, which is real state, correctly not a pending choice, and
            // exactly why PendingLetters filters to ChoiceLetter rather than mirroring the raw stack.
            Assert.Empty(Find.QuestManager.QuestsListForReading);
            Assert.Empty(GodViewSnapshot.Capture().PendingLetters);

            // Drives the real tick loop: Game.NewGame wires TickManager.PostTickers to Storyteller.StorytellerTick,
            // which only acts once every Storyteller.IncidentCycleLengthTicks ticks — advancing to the next one
            // runs the queued incident through the production path (IncidentQueue -> Storyteller.TryFire ->
            // IncidentWorker.TryExecute), the same path a real game's own storyteller comps use, not a
            // hand-built letter.
            int ticksToNextCycle = Storyteller.IncidentCycleLengthTicks -
                (Find.TickManager.TicksGame % Storyteller.IncidentCycleLengthTicks);
            for (int i = 0; i < ticksToNextCycle; i++) Find.TickManager.DoSingleTick();

            Quest quest = Assert.Single(Find.QuestManager.QuestsListForReading);
            Assert.Equal(QuestState.NotYetAccepted, quest.State);

            // Read side: the offer is on the snapshot, not just on the raw LetterStack.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            PendingLetterView pending = Assert.Single(snapshot.PendingLetters);
            Assert.Equal(quest.name, pending.Label);
            Assert.Equal(2, pending.Choices.Count);
            Assert.Equal("Accept", pending.Choices[0].Label);
            Assert.Equal(0, pending.Choices[0].Index);
            Assert.Equal("Reject", pending.Choices[1].Label);

            // Write side: answering it actually runs Quest.Accept() — previously unreachable from src/ at all.
            GodCommandResult result = GodCommands.ResolveLetterChoice(pending.LetterID, pending.Choices[0].Index);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Equal(QuestState.Ongoing, quest.State);
            Assert.DoesNotContain(Find.LetterStack.LettersListForReading, l => l.letterID == pending.LetterID);
            Assert.Empty(GodViewSnapshot.Capture().PendingLetters);
        }

        // ---- read model ----

        [Fact]
        public void PendingLetters_reports_only_ChoiceLetters_with_their_labels_choices_and_indices()
        {
            Find.LetterStack.ReceiveLetter("Just FYI", "nothing to decide", LetterDefOf.NeutralEvent);

            ChoiceLetter choice = NewChoiceLetter("Decide", "body");
            choice.choices.Add(new LetterChoice("Yes"));
            choice.choices.Add(new LetterChoice("No"));
            Find.LetterStack.ReceiveLetter(choice);

            List<PendingLetterView> pending = GodViewSnapshot.Capture().PendingLetters.ToList();

            PendingLetterView view = Assert.Single(pending);
            Assert.Equal(choice.letterID, view.LetterID);
            Assert.Equal("Decide", view.Label);
            Assert.Equal("body", view.Text);
            Assert.Equal(2, view.Choices.Count);
            Assert.Equal(0, view.Choices[0].Index);
            Assert.Equal("Yes", view.Choices[0].Label);
            Assert.Equal(1, view.Choices[1].Index);
            Assert.Equal("No", view.Choices[1].Label);
        }

        [Fact]
        public void Letter_ids_are_stable_unique_and_never_reused_even_after_the_letter_that_held_one_resolves()
        {
            ChoiceLetter first = NewChoiceLetter("First");
            first.choices.Add(new LetterChoice("Ok"));
            Find.LetterStack.ReceiveLetter(first);
            string firstId = first.letterID;
            Assert.False(string.IsNullOrEmpty(firstId));

            GodCommands.ResolveLetterChoice(firstId, 0); // resolves and removes `first`.

            ChoiceLetter second = NewChoiceLetter("Second");
            second.choices.Add(new LetterChoice("Ok"));
            Find.LetterStack.ReceiveLetter(second);

            Assert.NotEqual(firstId, second.letterID);
        }

        // ---- resolving ----

        [Fact]
        public void Resolving_a_letter_runs_the_chosen_action_removes_it_and_refuses_a_second_resolution()
        {
            ChoiceLetter choice = NewChoiceLetter();
            bool accepted = false;
            choice.choices.Add(new LetterChoice("Accept", () => accepted = true));
            choice.choices.Add(new LetterChoice("Reject"));
            Find.LetterStack.ReceiveLetter(choice);
            string id = choice.letterID;

            GodCommandResult result = GodCommands.ResolveLetterChoice(id, 0);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(accepted);
            Assert.DoesNotContain(Find.LetterStack.LettersListForReading, l => l.letterID == id);

            GodCommandResult second = GodCommands.ResolveLetterChoice(id, 0);
            Assert.Equal(GodCommandOutcome.Refused, second.Outcome);
            Assert.NotEmpty(second.Reason);
        }

        [Fact]
        public void A_bad_choice_taken_knowingly_still_goes_through()
        {
            // docs/design/player-first.md §5: refuse only the impossible, never the unwise. "Reject" costs the
            // player whatever rejecting a quest costs in the simulation, and this command must still say Done.
            ChoiceLetter choice = NewChoiceLetter();
            choice.choices.Add(new LetterChoice("Accept", () => { }));
            choice.choices.Add(new LetterChoice("Reject", () => { }));
            Find.LetterStack.ReceiveLetter(choice);

            GodCommandResult result = GodCommands.ResolveLetterChoice(choice.letterID, 1);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
        }

        // ---- refusals ----

        [Fact]
        public void ResolveLetterChoice_refuses_an_unknown_letter_id()
        {
            GodCommandResult result = GodCommands.ResolveLetterChoice("Letter_no_such_thing", 0);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.NotEmpty(result.Reason);
        }

        [Fact]
        public void ResolveLetterChoice_refuses_an_empty_letter_id()
        {
            GodCommandResult result = GodCommands.ResolveLetterChoice("", 0);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void ResolveLetterChoice_refuses_an_out_of_range_choice_index_without_consuming_the_letter()
        {
            ChoiceLetter choice = NewChoiceLetter();
            choice.choices.Add(new LetterChoice("Only option"));
            Find.LetterStack.ReceiveLetter(choice);

            GodCommandResult tooHigh = GodCommands.ResolveLetterChoice(choice.letterID, 1);
            Assert.Equal(GodCommandOutcome.Refused, tooHigh.Outcome);

            GodCommandResult negative = GodCommands.ResolveLetterChoice(choice.letterID, -1);
            Assert.Equal(GodCommandOutcome.Refused, negative.Outcome);

            // Refused, not consumed: the letter is still exactly where it was.
            Assert.Contains(Find.LetterStack.LettersListForReading, l => l.letterID == choice.letterID);
            Assert.Single(GodViewSnapshot.Capture().PendingLetters);
        }

        [Fact]
        public void ResolveLetterChoice_refuses_a_letter_that_has_already_timed_out()
        {
            Find.TickManager.DebugSetTicksGame(0);
            ChoiceLetter choice = NewChoiceLetter();
            choice.choices.Add(new LetterChoice("Accept"));
            bool timedOut = false;
            choice.SetTimeout(10, () => timedOut = true);
            Find.LetterStack.ReceiveLetter(choice);
            string id = choice.letterID;

            Find.TickManager.DebugSetTicksGame(10);
            Find.LetterStack.LetterStackTick();
            Assert.True(timedOut);
            Assert.Empty(Find.LetterStack.LettersListForReading);

            GodCommandResult result = GodCommands.ResolveLetterChoice(id, 0);
            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
        }

        // ---- timeout is unaffected by the new path ----

        [Fact]
        public void Timeout_still_removes_and_runs_its_action_exactly_as_before_the_id_and_resolve_path_existed()
        {
            Find.TickManager.DebugSetTicksGame(0);
            ChoiceLetter choice = NewChoiceLetter();
            choice.choices.Add(new LetterChoice("Accept"));
            bool timedOut = false;
            choice.SetTimeout(10, () => timedOut = true);
            Find.LetterStack.ReceiveLetter(choice);

            Find.TickManager.DebugSetTicksGame(5);
            Find.LetterStack.LetterStackTick();
            Assert.False(timedOut);
            Assert.Single(Find.LetterStack.LettersListForReading);

            Find.TickManager.DebugSetTicksGame(10);
            Find.LetterStack.LetterStackTick();
            Assert.True(timedOut);
            Assert.Empty(Find.LetterStack.LettersListForReading);
        }

        // ---- Scribe round trip ----

        [Fact]
        public void A_pending_choice_letter_survives_a_Scribe_round_trip_and_is_still_answerable_by_the_same_id()
        {
            ChoiceLetter choice = NewChoiceLetter("Offer", "Decide.");
            bool acceptedBeforeSave = false;
            choice.choices.Add(new LetterChoice("Accept", () => acceptedBeforeSave = true));
            choice.choices.Add(new LetterChoice("Reject"));
            Find.LetterStack.ReceiveLetter(choice);
            string id = choice.letterID;

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            // The pre-save stack is untouched by the save itself — Scribe never writes through `ref` while
            // saving (see Scribe_Values/Scribe_Collections) — so this also proves the fix to ChoiceLetter's own
            // ExposeData (which used to drop `choices` on every round trip) did not do it by mutating the live
            // object in place.
            Assert.Single(Find.LetterStack.LettersListForReading);

            Find.LetterStack = loaded;

            var loadedChoice = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            Assert.Equal(id, loadedChoice.letterID);
            Assert.Equal("Offer", loadedChoice.label);
            Assert.Equal("Decide.", loadedChoice.text);
            Assert.Equal(2, loadedChoice.choices.Count);
            Assert.Equal("Accept", loadedChoice.choices[0].label);
            Assert.Equal("Reject", loadedChoice.choices[1].label);

            // The read model finds it by the same id after the load, exactly as before.
            PendingLetterView pending = Assert.Single(GodViewSnapshot.Capture().PendingLetters);
            Assert.Equal(id, pending.LetterID);
            Assert.Equal(2, pending.Choices.Count);

            // Answerable by the same id: the command still succeeds and still removes it. The action delegate
            // is documented as never surviving Scribe (Letter.cs's own class doc on LetterChoice) — resolving
            // still works, only the side effect that used to run on Accept is gone, which is the one thing that
            // *cannot* be fixed without saving arbitrary code, and is not what "answerable" promises.
            GodCommandResult result = GodCommands.ResolveLetterChoice(id, 0);
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.False(acceptedBeforeSave);
            Assert.Empty(loaded.LettersListForReading);
        }

        [Fact]
        public void The_next_letter_id_counter_itself_survives_a_Scribe_round_trip_so_a_reload_never_reissues_an_id()
        {
            ChoiceLetter resolvedBeforeSave = NewChoiceLetter("Resolved");
            resolvedBeforeSave.choices.Add(new LetterChoice("Ok"));
            Find.LetterStack.ReceiveLetter(resolvedBeforeSave);
            string resolvedId = resolvedBeforeSave.letterID;
            GodCommands.ResolveLetterChoice(resolvedId, 0); // gone from the stack, but its id must stay retired.

            ChoiceLetter stillPending = NewChoiceLetter("Pending");
            stillPending.choices.Add(new LetterChoice("Ok"));
            Find.LetterStack.ReceiveLetter(stillPending);

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Find.LetterStack = loaded;

            ChoiceLetter afterLoad = NewChoiceLetter("AfterLoad");
            afterLoad.choices.Add(new LetterChoice("Ok"));
            Find.LetterStack.ReceiveLetter(afterLoad);

            Assert.NotEqual(resolvedId, afterLoad.letterID);
            Assert.NotEqual(stillPending.letterID, afterLoad.letterID);
        }
    }
}
