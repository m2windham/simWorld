using SimWorld.Defs;

namespace SimWorld.Health
{
    /// <summary>
    /// The damage a surgeon deals when an operation goes wrong. Its own <c>[DefOf]</c> class rather than a
    /// line in <see cref="DamageDefOf"/>, per <c>CLAUDE.md</c> — <c>DefOfHelper</c> scans every
    /// <c>[DefOf]</c> type, so the binding is identical and cannot collide with another lane's edit.
    /// </summary>
    [DefOf]
    public static class SurgeryDamageDefOf
    {
        /// <summary>A cut that is not violence: see <c>Damages_Surgery.xml</c> for why the distinction had to
        /// be made the moment anything read <see cref="DamageDef.externalViolence"/>.</summary>
        public static DamageDef SurgicalCut = null!;
    }
}
