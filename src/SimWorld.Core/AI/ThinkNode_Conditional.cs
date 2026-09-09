using System.Collections.Generic;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Guards its subnodes behind a condition (RimWorld: <c>Verse.AI.ThinkNode_Conditional</c>). When
    /// <see cref="Satisfied"/> is false, this node contributes nothing and the enclosing
    /// <see cref="ThinkNode_Priority"/> falls through to its next child — used for the needs tier so a
    /// pawn who is not hungry never even asks <see cref="JobGiver_GetFood"/> to look.
    /// </summary>
    public abstract class ThinkNode_Conditional : ThinkNode
    {
        public List<ThinkNode> subNodes = new List<ThinkNode>();

        protected abstract bool Satisfied(Pawn pawn);

        public override ThinkResult TryIssueJobPackage(Pawn pawn)
        {
            if (!Satisfied(pawn)) return ThinkResult.NoJob;
            for (int i = 0; i < subNodes.Count; i++)
            {
                ThinkResult result = subNodes[i].TryIssueJobPackage(pawn);
                if (result.IsValid) return result;
            }
            return ThinkResult.NoJob;
        }
    }

    /// <summary>
    /// True while a mental state is active. Placed first in the humanlike tree so it pre-empts needs, orders
    /// and work entirely (RimWorld: mental states run their own substituted think tree, replacing the whole
    /// evaluation rather than sitting inside it as a guarded branch — see this module's report for the
    /// deliberate simplification here).
    /// </summary>
    public sealed class ThinkNode_ConditionalInMentalState : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.InMentalState;
    }

    /// <summary>True while the pawn's food need has fallen below "fed".</summary>
    public sealed class ThinkNode_ConditionalHungry : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.needs.food != null && pawn.needs.food.CurCategory != HungerCategory.Fed;
    }

    /// <summary>True while the pawn's rest need has fallen below "rested" and it is not already resting.</summary>
    public sealed class ThinkNode_ConditionalTired : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) =>
            pawn.needs.rest != null && !pawn.Asleep && pawn.needs.rest.CurCategory != RestCategory.Rested;
    }

    /// <summary>
    /// True while this animal is marked angry at a would-be tamer after a failed taming roll (system:
    /// ai.animals). Placed first in the animal think tree, mirroring
    /// <see cref="ThinkNode_ConditionalInMentalState"/>'s position in the humanlike one — see
    /// <see cref="MindState.Pawn_MindState.angryAt"/>'s own doc for why this is the trimmed stand-in for
    /// RimWorld's manhunter mental state.
    /// </summary>
    public sealed class ThinkNode_ConditionalAngryAtHandler : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) =>
            pawn.mindState.angryAt != null && Find.TickManager.TicksGame < pawn.mindState.angryUntilTick;
    }
}
