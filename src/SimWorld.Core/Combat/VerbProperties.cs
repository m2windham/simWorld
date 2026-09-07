using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Combat
{
    /// <summary>Distance band a shot or a weapon's accuracy falls into (RimWorld: the four accuracy bands on <c>Verse.VerbProperties</c>).</summary>
    public enum RangeCategory
    {
        Touch,
        Short,
        Medium,
        Long,
    }

    /// <summary>
    /// One attack a Thing can perform (RimWorld: <c>Verse.VerbProperties</c>): a ranged shot (<see cref="Verb_LaunchProjectile"/>)
    /// or a melee swing (<see cref="Verb_MeleeAttack"/>), picked by <see cref="verbClass"/>. A ThingDef's
    /// <c>verbs</c> list holds these directly for ranged weapons; melee attacks are assembled at runtime by
    /// <see cref="MeleeVerbUtility"/> from a <see cref="Tool"/> and a <see cref="ManeuverDef"/> template.
    /// </summary>
    public class VerbProperties
    {
        /// <summary>RimWorld's fixed accuracy-band boundaries, in cells.</summary>
        public const float TouchDistance = 3f;
        public const float ShortDistance = 12f;
        public const float MediumDistance = 25f;
        public const float LongDistance = 40f;

        public Type verbClass = typeof(Verb_LaunchProjectile);
        public string? label;
        public float range = 90f;
        public float minRange;

        /// <summary>Seconds of aiming before the first shot of a burst; see <see cref="Verb.SecondsToTicks"/>.</summary>
        public float warmupTime;

        /// <summary>Seconds of recovery after the burst ends before the verb is usable again.</summary>
        public float defaultCooldownTime = 1f;

        public float accuracyTouch = 0.96f;
        public float accuracyShort = 0.92f;
        public float accuracyMedium = 0.82f;
        public float accuracyLong = 0.65f;

        public int burstShotCount = 1;
        public int ticksBetweenBurstShots = 15;
        public float forcedMissRadius;

        /// <summary>Projectile ThingDef fired by <see cref="Verb_LaunchProjectile"/>.</summary>
        public ThingDef? defaultProjectile;

        public float meleeDamageBaseAmount;
        public DamageDef? meleeDamageDef;
        public float meleeArmorPenetrationBase;

        public bool IsMeleeAttack => typeof(Verb_MeleeAttack).IsAssignableFrom(verbClass);

        /// <summary>Which of the four fixed bands a distance falls in (Touch ≤3, Short ≤12, Medium ≤25, else Long).</summary>
        public static RangeCategory GetRangeCategory(float distance)
        {
            if (distance <= TouchDistance) return RangeCategory.Touch;
            if (distance <= ShortDistance) return RangeCategory.Short;
            if (distance <= MediumDistance) return RangeCategory.Medium;
            return RangeCategory.Long;
        }

        /// <summary>
        /// Accuracy at a continuous distance: linear interpolation between the four band accuracies at
        /// RimWorld's band distances (3/12/25/40 cells), clamped to the end accuracies beyond them.
        /// <paramref name="equipment"/> is a hook for future per-instance quality; no Thing module exists yet
        /// so it is always null and unused.
        /// </summary>
        public float AdjustedAccuracy(float distance, object? equipment = null)
        {
            if (distance <= TouchDistance) return accuracyTouch;
            if (distance <= ShortDistance) return Lerp(accuracyTouch, accuracyShort, TouchDistance, ShortDistance, distance);
            if (distance <= MediumDistance) return Lerp(accuracyShort, accuracyMedium, ShortDistance, MediumDistance, distance);
            if (distance <= LongDistance) return Lerp(accuracyMedium, accuracyLong, MediumDistance, LongDistance, distance);
            return accuracyLong;
        }

        private static float Lerp(float a, float b, float x0, float x1, float x)
        {
            float t = x1 > x0 ? (x - x0) / (x1 - x0) : 0f;
            return a + (b - a) * t;
        }

        /// <summary>Base melee armor penetration; equipment quality scaling is a future hook (no Thing module yet).</summary>
        public float AdjustedArmorPenetration => meleeArmorPenetrationBase;

        /// <summary>Base melee damage amount; equipment quality scaling is a future hook (no Thing module yet).</summary>
        public float AdjustedMeleeDamageAmount(object? equipment = null) => meleeDamageBaseAmount;

        public IEnumerable<string> ConfigErrors()
        {
            if (verbClass == null || !typeof(Verb).IsAssignableFrom(verbClass)) yield return "verbClass must derive from Verb.";
            if (range < minRange) yield return "range is less than minRange.";
            if (burstShotCount < 1) yield return "burstShotCount must be at least 1.";
            if (ticksBetweenBurstShots < 1) yield return "ticksBetweenBurstShots must be at least 1.";
            if (IsMeleeAttack)
            {
                if (meleeDamageDef == null) yield return "melee verb has no meleeDamageDef.";
            }
            else if (defaultProjectile == null)
            {
                yield return "ranged verb has no defaultProjectile.";
            }
        }
    }

    /// <summary>Flight and impact tunables for a projectile ThingDef (RimWorld: <c>Verse.ProjectileProperties</c>). No flying projectile is simulated: a hit resolves instantly against the target.</summary>
    public class ProjectileProperties
    {
        public DamageDef? damageDef;
        public float damageAmountBase;
        public float armorPenetrationBase;
        public float speed = 20f;
        public float stoppingPower = 1f;

        public IEnumerable<string> ConfigErrors()
        {
            if (damageDef == null) yield return "projectile has no damageDef.";
            if (damageAmountBase <= 0f) yield return "projectile damageAmountBase must be positive.";
        }
    }
}
