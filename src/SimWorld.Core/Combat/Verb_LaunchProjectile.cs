using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

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
        /// <summary>
        /// Cover between caster and target for the next shot. Overrides <see cref="DefaultCover"/> — set
        /// this to inject synthetic cover (as this module's own tests still do) without a map at all. Left
        /// null, real geometry now applies automatically once caster and target share a spawned map (see
        /// <see cref="DefaultCover"/>): before the map/region module existed this always defaulted to no
        /// cover, which is still exactly what happens for two unspawned pawns (the existing combat tests'
        /// own setup), so nothing already passing changes.
        /// </summary>
        public Func<Thing, Pawn, IReadOnlyList<CoverInfo>>? CoverLookup;

        public ShotResult? LastShot { get; private set; }

        public Verb_LaunchProjectile(Thing caster, VerbProperties verbProps) : base(caster, verbProps)
        {
        }

        protected override void TryCastShot(Pawn target, float distance)
        {
            IReadOnlyList<CoverInfo> cover = CoverLookup != null ? CoverLookup(caster, target) : DefaultCover(target);
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

        /// <summary>
        /// Real cover, straight off the map's own grids (RimWorld: the map/AI-side caller that feeds
        /// <c>Verb.TryStartCastOn</c> always resolves <c>CoverUtility.CalculateCoverGiverSet</c> itself —
        /// this port folds that resolution in here instead, since nothing else in this pass owns "the thing
        /// that fires this shot" the way a caller-supplied cover list would need). Empty when caster or
        /// target is unspawned, or the two are on different maps — the state every existing combat test's
        /// pawns are already in, so this is a strict no-op there.
        /// </summary>
        private IReadOnlyList<CoverInfo> DefaultCover(Pawn target)
        {
            Map.Map? map = caster.Map;
            if (map == null || !ReferenceEquals(target.Map, map)) return Array.Empty<CoverInfo>();
            return CoverUtility.CalculateCoverGiverSet(target.Position, caster.Position, map, target);
        }
    }
}
