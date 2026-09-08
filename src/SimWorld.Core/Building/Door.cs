namespace SimWorld.Building
{
    /// <summary>
    /// A doorway (RimWorld: <c>Verse.Building_Door</c>). <b>Deviation:</b> real RimWorld doors swing open for
    /// an approaching pawn and block everyone else meanwhile; this pass's content sets a Door's
    /// <c>passability</c> to <c>Standable</c> outright (always open, no swing state, no faction-allowed
    /// check) — that mechanic is out of this pass's scope. What a Door *does* model: it still blocks
    /// <see cref="RoomTracker"/>'s room flood fill exactly like a wall (its <c>fillPercent</c> is 1, same as
    /// a wall), so a doorway still separates two <see cref="Room"/>s — <see cref="RoomGroup"/> exists
    /// precisely to re-merge rooms connected only by a Door back into one thermal unit, matching real
    /// RimWorld's own Room/RoomGroup split.
    /// </summary>
    public class Door : Building
    {
    }
}
