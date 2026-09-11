using SimWorld.Sim;

namespace SimWorld.Offices
{
    /// <summary>
    /// Numbers this module owns. RimWorld has no station to port, so none of them are RimWorld's: each is
    /// pinned by a behaviour test (a band, a trend, an invariant) rather than trusted as a literal, per
    /// CLAUDE.md's rule for anything that could not be sourced.
    /// </summary>
    public static class OfficeTuning
    {
        /// <summary>
        /// How often seats are reconciled. The long tick, like every other civilization-scale sweep here
        /// (<c>GodManager.GodTick</c>, <c>Storyteller.StorytellerTick</c>, <c>FamilyManager.DemographyTick</c>,
        /// <c>GuildManager.GuildManagerTick</c>) — a succession is a fact about the civilization, not an
        /// event that has to land on the exact tick a leader stopped breathing. It also sets the worst-case
        /// interregnum: a settlement is leaderless for at most one sweep, and during it nobody in that town
        /// carries the stewardship's <c>RoleDef</c>, so the work it emphasizes is not emphasized by anyone.
        /// </summary>
        public const int ReconcileIntervalTicks = GenTicks.TickLongInterval;

        /// <summary>
        /// How many citizens' opinions are counted in an election
        /// (<see cref="OfficeSelectionWorker_Esteem"/>), most senior first. Not a performance constant with a
        /// story attached — see that class's own doc: opinion models people who know each other, and a live
        /// roster in this port can reach tens of thousands. What the tests pin is that the electorate is
        /// bounded and deterministic, never this number.
        /// </summary>
        public const int ElectorateCap = 250;
    }
}
