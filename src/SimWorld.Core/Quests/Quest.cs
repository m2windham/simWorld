using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>Life of a <see cref="Quest"/> (RimWorld: <c>RimWorld.QuestState</c>).</summary>
    public enum QuestState
    {
        NotYetAccepted,
        Ongoing,
        EndedSuccess,
        EndedFailed,
        EndedInvalid,
        EndedOfferExpired,
        EndedUnknownOutcome,
    }

    /// <summary>How a quest was resolved when <see cref="Quest.End"/> was called (RimWorld: <c>RimWorld.QuestEndOutcome</c>).
    /// Offer expiry is not one of these — it is its own <see cref="QuestState.EndedOfferExpired"/>, reached by
    /// <see cref="Quest.QuestTick"/> rather than an authored <see cref="QuestPart_QuestEnd"/>.</summary>
    public enum QuestEndOutcome
    {
        Unknown,
        Success,
        Fail,
        Invalid,
    }

    /// <summary>
    /// One generated quest instance (RimWorld: <c>RimWorld.Quest</c>): the parts <see cref="QuestGen"/> built
    /// from a <see cref="QuestScriptDef"/>'s node tree, plus the bookkeeping (acceptance, offer expiry,
    /// rewards given) that drives them. Owned by <see cref="QuestManager"/>.
    /// </summary>
    public sealed class Quest : IExposable, ILoadReferenceable
    {
        public int id;
        public QuestScriptDef root = null!;
        public string name = "";
        public string description = "";

        /// <summary>The signal <see cref="Accept"/> sends; every root-level part built while generating listens for it.</summary>
        public string initiateSignal = "";

        public int appearanceTick;
        public int acceptanceTick = -1;

        /// <summary>Ticks after <see cref="appearanceTick"/> before an unaccepted offer lapses; -1 = never.</summary>
        public int ticksUntilAcceptanceExpiry = -1;

        private QuestState state = QuestState.NotYetAccepted;
        private readonly List<QuestPart> parts = new List<QuestPart>();
        private readonly List<RewardRecord> rewardsGiven = new List<RewardRecord>();

        public QuestState State => state;

        public IReadOnlyList<QuestPart> PartsListForReading => parts;

        public IReadOnlyList<RewardRecord> RewardsGiven => rewardsGiven;

        /// <summary>Ticks since <see cref="Accept"/>, or -1 before acceptance.</summary>
        public int TicksSinceAccepted => acceptanceTick < 0 ? -1 : Find.TickManager.TicksGame - acceptanceTick;

        private bool IsEnded =>
            state == QuestState.EndedSuccess || state == QuestState.EndedFailed || state == QuestState.EndedInvalid
            || state == QuestState.EndedOfferExpired || state == QuestState.EndedUnknownOutcome;

        public string GetUniqueLoadID() => "Quest_" + id.ToString(CultureInfo.InvariantCulture);

        public void AddPart(QuestPart part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            part.quest = this;
            parts.Add(part);
        }

        /// <summary>Moves the quest from <see cref="QuestState.NotYetAccepted"/> to <see cref="QuestState.Ongoing"/>
        /// and fires <see cref="initiateSignal"/>. A no-op once already accepted or ended.</summary>
        public void Accept()
        {
            if (state != QuestState.NotYetAccepted) return;
            state = QuestState.Ongoing;
            acceptanceTick = Find.TickManager.TicksGame;
            SendSignal(initiateSignal);
        }

        public void SendSignal(string tag, Dictionary<string, object?>? args = null)
        {
            if (string.IsNullOrEmpty(tag)) return;
            Received(new Signal(tag, args));
        }

        public void Received(Signal signal)
        {
            foreach (QuestPart part in parts.ToArray())
            {
                part.Notify_QuestSignalReceived(signal);
            }
        }

        /// <summary>Ticks every part while ongoing; while unaccepted, expires the offer once <see cref="ticksUntilAcceptanceExpiry"/> passes.</summary>
        public void QuestTick()
        {
            if (state == QuestState.Ongoing)
            {
                foreach (QuestPart part in parts.ToArray()) part.QuestPartTick();
            }
            else if (state == QuestState.NotYetAccepted && ticksUntilAcceptanceExpiry >= 0
                     && Find.TickManager.TicksGame >= appearanceTick + ticksUntilAcceptanceExpiry)
            {
                Expire();
            }
        }

        /// <summary>Lapses an unaccepted offer. A no-op once accepted or already ended.</summary>
        public void Expire()
        {
            if (state != QuestState.NotYetAccepted) return;
            state = QuestState.EndedOfferExpired;
            CleanupQuestParts();
        }

        /// <summary><paramref name="sendLetter"/> is kept for parity with RimWorld's call sites; SimWorld has no
        /// distinct "quest ended" letter type yet — a script sends its own via <see cref="QuestPart_Letter"/> if it wants one.</summary>
        public void End(QuestEndOutcome outcome, bool sendLetter = true)
        {
            if (IsEnded) return;
            state = outcome switch
            {
                QuestEndOutcome.Success => QuestState.EndedSuccess,
                QuestEndOutcome.Fail => QuestState.EndedFailed,
                QuestEndOutcome.Invalid => QuestState.EndedInvalid,
                _ => QuestState.EndedUnknownOutcome,
            };
            CleanupQuestParts();
            _ = sendLetter;
        }

        public void CleanupQuestParts()
        {
            foreach (QuestPart part in parts) part.Disable();
        }

        public void Notify_RewardsGiven(IEnumerable<RewardRecord> rewards)
        {
            rewardsGiven.AddRange(rewards);
        }

        public string RewardsSummary
        {
            get
            {
                if (rewardsGiven.Count == 0) return "";
                var pieces = new List<string>();
                foreach (RewardRecord r in rewardsGiven)
                {
                    pieces.Add(r.amount.ToString(CultureInfo.InvariantCulture) + " " + r.kind);
                }
                return string.Join(", ", pieces);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            QuestScriptDef? r = root;
            Scribe_Defs.Look(ref r, "root");
            root = r!;
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref description, "description", "");
            Scribe_Values.Look(ref state, "state", QuestState.NotYetAccepted);
            Scribe_Values.Look(ref initiateSignal, "initiateSignal", "");
            Scribe_Values.Look(ref appearanceTick, "appearanceTick");
            Scribe_Values.Look(ref acceptanceTick, "acceptanceTick", -1);
            Scribe_Values.Look(ref ticksUntilAcceptanceExpiry, "ticksUntilAcceptanceExpiry", -1);

            List<QuestPart>? partList = new List<QuestPart>(parts);
            Scribe_Collections.Look(ref partList, "parts", LookMode.Deep);
            parts.Clear();
            if (partList != null) parts.AddRange(partList);
            foreach (QuestPart part in parts) part.quest = this;

            List<RewardRecord>? rewardList = new List<RewardRecord>(rewardsGiven);
            Scribe_Collections.Look(ref rewardList, "rewardsGiven", LookMode.Deep);
            rewardsGiven.Clear();
            if (rewardList != null) rewardsGiven.AddRange(rewardList);
        }
    }
}
