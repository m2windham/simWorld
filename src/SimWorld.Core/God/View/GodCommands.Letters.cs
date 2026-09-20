using System.Globalization;

using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The write half of the letters/quests seam — see <c>GodViewSnapshot.Letters.cs</c> for the read half.
    ///
    /// <para/>Before this, nothing in <c>src/</c> ever resolved a <see cref="LetterChoice"/>:
    /// <see cref="LetterStack.LetterStackTick"/> only ever ran a timeout, so every choice the game offered —
    /// concretely, every quest <see cref="Director.IncidentWorker_GiveQuest"/> ever raised through
    /// <see cref="LetterDefOf.AcceptQuest"/> — could only lapse, never be taken. <c>docs/design/player-first.md</c>
    /// §2's lever test asks what carries an input to its consequence through the simulation's own machinery;
    /// for "accept this quest" the machinery (<see cref="Quests.Quest.Accept"/>, the whole <c>QuestPart</c>
    /// tree behind it) was already built and simply had no caller. This method is that caller.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Answers a pending <see cref="ChoiceLetter"/>: runs the chosen option's action and removes the
        /// letter from the stack, exactly as if the player had clicked it. <paramref name="letterID"/> is
        /// <see cref="PendingLetterView.LetterID"/> and <paramref name="choiceIndex"/> is
        /// <see cref="PendingLetterChoiceView.Index"/> — both read straight off the snapshot this command
        /// answers, so a host never derives either one itself.
        ///
        /// <para/><b>What this refuses, and what it does not.</b> Per <c>docs/design/player-first.md</c> §5,
        /// only the physically impossible is refused: no such letter (unknown, already resolved, or timed out
        /// — all read the same from here, since a letter that is not on the stack is not on the stack whatever
        /// the reason) and a choice index outside the letter's own <c>choices</c>. A <em>bad</em> choice —
        /// accepting a quest that will cost the civilization dearly — is never refused; the action runs exactly
        /// as authored, cost and all.
        ///
        /// <para/><b>Ordering.</b> The letter is removed from the stack <i>before</i> its action runs, mirroring
        /// <see cref="LetterStack.LetterStackTick"/>'s own timeout handling — so an action that itself raises a
        /// new letter (accepting one quest can offer another) never finds the letter it just answered still
        /// sitting on the stack.
        /// </summary>
        public static GodCommandResult ResolveLetterChoice(string letterID, int choiceIndex)
        {
            if (string.IsNullOrEmpty(letterID))
            {
                return GodCommandResult.Refused("No letter id given.");
            }

            LetterStack stack = Find.LetterStack;
            ChoiceLetter? found = null;
            foreach (Letter letter in stack.LettersListForReading)
            {
                if (letter is ChoiceLetter choice && choice.letterID == letterID)
                {
                    found = choice;
                    break;
                }
            }

            if (found == null)
            {
                return GodCommandResult.Refused(
                    "No pending choice letter '" + letterID +
                    "' — it may already have been answered, timed out, or never existed.");
            }

            if (choiceIndex < 0 || choiceIndex >= found.choices.Count)
            {
                return GodCommandResult.Refused(
                    "Choice " + choiceIndex.ToString(CultureInfo.InvariantCulture) + " is out of range for '" +
                    letterID + "' (" + found.choices.Count.ToString(CultureInfo.InvariantCulture) + " option(s)).");
            }

            LetterChoice picked = found.choices[choiceIndex];
            string letterLabel = found.label;
            stack.RemoveLetter(found);
            picked.action?.Invoke();

            return GodCommandResult.Done("'" + picked.label + "' chosen for '" + letterLabel + "'.");
        }
    }
}
