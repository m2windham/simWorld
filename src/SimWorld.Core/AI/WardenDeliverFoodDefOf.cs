using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>
    /// The def <see cref="WorkGiver_Warden_DeliverFood"/> binds by name. Its own class rather than another
    /// field on <see cref="JobDefOf"/>: <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a
    /// module's bindings need no edit to a file another lane may also be editing (CLAUDE.md — "add a file
    /// rather than edit a shared one"). <see cref="CorpseWorkDefOf"/>, <see cref="HuntingDefOf"/> and
    /// <see cref="RepairJobDefOf"/> already do exactly this.
    /// </summary>
    [DefOf]
    public static class WardenDeliverFoodDefOf
    {
        /// <summary>Carry one meal to a prisoner and put it down where they are, rather than feeding them
        /// (RimWorld: the <c>DeliverFood</c> JobDef, driver <c>RimWorld.JobDriver_FoodDeliver</c>).</summary>
        public static JobDef DeliverFood = null!;
    }
}
