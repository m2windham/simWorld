using System;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// Mirrors a hediff's stage as the thought's stage (RimWorld: <c>ThoughtWorker_Hediff</c>); a thought with
    /// fewer stages than the hediff saturates at its last one.
    /// </summary>
    public class ThoughtWorker_Hediff : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (def.hediff == null) return ThoughtState.Inactive;
            Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def.hediff);
            if (hediff == null || !hediff.Visible) return ThoughtState.Inactive;
            int stage = Math.Max(0, hediff.CurStageIndex);
            if (stage >= def.stages.Count) stage = def.stages.Count - 1;
            return ThoughtState.ActiveAtStage(stage);
        }
    }

    /// <summary>Four bands of pain from the hediff set's total (RimWorld: <c>ThoughtWorker_Pain</c>).</summary>
    public class ThoughtWorker_Pain : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            float pain = pawn.health.hediffSet.PainTotal;
            if (pain < 0.0001f) return ThoughtState.Inactive;
            if (pain < 0.15f) return ThoughtState.ActiveAtStage(0);
            if (pain < 0.4f) return ThoughtState.ActiveAtStage(1);
            if (pain < 0.8f) return ThoughtState.ActiveAtStage(2);
            return ThoughtState.ActiveAtStage(3);
        }
    }

    /// <summary>Active while any visible hediff is flagged as making the pawn feel sick (RimWorld: <c>ThoughtWorker_Sick</c>).</summary>
    public class ThoughtWorker_Sick : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            return pawn.health.hediffSet.AnyHediffMakesSickThought ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }
}
