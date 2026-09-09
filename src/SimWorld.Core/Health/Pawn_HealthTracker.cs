using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>Mobile → Down → Dead (RimWorld: <c>Verse.PawnHealthState</c>).</summary>
    public enum PawnHealthState
    {
        Mobile,
        Down,
        Dead,
    }

    /// <summary>
    /// A pawn's health (RimWorld: <c>Verse.Pawn_HealthTracker</c>): owns the hediff set, capacities, immunity
    /// and summary; ticks hediffs, natural healing and bleeding; derives downed and dead from the body's state.
    /// </summary>
    public class Pawn_HealthTracker : IExposable
    {
        private readonly Pawn pawn;
        public HediffSet hediffSet;
        public PawnCapacitiesHandler capacities;
        public ImmunityHandler immunity;
        public SummaryHealthHandler summaryHealth;

        /// <summary>
        /// Surgeries queued on this pawn (RimWorld: <c>Pawn_HealthTracker.surgeryBills</c>). A medical bill
        /// lives on the patient, not on a workbench — the operation happens wherever the patient is, and
        /// queueing one is a decision about a person rather than about a production line.
        /// </summary>
        public BillStack surgeryBills;

        private PawnHealthState healthState = PawnHealthState.Mobile;
        private bool forceDowned;
        private int deathTick = -1;
        private HediffDef? deathCauseHediff;
        private DamageDef? deathCauseDamage;
        private readonly List<Hediff_Injury> tmpInjuries = new List<Hediff_Injury>();

        public Pawn_HealthTracker(Pawn pawn)
        {
            surgeryBills = new BillStack(new SurgeryBillGiver(pawn));
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            hediffSet = new HediffSet(pawn);
            capacities = new PawnCapacitiesHandler(pawn);
            immunity = new ImmunityHandler(pawn);
            summaryHealth = new SummaryHealthHandler(pawn);
        }

        public PawnHealthState State => healthState;

        public bool Downed => healthState == PawnHealthState.Down;

        public bool Dead => healthState == PawnHealthState.Dead;

        public int DeathTick => deathTick;

        public HediffDef? DeathCauseHediff => deathCauseHediff;

        public DamageDef? DeathCauseDamage => deathCauseDamage;

        /// <summary>Downs the pawn regardless of its body (RimWorld's <c>forceIncap</c>); cleared by the caller.</summary>
        public bool ForceDowned
        {
            get => forceDowned;
            set
            {
                if (forceDowned == value) return;
                forceDowned = value;
                CheckForStateChange(null, null);
            }
        }

        /// <summary>Pain at or above the pawn's shock threshold downs it.</summary>
        public bool InPainShock => hediffSet.PainTotal >= pawn.PainShockThreshold;

        // ---- adding and removing ----

        public Hediff AddHediff(HediffDef def, BodyPartRecord? part = null)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
            AddHediff(hediff, part);
            return hediff;
        }

        public void AddHediff(Hediff hediff, BodyPartRecord? part = null, DamageInfo? dinfo = null)
        {
            if (hediff == null) throw new ArgumentNullException(nameof(hediff));
            if (part != null) hediff.Part = part;
            hediffSet.AddDirect(hediff, dinfo);
            CheckForStateChange(dinfo, hediff);
        }

        public bool RemoveHediff(Hediff hediff)
        {
            bool removed = hediffSet.Remove(hediff);
            if (removed) CheckForStateChange(null, hediff);
            return removed;
        }

        /// <summary>
        /// Clears every hediff on the part and its children, making the part whole again
        /// (RimWorld: <c>Pawn_HealthTracker.RestorePart</c>); used before installing a replacement.
        /// </summary>
        public void RestorePart(BodyPartRecord part, Hediff? diffException = null)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffSet.hediffs[i];
                if (ReferenceEquals(hediff, diffException) || hediff.Part == null) continue;
                if (ReferenceEquals(hediff.Part, part) || part.IsAncestorOf(hediff.Part))
                {
                    hediffSet.hediffs.RemoveAt(i);
                    hediff.PostRemoved();
                }
            }
            hediffSet.DirtyCache();
            CheckForStateChange(null, null);
        }

        // ---- notifications ----

        public void Notify_HediffChanged(Hediff? hediff)
        {
            hediffSet.DirtyCache();
            CheckForStateChange(null, hediff);
        }

        public void Notify_HediffSetChanged()
        {
            capacities.Notify_CapacityLevelsDirty();
            summaryHealth.Notify_HealthChanged();
        }

        // ---- ticking ----

        public void HealthTick()
        {
            if (Dead) return;

            List<Hediff> hediffs = hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (i >= hediffs.Count) continue;
                hediffs[i].Tick();
                if (Dead) return;
            }

            bool removedAny = false;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i].ShouldRemove)
                {
                    Hediff removed = hediffs[i];
                    hediffs.RemoveAt(i);
                    removed.PostRemoved();
                    removedAny = true;
                }
            }
            if (removedAny) hediffSet.DirtyCache();

            immunity.ImmunityHandlerTick();

            if (pawn.RaceProps.IsFlesh)
            {
                if (pawn.IsHashIntervalTick(HealthTuning.HealInterval))
                {
                    TryNaturalHealing();
                    if (Dead) return;
                }
                if (pawn.IsHashIntervalTick(HealthTuning.BleedInterval))
                {
                    float bleedRate = hediffSet.BleedRateTotal;
                    if (bleedRate >= HealthTuning.MinBleedRateToBleed)
                    {
                        HealthUtility.AdjustSeverity(pawn, HediffDefOf.BloodLoss, bleedRate * HealthTuning.BloodLossPerBleedUnitPerInterval);
                    }
                }
            }

            CheckForStateChange(null, null);
        }

        /// <summary>
        /// Every 600 ticks one random healable wound closes by 8/day (+4 lying down), scaled by health scale and
        /// hediff healing factors; a tended wound additionally heals 22 × tend quality per day unless the pawn is
        /// starving (RimWorld: <c>Pawn_HealthTracker.HealthTick</c>).
        /// </summary>
        private void TryNaturalHealing()
        {
            bool healed = false;
            // Indexed loops rather than GetHediffs<T>(): that helper is a `yield return` iterator and
            // allocates an enumerator per call even when the pawn has no injuries at all. This runs on
            // every heal interval for every pawn regardless (docs/perf/baseline.md §4).
            tmpInjuries.Clear();
            List<Hediff> all = hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Hediff_Injury injury && injury.CanHealNaturally()) tmpInjuries.Add(injury);
            }
            if (tmpInjuries.Count > 0)
            {
                float perDay = HealthTuning.BaseHealPerDay;
                if (pawn.Lying) perDay += HealthTuning.LyingDownHealBonusPerDay;
                perDay *= hediffSet.NaturalHealingFactor;
                Hediff_Injury target = tmpInjuries[Rand.Range(0, tmpInjuries.Count)];
                target.Heal(perDay * pawn.HealthScale / HealthTuning.HealIntervalsPerDay);
                healed = true;
            }

            bool starving = pawn.needs.food != null && pawn.needs.food.Starving;
            if (!starving)
            {
                tmpInjuries.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is Hediff_Injury injury && injury.IsTended && !injury.IsPermanent) tmpInjuries.Add(injury);
                }
                if (tmpInjuries.Count > 0)
                {
                    Hediff_Injury target = tmpInjuries[Rand.Range(0, tmpInjuries.Count)];
                    target.Heal(HealthTuning.TendedHealPerDay * target.TendQuality * pawn.HealthScale / HealthTuning.HealIntervalsPerDay);
                    healed = true;
                }
            }
            tmpInjuries.Clear();

            if (healed)
            {
                for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
                {
                    if (hediffSet.hediffs[i] is Hediff_Injury injury && injury.ShouldRemove)
                    {
                        hediffSet.hediffs.RemoveAt(i);
                        injury.PostRemoved();
                    }
                }
                hediffSet.DirtyCache();
            }
        }

        // ---- state ----

        public void CheckForStateChange(DamageInfo? dinfo, Hediff? hediff)
        {
            if (Dead) return;
            if (ShouldBeDead())
            {
                Kill(dinfo, hediff);
                return;
            }
            if (!Downed)
            {
                if (ShouldBeDowned()) MakeDowned();
            }
            else if (!ShouldBeDowned())
            {
                MakeUndowned();
            }
        }

        public bool ShouldBeDead()
        {
            if (Dead) return true;
            List<Hediff> hediffs = hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].CauseDeathNow()) return true;
            }
            if (ShouldBeDeadFromRequiredCapacity() != null) return true;
            if (hediffSet.CorePartEfficiency <= HealthTuning.MinCapableLevel) return true;
            return ShouldBeDeadFromLethalDamageThreshold();
        }

        /// <summary>The first lethal capacity the pawn no longer has, or null.</summary>
        public PawnCapacityDef? ShouldBeDeadFromRequiredCapacity()
        {
            bool flesh = pawn.RaceProps.IsFlesh;
            IReadOnlyList<PawnCapacityDef> all = DefDatabase<PawnCapacityDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                bool lethal = flesh ? all[i].lethalFlesh : all[i].lethalMechanoids;
                if (lethal && !capacities.CapableOf(all[i])) return all[i];
            }
            return null;
        }

        public bool ShouldBeDeadFromLethalDamageThreshold() => hediffSet.TotalInjurySeverityCached >= HealthTuning.LethalDamageThreshold;

        public bool ShouldBeDowned()
        {
            if (forceDowned) return true;
            if (InPainShock) return true;
            if (!capacities.CanBeAwake) return true;
            return !capacities.CapableOf(PawnCapacityDefOf.Moving);
        }

        private void MakeDowned()
        {
            healthState = PawnHealthState.Down;
            pawn.Notify_Downed();
        }

        private void MakeUndowned()
        {
            healthState = PawnHealthState.Mobile;
        }

        public void Kill(DamageInfo? dinfo, Hediff? exactCulprit)
        {
            if (Dead) return;
            healthState = PawnHealthState.Dead;
            deathTick = Find.TickManager?.TicksGame ?? -1;
            deathCauseDamage = dinfo?.Def;
            deathCauseHediff = exactCulprit?.def;
            hediffSet.DirtyCache();
            pawn.Notify_Died();
        }

        // ---- Scribe ----

        public void ExposeData()
        {
            Scribe_Values.Look(ref healthState, "healthState", PawnHealthState.Mobile);
            Scribe_Values.Look(ref forceDowned, "forceDowned");
            Scribe_Values.Look(ref deathTick, "deathTick", -1);
            Scribe_Defs.Look(ref deathCauseHediff, "deathCauseHediff");
            Scribe_Defs.Look(ref deathCauseDamage, "deathCauseDamage");
            HediffSet? set = hediffSet;
            Scribe_Deep.Look(ref set, "hediffSet", pawn);
            hediffSet = set ?? new HediffSet(pawn);
            ImmunityHandler? imm = immunity;
            Scribe_Deep.Look(ref imm, "immunity", pawn);
            immunity = imm ?? new ImmunityHandler(pawn);
            BillStack? bills = surgeryBills;
            Scribe_Deep.Look(ref bills, "surgeryBills", new SurgeryBillGiver(pawn));
            surgeryBills = bills ?? new BillStack(new SurgeryBillGiver(pawn));

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hediffSet.DirtyCache();

                // A Bill_Medical saves its body part as an address, not an object — see that class. The pawn
                // is the only thing that can turn the address back into a part of a real body.
                for (int i = 0; i < surgeryBills.Count; i++)
                {
                    (surgeryBills[i] as Bill_Medical)?.ResolvePartAfterLoad(pawn);
                }
            }
        }
    }

    /// <summary>
    /// One number for "how hurt is this pawn" (RimWorld: <c>Verse.SummaryHealthHandler</c>). Wounds scale it by
    /// their fraction of the part's hit points weighted by the part's share of the body; missing parts remove
    /// their share outright. RimWorld's exact weighting is not reproduced, only its shape.
    /// </summary>
    public class SummaryHealthHandler
    {
        private readonly Pawn pawn;
        private float cached = -1f;

        public SummaryHealthHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public float SummaryHealthPercent
        {
            get
            {
                if (pawn.Dead) return 0f;
                if (cached < 0f) cached = Calculate();
                return cached;
            }
        }

        public void Notify_HealthChanged()
        {
            cached = -1f;
        }

        private float Calculate()
        {
            HediffSet set = pawn.health.hediffSet;
            float health = 1f;
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && injury.Part != null && injury.Visible)
                {
                    float max = injury.Part.def.GetMaxHealth(pawn);
                    if (max > 0f) health *= 1f - GenMath.Clamp01(injury.Severity / max) * injury.Part.coverageAbsWithChildren;
                }
            }
            health *= set.GetCoverageOfNotMissingNaturalParts();
            return GenMath.Clamp01(health);
        }
    }
}
