using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>
    /// Every generated <see cref="Quest"/> (RimWorld: <c>RimWorld.QuestManager</c>), reached ambiently through
    /// <see cref="Sim.Find.QuestManager"/>. Also tracks per-<see cref="QuestScriptDef"/> refire spacing
    /// (<see cref="CanFire"/>/<see cref="Notify_QuestScriptFired"/>) — RimWorld's <c>StoryState</c> tracks this
    /// per <c>IncidentDef</c> instead, but a single <c>GiveQuest</c> incident here can generate any of several
    /// scripts, so the spacing has to live one level down, on the script itself.
    /// </summary>
    public sealed class QuestManager : IExposable
    {
        private readonly List<Quest> quests = new List<Quest>();
        private readonly Dictionary<QuestScriptDef, int> lastFireTicks = new Dictionary<QuestScriptDef, int>();

        /// <summary>Optional hook a future economy/faction system sets to actually apply rewards; see <see cref="IQuestRewardSink"/>.</summary>
        public IQuestRewardSink? RewardSink { get; set; }

        public IReadOnlyList<Quest> QuestsListForReading => quests;

        public IEnumerable<Quest> QuestsInDisplayOrder => quests.OrderByDescending(q => q.appearanceTick);

        public bool IsAnyQuestAccepted => quests.Any(q => q.State != QuestState.NotYetAccepted);

        public void Add(Quest quest)
        {
            if (quest == null) throw new ArgumentNullException(nameof(quest));
            quests.Add(quest);
        }

        public bool Remove(Quest quest) => quests.Remove(quest);

        public void QuestManagerTick()
        {
            foreach (Quest quest in quests.ToArray()) quest.QuestTick();
        }

        /// <summary>Delivers <paramref name="signal"/> to every quest that is currently ongoing.</summary>
        public void SendSignal(Signal signal)
        {
            foreach (Quest quest in quests)
            {
                if (quest.State == QuestState.Ongoing) quest.Received(signal);
            }
        }

        /// <summary>True when <paramref name="def"/> either has no refire spacing or has not fired within it.</summary>
        public bool CanFire(QuestScriptDef def, int nowTick)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.minRefireDays <= 0f) return true;
            if (!lastFireTicks.TryGetValue(def, out int last)) return true;
            return nowTick >= last + (int)(def.minRefireDays * GenDate.TicksPerDay);
        }

        public void Notify_QuestScriptFired(QuestScriptDef def, int tick)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            lastFireTicks[def] = tick;
        }

        public void ExposeData()
        {
            List<Quest>? questList = new List<Quest>(quests);
            Scribe_Collections.Look(ref questList, "quests", LookMode.Deep);
            quests.Clear();
            if (questList != null) quests.AddRange(questList);

            Dictionary<QuestScriptDef, int>? refireDict = new Dictionary<QuestScriptDef, int>(lastFireTicks);
            Scribe_Collections.Look(ref refireDict, "lastFireTicks", LookMode.Def, LookMode.Value);
            lastFireTicks.Clear();
            if (refireDict != null)
            {
                foreach (KeyValuePair<QuestScriptDef, int> kv in refireDict) lastFireTicks[kv.Key] = kv.Value;
            }
        }
    }
}
