using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Health
{
    /// <summary>
    /// How far along a hypothermia or heatstroke is, as its stage index (RimWorld:
    /// <c>Verse.TemperatureInjuryStage</c>, a byte enum with these six members in this order). The hediffs'
    /// own stages line up with the first five; <see cref="Deadly"/> is never a stage index.
    /// </summary>
    public enum TemperatureInjuryStage : byte
    {
        Hidden,
        Initial,
        Minor,
        Serious,
        Extreme,
        Deadly,
    }

    [DefOf]
    public static class TemperatureDefOf
    {
        public static HediffDef Hypothermia = null!;
        public static HediffDef Heatstroke = null!;
        public static DamageDef Frostbite = null!;
        public static StatDef ComfyTemperatureMin = null!;
        public static StatDef ComfyTemperatureMax = null!;
    }

    /// <summary>
    /// Hypothermia and heatstroke driven by the air around a pawn (RimWorld: <c>HediffGiver_Hypothermia</c>
    /// and <c>HediffGiver_Heat</c>, both in the <c>OrganicStandard</c> hediff-giver set, whose
    /// <c>OnIntervalPassed</c> the health tracker calls every 60 ticks). Mechanics and constants were read
    /// from the decompiled RimWorld source (josh-m/RW-Decompile, B18-era: <c>HediffGiver_Hypothermia</c>,
    /// <c>HediffGiver_Heat</c>, <c>GenTemperature.SafeTemperatureRange</c>) and agree with the wiki's
    /// Ailments page, which gives the same rates per 60 ticks.
    /// <list type="bullet">
    /// <item><description>Below the safe minimum (comfortable minimum − 10 °C) hypothermia rises by
    /// 0.0000645 per degree under it, at least 0.00075 per interval.</description></item>
    /// <item><description>Above the safe maximum (comfortable maximum + 10 °C) heatstroke rises by
    /// 0.0000645 per <i>adjusted</i> degree over it (the excess bent through
    /// <see cref="HeatOverageCurve"/>), at least 0.000375 per interval.</description></item>
    /// <item><description>Once back inside the comfortable bound it recovers by 2.7% of its severity per
    /// interval, clamped to 0.0015..0.015.</description></item>
    /// <item><description>Hypothermia above 0.37 in air below 0 °C rolls 0.025 × severity per interval for
    /// frostbite on a frostbite-vulnerable part, for half that part's hit points.</description></item>
    /// </list>
    /// Not ported: the insectoid hypothermic slowdown (no insectoids), RimWorld's burn damage from air 150 °C
    /// past the comfortable maximum (no source of such air here), and harm to a pawn that is not on a map.
    /// </summary>
    public static class TemperatureInjuryUtility
    {
        /// <summary>RimWorld calls the hediff givers from the health tick every 60 ticks.</summary>
        public const int IntervalTicks = 60;

        /// <summary>Safe range = comfortable range widened by this on each side (RimWorld: 10).</summary>
        public const float SafeMargin = 10f;

        public const float SeverityPerDegree = 6.45e-5f;
        public const float MinHypothermiaGain = 0.00075f;
        public const float MinHeatstrokeGain = 0.000375f;

        public const float RecoveryFraction = 0.027f;
        public const float MinRecovery = 0.0015f;
        public const float MaxRecovery = 0.015f;

        public const float FrostbiteMaxTemperature = 0f;
        public const float FrostbiteMinSeverity = 0.37f;
        public const float FrostbiteChancePerSeverity = 0.025f;
        public const float FrostbitePartHitPointsFraction = 0.5f;

        /// <summary>Degrees over the safe maximum → effective degrees (RimWorld's heat overage curve).</summary>
        public static readonly SimpleCurve HeatOverageCurve = new SimpleCurve
        {
            { 0f, 0f },
            { 25f, 25f },
            { 50f, 40f },
            { 100f, 60f },
            { 200f, 80f },
            { 400f, 100f },
            { 4000f, 1000f },
        };

        private static readonly List<BodyPartRecord> tmpFrostbiteParts = new List<BodyPartRecord>();

        /// <summary>The pawn's comfortable range: its <c>ComfyTemperatureMin</c>/<c>Max</c> stats, which
        /// worn apparel's insulation widens (RimWorld: <c>GenTemperature.ComfortableTemperatureRange</c>).</summary>
        public static FloatRange ComfortableTemperatureRange(Pawn pawn) =>
            new FloatRange(pawn.GetStatValue(TemperatureDefOf.ComfyTemperatureMin), pawn.GetStatValue(TemperatureDefOf.ComfyTemperatureMax));

        /// <summary>RimWorld: <c>GenTemperature.SafeTemperatureRange</c>.</summary>
        public static FloatRange SafeTemperatureRange(Pawn pawn)
        {
            FloatRange comfy = ComfortableTemperatureRange(pawn);
            return new FloatRange(comfy.min - SafeMargin, comfy.max + SafeMargin);
        }

        /// <summary>The air at the pawn's cell (RimWorld: <c>Thing.AmbientTemperature</c>).</summary>
        public static float AmbientTemperature(Pawn pawn) => RotUtility.AmbientTemperatureAt(pawn.Map!, pawn.Position);

        /// <summary>RimWorld: <c>HediffSet.HasTemperatureInjury</c> — either hediff at or past
        /// <paramref name="minStage"/>.</summary>
        public static bool HasTemperatureInjury(Pawn pawn, TemperatureInjuryStage minStage)
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffDef def = hediffs[i].def;
                if ((def == TemperatureDefOf.Hypothermia || def == TemperatureDefOf.Heatstroke)
                    && hediffs[i].CurStageIndex >= (int)minStage)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>One 60-tick interval of both givers. Called from the health tick for spawned flesh pawns.</summary>
        public static void TemperatureInterval(Pawn pawn)
        {
            if (!pawn.Spawned || TemperatureDefOf.Hypothermia == null) return;
            float ambient = AmbientTemperature(pawn);
            FloatRange comfy = ComfortableTemperatureRange(pawn);
            ColdInterval(pawn, ambient, comfy);
            if (pawn.Dead) return;
            HeatInterval(pawn, ambient, comfy);
        }

        private static void ColdInterval(Pawn pawn, float ambient, FloatRange comfy)
        {
            HediffDef def = TemperatureDefOf.Hypothermia;
            float safeMin = comfy.min - SafeMargin;
            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (ambient < safeMin)
            {
                float gain = (safeMin - ambient) * SeverityPerDegree;
                HealthUtility.AdjustSeverity(pawn, def, gain > MinHypothermiaGain ? gain : MinHypothermiaGain);
                if (pawn.Dead) return;
                existing ??= pawn.health.hediffSet.GetFirstHediffOfDef(def);
            }
            if (existing == null) return;
            if (ambient > comfy.min)
            {
                Recover(existing);
            }
            else if (ambient < FrostbiteMaxTemperature && existing.Severity > FrostbiteMinSeverity)
            {
                TryFrostbite(pawn, existing.Severity);
            }
        }

        private static void HeatInterval(Pawn pawn, float ambient, FloatRange comfy)
        {
            HediffDef def = TemperatureDefOf.Heatstroke;
            float safeMax = comfy.max + SafeMargin;
            if (ambient > safeMax)
            {
                float gain = HeatOverageCurve.Evaluate(ambient - safeMax) * SeverityPerDegree;
                HealthUtility.AdjustSeverity(pawn, def, gain > MinHeatstrokeGain ? gain : MinHeatstrokeGain);
                return;
            }
            if (ambient < comfy.max)
            {
                Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null) Recover(existing);
            }
        }

        private static void Recover(Hediff hediff)
        {
            float drop = GenMath.Clamp(hediff.Severity * RecoveryFraction, MinRecovery, MaxRecovery);
            hediff.Severity -= drop;
        }

        /// <summary>Frostbite on a random intact vulnerable part, weighted by its vulnerability.</summary>
        private static void TryFrostbite(Pawn pawn, float severity)
        {
            if (!Rand.Chance(FrostbiteChancePerSeverity * severity)) return;
            HediffSet set = pawn.health.hediffSet;
            IReadOnlyList<BodyPartRecord> all = set.Body.AllParts;
            tmpFrostbiteParts.Clear();
            float totalWeight = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                BodyPartRecord part = all[i];
                if (part.def.frostbiteVulnerability <= 0f || set.PartIsMissing(part)) continue;
                tmpFrostbiteParts.Add(part);
                totalWeight += part.def.frostbiteVulnerability;
            }
            if (tmpFrostbiteParts.Count == 0) return;
            float pick = Rand.Value * totalWeight;
            BodyPartRecord chosen = tmpFrostbiteParts[tmpFrostbiteParts.Count - 1];
            for (int i = 0; i < tmpFrostbiteParts.Count; i++)
            {
                pick -= tmpFrostbiteParts[i].def.frostbiteVulnerability;
                if (pick <= 0f)
                {
                    chosen = tmpFrostbiteParts[i];
                    break;
                }
            }
            tmpFrostbiteParts.Clear();
            int amount = (int)System.Math.Ceiling(chosen.def.hitPoints * FrostbitePartHitPointsFraction);
            pawn.TakeDamage(new DamageInfo(TemperatureDefOf.Frostbite, amount, 0f, null, chosen));
        }
    }
}
