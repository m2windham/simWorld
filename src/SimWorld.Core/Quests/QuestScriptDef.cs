using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Quests
{
    /// <summary>
    /// One generatable quest (RimWorld: <c>RimWorld.QuestScriptDef</c>): a node tree plus the knobs
    /// <see cref="Director.IncidentWorker_GiveQuest"/> uses to pick among the loaded scripts.
    /// <see cref="nameTemplate"/> stands in for RimWorld's grammar-resolved <c>questNameRules</c> — SimWorld's
    /// translation keeps a plain string rather than porting the whole Rules grammar engine.
    /// </summary>
    public class QuestScriptDef : Def
    {
        public QuestNode root = null!;

        public float rootSelectionWeight = 1f;

        public float rootMinPoints;

        public float rootMaxPoints = float.MaxValue;

        /// <summary>Minimum days between two firings of this exact script; 0 disables the check.</summary>
        public float minRefireDays;

        /// <summary>When true, <see cref="Director.IncidentWorker_GiveQuest"/> accepts the quest immediately
        /// instead of offering it through a choice letter.</summary>
        public bool autoAccept;

        /// <summary>How long, in days, the generated offer stays open before it lapses; also fed to the script
        /// as the slate variables "expireDays"/"expireTicks" for any <see cref="QuestNode_Delay"/> to use.</summary>
        public FloatRange expireDaysRange = new FloatRange(4f, 8f);

        /// <summary>Reserved for a future "can only ever fire once, hand-authored" tier of script; not yet
        /// enforced anywhere (RimWorld: <c>QuestScriptDef.isRootSpecial</c>).</summary>
        public bool isRootSpecial;

        public string? nameTemplate;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (root == null) yield return "root is required.";
            if (rootMaxPoints < rootMinPoints) yield return "rootMaxPoints must be >= rootMinPoints.";
        }
    }
}
