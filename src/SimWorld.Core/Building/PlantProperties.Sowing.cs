using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Defs
{
    /// <summary>
    /// The sowing-skill half of <see cref="PlantProperties"/>, in its own partial file (CLAUDE.md: add a file
    /// rather than edit a shared one) — RimWorld's <c>PlantProperties.sowMinSkill</c> and the one rule that
    /// reads it.
    /// </summary>
    public partial class PlantProperties
    {
        /// <summary>
        /// Plants skill a citizen needs before they will sow this plant (RimWorld:
        /// <c>PlantProperties.sowMinSkill</c>, default 0 — anyone). Healroot is the first species that sets it
        /// (8, the wiki's figure). Read by <see cref="Building.WorkGiver_GrowerSow"/> exactly where RimWorld's
        /// <c>WorkGiver_GrowerSow.JobOnCell</c> reads it, and by <see cref="Building.FarmingInitiative"/> to
        /// ask whether anyone in the settlement could sow the plant at all (RimWorld:
        /// <c>Command_SetPlantToGrow.WarnAsAppropriate</c> asks the same question of the player).
        /// </summary>
        public int sowMinSkill;

        /// <summary>
        /// Whether <paramref name="pawn"/> is skilled enough to sow this plant. RimWorld's test, restated:
        /// <c>sowMinSkill &gt; 0 &amp;&amp; pawn.skills != null &amp;&amp; level &lt; sowMinSkill</c> refuses, so a
        /// pawn with no skills tracker at all is never refused, and a plant that sets no minimum is sowable by
        /// anyone.
        /// </summary>
        public bool PawnMeetsSowMinSkill(Pawn pawn)
        {
            if (sowMinSkill <= 0 || pawn.skills == null) return true;
            return (pawn.skills.GetSkill(SkillDefOf.Plants)?.Level ?? 0) >= sowMinSkill;
        }
    }
}
