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
    /// The fourth playtest's refused Lost caravan came straight back: Reject only dismissed the letter, so the offer
    /// lapsed on its own with nothing remembered. Reject is now its own kind (<see cref="LetterChoiceKind.RejectQuest"/>)
    /// that ends the offer, and like Accept it has to be rebuilt after a load (<c>ChoiceLetter.ActionFor</c>).
    /// </summary>
    [Collection("GlobalDefs")]
    public class RejectQuestRestoreTests : ContentTestBase
    {
        public RejectQuestRestoreTests(CoreContentFixture content) : base(content)
        {
            Find.LetterStack = new LetterStack();
            Find.QuestManager = new QuestManager();
            Find.Storyteller = new Storyteller();
            QuestGen.ResetIdCounterForTests();
        }

        [Fact]
        public void Refusing_a_restored_quest_offer_ends_the_offer()
        {
            var quest = new Quest { name = "A debt called in" };
            Find.QuestManager.Add(quest);
            var letter = new ChoiceLetter { def = LetterDefOf.AcceptQuest, label = quest.name, text = "Decide.", quest = quest };
            letter.choices.Add(new LetterChoice("Accept", () => quest.Accept(), LetterChoiceKind.AcceptQuest));
            letter.choices.Add(new LetterChoice("Reject", () => quest.Reject(), LetterChoiceKind.RejectQuest));
            Find.LetterStack.ReceiveLetter(letter);

            string xml = Scribe.SaveToString(Find.LetterStack, "letters");
            LetterStack loaded = Scribe.Load<LetterStack>(xml, "letters", out IReadOnlyList<string> errors);
            // Saving the stack alone cannot carry the quest reference; a whole-game load resolves it.
            Assert.Contains("Quest", Assert.Single(errors), System.StringComparison.Ordinal);
            Find.LetterStack = loaded;
            var restored = Assert.IsType<ChoiceLetter>(Assert.Single(loaded.LettersListForReading));
            restored.quest = quest;

            Assert.Equal(LetterChoiceKind.RejectQuest, restored.choices[1].kind);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.ResolveLetterChoice(restored.letterID, 1).Outcome);
            Assert.Equal(QuestState.EndedOfferExpired, quest.State);
            Assert.Empty(loaded.LettersListForReading);
        }
    }
}
