using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Thoughts;

namespace SimWorld.God
{
    /// <summary>
    /// The situational half of an edict's mood consequence (<see cref="EdictDef.moodThought"/>): active for a
    /// pawn exactly while some active edict names this <see cref="ThoughtDef"/> as its <c>moodThought</c> and
    /// its <see cref="EdictWorker.AppliesTo"/> applies to that pawn. Scans <see cref="EdictDef"/> rather than
    /// storing a back-reference, the same direction <see cref="Thoughts.ThoughtWorker_Hediff"/> reads
    /// <c>def.hediff</c> instead of a hediff pointing back at its thought — the def that adds the consequence
    /// (<see cref="EdictDef"/>) is the one that names it, not the other way around.
    /// <para/>
    /// Because <see cref="Thoughts.SituationalThoughtHandler"/> already recomputes this on its own cadence, an
    /// edict's mood cost appears the instant <see cref="GodManager.Activate"/> runs and disappears the instant
    /// <see cref="GodManager.Deactivate"/> does — no explicit per-pawn grant or removal call needed, and
    /// nothing left behind afterward, matching the think-tree half's own "leaves no trace" rule.
    /// </summary>
    public sealed class ThoughtWorker_UnderEdict : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            var edicts = DefDatabase<EdictDef>.AllDefsListForReading;
            for (int i = 0; i < edicts.Count; i++)
            {
                EdictDef edict = edicts[i];
                if (edict.moodThought != def) continue;
                if (!Find.God.IsActive(edict)) continue;
                if (edict.Worker.AppliesTo(pawn)) return ThoughtState.ActiveDefault;
            }
            return ThoughtState.Inactive;
        }
    }
}
