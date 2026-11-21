using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// True for a pawn of the player's own faction (RimWorld: <c>RimWorld.ThinkNode_ConditionalOfPlayerFaction</c>,
    /// <c>pawn.Faction == Faction.OfPlayer</c>). Read here as <c>faction.def.isPlayer</c>, the same test
    /// <see cref="Needs.Need_Seeker"/> uses for "one of ours". RimWorld's animal tree hangs a tame animal's
    /// critical needs off it, <see cref="JobGiver_SeekSafeTemperature"/> among them, so a wild animal never
    /// seeks safe air. Its own file (CLAUDE.md: add a file rather than edit a shared one).
    /// </summary>
    public sealed class ThinkNode_ConditionalOfPlayerFaction : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn) => pawn.Faction != null && pawn.Faction.def.isPlayer;
    }
}
