using SimWorld.Factions;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Who a doctor may act on: their own faction's own pawns, or a prisoner that faction currently holds —
    /// shared by <see cref="WorkGiver_Tend"/> and <see cref="WorkGiver_RescueDowned"/> so the two never drift
    /// apart on the same "prisoners versus colonists" question (RimWorld's own <c>WorkGiver_Tend</c>/
    /// <c>WorkGiver_Rescuer</c> both tend/rescue a prisoner exactly as readily as a colonist, gated only by
    /// the colony's medical-care setting for that prisoner — a per-prisoner setting this port does not have,
    /// so here it is simply allowed whenever the prisoner is held at all). A captured pawn keeps its original
    /// <see cref="Pawn.faction"/> even once a prisoner (see <see cref="Pawn_GuestTracker"/>'s own doc on why
    /// that state lives on the <em>host</em> faction instead), so this checks the host-faction reverse lookup
    /// rather than plain faction equality — the same check <see cref="WorkGiver_Warden_Feed"/> already applies
    /// to its own, narrower (prisoners-only) scan.
    /// </summary>
    public static class DoctorUtility
    {
        public static bool IsCaredForBy(Pawn carer, Pawn patient)
        {
            if (carer.faction == null) return false;
            if (ReferenceEquals(patient.faction, carer.faction)) return true;
            return ReferenceEquals(CaptureUtility.FindHostFaction(patient), carer.faction);
        }
    }
}
