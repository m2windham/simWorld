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
    public sealed class LetterChoice
    {
        public string label;
        public Action? action;

        public LetterChoice(string label, Action? action = null)
        {
            this.label = label ?? throw new ArgumentNullException(nameof(label));
            this.action = action;
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
        }
    }
}
