using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Combat
{
    /// <summary>One piece of cover between a shooter and a target: the chance it blocks the shot (RimWorld: <c>Verse.CoverInfo</c>).</summary>
    public readonly struct CoverInfo
    {
        public readonly float blockChance;

        /// <summary>The Thing actually providing this cover, when known (null for synthetic/test cover).</summary>
        public readonly Thing? thing;

        public CoverInfo(float blockChance, Thing? thing = null)
        {
            this.blockChance = GenMath.Clamp01(blockChance);
            this.thing = thing;
        }
    }

    /// <summary>
    /// Combines cover pieces into one block/pass chance (RimWorld: <c>Verse.CoverUtility</c>), and — once a
    /// map exists — finds what actually stands between a shooter and a target on it.
    /// </summary>
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

        // ---- geometry: what actually stands between shooter and target (RimWorld: Verse.CoverUtility) ----

        /// <summary>
        /// A full-fillage neighbour (a wall the target is standing beside) blocks a shot less often than one
        /// literally in the shot's path — it is cover, not an obstruction — so RimWorld caps it at 0.75
        /// rather than the 1.0 <see cref="Defs.FillCategory.Full"/> would otherwise imply (RimWorld:
        /// <c>ThingDef.BaseBlockChance</c>'s own <c>FillCategory.Full</c> case).
        /// </summary>
        public const float FullFillageCoverBlockChance = 0.75f;

        /// <summary>
        /// A cover-providing Thing's block chance before angle/distance weighting (RimWorld:
        /// <c>CoverUtility.BaseBlockChance</c>). <b>Deviation:</b> real RimWorld also zeroes this for an open
        /// door; this port's <see cref="Building.Door"/> has no open/closed state (see its own remarks) and
        /// always reports <see cref="Defs.FillCategory.Full"/>, so a Door here always counts as (capped)
        /// cover rather than sometimes none.
        /// </summary>
        public static float BaseBlockChance(Thing cover)
        {
            if (cover == null) throw new ArgumentNullException(nameof(cover));
            return cover.def.Fillage == Defs.FillCategory.Full ? FullFillageCoverBlockChance : cover.def.fillPercent;
        }

        /// <summary>The best (highest-fillPercent) cover-providing Thing standing in <paramref name="c"/>, or null (RimWorld: <c>Verse.Map.coverGrid</c>, computed live here instead of cached — cover is looked up once per shot, not per tick).</summary>
        public static Thing? GetCoverAt(Map.IntVec3 c, Map.Map map)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            Thing? best = null;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.def.Fillage == Defs.FillCategory.None) continue;
                if (best == null || t.def.fillPercent > best.def.fillPercent) best = t;
            }
            return best;
        }

        /// <summary>
        /// What stands in the 8 cells around <paramref name="targetCell"/> and how much each piece blocks a
        /// shot from <paramref name="shooterLoc"/> (RimWorld: <c>CoverUtility.CalculateCoverGiverSet</c> +
        /// <c>TryFindAdjustedCoverInCell</c>). Cover is about what the <i>target</i> can duck behind, not
        /// what lies along the shot's own path — a piece square-on between shooter and target (small angle
        /// off that line) blocks close to its full rating; one badly misaligned (beyond 65°) contributes
        /// nothing; and cover within point-blank range of the shooter is easier to shoot past regardless of
        /// angle. <paramref name="targetThing"/>, when given, is never itself counted as its own cover.
        /// </summary>
        public static List<CoverInfo> CalculateCoverGiverSet(Map.IntVec3 targetCell, Map.IntVec3 shooterLoc, Map.Map map, Thing? targetThing = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var result = new List<CoverInfo>();
            if (shooterLoc == targetCell) return result;

            for (int i = 0; i < Map.GenAdj.AdjacentCells.Length; i++)
            {
                Map.IntVec3 adjCell = targetCell + Map.GenAdj.AdjacentCells[i];
                if (!Map.GenGrid.InBounds(adjCell, map)) continue;
                if (TryAdjustedCoverInCell(shooterLoc, targetCell, targetThing, adjCell, map, out CoverInfo info)) result.Add(info);
            }
            return result;
        }

        private static bool TryAdjustedCoverInCell(Map.IntVec3 shooterLoc, Map.IntVec3 targetCell, Thing? targetThing, Map.IntVec3 adjCell, Map.Map map, out CoverInfo result)
        {
            result = default;
            Thing? cover = GetCoverAt(adjCell, map);
            if (cover == null || ReferenceEquals(cover, targetThing)) return false;

            float shooterAngle = Map.GenGeo.AngleFlat(shooterLoc - targetCell);
            float coverAngle = Map.GenGeo.AngleFlat(adjCell - targetCell);
            float angleDiff = Map.GenGeo.AngleDifferenceBetween(coverAngle, shooterAngle);
            if (!targetCell.AdjacentToCardinal(adjCell)) angleDiff *= 1.75f;

            float blockChance = BaseBlockChance(cover);
            if (angleDiff < 15f) { /* full value */ }
            else if (angleDiff < 27f) blockChance *= 0.8f;
            else if (angleDiff < 40f) blockChance *= 0.6f;
            else if (angleDiff < 52f) blockChance *= 0.4f;
            else if (angleDiff < 65f) blockChance *= 0.2f;
            else return false; // too far off the shooter's line to count as cover at all.

            float shooterToAdjDist = (shooterLoc - adjCell).LengthHorizontal;
            if (shooterToAdjDist < 1.9f) blockChance *= 0.3333f;
            else if (shooterToAdjDist < 2.9f) blockChance *= 0.66666f;

            result = new CoverInfo(blockChance, cover);
            return true;
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

        /// <summary>
        /// Builds the report for one shot of <paramref name="verb"/> from <paramref name="caster"/> at
        /// <paramref name="target"/>, <paramref name="distance"/> cells away. <paramref name="caster"/> is a
        /// <see cref="Thing"/>, not just a <see cref="Pawn"/> (RimWorld: <c>Verse.ShotReport.HitReportFor</c>
        /// takes a bare <c>Thing</c> too) so a turret can fire through this same path: a non-pawn caster has
        /// no shooting skill to raise to the power of distance, so its per-cell accuracy is a flat 1 — the
        /// same "no degradation" real RimWorld gives a turret, which only ever loses accuracy through its
        /// weapon's own <see cref="VerbProperties.AdjustedAccuracy"/> band falloff.
        /// </summary>
        public static ShotReport HitReportFor(Thing caster, Verb verb, Pawn target, float distance, IReadOnlyList<CoverInfo>? cover = null)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster));
            if (verb == null) throw new ArgumentNullException(nameof(verb));
            if (target == null) throw new ArgumentNullException(nameof(target));

            float accuracy = 1f;
            if (caster is Pawn casterPawn)
            {
                int shootingLevel = CombatStats.Skills.ShootingLevel(casterPawn);
                accuracy = CombatStats.ShootingAccuracyPawn(shootingLevel);
            }

            return new ShotReport
            {
                distance = distance,
                factorFromShootingAccuracy = (float)Math.Pow(accuracy, Math.Max(0f, distance)),
                factorFromEquipment = verb.verbProps.AdjustedAccuracy(distance),
                factorFromTargetSize = Math.Max(0.5f, target.BodySize),
                factorFromWeather = WeatherAccuracyFactor(caster, target),
                forcedMissRadius = verb.verbProps.forcedMissRadius,
                passCoverChance = CoverUtility.PassChance(cover),
            };
        }

        /// <summary>
        /// The weather's penalty to this shot (RimWorld: <c>ShotReport.HitReportFor</c> reads
        /// <c>Map.weatherManager.CurWeatherAccuracyMultiplier</c> when either end of the shot is under open
        /// sky, and leaves a shot fired entirely indoors alone). <see cref="factorFromWeather"/> was written
        /// with this in mind and had defaulted to 1 because no weather module existed; clear weather is still
        /// exactly 1, so nothing that was tuned against fair-weather shooting moves.
        /// </summary>
        private static float WeatherAccuracyFactor(Thing caster, Pawn target)
        {
            Map.Map? map = caster.Map;
            if (map == null) return 1f;
            bool casterSheltered = Map.GenGrid.Roofed(caster.Position, map);
            bool targetSheltered = !ReferenceEquals(target.Map, map) || Map.GenGrid.Roofed(target.Position, map);
            if (casterSheltered && targetSheltered) return 1f;
            return map.weatherManager.CurWeatherAccuracyMultiplier;
        }
    }
}
