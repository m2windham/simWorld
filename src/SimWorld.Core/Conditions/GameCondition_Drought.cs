using SimWorld.Sim;

namespace SimWorld.Conditions
{
    /// <summary>
    /// A drought: while it lasts, <see cref="Severity"/> scales every growth-rate and off-map production
    /// figure reaching this scope — see <see cref="GameConditionManager.AggregateGrowthFactor"/>. RimWorld has
    /// no drought condition to port; this is the multiplicative mirror of
    /// <see cref="GameCondition_TemperatureOffset"/> for an additive one.
    ///
    /// <para/><b>Why severity lives on the instance and not on the def.</b> <see cref="GameCondition_TemperatureOffset"/>
    /// reads its number straight off <see cref="GameConditionDef.temperatureOffset"/> because a heat wave is
    /// always the same heat wave. A drought is not: "a bad year" is a matter of degree, and a single
    /// content-authored number would make every drought in a game's history identical. So
    /// <see cref="IncidentWorker_Drought"/> rolls it once per firing, through the seeded stream, and carries
    /// it here — the same reason <c>Pawn</c> generation rolls traits onto the instance rather than reading
    /// them off <c>PawnKindDef</c>.
    ///
    /// <para/><b>Never exactly 0.</b> <see cref="IncidentWorker_Drought.SeverityRange"/>'s floor is above zero:
    /// a drought thins a harvest, it does not annihilate one, and a factor of exactly 0 here would make
    /// <see cref="Director.ResourceImpactLedger"/> unable to tell "a catastrophic drought" from "land that was
    /// never going to grow anything regardless" — both would show the same total denial once the base rate is
    /// already 0. Keeping the floor above 0 keeps those two cases distinguishable at the instrument.
    /// </summary>
    public class GameCondition_Drought : GameCondition
    {
        /// <summary>The multiplier this firing applies, in (0,1]. 1 — no effect — until
        /// <see cref="IncidentWorker_Drought"/> sets it, so a condition somehow ticked before that costs
        /// nothing rather than zeroing every field it reaches.</summary>
        public float Severity { get; set; } = 1f;

        public override float GrowthFactor() => Severity;

        public override void ExposeData()
        {
            base.ExposeData();
            float severity = Severity;
            Scribe_Values.Look(ref severity, "severity", 1f);
            Severity = severity;
        }
    }
}
