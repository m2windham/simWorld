using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    public static partial class GodCommands
    {
        /// <summary>
        /// Takes a letter off the stack once the player has read it (RimWorld: right-clicking an envelope).
        /// Removing it changes nothing else: a letter is news, and the news already happened.
        ///
        /// <para/>Refuses a letter that asks a question (<see cref="ChoiceLetter"/>). Dismissing one would drop
        /// the decision without making it — the offer would vanish and its timeout consequence would never
        /// run, which is a consequence the simulation owes and would silently skip. Answer it with
        /// <see cref="ResolveLetterChoice"/> instead; its own "dismiss"-style option, where it has one, is how
        /// a player declines.
        /// </summary>
        /// <returns>Done when removed; Refused for an empty id, an id not on the stack, or a choice letter.</returns>
        public static GodCommandResult DismissLetter(string letterID)
        {
            if (string.IsNullOrEmpty(letterID))
            {
                return GodCommandResult.Refused("No letter id given.");
            }

            LetterStack stack = Find.LetterStack;
            Letter? letter = stack.FindByID(letterID);
            if (letter == null)
            {
                return GodCommandResult.Refused(
                    "No letter '" + letterID + "' on the stack — it may already have been dismissed or answered.");
            }

            if (letter is ChoiceLetter)
            {
                return GodCommandResult.Refused(
                    "'" + letter.label + "' is waiting for an answer. Choose one of its options rather than " +
                    "dismissing it.");
            }

            stack.RemoveLetter(letter);
            return GodCommandResult.Done("'" + letter.label + "' dismissed.");
        }
    }
}
