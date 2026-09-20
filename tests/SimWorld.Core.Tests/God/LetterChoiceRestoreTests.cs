using System.Collections.Generic;

using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Letters;
using SimWorld.Quests;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// A choice's <i>consequence</i> survives a save, and where it cannot, the command says so instead of
    /// reporting success.
    ///
    /// <para/><b>The defect these pin.</b> The letters seam arrived answerable but hollow across a load: a
    /// <see cref="LetterChoice"/>'s <c>action</c> is a delegate and cannot be written to a save file, so a
    /// loaded letter had a label and nothing behind it. <c>GodCommands.ResolveLetterChoice</c> ran
    /// <c>action?.Invoke()</c> and returned <c>Done</c> either way — so a player could accept a quest, be told
    /// it was accepted, and have no quest running. That is the over-claim rule (<c>docs/design/phase-2-pressure.md</c>)
    /// in its most expensive form yet: the previous four instances handed out a wrong number, this one handed
    /// out a wrong belief about the world.
    ///
    /// <para/><b>The fix has two halves and both are tested here.</b> The <i>kind</i> of consequence is saved
    /// (<see cref="LetterChoiceKind"/>) and the action is rebuilt from it on load, so the common case genuinely
    /// works. And where the kind's own precondition is not met — an <see cref="LetterChoiceKind.AcceptQuest"/>
    /// whose quest did not come back — the command refuses and leaves the letter on the stack, because an
    /// unanswerable letter the player can see is strictly better than an answered one that did nothing.
    ///
    /// <para/><b>Why the second half needed its own thought.</b> The first version of the guard tested only
    /// whether the action was null. A rebuilt AcceptQuest action is never null — it is a live delegate that
    /// reads <c>quest</c> when invoked — so with the quest gone it would have passed the guard, invoked
    /// harmlessly, and reported <c>Done</c>. The over-claim would have returned wearing a different hat, which
    /// is exactly how it keeps coming back.
    /// </summary>
    public class LetterChoiceRestoreTests : ContentTestBase
    {
        public LetterChoiceRestoreTests(CoreContentFixture content) : base(content)
        {
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new Storyteller();
            QuestGen.ResetIdCounterForTests();
        }

        /// <summary>A quest letter shaped exactly as <c>IncidentWorker_GiveQuest.SendAcceptLetter</c> builds
        /// one — the kind is the part that matters, and it is the part that used to be absent.</summary>
        private static ChoiceLetter QuestOfferFor(Quest quest)
        {
            var letter = new ChoiceLetter
            {
                def = LetterDefOf.AcceptQuest,
                label = quest.name,
                text = "Decide.",
                quest = quest,
            };
            letter.choices.Add(new LetterChoice("Accept", () => quest.Accept(), LetterChoiceKind.AcceptQuest));
            letter.choices.Add(new LetterChoice("Reject", null, LetterChoiceKind.Dismiss));
            return letter;
        }

        private static Quest NewOfferedQuest()
        {
            var quest = new Quest { name = "A debt called in" };
            Find.QuestManager.Add(quest);
            return quest;
        }

        /// <summary>
        /// Saving the letter stack on its own cannot carry the object a <c>Scribe_References</c> field points
        /// at — a whole-game load resolves it from the <see cref="QuestManager"/> that was saved alongside. So
        /// exactly one error is expected here, naming that reference, and asserting on it rather than on an
        /// empty list is the point: it is what produces the "the quest did not survive" state these tests need,
        /// and swallowing it with a bare <c>Assert.Empty</c> would have hidden a genuinely broken load later.
        /// </summary>
        private static void AssertOnlyTheQuestReferenceIsUnresolved(IReadOnlyList<string> errors)
        {
            string only = Assert.Single(errors);
            Assert.Contains("Quest", only, System.StringComparison.Ordinal);
            Assert.Contains("resolve", only, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_kind_of_a_choice_survives_a_save_so_the_action_can_be_rebuilt()
        {
            Find.LetterStack.ReceiveLetter(QuestOfferFor(NewOfferedQuest()));

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            AssertOnlyTheQuestReferenceIsUnresolved(errors);

            var restored = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            Assert.Equal(LetterChoiceKind.AcceptQuest, restored.choices[0].kind);
            Assert.Equal(LetterChoiceKind.Dismiss, restored.choices[1].kind);

            // Rebuilt, not carried: the delegate is a new one made from the kind, and it exists.
            Assert.NotNull(restored.choices[0].action);
            Assert.Null(restored.choices[1].action);
        }

        /// <summary>
        /// The headline. Answering a restored quest offer actually accepts the quest.
        ///
        /// <para/>The quest is reattached by hand because saving the letter stack alone does not carry the
        /// object a <c>Scribe_References</c> field points at — a whole-game load does. What this asserts is the
        /// half that was broken and is this change's own: that the action was rebuilt from the saved kind, and
        /// that it reads <c>quest</c> at <i>invoke</i> time rather than binding whatever the field held while
        /// the reference was still unresolved.
        /// </summary>
        [Fact]
        public void Answering_a_restored_quest_offer_actually_accepts_the_quest()
        {
            Quest quest = NewOfferedQuest();
            Find.LetterStack.ReceiveLetter(QuestOfferFor(quest));

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            AssertOnlyTheQuestReferenceIsUnresolved(errors);
            Find.LetterStack = loaded;

            var restored = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            restored.quest = quest;   // what a whole-game load's reference resolution does

            Assert.Equal(QuestState.NotYetAccepted, quest.State);

            GodCommandResult result = GodCommands.ResolveLetterChoice(restored.letterID, 0);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(QuestState.Ongoing, quest.State);   // the consequence, not just the report
            Assert.Empty(loaded.LettersListForReading);
        }

        [Fact]
        public void A_quest_offer_whose_quest_did_not_survive_is_refused_rather_than_reported_done()
        {
            Find.LetterStack.ReceiveLetter(QuestOfferFor(NewOfferedQuest()));

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            AssertOnlyTheQuestReferenceIsUnresolved(errors);
            Find.LetterStack = loaded;

            var restored = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            Assert.Null(restored.quest);   // the precondition this guard exists for

            GodCommandResult result = GodCommands.ResolveLetterChoice(restored.letterID, 0);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("quest", result.Reason, System.StringComparison.OrdinalIgnoreCase);

            // And the letter stays: a player who can still see the offer can still act on it once the quest is
            // back, where a removed one is gone for good and silently.
            Assert.Single(loaded.LettersListForReading);
        }

        /// <summary>Dismissal is exempt, and deliberately so: a null action on a
        /// <see cref="LetterChoiceKind.Dismiss"/> choice is not a failure to restore one, because the letter
        /// leaving the stack is the entire effect. Refusing here would make every saved letter unanswerable.</summary>
        [Fact]
        public void Rejecting_a_restored_offer_still_works_because_dismissal_needs_no_action()
        {
            Find.LetterStack.ReceiveLetter(QuestOfferFor(NewOfferedQuest()));

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            AssertOnlyTheQuestReferenceIsUnresolved(errors);
            Find.LetterStack = loaded;

            var restored = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            GodCommandResult result = GodCommands.ResolveLetterChoice(restored.letterID, 1);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Empty(loaded.LettersListForReading);
        }
    }
}
