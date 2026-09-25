using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// True while the pawn is starving (RimWorld: <c>RimWorld.ThinkNode_ConditionalStarving</c>). The one need
    /// RimWorld's humanlike tree lets outrank emergency work: a starving pawn eats first, a merely hungry one
    /// tends the bleeding first. It sits directly above the emergency-work tier in
    /// <c>ThinkTrees_Humanlike.xml</c> for that reason and for no other — the ordinary hunger tier below still
    /// feeds everyone else.
    /// <para/>
    /// Its own file rather than a class appended to <c>ThinkNode_Conditional.cs</c> (CLAUDE.md: add a file
    /// rather than edit a shared one).
    /// </summary>
    public sealed class ThinkNode_ConditionalStarving : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.needs.food != null && pawn.needs.food.Starving;
    }
}
