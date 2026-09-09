namespace SimWorld.Crafting
{
    /// <summary>
    /// Tuning for produce cycles and butchering (system: Animals — <c>crafting.animals</c> in
    /// <c>docs/status.json</c>). RimWorld scales real meat/leather/wool/egg yields off body-size-derived
    /// stats (<c>StatDefOf.MeatAmount</c> etc.) whose exact curve is not available to this port; the
    /// constants below are this port's own linear stand-in. What each backs is pinned by trend tests in
    /// <c>CraftingTests</c> — never the literal. See <see cref="Pawns.AnimalTuning"/> for the taming/training
    /// side of the same module.
    /// </summary>
    public static class HusbandryTuning
    {
        /// <summary>Meat yield (in stack-count units of the race's <see cref="Pawns.RaceProperties.meatDef"/>)
        /// per point of <see cref="Pawns.Pawn.BodySize"/>. Backs "a bigger animal yields more meat".</summary>
        public const float MeatPerBodySize = 25f;

        /// <summary>Leather yield per point of body size. Backs "a bigger animal yields more leather".</summary>
        public const float LeatherPerBodySize = 20f;

        /// <summary>Cadence <c>CompHasGatherableBodyResource</c> actually accrues fullness on — coarse
        /// (~33s of game time), never per-tick-per-animal work (<c>docs/perf/baseline.md</c>).</summary>
        public const int ProduceCheckIntervalTicks = Sim.GenTicks.TickLongInterval;
    }
}
