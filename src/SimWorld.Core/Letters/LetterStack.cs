using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Letters
{
    /// <summary>
    /// Every letter currently waiting for the player (RimWorld: <c>RimWorld.LetterStack</c>), reached ambiently
    /// through <see cref="Sim.Find.LetterStack"/> the way ticking reaches <see cref="Sim.Find.TickManager"/>.
    /// </summary>
    public sealed class LetterStack : IExposable
    {
        private readonly List<Letter> letters = new List<Letter>();

        public IReadOnlyList<Letter> LettersListForReading => letters;

        /// <summary>Raised for every letter handed to the stack, whether or not it is visible (see <see cref="Letter.CanShowInLetterStack"/>).</summary>
        public event Action<Letter>? LetterReceived;

        public void ReceiveLetter(Letter let)
        {
            if (let == null) throw new ArgumentNullException(nameof(let));
            let.arrivalTick = Find.TickManager.TicksGame;
            if (let.CanShowInLetterStack)
            {
                letters.Add(let);
            }
            let.Received();
            LetterReceived?.Invoke(let);
        }

        /// <summary>Builds a letter of <paramref name="def"/>'s <see cref="LetterDef.letterClass"/> and receives it.</summary>
        public Letter ReceiveLetter(string label, string text, LetterDef def, List<string>? lookTargets = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var let = (Letter)Activator.CreateInstance(def.letterClass)!;
            let.def = def;
            let.label = label;
            let.text = text;
            let.lookTargets = lookTargets;
            ReceiveLetter(let);
            return let;
        }

        public bool RemoveLetter(Letter let) => letters.Remove(let);

        /// <summary>Runs any due <see cref="ChoiceLetter"/> timeout and removes the letter. Call once per game tick.</summary>
        public void LetterStackTick()
        {
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                if (letters[i] is ChoiceLetter choice && choice.TimeoutActive && choice.TimeoutTicks <= 0)
                {
                    Action? action = choice.timeoutAction;
                    RemoveLetter(choice);
                    action?.Invoke();
                }
            }
        }

        public void ExposeData()
        {
            List<Letter>? list = new List<Letter>(letters);
            Scribe_Collections.Look(ref list, "letters", LookMode.Deep);
            letters.Clear();
            if (list != null) letters.AddRange(list);
        }
    }
}
