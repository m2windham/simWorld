using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>A kind of harm (RimWorld: <c>Verse.DamageDef</c>); maps to the injury hediff it leaves.</summary>
    public class DamageDef : Def
    {
        public Type workerClass = typeof(DamageWorker_AddInjury);
        public HediffDef? hediff;
        public HediffDef? hediffSkin;
        public HediffDef? hediffSolid;
        public bool harmsHealth = true;
        public bool makesBlood = true;
        public bool isRanged;
        public bool isExplosive;
        public bool hasForcefulImpact = true;
        public bool externalViolence = true;
        public bool consideredHelpful;
        public string? deathMessage;

        private DamageWorker? workerInt;

        public DamageWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (DamageWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (harmsHealth && hediff == null) yield return "damage that harms health needs a hediff.";
            if (!typeof(DamageWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from DamageWorker.";
        }
    }

    /// <summary>One instance of damage being applied (RimWorld: <c>Verse.DamageInfo</c>).</summary>
    public sealed class DamageInfo
    {
        public DamageDef Def { get; }
        public float Amount { get; set; }
        public float ArmorPenetration { get; set; }
        public object? Instigator { get; set; }
        public BodyPartRecord? HitPart { get; set; }
        public BodyPartHeight Height { get; set; } = BodyPartHeight.Undefined;
        public BodyPartDepth Depth { get; set; } = BodyPartDepth.Undefined;

        public DamageInfo(DamageDef def, float amount, float armorPenetration = 0f, object? instigator = null, BodyPartRecord? hitPart = null)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Amount = amount;
            ArmorPenetration = armorPenetration;
            Instigator = instigator;
            HitPart = hitPart;
        }

        public override string ToString() => Def.defName + " " + Amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + (HitPart != null ? " → " + HitPart.Label : "");
    }

    /// <summary>What a damage application did.</summary>
    public sealed class DamageResult
    {
        public readonly List<Hediff> hediffs = new List<Hediff>();
        public float totalDamageDealt;
        public BodyPartRecord? lastHitPart;
        public bool wounded;
        public bool deflected;
    }

    /// <summary>Applies a DamageDef to a target (RimWorld: <c>Verse.DamageWorker</c>).</summary>
    public class DamageWorker
    {
        public DamageDef def = null!;

        public virtual DamageResult Apply(DamageInfo dinfo, Pawn victim)
        {
            if (dinfo == null) throw new ArgumentNullException(nameof(dinfo));
            if (victim == null) throw new ArgumentNullException(nameof(victim));
            var result = new DamageResult();
            if (!def.harmsHealth || dinfo.Amount <= 0f || victim.Dead) return result;
            ApplyToPawn(dinfo, victim, result);
            return result;
        }

        protected virtual void ApplyToPawn(DamageInfo dinfo, Pawn pawn, DamageResult result)
        {
        }
    }

    /// <summary>
    /// The standard worker: picks a part (given, or random by coverage), creates the matching injury hediff
    /// at the damage amount, and adds it — the health tracker handles destruction, downing and death
    /// (RimWorld: <c>Verse.DamageWorker_AddInjury</c>).
    /// </summary>
    public class DamageWorker_AddInjury : DamageWorker
    {
        protected override void ApplyToPawn(DamageInfo dinfo, Pawn pawn, DamageResult result)
        {
            BodyPartRecord? part = dinfo.HitPart ?? pawn.health.hediffSet.GetRandomNotMissingPart(def, dinfo.Height, dinfo.Depth, Rand.Current);
            if (part == null) return;
            if (pawn.health.hediffSet.PartIsMissing(part)) return;

            HediffDef hediffDef = ChooseHediffDef(part);
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(hediffDef, pawn, part);
            injury.Severity = dinfo.Amount;
            injury.TryGetComp<HediffComp_GetsPermanent>()?.PreFinalizeInjury();

            pawn.health.AddHediff(injury, part, dinfo);
            result.hediffs.Add(injury);
            result.totalDamageDealt += dinfo.Amount;
            result.lastHitPart = part;
            result.wounded = true;
        }

        protected virtual HediffDef ChooseHediffDef(BodyPartRecord part)
        {
            if (part.def.solid && def.hediffSolid != null) return def.hediffSolid;
            if (part.def.skinCovered && part.depth == BodyPartDepth.Outside && def.hediffSkin != null) return def.hediffSkin;
            return def.hediff!;
        }
    }
}
