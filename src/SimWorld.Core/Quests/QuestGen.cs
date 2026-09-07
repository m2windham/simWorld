using System;
using System.Globalization;
using SimWorld.Sim;

namespace SimWorld.Quests
{
    /// <summary>
    /// Drives one quest generation pass (RimWorld: <c>RimWorld.QuestGen</c>): builds a fresh <see cref="Quest"/>
    /// from a <see cref="QuestScriptDef"/>'s node tree, seeding the acceptance-offer expiry from
    /// <see cref="QuestScriptDef.expireDaysRange"/> before running the tree. <see cref="quest"/>/<see cref="slate"/>
    /// are only valid while <see cref="Generate"/> is on the stack — nodes read them through these ambient
    /// properties rather than as parameters.
    /// </summary>
    public static class QuestGen
    {
        [ThreadStatic] private static Quest? questInst;
        [ThreadStatic] private static Slate? slateInst;
        [ThreadStatic] private static int nextId;

        public static Quest quest => questInst ?? throw new InvalidOperationException("QuestGen.quest read outside a Generate() call.");

        public static Slate slate => slateInst ?? throw new InvalidOperationException("QuestGen.slate read outside a Generate() call.");

        public static Quest Generate(QuestScriptDef scriptDef, Slate providedSlate)
        {
            if (scriptDef == null) throw new ArgumentNullException(nameof(scriptDef));
            if (scriptDef.root == null) throw new ArgumentException("QuestScriptDef '" + scriptDef.defName + "' has no root node.", nameof(scriptDef));
            if (providedSlate == null) throw new ArgumentNullException(nameof(providedSlate));

            var generated = new Quest
            {
                id = ++nextId,
                root = scriptDef,
                name = scriptDef.nameTemplate ?? scriptDef.LabelCap,
                appearanceTick = Find.TickManager.TicksGame,
            };

            Quest? previousQuest = questInst;
            Slate? previousSlate = slateInst;
            questInst = generated;
            slateInst = providedSlate;
            try
            {
                float expireDays = Rand.Range(scriptDef.expireDaysRange);
                int expireTicks = expireDays > 0f ? (int)(expireDays * GenDate.TicksPerDay) : -1;
                slate.Set("expireDays", expireDays);
                slate.Set("expireTicks", expireTicks);
                generated.ticksUntilAcceptanceExpiry = expireTicks;

                generated.initiateSignal = GenerateNewSignal("initiate");
                slate.Set("inSignal", generated.initiateSignal);

                scriptDef.root.Run();
            }
            finally
            {
                questInst = previousQuest;
                slateInst = previousSlate;
            }
            return generated;
        }

        public static void AddPart(QuestPart part) => quest.AddPart(part);

        /// <summary>A signal tag unique to the quest being generated (RimWorld: <c>RimWorld.QuestGenUtility.HardcodedSignalWithQuestID</c>).
        /// Callers should use a distinct <paramref name="tag"/> per node kind within one quest so tags never collide.</summary>
        public static string GenerateNewSignal(string tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            return "quest" + quest.id.ToString(CultureInfo.InvariantCulture) + "." + tag;
        }

        /// <summary>Test hook: resets the per-thread id counter so generated quest ids are deterministic across tests.</summary>
        public static void ResetIdCounterForTests() => nextId = 0;
    }
}
