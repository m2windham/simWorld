using System.Collections.Generic;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>
    /// One step of a <see cref="QuestScriptDef.root"/> tree (RimWorld: <c>RimWorld.QuestGen.QuestNode</c>).
    /// <see cref="Run"/> executes it against the ambient <see cref="QuestGen.quest"/>/<see cref="QuestGen.slate"/>
    /// during <see cref="QuestGen.Generate"/> — usually by adding a <see cref="QuestPart"/> via <see cref="QuestGen.AddPart"/>.
    /// <see cref="TestRun"/> is a side-effect-free dry run (SimWorld's compact node set has no node that needs
    /// to fail generation yet, so every node accepts by default; kept for parity and for <see cref="QuestNode_Sequence"/>
    /// to short-circuit).
    /// </summary>
    public abstract class QuestNode
    {
        public bool TestRun(Slate slate) => TestRunInt(slate);

        protected virtual bool TestRunInt(Slate slate) => true;

        public void Run()
        {
            RunInt();
            PostQuestAdded();
        }

        protected abstract void RunInt();

        /// <summary>Hook for a node to react after its part(s) have joined the quest; no-op by default.</summary>
        protected virtual void PostQuestAdded()
        {
        }

        /// <summary>The slate's current in-signal — the tag whatever <see cref="QuestPart"/> this node builds
        /// should wake on. Threaded through the slate (rather than a method parameter) so nested nodes see it.</summary>
        protected static string CurrentInSignal => QuestGen.slate.Get<string>("inSignal", "") ?? "";
    }

    /// <summary>Runs every child node in order (RimWorld: <c>RimWorld.QuestNode_Sequence</c>).</summary>
    public sealed class QuestNode_Sequence : QuestNode
    {
        public List<QuestNode> nodes = new List<QuestNode>();

        protected override bool TestRunInt(Slate slate)
        {
            foreach (QuestNode node in nodes)
            {
                if (!node.TestRun(slate)) return false;
            }
            return true;
        }

        protected override void RunInt()
        {
            foreach (QuestNode node in nodes) node.Run();
        }
    }

    /// <summary>Writes one slate value (RimWorld: <c>RimWorld.QuestNode_Set</c>, simplified: no expression
    /// language, only a literal string or a straight copy of another slate value).</summary>
    public sealed class QuestNode_Set : QuestNode
    {
        public string name = "";
        public string? value;

        /// <summary>When set, copies this slate variable's value (of whatever type it holds) instead of <see cref="value"/>.</summary>
        public string? valueRef;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            if (valueRef != null && slate.TryGet<object>(valueRef, out object? copied))
            {
                slate.Set(name, copied);
            }
            else
            {
                slate.Set(name, value);
            }
        }
    }

    /// <summary>Branches on whether a slate variable exists (RimWorld: <c>RimWorld.QuestNode_IsSet</c>... equivalent,
    /// SimWorld's compact translation).</summary>
    public sealed class QuestNode_IsSet : QuestNode
    {
        public string name = "";
        public QuestNode? node;
        public QuestNode? elseNode;

        protected override void RunInt()
        {
            if (QuestGen.slate.Exists(name)) node?.Run();
            else elseNode?.Run();
        }
    }

    /// <summary>Branches on a slate boolean (RimWorld: <c>RimWorld.QuestNode_IsTrue</c>-equivalent).</summary>
    public sealed class QuestNode_IsTrue : QuestNode
    {
        public string name = "";
        public QuestNode? node;
        public QuestNode? elseNode;

        protected override void RunInt()
        {
            if (QuestGen.slate.Get(name, false)) node?.Run();
            else elseNode?.Run();
        }
    }

    /// <summary>
    /// Adds a <see cref="QuestPart_Delay"/> gated on the current in-signal, then runs <see cref="node"/> with
    /// the slate's in-signal switched to the delay's completion signal — so whatever <see cref="node"/> builds
    /// only wakes once the delay elapses (RimWorld: <c>RimWorld.QuestNode_Delay</c>).
    /// </summary>
    public sealed class QuestNode_Delay : QuestNode
    {
        public IntRange delayTicks;

        /// <summary>When set, the delay length is read from this slate variable (an int, already in ticks)
        /// instead of <see cref="delayTicks"/>.</summary>
        public string? delayTicksRef;

        public QuestNode? node;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            int ticks = delayTicksRef != null && slate.TryGet<int>(delayTicksRef, out int fromSlate)
                ? fromSlate
                : Rand.Range(delayTicks);
            string outSignal = QuestGen.GenerateNewSignal("delayCompleted");

            QuestGen.AddPart(new QuestPart_Delay
            {
                inSignal = CurrentInSignal,
                delayTicks = ticks,
                outSignalCompleted = outSignal,
            });

            slate.Set("inSignal", outSignal);
            node?.Run();
        }
    }

    /// <summary>Adds a <see cref="QuestPart_Letter"/> gated on the current in-signal (RimWorld: <c>RimWorld.QuestNode_Letter</c>).</summary>
    public sealed class QuestNode_Letter : QuestNode
    {
        public LetterDef letterDef = null!;
        public string label = "";
        public string text = "";

        protected override void RunInt()
        {
            QuestGen.AddPart(new QuestPart_Letter
            {
                inSignal = CurrentInSignal,
                letterDef = letterDef,
                label = label,
                text = text,
            });
        }
    }

    /// <summary>Adds a <see cref="QuestPart_Reward"/> gated on the current in-signal (RimWorld: <c>RimWorld.QuestNode_GiveRewards</c>,
    /// trimmed to the two reward kinds SimWorld models so far).</summary>
    public sealed class QuestNode_GiveReward : QuestNode
    {
        public float? silverAmount;
        public string? silverRef;
        public float goodwillAmount;
        public string? goodwillFactionDefName;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            float silver = silverAmount ?? (silverRef != null ? slate.Get(silverRef, 0f) : 0f);

            var part = new QuestPart_Reward { inSignal = CurrentInSignal };
            if (silver > 0f) part.rewards.Add(new RewardRecord("silver", silver));
            if (goodwillAmount != 0f) part.rewards.Add(new RewardRecord("goodwill", goodwillAmount, goodwillFactionDefName));
            QuestGen.AddPart(part);
        }
    }

    /// <summary>Adds a <see cref="QuestPart_QuestEnd"/> gated on the current in-signal (RimWorld: <c>RimWorld.QuestNode_End</c>).</summary>
    public sealed class QuestNode_End : QuestNode
    {
        public QuestEndOutcome outcome = QuestEndOutcome.Success;

        protected override void RunInt()
        {
            QuestGen.AddPart(new QuestPart_QuestEnd { inSignal = CurrentInSignal, outcome = outcome });
        }
    }
}
