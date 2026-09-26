using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// A lord job's toils and the transitions between them (RimWorld: <c>Verse.AI.Group.StateGraph</c>).
    /// Built fresh by <see cref="LordJob.CreateGraph"/> every time a lord takes a job — including on load,
    /// which is why nothing in here is saved: the lord saves which toil it is in and what its triggers
    /// remember, by index, and the graph itself is rebuilt around them.
    /// </summary>
    public sealed class StateGraph
    {
        public readonly List<LordToil> lordToils = new List<LordToil>();

        public readonly List<Transition> transitions = new List<Transition>();

        /// <summary>The first toil added, as in RimWorld.</summary>
        public LordToil? StartingToil => lordToils.Count > 0 ? lordToils[0] : null;

        public void AddToil(LordToil toil) => lordToils.Add(toil);

        /// <summary>High priority goes to the front, so it is checked before anything already there —
        /// which is how RimWorld makes "half of us are down" outrank every other way out.</summary>
        public void AddTransition(Transition transition, bool highPriority = false)
        {
            if (highPriority) transitions.Insert(0, transition);
            else transitions.Add(transition);
        }
    }

    /// <summary>
    /// An edge of a <see cref="StateGraph"/>: from any of <see cref="sources"/> to <see cref="target"/> when
    /// any one of <see cref="triggers"/> fires (RimWorld: <c>Verse.AI.Group.Transition</c>). Only the
    /// behaviour a raid uses is here — every transition this port builds refuses to move to the state it is
    /// already in, which is RimWorld's default.
    /// </summary>
    public sealed class Transition
    {
        public readonly List<LordToil> sources = new List<LordToil>();
        public readonly LordToil target;
        public readonly List<Trigger> triggers = new List<Trigger>();
        public readonly List<TransitionAction> preActions = new List<TransitionAction>();

        public Transition(LordToil firstSource, LordToil target)
        {
            this.target = target;
            sources.Add(firstSource);
        }

        public void AddSource(LordToil source)
        {
            if (!sources.Contains(source)) sources.Add(source);
        }

        public void AddTrigger(Trigger trigger) => triggers.Add(trigger);

        public void AddPreAction(TransitionAction action) => preActions.Add(action);

        public void SourceToilBecameActive(Transition transition, LordToil? previousToil)
        {
            for (int i = 0; i < triggers.Count; i++) triggers[i].SourceToilBecameActive(transition, previousToil);
        }

        /// <summary>Offers <paramref name="signal"/> to every trigger in order and takes the transition on
        /// the first that fires. Every trigger is asked until one fires, so a counting trigger further down the
        /// list still counts on a tick where an earlier one did not fire.</summary>
        public bool CheckSignal(Lord lord, TriggerSignal signal)
        {
            for (int i = 0; i < triggers.Count; i++)
            {
                if (!triggers[i].ActivateOn(lord, signal)) continue;
                Execute(lord);
                return true;
            }
            return false;
        }

        public void Execute(Lord lord)
        {
            if (ReferenceEquals(target, lord.CurLordToil)) return;
            for (int i = 0; i < preActions.Count; i++) preActions[i].DoAction(this);
            lord.GotoToil(target);
        }
    }

    /// <summary>Something done as a transition is taken (RimWorld: <c>Verse.AI.Group.TransitionAction</c>).</summary>
    public abstract class TransitionAction
    {
        public abstract void DoAction(Transition trans);
    }

    /// <summary>
    /// Tells the player why the raid changed its mind (RimWorld: <c>Verse.AI.Group.TransitionAction_Message</c>,
    /// whose default type is <c>MessageTypeDefOf.NeutralEvent</c>).
    ///
    /// <para/><b>A letter, not a message, and a chronicle line as well — both recorded translations.</b>
    /// RimWorld shows a transient message. This port has no message channel: the letter stack is the only way
    /// anything reaches the player, so the message arrives as a <see cref="LetterDefOf.NeutralEvent"/> letter,
    /// matching the message's own type. It also goes into the chronicle, because the unwatched half of the
    /// same event already does: <c>Director.SettlementRaidResolver</c> writes "struck … and were driven off"
    /// for a raid nobody saw, and a raid the player watched break and run is not less of an event than one
    /// they were only told about. The chronicle line puts <see cref="label"/> before the colon, which is the
    /// category <c>Director.MomentCurator</c> reads — so the first raid ever to break is remembered.
    /// </summary>
    public sealed class TransitionAction_Message : TransitionAction
    {
        public readonly string label;
        public readonly string message;

        public TransitionAction_Message(string label, string message)
        {
            this.label = label;
            this.message = message;
        }

        public override void DoAction(Transition trans)
        {
            var lookTargets = new List<string>();
            Lord? lord = trans.target.lord;
            if (lord != null)
            {
                for (int i = 0; i < lord.ownedPawns.Count; i++) lookTargets.Add(lord.ownedPawns[i].GetUniqueLoadID());
            }
            Find.LetterStack.ReceiveLetter(label, message, LetterDefOf.NeutralEvent, lookTargets);
            Find.Storyteller.RecordChronicle(label + ": " + message);
        }

        /// <summary>RimWorld's <c>{0}</c> in every raid message: <c>faction.def.pawnsPlural.CapitalizeFirst()</c>.</summary>
        public static string PawnsPluralCap(Faction faction)
        {
            string plural = faction.def.pawnsPlural ?? faction.def.label ?? "raiders";
            if (plural.Length == 0) return plural;
            return char.ToUpperInvariant(plural[0]) + plural.Substring(1);
        }
    }
}
