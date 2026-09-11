using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Health
{
    /// <summary>
    /// Flame damage: a real burn injury through the ordinary health pipeline, and then the victim itself
    /// catches fire (RimWorld: <c>RimWorld.DamageWorker_Flame</c>). Named on the <c>Flame</c> DamageDef in
    /// <c>Damages_Fire.xml</c>.
    /// <para/>
    /// This is the one path by which something in this codebase can set something else alight without
    /// already being a <see cref="Fire"/>, so it is what keeps the fire system reachable: an incendiary IED
    /// (<c>TrapIEDIncendiary</c>) throws Flame damage through <c>GenExplosion</c>, every pawn it catches
    /// starts burning, and the fire spreads from there into whatever wooden building the pawn is standing in.
    /// <para/>
    /// <b>Left out:</b> RimWorld also overrides <c>ExplosionAffectCell</c> so a flame explosion sets the
    /// <i>cells</i> in its blast alight, not only the pawns. This port's <c>Combat.GenExplosion</c> routes
    /// non-pawn targets straight to <c>Thing.TakeDamage</c> without consulting the DamageDef's worker at all,
    /// so there is no hook to override — wiring it needs one line in <c>GenExplosion.ApplyTo</c>
    /// (<c>FireUtility.TryStartFireIn(cell, map, FireUtility.FireSizeOnIgnite)</c> when the def is Flame),
    /// deliberately not taken here because that file belongs to no lane this batch. See this module's report.
    /// </summary>
    public class DamageWorker_Flame : DamageWorker_AddInjury
    {
        public override DamageResult Apply(DamageInfo dinfo, Pawn victim)
        {
            DamageResult result = base.Apply(dinfo, victim);
            if (!victim.Dead && !victim.Destroyed)
            {
                victim.TryAttachFire(Rand.Range(FireUtility.FireSizeOnIgnite));
            }
            return result;
        }
    }
}
