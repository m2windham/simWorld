namespace SimWorld.Quests
{
    /// <summary>
    /// Builds a <see cref="QuestPart_Tribute"/> gated on the current in-signal, then runs
    /// <see cref="paidNode"/> and <see cref="refusedNode"/> with the slate's in-signal switched to each
    /// branch's own completion signal — so whatever each sub-tree builds only wakes down the branch that
    /// actually happened.
    ///
    /// <para/>The same shape <see cref="QuestNode_Delay"/> uses to hang a sub-tree off a signal, doubled. It
    /// is a <i>runtime</i> branch, which is what this needs and what <see cref="QuestNode_IsSet"/> and
    /// <see cref="QuestNode_IsTrue"/> cannot give: those branch during
    /// <see cref="QuestGen.Generate"/>, on the slate as it stands before the quest has run, and whether a
    /// civilization can meet a demand is not knowable then.
    ///
    /// <para/>Lives in its own file rather than being appended to <c>QuestNode.cs</c>, per this repo's
    /// "add a file rather than edit a shared one" convention — <c>DefLoader</c> resolves a <c>Class=</c>
    /// by full type name and does not care which file it sits in.
    /// </summary>
    public sealed class QuestNode_Tribute : QuestNode
    {
        /// <summary>How much silver is demanded, as a literal.</summary>
        public float? silverAmount;

        /// <summary>When set, the demand is read from this slate variable instead of
        /// <see cref="silverAmount"/> — which is how the shipped script scales the demand with the threat
        /// points the storyteller bought the quest at.</summary>
        public string? silverRef;

        public string? factionDefName;

        public float goodwillOnPayment;

        public float goodwillOnRefusal;

        public QuestNode? paidNode;

        public QuestNode? refusedNode;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            float silver = silverAmount ?? (silverRef != null ? slate.Get(silverRef, 0f) : 0f);

            // Deliberately NOT scaled by DifficultyDef.questRewardValueFactor. That factor is applied at
            // reward generation (QuestNode_GiveReward) because it is a factor on reward *value*, and a tribute
            // is a cost, not a reward — scaling it here would use one knob for two opposite meanings. A
            // difficulty that wants harsher demands wants its own field, not this one.
            string paidSignal = QuestGen.GenerateNewSignal("tributePaid");
            string refusedSignal = QuestGen.GenerateNewSignal("tributeRefused");

            QuestGen.AddPart(new QuestPart_Tribute
            {
                inSignal = CurrentInSignal,
                silverDemanded = silver,
                factionDefName = factionDefName,
                goodwillOnPayment = goodwillOnPayment,
                goodwillOnRefusal = goodwillOnRefusal,
                outSignalPaid = paidSignal,
                outSignalRefused = refusedSignal,
            });

            // Each branch is generated with its own in-signal, and the slate is put back afterwards so a node
            // following this one in a sequence is not left inside the refused branch.
            string resumeSignal = CurrentInSignal;

            slate.Set("inSignal", paidSignal);
            paidNode?.Run();

            slate.Set("inSignal", refusedSignal);
            refusedNode?.Run();

            slate.Set("inSignal", resumeSignal);
        }
    }
}
