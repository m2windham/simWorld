using System.Collections.Generic;

namespace SimWorld.God.View
{
    /// <summary>
    /// One option on a <see cref="PendingLetterView"/> — a label and the index <see cref="GodCommands.ResolveLetterChoice"/>
    /// expects back. Not <see cref="Letters.LetterChoice"/> itself: that type carries a live
    /// <see cref="System.Action"/> delegate, and handing it across the seam is exactly what
    /// <c>GodViewSeamIntegrityTests</c> exists to catch. The host gets a label to draw and a number to send
    /// back; the core is the only thing that ever touches the action.
    /// </summary>
    public sealed class PendingLetterChoiceView
    {
        internal PendingLetterChoiceView(int index, string label)
        {
            Index = index;
            Label = label;
        }

        /// <summary>What to pass as <c>choiceIndex</c> to <see cref="GodCommands.ResolveLetterChoice"/> to pick
        /// this option. The position in <see cref="PendingLetterView.Choices"/>, not a separately-tracked id —
        /// a <see cref="Letters.ChoiceLetter"/>'s own <c>choices</c> list is exactly this stable while the
        /// letter stands (nothing reorders or removes a single choice out from under a pending letter; the
        /// whole letter goes when one is picked), so a second handle here would only be a second name for the
        /// same fact.</summary>
        public int Index { get; }

        public string Label { get; }
    }

    /// <summary>
    /// One <see cref="Letters.ChoiceLetter"/> still waiting for the player, as the god view needs it
    /// (<c>docs/spec/simworld-spec.md</c> §12a; <c>docs/design/player-first.md</c> §2 — this is the read half
    /// of the one lever the lever test's audit found entirely missing: the game asking a question with no way
    /// to hear the answer). <see cref="LetterID"/> is the handle <see cref="GodCommands.ResolveLetterChoice"/>
    /// takes back; see <see cref="Letters.LetterStack"/>'s own doc for why it is a string minted by a
    /// persisted counter rather than an index, a tick, or the letter object itself.
    ///
    /// <para/>Values only, like every other type on this seam: no <see cref="Letters.Letter"/>, no
    /// <see cref="Letters.LetterDef"/>, and — the one that actually matters here — no
    /// <see cref="System.Action"/> from any <see cref="Letters.LetterChoice"/>. A host can read what it is
    /// being asked and answer with a name and a number; it can never reach into what answering does.
    /// </summary>
    public sealed class PendingLetterView
    {
        internal PendingLetterView(string letterID, string label, string text, IReadOnlyList<PendingLetterChoiceView> choices)
        {
            LetterID = letterID;
            Label = label;
            Text = text;
            Choices = choices;
        }

        /// <summary>The handle to pass back to <see cref="GodCommands.ResolveLetterChoice"/>. Stable across a
        /// save and load and across every other letter arriving or resolving in the meantime.</summary>
        public string LetterID { get; }

        public string Label { get; }

        public string Text { get; }

        /// <summary>Every option this letter offers, in the order <see cref="GodCommands.ResolveLetterChoice"/>
        /// expects their indices in. Never empty for a letter that reached the stack through the ordinary
        /// paths — <see cref="Letters.LetterStack.ReceiveLetter(Letters.Letter)"/> does not enforce that, so an
        /// ad-hoc <see cref="Letters.ChoiceLetter"/> with no choices at all is possible and simply offers
        /// nothing to pick.</summary>
        public IReadOnlyList<PendingLetterChoiceView> Choices { get; }

        /// <summary>Every <see cref="Letters.ChoiceLetter"/> currently on <paramref name="stack"/>, in stack
        /// order. A <see cref="Letters.StandardLetter"/> — or any other non-choice letter — is not a pending
        /// decision and is left out, the same way <see cref="ConditionLine"/> and <see cref="ChronicleLine"/>
        /// leave out whatever their own source doesn't apply to.</summary>
        internal static List<PendingLetterView> PendingOn(Letters.LetterStack stack)
        {
            var views = new List<PendingLetterView>();
            IReadOnlyList<Letters.Letter> letters = stack.LettersListForReading;
            for (int i = 0; i < letters.Count; i++)
            {
                if (letters[i] is Letters.ChoiceLetter choice)
                {
                    var choiceViews = new List<PendingLetterChoiceView>(choice.choices.Count);
                    for (int c = 0; c < choice.choices.Count; c++)
                    {
                        choiceViews.Add(new PendingLetterChoiceView(c, choice.choices[c].label));
                    }
                    views.Add(new PendingLetterView(choice.letterID, choice.label, choice.text, choiceViews));
                }
            }
            return views;
        }
    }
}
