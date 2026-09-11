using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Filth
{
    /// <summary>
    /// Blood on the floor from a wound that is still open (RimWorld:
    /// <c>Pawn_HealthTracker.DropBloodFilth</c>, rolled inside the same bleed branch of
    /// <c>Pawn_HealthTracker.HealthTick</c> that this port calls it from).
    /// <para/>
    /// The roll lives here rather than in <c>Health/Pawn_HealthTracker.cs</c> so that the edit to that shared
    /// file is a single call (CLAUDE.md: add a file rather than edit a shared one, and keep an unavoidable
    /// edit minimal). Health owns when a pawn is bleeding; filth owns what that leaves on the ground.
    /// </summary>
    public static class BloodFilthUtility
    {
        /// <summary>
        /// Chance per bleed interval, per unit of bleed rate, that a drop lands. Heavier bleeding leaves more
        /// blood, which is RimWorld's own relation (its roll scales with <c>BleedRateTotal</c> and body size);
        /// the constant itself is SimWorld's own and unsourced, so the test pins the trend — a badly bleeding
        /// pawn leaves more blood than a lightly bleeding one, and a pawn that is not bleeding leaves none —
        /// rather than this number (CLAUDE.md).
        /// </summary>
        public const float BloodFilthChancePerBleedRate = 0.35f;

        /// <summary>Called once per bleed interval for a pawn whose bleed rate has passed
        /// <c>HealthTuning.MinBleedRateToBleed</c>. A pawn who is not on a map — in a caravan, in a pod — has
        /// no floor to bleed onto, and that is a silent no-op rather than an error.</summary>
        public static void DropBloodFilth(Pawn pawn, float bleedRate)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return;
            if (!pawn.RaceProps.IsFlesh || bleedRate <= 0f) return;

            // Scaled by body size the way RimWorld's is: a downed muffalo makes a bigger mess than a rat.
            float chance = bleedRate * pawn.BodySize * BloodFilthChancePerBleedRate;
            if (!Rand.Chance(chance)) return;

            FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, FilthDefOf.Filth_Blood, pawn.Label);
        }
    }
}
