using System.Collections.Generic;

using SimWorld.Sim;

namespace SimWorld.God.View
{
    public sealed partial class GodViewSnapshot
    {
        private IReadOnlyList<LetterView>? letters;

        /// <summary>
        /// Every letter on the stack, oldest first — the letter feed. Empty before a game starts. Built the
        /// first time it is read and cached on this snapshot, like <see cref="PendingLetters"/>.
        /// </summary>
        public IReadOnlyList<LetterView> Letters => letters ??= LetterView.On(Find.LetterStack);
    }
}
