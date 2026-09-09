using System;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// Data half of <see cref="CompDrug"/> (RimWorld: <c>RimWorld.CompProperties_Drug</c>), trimmed to what
    /// health.addictions needs: tolerance building, addiction, and the joy a recreational dose gives. RimWorld's
    /// own overdose mechanic is left out — nothing in this pass models an overdose death, so promising one
    /// through content would be undeliverable; see <c>CompDrug</c>'s own remarks.
    /// </summary>
    public class CompProperties_Drug : CompProperties
    {
        /// <summary>Null for a non-addictive ingestible that still wants tolerance-free joy (not used by this
        /// pass's content, but kept optional rather than required — RimWorld's own Penoxycyline has none either).</summary>
        public ChemicalDef? chemical;

        /// <summary>Joy kind a dose grants alongside <c>ThingDef.ingestible.joy</c> (RimWorld nests this the
        /// same way: the drug names which <c>JoyKindDef</c> tolerance bucket its joy builds).</summary>
        public JoyKindDef? joyKind;

        /// <summary>Tolerance severity gained per dose. SimWorld's own number, not sourced from RimWorld's real
        /// per-drug rates; <c>DrugTests</c> pins the trend (repeated doses raise tolerance) rather than the
        /// literal.</summary>
        public float toleranceGainPerDose = 0.2f;

        /// <summary>Chance a dose starts the addiction once tolerance is at/above <see cref="ChemicalDef.minToleranceToAddict"/>. SimWorld's own number.</summary>
        public float addictionChancePerDoseAboveThreshold = 0.15f;

        /// <summary>Severity the addiction hediff starts at when a dose rolls it into existence. SimWorld's own number.</summary>
        public float addictionInitialSeverity = 0.2f;

        /// <summary>How much severity one dose relieves off an existing addiction — satisfying the craving for
        /// a while, rather than curing it (see <c>CompDrug.PostIngested</c>'s remarks). SimWorld's own number.</summary>
        public float doseSatisfiesSeverity = 0.35f;

        public CompProperties_Drug()
        {
            compClass = typeof(CompDrug);
        }
    }

    /// <summary>
    /// A drug's effect on the pawn who takes it (RimWorld: <c>RimWorld.CompDrug</c>). Called by
    /// <see cref="DrugIngestUtility.Ingest"/> once per dose consumed.
    ///
    /// <b>Model (health.addictions, this pass's own — RimWorld's exact internal wiring between tolerance,
    /// addiction and withdrawal isn't reproduced here, only its shape):</b> every dose raises the chemical's
    /// <see cref="ChemicalDef.toleranceHediff"/> severity (and, separately, that hediff decays slowly per day —
    /// see its content — so a pawn who stops using eventually loses their tolerance). Once tolerance is at or
    /// above <see cref="ChemicalDef.minToleranceToAddict"/>, each further dose has a chance to start the
    /// <see cref="ChemicalDef.addictionHediff"/>. Once addicted, that hediff itself climbs on its own between
    /// doses (its content gives it a positive <c>severityPerDay</c> — the craving building) and each new dose
    /// knocks its severity back down instead of adding to tolerance again; <see cref="Thoughts.ThoughtWorker_Hediff"/>
    /// (already real; see its own remarks) turns the addiction hediff's stage straight into a mood-affecting
    /// Thought, which is how withdrawal actually reaches <c>Need_Mood</c> — no new Needs/Thoughts code, only content.
    /// <b>Left out:</b> overdose, and any chance of the addiction curing itself outright — both real RimWorld
    /// mechanics, both out of this pass's scope.
    /// </summary>
    public class CompDrug : ThingComp
    {
        public CompProperties_Drug Props => (CompProperties_Drug)props;

        public void PostIngested(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            IngestibleProperties? ingestible = parent.def.ingestible;
            if (ingestible != null && ingestible.joy > 0f && Props.joyKind != null)
            {
                pawn.needs.joy?.GainJoy(ingestible.joy, Props.joyKind);
            }

            ChemicalDef? chemical = Props.chemical;
            if (chemical == null) return;

            if (chemical.addictionHediff != null)
            {
                Hediff? existingAddiction = pawn.health.hediffSet.GetFirstHediffOfDef(chemical.addictionHediff);
                if (existingAddiction != null)
                {
                    existingAddiction.Severity -= Props.doseSatisfiesSeverity;
                    if (chemical.toleranceHediff != null)
                    {
                        HealthUtility.AdjustSeverity(pawn, chemical.toleranceHediff, Props.toleranceGainPerDose);
                    }
                    return;
                }
            }

            float toleranceBefore = chemical.toleranceHediff != null
                ? pawn.health.hediffSet.GetFirstHediffOfDef(chemical.toleranceHediff)?.Severity ?? 0f
                : 0f;

            if (chemical.toleranceHediff != null)
            {
                HealthUtility.AdjustSeverity(pawn, chemical.toleranceHediff, Props.toleranceGainPerDose);
            }

            if (chemical.addictionHediff != null
                && toleranceBefore >= chemical.minToleranceToAddict
                && Rand.Chance(Props.addictionChancePerDoseAboveThreshold))
            {
                HealthUtility.AdjustSeverity(pawn, chemical.addictionHediff, Props.addictionInitialSeverity);
            }
        }
    }
}
