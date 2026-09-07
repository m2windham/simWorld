using System;
using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.Combat
{
    /// <summary>One piece of cover between a shooter and a target: the chance it blocks the shot (RimWorld: <c>Verse.CoverInfo</c>). Map geometry lands with the map module; this is the pure math.</summary>
    public readonly struct CoverInfo
    {
        public readonly float blockChance;

        public CoverInfo(float blockChance)
        {
            this.blockChance = GenMath.Clamp01(blockChance);
        }
    }

    /// <summary>Combines cover pieces into one block/pass chance (RimWorld: <c>Verse.CoverUtility</c>).</summary>
    public static class CoverUtility
    {
        /// <summary>Probability that at least one piece of cover blocks the shot.</summary>
        public static float CalculateOverallBlockChance(IReadOnlyList<CoverInfo>? covers) => 1f - PassChance(covers);

        /// <summary>Probability that nothing blocks the shot: Π(1 − blockChance).</summary>
        public static float PassChance(IReadOnlyList<CoverInfo>? covers)
        {
            if (covers == null || covers.Count == 0) return 1f;
            float pass = 1f;
            for (int i = 0; i < covers.Count; i++) pass *= 1f - covers[i].blockChance;
            return pass;
        }
    }

    /// <summary>
    /// The odds one ranged shot connects (RimWorld: <c>Verse.ShotReport</c>): the shooter's skill-driven
    /// per-cell accuracy raised to the power of distance, the weapon's own accuracy at that range, the
    /// target's size, and cover. <see cref="TotalEstimatedHitChance"/> is what actually gets rolled against.
    /// </summary>
    public sealed class ShotReport
    {
        /// <summary>RimWorld's per-shot floor: even a terrible shot always has at least this much of a chance.</summary>
        public const float MinChance = 0.0201f;

        public float distance;
        public float factorFromShootingAccuracy;
        public float factorFromEquipment;
        public float factorFromTargetSize = 1f;
        public float factorFromWeather = 1f;
        public float factorFromLighting = 1f;
        public float forcedMissRadius;
        public float passCoverChance = 1f;

        /// <summary>Chance to hit a standard (body size 1) target before cover, floored at <see cref="MinChance"/>.</summary>
        public float AimOnTargetChance_StandardTarget =>
            GenMath.Clamp(factorFromShootingAccuracy * factorFromEquipment * factorFromWeather * factorFromLighting, MinChance, 1f);

        /// <summary>The standard-target chance scaled by how big the actual target is.</summary>
        public float AimOnTargetChance => GenMath.Clamp01(AimOnTargetChance_StandardTarget * factorFromTargetSize);

        /// <summary>Chance nothing between shooter and target blocks the shot.</summary>
        public float PassCoverChance => passCoverChance;

        /// <summary>On-target chance times the chance nothing blocks it: the number actually rolled against.</summary>
        public float TotalEstimatedHitChance => GenMath.Clamp01(AimOnTargetChance * PassCoverChance);

        /// <summary>Builds the report for one shot of <paramref name="verb"/> from <paramref name="caster"/> at <paramref name="target"/>, <paramref name="distance"/> cells away.</summary>
        public static ShotReport HitReportFor(Pawn caster, Verb verb, Pawn target, float distance, IReadOnlyList<CoverInfo>? cover = null)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster));
            if (verb == null) throw new ArgumentNullException(nameof(verb));
            if (target == null) throw new ArgumentNullException(nameof(target));

            int shootingLevel = CombatStats.Skills.ShootingLevel(caster);
            float accuracy = CombatStats.ShootingAccuracyPawn(shootingLevel);

            return new ShotReport
            {
                distance = distance,
                factorFromShootingAccuracy = (float)Math.Pow(accuracy, Math.Max(0f, distance)),
                factorFromEquipment = verb.verbProps.AdjustedAccuracy(distance),
                factorFromTargetSize = Math.Max(0.5f, target.BodySize),
                forcedMissRadius = verb.verbProps.forcedMissRadius,
                passCoverChance = CoverUtility.PassChance(cover),
            };
        }
    }
}
