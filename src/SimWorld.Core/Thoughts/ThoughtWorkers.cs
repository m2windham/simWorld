using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Social;

namespace SimWorld.Thoughts
{
    /// <summary>Always on; used for trait-flavour thoughts gated by requiredTraits.</summary>
    public class ThoughtWorker_AlwaysActive : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn) => ThoughtState.ActiveDefault;
    }

    /// <summary>
    /// Active while the pawn has at least one <see cref="DirectPawnRelation"/> of <see
    /// cref="ThoughtDef.requiredDirectRelation"/>'s kind with anyone (RimWorld: the spatial "a friend is
    /// nearby"/"a rival is present" situational thoughts, translated to this module's own terms — see that
    /// field's own doc comment for why). One generic worker driving any number of content-only ThoughtDefs,
    /// the same shape <see cref="ThoughtWorker_Hediff"/> already uses for <see cref="ThoughtDef.hediff"/>.
    /// </summary>
    public class ThoughtWorker_HasDirectRelation : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            PawnRelationDef? relation = def.requiredDirectRelation;
            if (relation == null) return ThoughtState.Inactive;
            var relations = pawn.relations.directRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                if (relations[i].def == relation) return ThoughtState.ActiveDefault;
            }
            return ThoughtState.Inactive;
        }
    }

    /// <summary>Hungry / ravenously hungry / starving from the food need.</summary>
    public class ThoughtWorker_NeedFood : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            Need_Food? food = pawn.needs.food;
            if (food == null) return ThoughtState.Inactive;
            switch (food.CurCategory)
            {
                case HungerCategory.Hungry: return ThoughtState.ActiveAtStage(0);
                case HungerCategory.UrgentlyHungry: return ThoughtState.ActiveAtStage(1);
                case HungerCategory.Starving: return ThoughtState.ActiveAtStage(2);
                default: return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>Tired / very tired / exhausted from the rest need.</summary>
    public class ThoughtWorker_NeedRest : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            Need_Rest? rest = pawn.needs.rest;
            if (rest == null) return ThoughtState.Inactive;
            switch (rest.CurCategory)
            {
                case RestCategory.Tired: return ThoughtState.ActiveAtStage(0);
                case RestCategory.VeryTired: return ThoughtState.ActiveAtStage(1);
                case RestCategory.Exhausted: return ThoughtState.ActiveAtStage(2);
                default: return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>Recreation deprivation from the joy need; positive stages when well entertained.</summary>
    public class ThoughtWorker_NeedJoy : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            Need_Joy? joy = pawn.needs.joy;
            if (joy == null) return ThoughtState.Inactive;
            switch (joy.CurCategory)
            {
                case JoyCategory.Empty: return ThoughtState.ActiveAtStage(0);
                case JoyCategory.VeryLow: return ThoughtState.ActiveAtStage(1);
                case JoyCategory.Low: return ThoughtState.ActiveAtStage(2);
                case JoyCategory.High: return ThoughtState.ActiveAtStage(3);
                case JoyCategory.Extreme: return ThoughtState.ActiveAtStage(4);
                default: return ThoughtState.Inactive;
            }
        }
    }

    /// <summary>Active while the pawn is in a mental state whose Def blocks normal thoughts.</summary>
    public class ThoughtWorker_MentalState : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            return pawn.InMentalState ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }
}
