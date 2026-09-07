using System;
using System.Collections.Generic;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>A signal tag with optional arguments (RimWorld: <c>RimWorld.QuestPart.SignalArgs</c>), used to
    /// wake a <see cref="QuestPart"/> whose <see cref="QuestPart.inSignal"/> matches, or to record a quest's
    /// outcome-flavoured event (SimWorld addition: Quest_Tribute's failure records one such signal, see
    /// <see cref="QuestPart_QuestEnd"/>).</summary>
    public readonly struct Signal
    {
        public readonly string tag;
        public readonly Dictionary<string, object?>? args;

        public Signal(string tag, Dictionary<string, object?>? args = null)
        {
            this.tag = tag ?? throw new ArgumentNullException(nameof(tag));
            this.args = args;
        }
    }

    /// <summary>Lifecycle of one <see cref="QuestPart"/> (RimWorld: <c>RimWorld.QuestPartState</c>, informal there;
    /// made explicit here since SimWorld's parts are driven purely by signals rather than a UI-visible timeline).</summary>
    public enum QuestPartState
    {
        NeverEnabled,
        Enabled,
        Disabled,
    }

    /// <summary>
    /// One piece of standing quest behaviour (RimWorld: <c>RimWorld.QuestPart</c>): a rule that wakes on
    /// <see cref="inSignal"/> and does something — send a letter, count down a delay, hand out a reward, end
    /// the quest. <see cref="QuestNode"/>s build these during <see cref="QuestGen.Generate"/>; <see cref="Quest"/>
    /// owns and ticks them afterward.
    /// </summary>
    public abstract class QuestPart : IExposable
    {
        public Quest quest = null!;

        /// <summary>The signal tag that enables this part; empty means it never wakes on its own.</summary>
        public string inSignal = "";

        public QuestPartState State { get; protected set; } = QuestPartState.NeverEnabled;

        /// <summary>Runs this part's one-shot or ongoing effect. Called once, the moment <see cref="inSignal"/> arrives.</summary>
        public virtual void Enable(Signal signal)
        {
            State = QuestPartState.Enabled;
        }

        public virtual void Disable()
        {
            State = QuestPartState.Disabled;
        }

        /// <summary>Called every tick the owning quest is ongoing, regardless of this part's own state.</summary>
        public virtual void QuestPartTick()
        {
        }

        public void Notify_QuestSignalReceived(Signal signal)
        {
            if (inSignal.Length > 0 && signal.tag == inSignal)
            {
                Enable(signal);
            }
        }

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref inSignal, "inSignal", "");
            QuestPartState state = State;
            Scribe_Values.Look(ref state, "state", QuestPartState.NeverEnabled);
            State = state;
        }
    }

    /// <summary>Counts down from <see cref="delayTicks"/> once enabled, then sends <see cref="outSignalCompleted"/>
    /// (RimWorld: <c>RimWorld.QuestPart_Delay</c>).</summary>
    public sealed class QuestPart_Delay : QuestPart
    {
        public int delayTicks;
        public string outSignalCompleted = "";

        private int ticksLeft = -1;

        public int TicksLeft => ticksLeft;

        public override void Enable(Signal signal)
        {
            base.Enable(signal);
            ticksLeft = delayTicks;
        }

        public override void QuestPartTick()
        {
            if (State != QuestPartState.Enabled) return;
            if (ticksLeft > 0) ticksLeft--;
            if (ticksLeft <= 0)
            {
                State = QuestPartState.Disabled;
                if (outSignalCompleted.Length > 0) quest.SendSignal(outSignalCompleted);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref delayTicks, "delayTicks");
            Scribe_Values.Look(ref outSignalCompleted, "outSignalCompleted", "");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", -1);
        }
    }

    /// <summary>Sends a letter once enabled (RimWorld: <c>RimWorld.QuestPart_Letter</c>).</summary>
    public sealed class QuestPart_Letter : QuestPart
    {
        public LetterDef letterDef = null!;
        public string label = "";
        public string text = "";

        public override void Enable(Signal signal)
        {
            base.Enable(signal);
            Find.LetterStack.ReceiveLetter(label, text, letterDef);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            LetterDef? d = letterDef;
            Scribe_Defs.Look(ref d, "letterDef");
            letterDef = d!;
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref text, "text", "");
        }
    }

    /// <summary>One reward the player received (RimWorld: a flattened <c>RimWorld.Reward</c>). <see cref="factionDefName"/>
    /// is a free-form placeholder rather than a live faction reference (SimWorld: the Factions system lands separately).</summary>
    public sealed class RewardRecord : IExposable
    {
        public string kind = "";
        public float amount;
        public string? factionDefName;

        public RewardRecord()
        {
        }

        public RewardRecord(string kind, float amount, string? factionDefName = null)
        {
            this.kind = kind;
            this.amount = amount;
            this.factionDefName = factionDefName;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", "");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref factionDefName, "factionDefName");
        }
    }

    /// <summary>Hook for a future economy/faction system to actually apply a reward's effects; optional
    /// (RimWorld has no equivalent — rewards there are self-applying). Set <see cref="QuestManager.RewardSink"/>
    /// to receive every reward as it is given.</summary>
    public interface IQuestRewardSink
    {
        void GiveRewards(IReadOnlyList<RewardRecord> rewards);
    }

    /// <summary>Hands out <see cref="rewards"/> once enabled, records them on the quest, and offers them to
    /// <see cref="QuestManager.RewardSink"/> if one is set (RimWorld: <c>RimWorld.QuestPart_Reward</c>, simplified).</summary>
    public sealed class QuestPart_Reward : QuestPart
    {
        public List<RewardRecord> rewards = new List<RewardRecord>();

        public override void Enable(Signal signal)
        {
            base.Enable(signal);
            quest.Notify_RewardsGiven(rewards);
            Find.QuestManager.RewardSink?.GiveRewards(rewards);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            List<RewardRecord>? list = rewards;
            Scribe_Collections.Look(ref list, "rewards", LookMode.Deep);
            rewards = list ?? new List<RewardRecord>();
        }
    }

    /// <summary>Ends the quest with <see cref="outcome"/> once enabled (RimWorld: <c>RimWorld.QuestPart_EndQuest</c>).</summary>
    public sealed class QuestPart_QuestEnd : QuestPart
    {
        public QuestEndOutcome outcome;

        public override void Enable(Signal signal)
        {
            base.Enable(signal);
            quest.End(outcome);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref outcome, "outcome", QuestEndOutcome.Unknown);
        }
    }
}
