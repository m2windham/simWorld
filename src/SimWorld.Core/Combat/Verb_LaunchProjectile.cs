using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Combat
{
    /// <summary>What one ranged shot did (RimWorld: the outcome of <c>Verb_LaunchProjectile.TryCastShot</c>).</summary>
    public sealed class ShotResult
    {
        public bool Hit { get; }
        public ShotReport Report { get; }
        public DamageResult? DamageResult { get; }
        public BodyPartRecord? HitPart { get; }

        public ShotResult(bool hit, ShotReport report, DamageResult? damageResult, BodyPartRecord? hitPart)
        {
            Hit = hit;
            Report = report;
            DamageResult = damageResult;
            HitPart = hitPart;
        }
    }

    /// <summary>
    /// A gun or bow's ranged attack (RimWorld: <c>Verse.Verb_LaunchProjectile</c> / <c>Verb_Shoot</c>). Each
    /// shot rolls <see cref="ShotReport.TotalEstimatedHitChance"/>; a hit applies the projectile's damage
    /// straight to the target — no flight is simulated, since there is no map for a projectile to fly over yet.
    /// A miss that RimWorld might redirect into cover or a bystander is simply recorded as a miss here.
    /// </summary>
    public class Verb_LaunchProjectile : Verb
    {
        /// <summary>Cover between caster and target for the next shot; the map/cover system supplies this later. No cover by default.</summary>
        public Func<Pawn, Pawn, IReadOnlyList<CoverInfo>>? CoverLookup;

        public ShotResult? LastShot { get; private set; }

        public Verb_LaunchProjectile(Pawn caster, VerbProperties verbProps) : base(caster, verbProps)
        {
        }

        protected override void TryCastShot(Pawn target, float distance)
        {
            IReadOnlyList<CoverInfo> cover = CoverLookup?.Invoke(caster, target) ?? Array.Empty<CoverInfo>();
            ShotReport report = ShotReport.HitReportFor(caster, this, target, distance, cover);
            bool hit = Rand.Chance(report.TotalEstimatedHitChance);

            DamageResult? damageResult = null;
            BodyPartRecord? hitPart = null;
            if (hit)
            {
                ProjectileProperties? projectile = verbProps.defaultProjectile?.projectile;
                DamageDef? damageDef = projectile?.damageDef;
                if (damageDef != null)
                {
                    hitPart = target.health.hediffSet.GetRandomNotMissingPart(damageDef, BodyPartHeight.Undefined, BodyPartDepth.Undefined, Rand.Current);
                    var dinfo = new DamageInfo(damageDef, projectile!.damageAmountBase, projectile.armorPenetrationBase, caster, hitPart);
                    damageResult = damageDef.Worker.Apply(dinfo, target);
                }
            }
            LastShot = new ShotResult(hit, report, damageResult, hitPart);
        }
    }
}
