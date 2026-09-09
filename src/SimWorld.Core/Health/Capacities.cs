using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Health
{
    /// <summary>A thing a body can do, computed from its parts (RimWorld: <c>Verse.PawnCapacityDef</c>).</summary>
    public class PawnCapacityDef : Def
    {
        public Type workerClass = typeof(PawnCapacityWorker);
        public int listOrder;
        public bool showOnHumanlikes = true;
        public bool showOnAnimals = true;
        public bool showOnMechanoids;

        /// <summary>A flesh pawn with this capacity at zero dies.</summary>
        public bool lethalFlesh;

        public bool lethalMechanoids;
        public float minValue;

        private PawnCapacityWorker? workerInt;

        public PawnCapacityWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (PawnCapacityWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(PawnCapacityWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from PawnCapacityWorker.";
        }
    }

    /// <summary>Computes a capacity's raw level from the body (RimWorld: <c>Verse.PawnCapacityWorker</c>).</summary>
    public class PawnCapacityWorker
    {
        public PawnCapacityDef def = null!;

        public virtual float CalculateCapacityLevel(HediffSet diffSet) => 1f;

        public virtual bool CanHaveCapacity(BodyDef body) => true;

        protected static float CalculateCapacityAndRecord(HediffSet diffSet, PawnCapacityDef capacity)
        {
            return PawnCapacityUtility.CalculateCapacityLevel(diffSet, capacity);
        }
    }

    public class PawnCapacityWorker_Consciousness : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.ConsciousnessSource);
            if (level <= 0f) return 0f;
            level *= Math.Min(CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.BloodPumping), 1f);
            level *= Math.Min(CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.BloodFiltration), 1f);
            level *= Math.Min(CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Breathing), 1f);
            level *= 1f - diffSet.PainTotal * HealthTuning.PainConsciousnessFactor;
            return level;
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.ConsciousnessSource);
    }

    public class PawnCapacityWorker_Moving : PawnCapacityWorker
    {
        public const float AppendageWeight = 0.4f;

        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateLimbEfficiency(diffSet, BodyPartTagDefOf.MovingLimbCore, BodyPartTagDefOf.MovingLimbSegment, BodyPartTagDefOf.MovingLimbDigit, AppendageWeight, out float functional);
            if (functional < 0.4999f) return 0f;
            level *= CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.Pelvis), 1f);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.Spine), 1f);
            return level;
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.MovingLimbCore);
    }

    public class PawnCapacityWorker_Manipulation : PawnCapacityWorker
    {
        public const float AppendageWeight = 0.4f;

        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateLimbEfficiency(diffSet, BodyPartTagDefOf.ManipulationLimbCore, BodyPartTagDefOf.ManipulationLimbSegment, BodyPartTagDefOf.ManipulationLimbDigit, AppendageWeight, out _);
            return level * CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.ManipulationLimbCore);
    }

    public class PawnCapacityWorker_Sight : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            return PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.SightSource) * CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.SightSource);
    }

    public class PawnCapacityWorker_Hearing : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            return PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.HearingSource) * CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.HearingSource);
    }

    public class PawnCapacityWorker_Talking : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.TalkingSource);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.TalkingPathway), 1f);
            return level * CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.TalkingSource);
    }

    public class PawnCapacityWorker_Eating : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.EatingSource);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.EatingPathway), 1f);
            return level * CalculateCapacityAndRecord(diffSet, PawnCapacityDefOf.Consciousness);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.EatingSource);
    }

    public class PawnCapacityWorker_Breathing : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            float level = PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BreathingSource);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BreathingPathway), 1f);
            level *= Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BreathingSourceCage), 1f);
            return level;
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.BreathingSource);
    }

    public class PawnCapacityWorker_BloodPumping : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            return PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BloodPumpingSource);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.BloodPumpingSource);
    }

    public class PawnCapacityWorker_BloodFiltration : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            if (diffSet.Body.HasPartWithTag(BodyPartTagDefOf.BloodFiltrationSource))
            {
                return PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BloodFiltrationSource);
            }
            float kidneys = Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BloodFiltrationKidney), 1f);
            float liver = Math.Min(PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.BloodFiltrationLiver), 1f);
            return kidneys * liver;
        }

        public override bool CanHaveCapacity(BodyDef body) =>
            body.HasPartWithTag(BodyPartTagDefOf.BloodFiltrationSource) || body.HasPartWithTag(BodyPartTagDefOf.BloodFiltrationKidney) || body.HasPartWithTag(BodyPartTagDefOf.BloodFiltrationLiver);
    }

    public class PawnCapacityWorker_Metabolism : PawnCapacityWorker
    {
        public override float CalculateCapacityLevel(HediffSet diffSet)
        {
            return PawnCapacityUtility.CalculateTagEfficiency(diffSet, BodyPartTagDefOf.MetabolismSource);
        }

        public override bool CanHaveCapacity(BodyDef body) => body.HasPartWithTag(BodyPartTagDefOf.MetabolismSource);
    }

    /// <summary>Body-tree math behind every capacity (RimWorld: <c>Verse.PawnCapacityUtility</c>).</summary>
    public static class PawnCapacityUtility
    {
        /// <summary>Worker level, then hediff and gene (pawngen.genes) capacity modifiers: offsets summed,
        /// post-factors multiplied, setMax applied.</summary>
        public static float CalculateCapacityLevel(HediffSet diffSet, PawnCapacityDef capacity)
        {
            if (diffSet == null) throw new ArgumentNullException(nameof(diffSet));
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            if (!capacity.Worker.CanHaveCapacity(diffSet.Body)) return 0f;

            float level = capacity.Worker.CalculateCapacityLevel(diffSet);
            if (level > 0f)
            {
                float offset = 0f;
                float setMax = float.MaxValue;
                float postFactor = 1f;
                List<Hediff> hediffs = diffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    List<PawnCapacityModifier>? mods = hediffs[i].CapMods;
                    if (mods == null) continue;
                    for (int j = 0; j < mods.Count; j++)
                    {
                        if (mods[j].capacity != capacity) continue;
                        offset += mods[j].offset;
                        setMax = Math.Min(setMax, mods[j].setMax);
                        postFactor *= mods[j].postFactor;
                    }
                }
                if (diffSet.pawn.genes != null)
                {
                    foreach (PawnCapacityModifier mod in diffSet.pawn.genes.CapMods)
                    {
                        if (mod.capacity != capacity) continue;
                        offset += mod.offset;
                        setMax = Math.Min(setMax, mod.setMax);
                        postFactor *= mod.postFactor;
                    }
                }
                level += offset;
                level *= postFactor;
                level = Math.Min(level, setMax);
            }
            level = Math.Max(level, capacity.minValue);
            return (float)Math.Round(level, 4);
        }

        /// <summary>
        /// A part's working fraction: 0 when missing, the prosthetic's efficiency when replaced (itself or an
        /// ancestor), else remaining health fraction, plus any stage efficiency offsets.
        /// </summary>
        public static float CalculatePartEfficiency(HediffSet diffSet, BodyPartRecord part, bool ignoreAddedParts = false)
        {
            for (BodyPartRecord? ancestor = part.parent; ancestor != null; ancestor = ancestor.parent)
            {
                Hediff_AddedPart? replaced = diffSet.GetDirectlyAddedPartFor(ancestor);
                if (replaced != null) return replaced.def.addedPartProps!.partEfficiency;
            }
            if (diffSet.PartIsMissing(part)) return 0f;

            float efficiency;
            Hediff_AddedPart? added = ignoreAddedParts ? null : diffSet.GetDirectlyAddedPartFor(part);
            if (added != null)
            {
                efficiency = added.def.addedPartProps!.partEfficiency;
            }
            else
            {
                float max = part.def.GetMaxHealth(diffSet.pawn);
                efficiency = max <= 0f ? 1f : GenMath.Clamp01(diffSet.GetPartHealth(part) / max);
            }
            // Indexed loop rather than GetHediffsOnPart: that helper is a `yield return` iterator, so every
            // call heap-allocates an enumerator even when the part carries no hediffs at all. Ordinarily not
            // worth minding — but Pawn_HealthTracker.ShouldBeDead calls this once per pawn per tick
            // unconditionally, and at 64 bytes a call that was 86.5% of the entire simulation's allocation
            // (docs/perf/baseline.md §4). Same traversal, same order, no enumerator.
            List<Hediff> all = diffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                if (!ReferenceEquals(all[i].Part, part)) continue;
                HediffStage? stage = all[i].CurStage;
                if (stage != null) efficiency += stage.partEfficiencyOffset;
            }
            return Math.Max(efficiency, 0f);
        }

        /// <summary>Average efficiency of every part carrying <paramref name="tag"/>; 1 when the body has none.</summary>
        public static float CalculateTagEfficiency(HediffSet diffSet, BodyPartTagDef tag, float maximum = float.MaxValue)
        {
            IReadOnlyList<BodyPartRecord> parts = diffSet.Body.GetPartsWithTag(tag);
            if (parts.Count == 0) return 1f;
            float total = 0f;
            for (int i = 0; i < parts.Count; i++) total += CalculatePartEfficiency(diffSet, parts[i]);
            return Math.Min(total / parts.Count, maximum);
        }

        /// <summary>True when an ancestor of the part has been replaced by an added part.</summary>
        public static bool IsUnderAddedPart(HediffSet diffSet, BodyPartRecord part)
        {
            for (BodyPartRecord? p = part.parent; p != null; p = p.parent)
            {
                if (diffSet.HasDirectlyAddedPartFor(p)) return true;
            }
            return false;
        }

        /// <summary>
        /// Average over limbs of core × segments × lerp(1, digits, appendageWeight).
        /// <paramref name="functionalPercentage"/> is the share of limbs above zero. Segments and digits that a
        /// prosthetic has replaced are part of it and do not multiply in again — one simple prosthetic leg (85%)
        /// gives 92.5% moving, as in RimWorld.
        /// </summary>
        public static float CalculateLimbEfficiency(HediffSet diffSet, BodyPartTagDef limbCoreTag, BodyPartTagDef limbSegmentTag, BodyPartTagDef limbDigitTag, float appendageWeight, out float functionalPercentage)
        {
            IReadOnlyList<BodyPartRecord> cores = diffSet.Body.GetPartsWithTag(limbCoreTag);
            if (cores.Count == 0)
            {
                functionalPercentage = 0f;
                return 0f;
            }
            float total = 0f;
            int functional = 0;
            for (int i = 0; i < cores.Count; i++)
            {
                BodyPartRecord core = cores[i];
                float limb = CalculatePartEfficiency(diffSet, core);
                float digitSum = 0f;
                int digitCount = 0;
                foreach (BodyPartRecord child in core.GetChildParts())
                {
                    if (IsUnderAddedPart(diffSet, child)) continue;
                    if (child.HasTag(limbSegmentTag))
                    {
                        limb *= CalculatePartEfficiency(diffSet, child);
                    }
                    else if (child.HasTag(limbDigitTag))
                    {
                        digitSum += CalculatePartEfficiency(diffSet, child);
                        digitCount++;
                    }
                }
                if (digitCount > 0)
                {
                    float digits = digitSum / digitCount;
                    limb *= GenMath.Lerp(1f, digits, appendageWeight);
                }
                total += limb;
                if (limb > 0f) functional++;
            }
            functionalPercentage = (float)functional / cores.Count;
            return total / cores.Count;
        }
    }

    /// <summary>Cached capacity levels for one pawn (RimWorld: <c>Verse.PawnCapacitiesHandler</c>).</summary>
    public class PawnCapacitiesHandler
    {
        private readonly Pawn pawn;
        private readonly Dictionary<PawnCapacityDef, float> levels = new Dictionary<PawnCapacityDef, float>();
        private bool dirty = true;

        public PawnCapacitiesHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public float GetLevel(PawnCapacityDef capacity)
        {
            if (pawn.Dead) return 0f;
            if (dirty) Recalculate();
            return levels.TryGetValue(capacity, out float level) ? level : 0f;
        }

        public bool CapableOf(PawnCapacityDef capacity) => GetLevel(capacity) > HealthTuning.MinCapableLevel;

        public bool CanBeAwake => GetLevel(PawnCapacityDefOf.Consciousness) >= HealthTuning.ConsciousnessAwakeThreshold;

        public void Notify_CapacityLevelsDirty()
        {
            dirty = true;
        }

        private void Recalculate()
        {
            levels.Clear();
            dirty = false;
            HediffSet set = pawn.health.hediffSet;
            foreach (PawnCapacityDef capacity in DefDatabase<PawnCapacityDef>.AllDefsListForReading)
            {
                levels[capacity] = PawnCapacityUtility.CalculateCapacityLevel(set, capacity);
            }
        }
    }
}
