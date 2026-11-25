using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>
    /// The words of a quest offer: what is asked, what it pays, what it risks and how long it stands. RimWorld's
    /// offer window lists the rewards beside the description and says how long it is open; the fourth playtest's
    /// letter said only "A quest is available: Lost caravan.", which is not a decision anyone can make.
    /// The reward is read from what the quest's own reward parts promise, so the letter cannot say one thing
    /// and pay another.
    /// </summary>
    public static class QuestOfferText
    {
        public static string For(Quest quest)
        {
            QuestScriptDef? def = quest.root;
            string asked = quest.description.Length > 0 ? quest.description : def?.description ?? quest.name;
            var lines = new List<string> { asked };
            lines.Add("Reward: " + RewardText(quest.PromisedRewards) + ".");
            lines.Add("Risk: " + (string.IsNullOrEmpty(def?.riskText) ? "none to your people" : def!.riskText) + ".");
            if (quest.ticksUntilAcceptanceExpiry > 0)
            {
                lines.Add("The offer stands for " + GenDate.ToStringTicksToPeriod(quest.ticksUntilAcceptanceExpiry) + ".");
            }
            return string.Join("\n", lines);
        }

        /// <summary>"200 silver and 5 goodwill with the borderland civilization", or "nothing" when the quest
        /// pays nothing.</summary>
        public static string RewardText(IEnumerable<RewardRecord> rewards)
        {
            var pieces = new List<string>();
            foreach (RewardRecord r in rewards)
            {
                string amount = r.amount.ToString("0", CultureInfo.InvariantCulture);
                if (r.kind == "goodwill")
                {
                    string? faction = r.factionDefName != null ? DefDatabase<FactionDef>.GetNamedSilentFail(r.factionDefName)?.label : null;
                    pieces.Add(amount + " goodwill" + (faction != null ? " with the " + faction : ""));
                }
                else
                {
                    pieces.Add(amount + " " + r.kind);
                }
            }
            if (pieces.Count == 0) return "nothing";
            if (pieces.Count == 1) return pieces[0];
            return string.Join(", ", pieces.GetRange(0, pieces.Count - 1)) + " and " + pieces[pieces.Count - 1];
        }
    }
}
