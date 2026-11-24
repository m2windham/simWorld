using SimWorld.Research;

namespace SimWorld.Pawns
{
    public partial class PawnKindDef
    {
        /// <summary>
        /// The era whose arms this kind carries: a Medieval man-at-arms, a Classical legionary. Null for a kind
        /// of no particular era (the stone-age raiders, the outlanders' gunners). SimWorld's own field; RimWorld
        /// has no eras and gives each faction a fixed roster. A raiding faction's roster is narrowed to the kinds
        /// of the latest era the world has reached (<see cref="Factions.FactionAdvancement.CurrentRoster"/>), so
        /// the same points buy the arms of the age — and, since a kind's <see cref="combatPower"/> is its price,
        /// fewer, stronger raiders as the eras turn.
        /// </summary>
        public EraDef? era;
    }
}
