using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Health
{
    /// <summary>Adjusts one capacity while a hediff stage is active (RimWorld: <c>Verse.PawnCapacityModifier</c>).</summary>
    public class PawnCapacityModifier
    {
        public PawnCapacityDef capacity = null!;
        public float offset;
        public float setMax = float.MaxValue;
        public float postFactor = 1f;
    }

    /// <summary>Effects of a hediff at a severity band (RimWorld: <c>Verse.HediffStage</c>).</summary>
    public class HediffStage
    {
        public float minSeverity;
        public string? label;
        public bool becomeVisible = true;
        public bool lifeThreatening;
        public float painOffset;
        public float painFactor = 1f;
        public float partEfficiencyOffset;
        public float hungerRateFactor = 1f;
        public float hungerRateFactorOffset;
        public float restFallFactor = 1f;
        public float restFallFactorOffset;
        public float totalBleedFactor = 1f;
        public float naturalHealingFactor = -1f;
        public float deathMtbDays = -1f;
        public float forgetMemoryThoughtMtbDays = -1f;
        public float mentalBreakMtbDays = -1f;
        public List<PawnCapacityModifier>? capMods;
        public List<StatModifier>? statOffsets;
        public List<StatModifier>? statFactors;
    }

    /// <summary>Injury-specific tunables (RimWorld: <c>Verse.InjuryProps</c>).</summary>
    public class InjuryProps
    {
        public float painPerSeverity = 0.0125f;
        public float averagePainPerSeverityPermanent = 0.00625f;
        public float bleedRate = 0.06f;
        public bool canMerge = true;
        public string? destroyedLabel;
        public string? destroyedOutLabel;
        public bool alwaysUseDestroyedLabel;
    }

    /// <summary>Prosthetic/bionic part tunables (RimWorld: <c>Verse.AddedBodyPartProps</c>).</summary>
    public class AddedBodyPartProps
    {
        public float partEfficiency = 1f;
        public bool solid;
        public bool betterThanNatural;
    }

    /// <summary>
    /// A health condition: injury, disease, addiction, implant, chronic state (RimWorld: <c>Verse.HediffDef</c>).
    /// Behaviour comes from <see cref="hediffClass"/>, stage effects from <see cref="stages"/>, and
    /// composable mechanics (immunity, tending, decay…) from <see cref="comps"/>.
    /// </summary>
    public class HediffDef : Def
    {
        public Type hediffClass = typeof(Hediff);
        public List<HediffCompProperties>? comps;
        public float initialSeverity = 0.5f;
        public float lethalSeverity = -1f;
        public float maxSeverity = float.MaxValue;
        public float minSeverity;
        public bool tendable;
        public bool isBad = true;
        public bool makesSickThought;
        public bool everCurableByItem = true;
        public bool chronic;
        public bool displayWound;
        public bool countsAsAddedPartOrImplant;
        public string? labelNoun;
        public float priceImpact;
        public InjuryProps? injuryProps;
        public AddedBodyPartProps? addedPartProps;
        public List<HediffStage>? stages;

        public bool IsInjury => typeof(Hediff_Injury).IsAssignableFrom(hediffClass);

        public bool HasComp(Type compClass)
        {
            if (comps == null) return false;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i].compClass == compClass) return true;
            }
            return false;
        }

        public T? CompProps<T>() where T : HediffCompProperties
        {
            if (comps == null) return null;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is T typed) return typed;
            }
            return null;
        }

        public HediffStage? StageAtSeverity(float severity)
        {
            int index = StageIndexAtSeverity(severity);
            return index < 0 ? null : stages![index];
        }

        /// <summary>Highest stage whose minSeverity is reached; -1 when the def has no stages.</summary>
        public int StageIndexAtSeverity(float severity)
        {
            if (stages == null || stages.Count == 0) return -1;
            for (int i = stages.Count - 1; i >= 0; i--)
            {
                if (severity >= stages[i].minSeverity) return i;
            }
            return 0;
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++) comps[i].ResolveReferences(this);
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(Hediff).IsAssignableFrom(hediffClass)) yield return "hediffClass must derive from Hediff.";
            if (IsInjury && injuryProps == null) yield return "injury hediffs need injuryProps.";
            if (typeof(Hediff_AddedPart).IsAssignableFrom(hediffClass) && addedPartProps == null) yield return "added-part hediffs need addedPartProps.";
            if (stages != null)
            {
                for (int i = 1; i < stages.Count; i++)
                {
                    if (stages[i].minSeverity < stages[i - 1].minSeverity) yield return "stages must be ordered by minSeverity.";
                }
            }
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    foreach (string error in comps[i].ConfigErrors(this)) yield return error;
                }
            }
        }
    }

    /// <summary>Data half of a hediff component (RimWorld: <c>Verse.HediffCompProperties</c>).</summary>
    public abstract class HediffCompProperties
    {
        public Type compClass = null!;

        protected HediffCompProperties(Type compClass)
        {
            this.compClass = compClass;
        }

        public virtual void ResolveReferences(HediffDef parent)
        {
        }

        public virtual IEnumerable<string> ConfigErrors(HediffDef parent)
        {
            if (compClass == null || !typeof(HediffComp).IsAssignableFrom(compClass))
            {
                yield return GetType().Name + " has an invalid compClass.";
            }
        }
    }

    /// <summary>Disease that the body fights: immunity grows while sick, severity grows until immune.</summary>
    public class HediffCompProperties_Immunizable : HediffCompProperties
    {
        public float immunityPerDaySick;
        public float severityPerDayNotImmune;
        public float immunityPerDayNotSick;
        public float severityPerDayImmune;

        public HediffCompProperties_Immunizable() : base(typeof(HediffComp_Immunizable))
        {
        }
    }

    /// <summary>Tending state and effect (RimWorld: <c>HediffCompProperties_TendDuration</c>).</summary>
    public class HediffCompProperties_TendDuration : HediffCompProperties
    {
        /// <summary>Hours a tend lasts; negative means a single tend is permanent (injuries).</summary>
        public float baseTendDurationHours = -1f;

        /// <summary>A timed tend may be renewed this many hours before it lapses.</summary>
        public float tendOverlapHours = 3f;

        public float severityPerDayTended;
        public bool showTendQuality = true;
        public float disappearsAtTotalTendQuality = -1f;

        public bool TendIsPermanent => baseTendDurationHours < 0f;

        public HediffCompProperties_TendDuration() : base(typeof(HediffComp_TendDuration))
        {
        }
    }

    /// <summary>Constant severity drift.</summary>
    public class HediffCompProperties_SeverityPerDay : HediffCompProperties
    {
        public float severityPerDay;

        public HediffCompProperties_SeverityPerDay() : base(typeof(HediffComp_SeverityPerDay))
        {
        }
    }

    /// <summary>Removes the hediff after a random duration.</summary>
    public class HediffCompProperties_Disappears : HediffCompProperties
    {
        public IntRange disappearsAfterTicks = new IntRange(60000, 60000);

        public HediffCompProperties_Disappears() : base(typeof(HediffComp_Disappears))
        {
        }
    }

    /// <summary>Injuries that may scar instead of healing fully.</summary>
    public class HediffCompProperties_GetsPermanent : HediffCompProperties
    {
        public float becomePermanentChanceFactor = 1f;
        public string? permanentLabel;

        public HediffCompProperties_GetsPermanent() : base(typeof(HediffComp_GetsPermanent))
        {
        }
    }

    /// <summary>Untended wounds can become infected.</summary>
    public class HediffCompProperties_Infecter : HediffCompProperties
    {
        public float infectionChance;

        public HediffCompProperties_Infecter() : base(typeof(HediffComp_Infecter))
        {
        }
    }
}
