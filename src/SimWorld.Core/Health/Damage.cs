using System;
using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>
    /// Armor stat family a DamageDef's hits roll against (RimWorld: <c>Verse.DamageArmorCategoryDef</c>):
    /// Sharp, Blunt or Heat, each backed by its own <c>ArmorRating_*</c> StatDef.
    /// </summary>
    public class DamageArmorCategoryDef : Def
    {
        public StatDef armorRatingStat = null!;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (armorRatingStat == null) yield return "armor category has no armorRatingStat.";
        }
    }

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

        /// <summary>Armor stat this damage rolls against, via <see cref="ArmorUtility"/>; null skips armor entirely.</summary>
        public DamageArmorCategoryDef? armorCategory;

        /// <summary>Armor penetration used when the hit doesn't supply its own (RimWorld default -1: "use the weapon's").</summary>
        public float defaultArmorPenetration = -1f;

        /// <summary>Explosions punch through every apparel layer down to the skin; data only until apparel exists.</summary>
        public bool harmAllLayersUntilOutside;

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

        /// <summary>Armor halved a Sharp hit and converted it to Blunt (RimWorld: the "diminished" armor outcome).</summary>
        public bool diminished;
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
            // Damage to a pawn has exactly one funnel in this port — there is no Pawn override of
            // Thing.TakeDamage — so this is where RimWorld's Thing.TakeDamage would call PostApplyDamage,
            // and the only place a "something just hurt me" notification can be raised once per hit.
            victim.health.PostApplyDamage(dinfo, result.totalDamageDealt);
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

            // Armor may reduce the amount, deflect the hit outright, or turn a Sharp hit into a Blunt one;
            // with no armor sources at all this is a no-op and behaves exactly as before armor existed.
            DamageDef damageDef = def;
            float armorPenetration = dinfo.ArmorPenetration > 0f ? dinfo.ArmorPenetration : Math.Max(0f, def.defaultArmorPenetration);
            float amount = ArmorUtility.GetPostArmorDamage(pawn, dinfo.Amount, armorPenetration, part, ref damageDef, out bool deflected, out bool diminished);
            if (deflected)
            {
                result.deflected = true;
                return;
            }
            if (amount <= 0f) return;

            HediffDef hediffDef = ChooseHediffDef(damageDef, part);
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(hediffDef, pawn, part);
            injury.Severity = amount;
            injury.TryGetComp<HediffComp_GetsPermanent>()?.PreFinalizeInjury();

            pawn.health.AddHediff(injury, part, dinfo);
            result.hediffs.Add(injury);
            result.totalDamageDealt += amount;
            result.lastHitPart = part;
            result.wounded = true;
            result.diminished = diminished;
        }

        protected virtual HediffDef ChooseHediffDef(DamageDef damageDef, BodyPartRecord part)
        {
            if (part.def.solid && damageDef.hediffSolid != null) return damageDef.hediffSolid;
            if (part.def.skinCovered && part.depth == BodyPartDepth.Outside && damageDef.hediffSkin != null) return damageDef.hediffSkin;
            return damageDef.hediff!;
        }
    }
}
