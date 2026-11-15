using SimWorld.Sim;

namespace SimWorld.God.View
{
    public sealed partial class GodViewSnapshot
    {
        /// <summary>
        /// The work tab for the settlement on <paramref name="settlementTile"/> — see
        /// <see cref="WorkPriorityTable"/>. Null when no settlement sits on that tile (including before a world
        /// exists), the same contract as <see cref="CitizensOf"/>. A narrow query, asked for one settlement at a
        /// time, for the reason <see cref="CitizensOf"/> gives.
        /// </summary>
        public static WorkPriorityTable? WorkPrioritiesOf(int settlementTile)
        {
            World.Settlement? settlement = AttentionManager.SettlementAt(settlementTile);
            return settlement == null ? null : WorkPriorityTable.Of(settlement, Find.TickManager.TicksGame);
        }
    }
}
