using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>
    /// A health condition on a pawn, optionally attached to a body part (RimWorld: <c>Verse.Hediff</c>).
    /// Severity picks the active stage; components add mechanics; ticking ages it and applies drift.
    /// </summary>
    public class Hediff : IExposable
    {
        public HediffDef def = null!;
        public Pawn pawn = null!;
        public int ageTicks;
        public List<HediffComp>? comps;

        protected float severityInt;
        private BodyPartRecord? part;
        private int partIndex = -1;

        public Hediff()
        {
        }

        public Hediff(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public BodyPartRecord? Part
        {
            get => part;
            set
            {
                part = value;
                partIndex = value?.Index ?? -1;
            }
        }

        public virtual float Severity
        {
            get => severityInt;
            set
            {
                float clamped = GenMath.Clamp(value, def.minSeverity, def.maxSeverity);
                if (clamped == severityInt) return;
                severityInt = clamped;
                pawn?.health?.Notify_HediffChanged(this);
            }
        }

        public int CurStageIndex => def.StageIndexAtSeverity(Severity);

        public HediffStage? CurStage => def.StageAtSeverity(Severity);

        public virtual bool Visible
        {
            get
            {
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i].CompDisallowVisible) return false;
                    }
                }
                HediffStage? stage = CurStage;
                return stage == null || stage.becomeVisible;
            }
        }

        public virtual string Label
        {
            get
            {
                string label = def.label ?? def.defName;
                HediffStage? stage = CurStage;
                if (stage != null && !string.IsNullOrEmpty(stage.label)) label += " (" + stage.label + ")";
                return label;
            }
        }

        public virtual float PainOffset => CurStage?.painOffset ?? 0f;

        public virtual float PainFactor => CurStage?.painFactor ?? 1f;

        public virtual float BleedRate => 0f;

        public virtual List<PawnCapacityModifier>? CapMods => CurStage?.capMods;

        public virtual bool IsPermanent
        {
            get
            {
                var comp = TryGetComp<HediffComp_GetsPermanent>();
                return comp != null && comp.IsPermanent;
            }
        }

        public bool IsTended
        {
            get
            {
                var comp = TryGetComp<HediffComp_TendDuration>();
                return comp != null && comp.IsTended;
            }
        }

        public float TendQuality => TryGetComp<HediffComp_TendDuration>()?.tendQuality ?? 0f;

        public virtual bool TendableNow(bool ignoreTimer = false)
        {
            if (!def.tendable || Severity <= 0f || IsPermanent) return false;
            var comp = TryGetComp<HediffComp_TendDuration>();
            if (comp == null) return false;
            return comp.AllowTend(ignoreTimer);
        }

        public virtual bool ShouldRemove
        {
            get
            {
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i].CompShouldRemove) return true;
                    }
                }
                return Severity <= 0f;
            }
        }

        public virtual bool CauseDeathNow() => def.lethalSeverity > 0f && Severity >= def.lethalSeverity;

        public T? TryGetComp<T>() where T : HediffComp
        {
            if (comps == null) return null;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is T typed) return typed;
            }
            return null;
        }

        internal void InitializeComps()
        {
            if (def.comps == null || def.comps.Count == 0)
            {
                comps = null;
                return;
            }
            comps = new List<HediffComp>(def.comps.Count);
            for (int i = 0; i < def.comps.Count; i++)
            {
                var comp = (HediffComp)Activator.CreateInstance(def.comps[i].compClass)!;
                comp.parent = this;
                comp.props = def.comps[i];
                comps.Add(comp);
            }
        }

        /// <summary>Called once after construction, before the hediff is added.</summary>
        public virtual void PostMake()
        {
            severityInt = GenMath.Clamp(def.initialSeverity, def.minSeverity, def.maxSeverity);
            InitializeComps();
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompPostMake();
            }
        }

        public virtual void PostAdd(DamageInfo? dinfo)
        {
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompPostPostAdd(dinfo);
            }
        }

        public virtual void PostRemoved()
        {
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompPostPostRemoved();
            }
        }

        public virtual void Tick()
        {
            ageTicks++;
            if (comps != null)
            {
                float adjustment = 0f;
                for (int i = 0; i < comps.Count; i++) comps[i].CompPostTick(ref adjustment);
                if (adjustment != 0f) Severity += adjustment;
            }
            HediffStage? stage = CurStage;
            if (stage != null && stage.deathMtbDays > 0f && Rand.MTBEventOccurs(stage.deathMtbDays, GenDate.TicksPerDay, 1f))
            {
                pawn.health.Kill(null, this);
            }
        }

        public virtual void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompTended(quality, maxQuality, batchPosition);
            }
        }

        public virtual bool TryMergeWith(Hediff other) => false;

        public virtual void ExposeData()
        {
            HediffDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref severityInt, "severity");
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref partIndex, "partIndex", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                InitializeComps();
            }
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompExposeData();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && partIndex >= 0 && pawn?.RaceProps.body != null)
            {
                part = pawn.RaceProps.body.AllParts[partIndex];
            }
        }

        public override string ToString() => Label + (part != null ? " on " + part.Label : "") + " [" + Severity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>Runtime half of a hediff component (RimWorld: <c>Verse.HediffComp</c>).</summary>
    public abstract class HediffComp
    {
        public Hediff parent = null!;
        public HediffCompProperties props = null!;

        public Pawn Pawn => parent.pawn;

        public virtual void CompPostMake() { }
        public virtual void CompPostPostAdd(DamageInfo? dinfo) { }
        public virtual void CompPostPostRemoved() { }
        public virtual void CompPostTick(ref float severityAdjustment) { }
        public virtual void CompPostInjuryHeal(float amount) { }
        public virtual void CompTended(float quality, float maxQuality, int batchPosition) { }
        public virtual bool CompShouldRemove => false;
        public virtual bool CompDisallowVisible => false;
        public virtual void CompExposeData() { }
    }

    public class HediffComp_Immunizable : HediffComp
    {
        public HediffCompProperties_Immunizable Props => (HediffCompProperties_Immunizable)props;

        public float Immunity => Pawn.health.immunity.GetImmunity(parent.def);

        public bool FullyImmune => Immunity >= 1f;

        public override void CompPostTick(ref float severityAdjustment)
        {
            severityAdjustment += (FullyImmune ? Props.severityPerDayImmune : Props.severityPerDayNotImmune) / GenDate.TicksPerDay;
        }
    }

    public class HediffComp_TendDuration : HediffComp
    {
        public int tendTicksLeft = -1;
        public float tendQuality;
        public float totalTendQuality;

        public HediffCompProperties_TendDuration Props => (HediffCompProperties_TendDuration)props;

        public bool IsTended => tendTicksLeft > 0;

        public bool AllowTend(bool ignoreTimer)
        {
            if (!IsTended) return true;
            if (Props.TendIsPermanent) return false;
            if (ignoreTimer) return true;
            return tendTicksLeft <= Props.tendOverlapHours * GenDate.TicksPerHour;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Props.TendIsPermanent && tendTicksLeft > 0) tendTicksLeft--;
            if (IsTended && Props.severityPerDayTended != 0f)
            {
                severityAdjustment += Props.severityPerDayTended * tendQuality / GenDate.TicksPerDay;
            }
        }

        public override void CompTended(float quality, float maxQuality, int batchPosition)
        {
            tendQuality = GenMath.Clamp(quality + Rand.Range(-HealthTuning.TendQualityVariance, HealthTuning.TendQualityVariance), 0f, Math.Max(0f, maxQuality));
            totalTendQuality += tendQuality;
            if (Props.TendIsPermanent)
            {
                tendTicksLeft = int.MaxValue;
            }
            else
            {
                tendTicksLeft = Math.Max(0, tendTicksLeft) + (int)(Props.baseTendDurationHours * GenDate.TicksPerHour);
            }
            Pawn.health.Notify_HediffChanged(parent);
        }

        public override bool CompShouldRemove => Props.disappearsAtTotalTendQuality >= 0f && totalTendQuality >= Props.disappearsAtTotalTendQuality;

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref tendTicksLeft, "tendTicksLeft", -1);
            Scribe_Values.Look(ref tendQuality, "tendQuality");
            Scribe_Values.Look(ref totalTendQuality, "totalTendQuality");
        }
    }

    public class HediffComp_SeverityPerDay : HediffComp
    {
        public HediffCompProperties_SeverityPerDay Props => (HediffCompProperties_SeverityPerDay)props;

        public virtual float SeverityChangePerDay() => Props.severityPerDay;

        public override void CompPostTick(ref float severityAdjustment)
        {
            severityAdjustment += SeverityChangePerDay() / GenDate.TicksPerDay;
        }
    }

    public class HediffComp_Disappears : HediffComp
    {
        public int ticksToDisappear;

        public HediffCompProperties_Disappears Props => (HediffCompProperties_Disappears)props;

        public override void CompPostMake()
        {
            ticksToDisappear = Rand.Range(Props.disappearsAfterTicks);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            ticksToDisappear--;
        }

        public override bool CompShouldRemove => ticksToDisappear <= 0;

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref ticksToDisappear, "ticksToDisappear");
        }
    }

    /// <summary>
    /// Rolls once when the injury is added whether it will scar; if so it stops healing at a random
    /// residual severity and is permanent from then on (RimWorld: <c>HediffComp_GetsPermanent</c>).
    /// </summary>
    public class HediffComp_GetsPermanent : HediffComp
    {
        public const float NoThreshold = 9999f;

        private bool isPermanentInt;
        public float permanentDamageThreshold = NoThreshold;

        public HediffCompProperties_GetsPermanent Props => (HediffCompProperties_GetsPermanent)props;

        public bool IsPermanent
        {
            get => isPermanentInt;
            set
            {
                if (value == isPermanentInt) return;
                isPermanentInt = value;
                Pawn.health.Notify_HediffChanged(parent);
            }
        }

        /// <summary>Decides at injury time whether this wound will leave a permanent mark.</summary>
        public void PreFinalizeInjury()
        {
            float partFactor = parent.Part?.def.permanentInjuryChanceFactor ?? 1f;
            float chance = Props.becomePermanentChanceFactor * partFactor * HealthTuning.BecomePermanentBaseChance;
            if (Rand.Chance(chance))
            {
                permanentDamageThreshold = Rand.Range(1f, Math.Max(1f, parent.Severity * 0.7f));
            }
        }

        public override void CompPostInjuryHeal(float amount)
        {
            if (permanentDamageThreshold < NoThreshold && !IsPermanent && parent.Severity <= permanentDamageThreshold)
            {
                IsPermanent = true;
                parent.Severity = permanentDamageThreshold;
            }
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref isPermanentInt, "isPermanent");
            Scribe_Values.Look(ref permanentDamageThreshold, "permanentDamageThreshold", NoThreshold);
        }
    }

    /// <summary>Untended wounds roll for infection after a delay (RimWorld: <c>HediffComp_Infecter</c>).</summary>
    public class HediffComp_Infecter : HediffComp
    {
        private int ticksUntilInfect = -1;

        public HediffCompProperties_Infecter Props => (HediffCompProperties_Infecter)props;

        public int TicksUntilInfect => ticksUntilInfect;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            if (parent.Part != null && parent.Part.def.solid) return;
            ticksUntilInfect = Rand.Range(HealthTuning.InfectionDelayRange);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (ticksUntilInfect <= 0) return;
            ticksUntilInfect--;
            if (ticksUntilInfect == 0) CheckMakeInfection();
        }

        private void CheckMakeInfection()
        {
            ticksUntilInfect = -1;
            if (parent.IsPermanent || parent.Severity <= 0f || Pawn.Dead) return;
            float chance = Props.infectionChance;
            if (parent.IsTended)
            {
                chance *= 1f - 0.9f * GenMath.Clamp01(parent.TendQuality);
            }
            if (Rand.Chance(chance))
            {
                Pawn.health.AddHediff(HediffDefOf.WoundInfection, parent.Part);
            }
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref ticksUntilInfect, "ticksUntilInfect", -1);
        }
    }

    /// <summary>A wound on a part (RimWorld: <c>Verse.Hediff_Injury</c>): pain and bleeding scale with severity; heals naturally.</summary>
    public class Hediff_Injury : Hediff
    {
        public Hediff_Injury()
        {
        }

        public Hediff_Injury(Pawn pawn) : base(pawn)
        {
        }

        public InjuryProps Props => def.injuryProps!;

        public override float PainOffset
        {
            get
            {
                if (Severity <= 0f) return 0f;
                float perSeverity = IsPermanent ? Props.averagePainPerSeverityPermanent : Props.painPerSeverity;
                return Severity * perSeverity;
            }
        }

        public override float BleedRate
        {
            get
            {
                if (pawn.Dead || IsPermanent || IsTended || Severity <= 0f) return 0f;
                float partRate = Part?.def.bleedRate ?? 1f;
                return Severity * Props.bleedRate * partRate;
            }
        }

        public override string Label => IsPermanent ? (def.label ?? def.defName) + " (permanent)" : base.Label;

        public bool CanHealNaturally() => !IsPermanent;

        public bool CanMerge(Hediff_Injury other)
        {
            return Props.canMerge && other.def == def && ReferenceEquals(other.Part, Part) && !IsPermanent && !other.IsPermanent && !IsTended && !other.IsTended;
        }

        public override bool TryMergeWith(Hediff other)
        {
            if (!(other is Hediff_Injury injury) || !CanMerge(injury)) return false;
            Severity += injury.Severity;
            ageTicks = 0;
            return true;
        }

        public virtual void Heal(float amount)
        {
            if (amount <= 0f) return;
            Severity -= amount;
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].CompPostInjuryHeal(amount);
            }
        }
    }

    /// <summary>A destroyed or amputated part (RimWorld: <c>Verse.Hediff_MissingPart</c>); bleeds while fresh.</summary>
    public class Hediff_MissingPart : Hediff
    {
        private bool isFreshInt;
        public HediffDef? lastInjury;

        public Hediff_MissingPart()
        {
        }

        public Hediff_MissingPart(Pawn pawn) : base(pawn)
        {
        }

        public bool IsFresh
        {
            get => isFreshInt && !ParentIsMissing;
            set => isFreshInt = value;
        }

        public bool ParentIsMissing => Part?.parent != null && pawn.health.hediffSet.PartIsMissing(Part.parent);

        public override float Severity
        {
            get => Part?.def.GetMaxHealth(pawn) ?? 0f;
            set { }
        }

        public override float PainOffset
        {
            get
            {
                if (!IsFresh || Part == null || pawn.Dead) return 0f;
                return Part.def.GetMaxHealth(pawn) * (lastInjury?.injuryProps?.painPerSeverity ?? 0.0125f);
            }
        }

        public override float BleedRate
        {
            get
            {
                if (!IsFresh || Part == null || pawn.Dead || IsTended) return 0f;
                return Part.def.GetMaxHealth(pawn) * (lastInjury?.injuryProps?.bleedRate ?? 0.06f) * Part.def.bleedRate;
            }
        }

        public override string Label => (Part?.LabelCap ?? "Part") + " missing";

        public override bool ShouldRemove => false;

        public override bool CauseDeathNow() => false;

        public override bool TendableNow(bool ignoreTimer = false) => IsFresh;

        public override void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            base.Tended(quality, maxQuality, batchPosition);
            IsFresh = false;
            pawn.health.Notify_HediffChanged(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isFreshInt, "isFresh");
            Scribe_Defs.Look(ref lastInjury, "lastInjury");
        }
    }

    /// <summary>A prosthetic or bionic replacing a part (RimWorld: <c>Verse.Hediff_AddedPart</c>).</summary>
    public class Hediff_AddedPart : Hediff
    {
        public Hediff_AddedPart()
        {
        }

        public Hediff_AddedPart(Pawn pawn) : base(pawn)
        {
        }

        public override bool ShouldRemove => false;

        public override bool CauseDeathNow() => false;

        public override string Label => def.label ?? def.defName;

        /// <summary>Installing clears the part and marks its natural children as gone; the prosthetic stands in for them.</summary>
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            if (Part == null) return;
            pawn.health.RestorePart(Part, this);
            for (int i = 0; i < Part.parts.Count; i++)
            {
                var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, Part.parts[i]);
                missing.IsFresh = false;
                pawn.health.hediffSet.hediffs.Add(missing);
                missing.PostAdd(null);
            }
            pawn.health.hediffSet.DirtyCache();
        }
    }

    /// <summary>Constructs hediffs (RimWorld: <c>Verse.HediffMaker</c>).</summary>
    public static class HediffMaker
    {
        public static Hediff MakeHediff(HediffDef def, Pawn pawn, BodyPartRecord? part = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            var hediff = (Hediff)Activator.CreateInstance(def.hediffClass, pawn)!;
            hediff.def = def;
            hediff.Part = part;
            hediff.PostMake();
            return hediff;
        }
    }

    /// <summary>Severity helpers (RimWorld: <c>Verse.HealthUtility</c>).</summary>
    public static class HealthUtility
    {
        /// <summary>Adds <paramref name="delta"/> to the pawn's hediff of <paramref name="def"/>, creating it for a positive delta.</summary>
        public static void AdjustSeverity(Pawn pawn, HediffDef def, float delta)
        {
            if (delta == 0f) return;
            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
            {
                existing.Severity += delta;
            }
            else if (delta > 0f)
            {
                Hediff made = HediffMaker.MakeHediff(def, pawn);
                made.Severity = delta;
                pawn.health.AddHediff(made);
            }
        }
    }
}
