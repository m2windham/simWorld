using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Sim;

namespace SimWorld.Letters
{
    /// <summary>
    /// Every letter currently waiting for the player (RimWorld: <c>RimWorld.LetterStack</c>), reached ambiently
    /// through <see cref="Sim.Find.LetterStack"/> the way ticking reaches <see cref="Sim.Find.TickManager"/>.
    ///
    /// <para/><b>The id problem, and why it is solved here.</b> RimWorld's own <c>LetterStack</c> never needed
    /// a stable letter handle: its UI holds the <c>Letter</c> object directly, because UI and simulation share
    /// one assembly. This port's God/View seam cannot do that — a live <see cref="Letter"/> is exactly the kind
    /// of object §12a's snapshot rule forbids handing the host, and a <see cref="ChoiceLetter"/> carries a live
    /// <see cref="LetterChoice.action"/> delegate that must never cross it. So the host needs a handle that
    /// names a letter without being one, and three candidates were rejected before landing on
    /// <see cref="Letter.letterID"/>:
    /// <list type="bullet">
    /// <item><b>A list index into <see cref="LettersListForReading"/>.</b> Shifts the instant an earlier letter
    /// resolves, times out, or a new one arrives — the read model's own brief calls this out by name as the one
    /// shape a letter handle must never take, because a host that captured index 2 and acts on it a frame later
    /// can silently resolve someone else's letter.</item>
    /// <item><b>The arrival tick.</b> Not unique: nothing stops two incidents firing on the same
    /// <see cref="Sim.TickManager.TicksGame"/> (an incident cycle already batches every due incident onto one
    /// tick — see <see cref="Director.Storyteller.MakeIncidentsForInterval"/>), and a tick a host already reads
    /// off other snapshot fields inviting reuse as an id is exactly the kind of implicit contract that breaks
    /// quietly the day it stops holding.</item>
    /// <item><b>Object identity / a hash code.</b> Doesn't survive Scribe at all — a freshly loaded
    /// <see cref="Letter"/> is a different .NET object with a different hash than the one that was saved — and
    /// can't cross the seam as a string or int in the first place without being converted to something else,
    /// which just re-poses this same question one layer down.</item>
    /// </list>
    /// The answer is a monotonically increasing counter (<see cref="nextLetterId"/>), assigned once per letter
    /// in <see cref="ReceiveLetter(Letter)"/> and never reassigned. It is persisted here rather than left as an
    /// in-process counter (contrast <see cref="Quests.QuestGen"/>'s <c>[ThreadStatic] nextId</c>, which resets
    /// to zero on every process start and only ever avoids colliding with a loaded save because nothing has
    /// re-generated a quest yet in that process): a save reloaded into a fresh process must not hand out an id
    /// that an existing pending letter in that same save already holds, and deriving the counter from the
    /// loaded letters themselves (e.g. their max) would break the instant a resolved letter — no longer on the
    /// stack to be scanned — happened to hold the highest id. Persisting the counter directly sidesteps both
    /// failure modes: it always continues exactly where the save left off, whether or not the letter that used
    /// any particular id is still around to be asked.
    /// </summary>
    public sealed class LetterStack : IExposable
    {
        private readonly List<Letter> letters = new List<Letter>();

        /// <summary>Next id <see cref="ReceiveLetter(Letter)"/> will hand out. Starts at 1 so an unassigned
        /// <see cref="Letter.letterID"/> ("") is never mistaken for a real one.</summary>
        private int nextLetterId = 1;

        public IReadOnlyList<Letter> LettersListForReading => letters;

        /// <summary>Raised for every letter handed to the stack, whether or not it is visible (see <see cref="Letter.CanShowInLetterStack"/>).</summary>
        public event Action<Letter>? LetterReceived;

        public void ReceiveLetter(Letter let)
        {
            if (let == null) throw new ArgumentNullException(nameof(let));
            let.arrivalTick = Find.TickManager.TicksGame;
            // Idempotent: a letter can arrive on the stack only once in the ordinary flow, but a re-received
            // letter (a test replaying one, say) keeps whatever id it already had rather than being handed a
            // second one — an id is a property of the letter, not of the act of receiving it.
            if (string.IsNullOrEmpty(let.letterID))
            {
                let.letterID = "Letter_" + nextLetterId.ToString(CultureInfo.InvariantCulture);
                nextLetterId++;
            }
            if (let.CanShowInLetterStack)
            {
                letters.Add(let);
            }
            let.Received();
            LetterReceived?.Invoke(let);
        }

        /// <summary>Finds a letter still on the stack by its <see cref="Letter.letterID"/>, or null if it has
        /// already been resolved, timed out, or never existed. Never an index — see this type's own doc.</summary>
        public Letter? FindByID(string letterID)
        {
            if (string.IsNullOrEmpty(letterID)) return null;
            for (int i = 0; i < letters.Count; i++)
            {
                if (letters[i].letterID == letterID) return letters[i];
            }
            return null;
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

            // The counter, not a value derived from the letters above — see this type's own doc for why.
            Scribe_Values.Look(ref nextLetterId, "nextLetterId", 1);
        }
    }
}
