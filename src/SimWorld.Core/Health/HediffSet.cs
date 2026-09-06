using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>
    /// Every hediff on a pawn plus the aggregates derived from them (RimWorld: <c>Verse.HediffSet</c>):
    /// pain, bleeding, part health, missing parts, and coverage-weighted random part selection.
    /// </summary>
    public class HediffSet : IExposable
    {
        public readonly Pawn pawn;
        public List<Hediff> hediffs = new List<Hediff>();

        private float cachedPain = -1f;
        private float cachedBleedRate = -1f;

        public HediffSet(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public BodyDef Body => pawn.RaceProps.body ?? throw new InvalidOperationException(pawn.def.defName + " has no body.");

        public float PainTotal
        {
            get
            {
                if (cachedPain < 0f) cachedPain = CalculatePain();
                return cachedPain;
            }
        }

        public float BleedRateTotal
        {
            get
            {
                if (cachedBleedRate < 0f) cachedBleedRate = CalculateBleedRate();
                return cachedBleedRate;
            }
        }

        public bool AnyHediffMakesSickThought
        {
            get
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i].def.makesSickThought && hediffs[i].Visible) return true;
                }
                return false;
            }
        }

        /// <summary>Adds without state checks; use <see cref="Pawn_HealthTracker.AddHediff(Hediff, BodyPartRecord?, DamageInfo?)"/> normally.</summary>
        public void AddDirect(Hediff hediff, DamageInfo? dinfo = null)
        {
            if (hediff == null) throw new ArgumentNullException(nameof(hediff));
            if (hediff.def == null) throw new ArgumentException("Hediff has no def.", nameof(hediff));
            hediff.pawn = pawn;
            hediff.ageTicks = 0;

            bool merged = false;
            if (hediff is Hediff_Injury incoming)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] is Hediff_Injury existing && existing.TryMergeWith(incoming))
                    {
                        merged = true;
                        break;
                    }
                }
            }
            if (!merged)
            {
                hediffs.Add(hediff);
            }
            hediff.PostAdd(dinfo);
            DirtyCache();

            if (hediff is Hediff_Injury injury && injury.Part != null)
            {
                CheckPartDestroyed(injury);
            }
        }

        private void CheckPartDestroyed(Hediff_Injury injury)
        {
            BodyPartRecord part = injury.Part!;
            if (!part.def.destroyableByDamage || part.IsCorePart || PartIsMissing(part)) return;
            if (GetPartHealth(part) > 0f) return;

            // Everything on the destroyed subtree goes; a fresh missing-part hediff replaces it.
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                BodyPartRecord? p = hediffs[i].Part;
                if (p != null && (ReferenceEquals(p, part) || part.IsAncestorOf(p)))
                {
                    Hediff removed = hediffs[i];
                    hediffs.RemoveAt(i);
                    removed.PostRemoved();
                }
            }
            var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, part);
            missing.lastInjury = injury.def;
            missing.IsFresh = true;
            hediffs.Add(missing);
            missing.PostAdd(null);
            DirtyCache();
        }

        public bool Remove(Hediff hediff)
        {
            if (!hediffs.Remove(hediff)) return false;
            hediff.PostRemoved();
            DirtyCache();
            return true;
        }

        public void Clear()
        {
            hediffs.Clear();
            DirtyCache();
        }

        public bool HasHediff(HediffDef def, bool mustBeVisible = false)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == def && (!mustBeVisible || hediffs[i].Visible)) return true;
            }
            return false;
        }

        public bool HasHediff(HediffDef def, BodyPartRecord? part)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == def && ReferenceEquals(hediffs[i].Part, part)) return true;
            }
            return false;
        }

        public Hediff? GetFirstHediffOfDef(HediffDef def, bool mustBeVisible = false)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == def && (!mustBeVisible || hediffs[i].Visible)) return hediffs[i];
            }
            return null;
        }

        public IEnumerable<T> GetHediffs<T>() where T : Hediff
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is T typed) yield return typed;
            }
        }

        public IEnumerable<Hediff> GetHediffsOnPart(BodyPartRecord part)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (ReferenceEquals(hediffs[i].Part, part)) yield return hediffs[i];
            }
        }

        /// <summary>Remaining hit points of a part: max minus injuries on it; 0 when missing. Rounded like RimWorld.</summary>
        public float GetPartHealth(BodyPartRecord part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (PartIsMissing(part)) return 0f;
            float health = part.def.GetMaxHealth(pawn);
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && ReferenceEquals(injury.Part, part))
                {
                    health -= injury.Severity;
                }
            }
            return health <= 0f ? 0f : (float)Math.Round(health);
        }

        /// <summary>True when the part or any ancestor carries a missing-part hediff.</summary>
        public bool PartIsMissing(BodyPartRecord part)
        {
            for (BodyPartRecord? p = part; p != null; p = p.parent)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] is Hediff_MissingPart && ReferenceEquals(hediffs[i].Part, p)) return true;
                }
            }
            return false;
        }

        public bool HasDirectlyAddedPartFor(BodyPartRecord part) => GetDirectlyAddedPartFor(part) != null;

        public Hediff_AddedPart? GetDirectlyAddedPartFor(BodyPartRecord part)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_AddedPart added && ReferenceEquals(added.Part, part)) return added;
            }
            return null;
        }

        public IEnumerable<BodyPartRecord> GetNotMissingParts(BodyPartHeight height = BodyPartHeight.Undefined, BodyPartDepth depth = BodyPartDepth.Undefined)
        {
            IReadOnlyList<BodyPartRecord> all = Body.AllParts;
            for (int i = 0; i < all.Count; i++)
            {
                BodyPartRecord part = all[i];
                if (height != BodyPartHeight.Undefined && part.height != height) continue;
                if (depth != BodyPartDepth.Undefined && part.depth != depth) continue;
                if (PartIsMissing(part)) continue;
                yield return part;
            }
        }

        /// <summary>Random present part weighted by absolute coverage, optionally restricted by height/depth.</summary>
        public BodyPartRecord? GetRandomNotMissingPart(DamageDef? damageDef, BodyPartHeight height, BodyPartDepth depth, RandomStream rand)
        {
            var candidates = new List<BodyPartRecord>();
            foreach (BodyPartRecord part in GetNotMissingParts(height, depth))
            {
                if (part.coverageAbs > 0f) candidates.Add(part);
            }
            if (candidates.Count == 0)
            {
                candidates.Clear();
                foreach (BodyPartRecord part in GetNotMissingParts())
                {
                    if (part.coverageAbs > 0f) candidates.Add(part);
                }
            }
            if (candidates.Count == 0) return null;
            return GenCollection.RandomElementByWeight(candidates, p => p.coverageAbs, rand);
        }

        public BodyPartRecord? GetBrain()
        {
            foreach (BodyPartRecord part in Body.GetPartsWithTag(BodyPartTagDefOf.ConsciousnessSource))
            {
                if (!PartIsMissing(part)) return part;
            }
            return null;
        }

        public float CalculatePain()
        {
            if (!pawn.RaceProps.IsFlesh || pawn.Dead) return 0f;
            float pain = 0f;
            for (int i = 0; i < hediffs.Count; i++) pain += hediffs[i].PainOffset;
            for (int i = 0; i < hediffs.Count; i++) pain *= hediffs[i].PainFactor;
            return GenMath.Clamp01(pain);
        }

        public float CalculateBleedRate()
        {
            if (!pawn.RaceProps.IsFlesh || pawn.Dead) return 0f;
            float factor = 1f;
            float total = 0f;
            for (int i = 0; i < hediffs.Count; i++)
            {
                total += hediffs[i].BleedRate;
                HediffStage? stage = hediffs[i].CurStage;
                if (stage != null) factor *= stage.totalBleedFactor;
            }
            return total * factor / pawn.HealthScale;
        }

        public float TotalInjurySeverity()
        {
            float total = 0f;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury) total += injury.Severity;
            }
            return total;
        }

        /// <summary>Product of stage hunger factors plus the sum of offsets, never negative.</summary>
        public float HungerRateFactor
        {
            get
            {
                float factor = 1f;
                float offset = 0f;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    HediffStage? stage = hediffs[i].CurStage;
                    if (stage == null) continue;
                    factor *= stage.hungerRateFactor;
                    offset += stage.hungerRateFactorOffset;
                }
                return Math.Max(0f, factor + offset);
            }
        }

        /// <summary>Product of stage rest-fall factors plus the sum of offsets, never negative.</summary>
        public float RestFallFactor
        {
            get
            {
                float factor = 1f;
                float offset = 0f;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    HediffStage? stage = hediffs[i].CurStage;
                    if (stage == null) continue;
                    factor *= stage.restFallFactor;
                    offset += stage.restFallFactorOffset;
                }
                return Math.Max(0f, factor + offset);
            }
        }

        /// <summary>Product of every stage's naturalHealingFactor that sets one.</summary>
        public float NaturalHealingFactor
        {
            get
            {
                float factor = 1f;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    HediffStage? stage = hediffs[i].CurStage;
                    if (stage != null && stage.naturalHealingFactor >= 0f) factor *= stage.naturalHealingFactor;
                }
                return factor;
            }
        }

        public bool HasNaturallyHealingInjury()
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && injury.CanHealNaturally()) return true;
            }
            return false;
        }

        public bool HasTendedAndHealingInjury()
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && injury.IsTended && !injury.IsPermanent) return true;
            }
            return false;
        }

        /// <summary>Share of the body (by absolute coverage) that is still present: 1 minus every missing subtree.</summary>
        public float GetCoverageOfNotMissingNaturalParts()
        {
            float missing = 0f;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (!(hediffs[i] is Hediff_MissingPart) || hediffs[i].Part == null) continue;
                BodyPartRecord part = hediffs[i].Part!;
                if (part.parent != null && PartIsMissing(part.parent)) continue;
                missing += part.coverageAbsWithChildren;
            }
            return GenMath.Clamp01(1f - missing);
        }

        public void DirtyCache()
        {
            cachedPain = -1f;
            cachedBleedRate = -1f;
            pawn.health?.Notify_HediffSetChanged();
        }

        public void ExposeData()
        {
            List<Hediff>? list = hediffs;
            Scribe_Collections.Look(ref list, "hediffs", LookMode.Deep, pawn);
            hediffs = list ?? new List<Hediff>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hediffs.RemoveAll(h => h == null || h.def == null);
                for (int i = 0; i < hediffs.Count; i++) hediffs[i].pawn = pawn;
                DirtyCache();
            }
        }
    }
}
