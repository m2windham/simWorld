using SimWorld.Defs;

namespace SimWorld.Health
{
    /// <summary>
    /// Stats a tend actually spends (RimWorld: the medicine/tend-quality slice of <c>RimWorld.StatDefOf</c>).
    /// Its own class per CLAUDE.md ("a new [DefOf] binding goes in its own class, not appended to" the
    /// codebase's one existing <see cref="Stats.StatDefOf"/>) — these four bind together because
    /// <see cref="Health.TendUtility.CalculateBaseTendQuality"/> is the one place that reads all of them.
    /// </summary>
    [DefOf]
    public static class HealthStatDefOf
    {
        /// <summary>How much a unit of medicine is worth in <see cref="TendUtility.CalculateBaseTendQuality"/>'s
        /// multiplier (RimWorld: <c>StatDefOf.MedicalPotency</c>) — none 0.3, herbal 0.6, industrial 1.0.</summary>
        public static StatDef MedicalPotency = null!;

        /// <summary>The tend-quality ceiling a medicine's own ItemDef imposes, regardless of doctor skill
        /// (RimWorld: <c>StatDefOf.MedicalQualityMax</c>) — none/herbal 0.7, industrial 1.0.</summary>
        public static StatDef MedicalQualityMax = null!;

        /// <summary>
        /// A bed's immunity-gain multiplier while a pawn actually lies in it (RimWorld:
        /// <c>StatDefOf.BedRestEffectiveness</c>, read by <see cref="Stats.StatPart_BedStat"/> off
        /// <see cref="AI.RestUtility.CurrentBed"/>). <b>Not the same thing as</b>
        /// <see cref="Needs.Need_Rest.BedRestEffectiveness"/> — that is this port's own pre-existing, unsourced
        /// rest-need gain-rate constant (a different RimWorld stat role entirely, never wired to content); this
        /// one is the real, content-authored stat RimWorld's own bed ThingDefs set per bed type.
        /// </summary>
        public static StatDef BedRestEffectiveness = null!;

        /// <summary>Flat addition to tend quality for lying in a particular bed (RimWorld:
        /// <c>StatDefOf.MedicalTendQualityOffset</c>) — 0 for this port's one ordinary bed, the same as
        /// RimWorld's own regular bed; only a hospital bed (not shipped here) is ever above zero.</summary>
        public static StatDef MedicalTendQualityOffset = null!;
    }

    /// <summary>The medicine items this port ships (RimWorld: the medicine slice of <c>RimWorld.ThingDefOf</c>).</summary>
    [DefOf]
    public static class MedicineDefOf
    {
        /// <summary>Grown from Healroot in RimWorld; here, this era's founding band simply starts with some —
        /// see <c>Scenarios.xml</c>'s own comment for why (system: Health — medicine).</summary>
        public static ThingDef MedicineHerbal = null!;

        /// <summary>Ships as content for <see cref="MedicalCareCategory.NormalOrWorse"/>'s threshold and a
        /// future crafting chain; no recipe makes it yet (RimWorld's needs a drug lab and Neutroamine, neither
        /// of which this port has) — see this module's report.</summary>
        public static ThingDef MedicineIndustrial = null!;
    }
}
