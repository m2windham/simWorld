using System;
using System.Collections.Generic;
using SimWorld.Quests;
using SimWorld.Sim;

namespace SimWorld.Letters
{
    /// <summary>
    /// One notification on the <see cref="LetterStack"/> (RimWorld: <c>Verse.Letter</c>).
    /// <see cref="lookTargets"/> stores plain ids (RimWorld: a jump-to-target <c>GlobalTargetInfo</c> list)
    /// rather than live references. <b>The recorded reason for that — "SimWorld has no map yet" — has
    /// expired</b>: maps ship, and a letter raised about a pawn now carries that pawn's
    /// <c>Thing.GetUniqueLoadID</c>. Ids are still the right shape for a different reason: a letter outlives
    /// what it points at (the pawn it names is usually dead by the time anyone reads it), and the God view is
    /// values-and-defNames by rule (spec §12a), so a live reference could not cross to the host anyway.
    /// </summary>
    public abstract class Letter : IExposable
    {
        public LetterDef def = null!;

        public string label = "";

        public string text = "";

        public int arrivalTick;

        /// <summary>
        /// Stable handle for this one letter, assigned once by <see cref="LetterStack.ReceiveLetter(Letter)"/>
        /// and never reassigned or reused — see that method's own doc for why a monotonic counter persisted on
        /// the stack, rather than an index, a tick, or object identity, is what crosses the God/View seam.
        /// Empty only before a letter has ever been received (a bare <c>new ChoiceLetter()</c> in a test,
        /// mainly); every letter that ever sat on a real stack carries one.
        /// </summary>
        public string letterID = "";

        /// <summary>Ids (RimWorld: jump targets) this letter points at; resolved by a future Things/Map system.</summary>
        public List<string>? lookTargets;

        /// <summary>False for a letter that should notify (RimWorld: play its sound) without ever appearing in the stack.</summary>
        public virtual bool CanShowInLetterStack => true;

        /// <summary>Called once, when the letter is handed to the stack.</summary>
        public virtual void Received()
        {
        }

        /// <summary>UI hook: what happens when a player opens this letter. No UI exists yet, so nothing calls this.</summary>
        public virtual void OpenLetter()
        {
        }

        public virtual void ExposeData()
        {
            LetterDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref arrivalTick, "arrivalTick");
            Scribe_Values.Look(ref letterID, "letterID", "");
            List<string>? targets = lookTargets;
            Scribe_Collections.Look(ref targets, "lookTargets", LookMode.Value);
            lookTargets = targets;
        }
    }

    /// <summary>A letter with no player choice — RimWorld's ordinary notification (<c>Verse.StandardLetter</c>).</summary>
    public class StandardLetter : Letter
    {
    }

    /// <summary>One option on a <see cref="ChoiceLetter"/> (RimWorld: <c>RimWorld.DiaOption</c>, trimmed to what a
    /// letter needs). <see cref="action"/> is a live delegate and, like RimWorld's own dialog options, is never
    /// saved — a loaded <see cref="ChoiceLetter"/> keeps its text but not the ability to act on old choices.</summary>
    /// <summary>
    /// What a <see cref="LetterChoice"/> does, in a form that survives a save.
    ///
    /// <para/><b>Why this exists at all.</b> A choice's consequence is an <see cref="Action"/>, and a delegate
    /// cannot be written to a save file. Before this, a load produced choices with a label and a null action:
    /// the letter was still answerable and answering it did nothing, while the command reported success. That
    /// is the over-claim this codebase has now hit in four systems — a total or an outcome handed out without
    /// checking what actually happened — and it is worse here than the usual, because the player believes the
    /// quest they just accepted is running.
    ///
    /// <para/>So the <i>kind</i> of consequence is saved, and the action is rebuilt from it on load. Adding a
    /// case means adding a member here and a line in <see cref="ChoiceLetter.ExposeData"/>; the compiler will
    /// not remind you, so the enum is deliberately tiny and stays that way.
    /// </summary>
    public enum LetterChoiceKind
    {
        /// <summary>No consequence beyond the letter leaving the stack. Dismissal <i>is</i> the whole effect,
        /// so a null action here is correct rather than a failure to restore one.</summary>
        Dismiss = 0,

        /// <summary>Accepts the letter's own <see cref="ChoiceLetter.quest"/>.</summary>
        AcceptQuest = 1,
    }

    public sealed class LetterChoice
    {
        public string label;
        public Action? action;

        /// <summary>What this choice does, in the form that survives a save — see <see cref="LetterChoiceKind"/>.
        /// Defaults to <see cref="LetterChoiceKind.Dismiss"/>, so a choice constructed without one is a choice
        /// that claims no consequence, which is the safe direction to be wrong in.</summary>
        public LetterChoiceKind kind;

        public LetterChoice(string label, Action? action = null, LetterChoiceKind kind = LetterChoiceKind.Dismiss)
        {
            this.label = label ?? throw new ArgumentNullException(nameof(label));
            this.action = action;
            this.kind = kind;
        }
    }

    /// <summary>
    /// A letter offering the player a choice (RimWorld: <c>RimWorld.ChoiceLetter</c>) — accept/reject a quest,
    /// most often. <see cref="quest"/> links it back to the offer; an optional timeout
    /// (<see cref="SetTimeout"/>) lets <see cref="LetterStack.LetterStackTick"/> run <see cref="timeoutAction"/>
    /// on its own once the letter has sat unanswered long enough.
    /// </summary>
    public class ChoiceLetter : Letter
    {
        public List<LetterChoice> choices = new List<LetterChoice>();

        public Quest? quest;

        /// <summary>Not saved — see <see cref="LetterChoice.action"/>'s note.</summary>
        public Action? timeoutAction;

        private int timeoutTicksAbs = -1;

        /// <summary>True once <see cref="SetTimeout"/> has armed a countdown.</summary>
        public bool TimeoutActive => timeoutTicksAbs >= 0;

        /// <summary>Ticks remaining before <see cref="timeoutAction"/> fires; -1 when no timeout is armed.</summary>
        public int TimeoutTicks => TimeoutActive ? Math.Max(0, timeoutTicksAbs - Find.TickManager.TicksGame) : -1;

        /// <summary>Arms a countdown: <paramref name="action"/> runs (and the letter is removed) once
        /// <paramref name="ticksFromNow"/> ticks have passed without the player acting.</summary>
        public void SetTimeout(int ticksFromNow, Action? action)
        {
            timeoutTicksAbs = Find.TickManager.TicksGame + Math.Max(0, ticksFromNow);
            timeoutAction = action;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Quest? q = quest;
            Scribe_References.Look(ref q, "quest");
            quest = q;
            Scribe_Values.Look(ref timeoutTicksAbs, "timeoutTicksAbs", -1);

            // Labels only, never the actions — action is a live delegate and, per this type's own doc above,
            // is never saved. Before this, ExposeData never touched choices at all, so a save/load round trip
            // silently emptied it: a pending decision came back from a load with nothing to choose from, which
            // is a quieter way of failing the exact defect this lane exists to fix than an outright crash.
            // LookMode.Value on a List<string> is enough — LetterChoice itself is not IExposable and does not
            // need to be; there is nothing else on it worth carrying across a load.
            List<string>? labels = choices.ConvertAll(c => c.label);
            Scribe_Collections.Look(ref labels, "choiceLabels", LookMode.Value);
            List<string>? kinds = choices.ConvertAll(c => c.kind.ToString());
            Scribe_Collections.Look(ref kinds, "choiceKinds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                choices.Clear();
                if (labels != null)
                {
                    for (int i = 0; i < labels.Count; i++)
                    {
                        LetterChoiceKind kind = LetterChoiceKind.Dismiss;
                        if (kinds != null && i < kinds.Count)
                        {
                            Enum.TryParse(kinds[i], out kind);
                        }

                        choices.Add(new LetterChoice(labels[i], ActionFor(kind), kind));
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds a choice's consequence from the kind that survived the save.
        ///
        /// <para/>The delegate reads <see cref="quest"/> when it is <i>invoked</i>, not when it is built, which
        /// matters: <see cref="Scribe_References"/> may not have resolved the quest yet at
        /// <see cref="LoadSaveMode.LoadingVars"/> time, and capturing the field's value there would bind a null
        /// for good. Capturing <c>this</c> and reading the field later is correct whenever the reference
        /// resolves.
        ///
        /// <para/>A null <see cref="quest"/> at invoke time is not papered over here — see
        /// <c>GodCommands.ResolveLetterChoice</c>, which refuses rather than reporting a consequence it cannot
        /// deliver.
        /// </summary>
        private Action? ActionFor(LetterChoiceKind kind)
        {
            switch (kind)
            {
                case LetterChoiceKind.AcceptQuest:
                    return () => quest?.Accept();
                default:
                    return null;
            }
        }
    }
}
