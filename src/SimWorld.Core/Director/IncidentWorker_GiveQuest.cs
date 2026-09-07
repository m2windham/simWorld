using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Letters;
using SimWorld.Quests;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Bridges the storyteller to the quest system (SimWorld addition — RimWorld's closest equivalents are
    /// the family of quest-generating incident workers, e.g. <c>RimWorld.IncidentWorker_GiveQuest_TradeRequest</c>).
    /// Picks a <see cref="QuestScriptDef"/> whose point band covers this firing's <see cref="IncidentParms.points"/>
    /// and whose per-script refire spacing (<see cref="QuestManager.CanFire"/>) has elapsed, weighted by
    /// <see cref="QuestScriptDef.rootSelectionWeight"/>; generates it, records the offer in the storyteller's
    /// chronicle, and either accepts it immediately (<see cref="QuestScriptDef.autoAccept"/>) or offers it
    /// through the <see cref="LetterDefOf.AcceptQuest"/> choice letter.
    /// </summary>
    public sealed class IncidentWorker_GiveQuest : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => Candidates(parms).Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            List<QuestScriptDef> candidates = Candidates(parms).ToList();
            if (candidates.Count == 0) return false;
            if (!GenCollection.TryRandomElementByWeight(candidates, d => d.rootSelectionWeight, Rand.Current, out QuestScriptDef picked))
            {
                return false;
            }

            var slate = new Slate();
            slate.Set("points", parms.points);
            Quest quest = QuestGen.Generate(picked, slate);

            Find.QuestManager.Add(quest);
            Find.QuestManager.Notify_QuestScriptFired(picked, Find.TickManager.TicksGame);
            Find.Storyteller.RecordChronicle("Quest offered: " + quest.name);

            if (picked.autoAccept)
            {
                quest.Accept();
            }
            else
            {
                SendAcceptLetter(quest);
            }
            return true;
        }

        private IEnumerable<QuestScriptDef> Candidates(IncidentParms parms)
        {
            int now = Find.TickManager.TicksGame;
            foreach (QuestScriptDef d in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (parms.points < d.rootMinPoints || parms.points > d.rootMaxPoints) continue;
                if (!Find.QuestManager.CanFire(d, now)) continue;
                yield return d;
            }
        }

        private static void SendAcceptLetter(Quest quest)
        {
            LetterDef def = LetterDefOf.AcceptQuest;
            var letter = (ChoiceLetter)Activator.CreateInstance(def.letterClass)!;
            letter.def = def;
            letter.label = quest.name;
            letter.text = quest.description.Length > 0 ? quest.description : "A quest is available: " + quest.name + ".";
            letter.quest = quest;
            letter.choices.Add(new LetterChoice("Accept", () => quest.Accept()));
            letter.choices.Add(new LetterChoice("Reject"));
            if (quest.ticksUntilAcceptanceExpiry >= 0)
            {
                letter.SetTimeout(quest.ticksUntilAcceptanceExpiry, null);
            }
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}
