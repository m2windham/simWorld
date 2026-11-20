namespace SimWorld.Designations
{
    /// <summary>
    /// Who placed a <see cref="Designation"/>. RimWorld has no such field — there, only the player designates.
    /// Here a settlement's own initiatives place marks too (<see cref="AI.MiningInitiative"/>), and the player
    /// can place them through <c>Map.View.MapCommands.Designate</c>, so every mark says which: the player's are
    /// worked first (<see cref="AI.WorkGiver_PlayerDesignated"/>) and survive the settlement's initiative being
    /// switched off (<see cref="DesignationManager.SettlementInitiativeOn"/>).
    /// <para/><see cref="Settlement"/> is the zero value on purpose: a save written before this field existed
    /// loads every mark it holds as the settlement's, which is exactly who placed them.
    /// </summary>
    public enum DesignationSource
    {
        Settlement,
        Player,
    }
}
