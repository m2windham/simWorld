using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Things
{
    /// <summary>The damage a pick deals to rock (RimWorld: <c>DamageDefOf.Mining</c>), in its own
    /// <c>[DefOf]</c> class rather than a line in <see cref="DamageDefOf"/> — see
    /// <c>Health/SurgeryDamageDefOf.cs</c> for the same call and its reason.</summary>
    [DefOf]
    public static class MiningDamageDefOf
    {
        /// <summary>Damage to rock, not to a body. See <c>Damages_Mining.xml</c>.</summary>
        public static DamageDef Mining = null!;
    }
}
