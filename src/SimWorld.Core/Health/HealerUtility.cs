using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Health
{
    /// <summary>
    /// Who the settlement lets doctor: its best healers, as a RimWorld player would arrange it on the work tab.
    /// <para/>
    /// <b>The RimWorld mechanic.</b> RimWorld's <c>WorkGiver_Tend</c> takes any colonist with Doctor enabled, and a
    /// new colonist starts with Doctor enabled (from memory of <c>WorkTypes.xml</c>'s <c>alwaysStartActive</c>, not
    /// checked from here). An unmanaged colony therefore tends the way this port did: whoever is nearest and free,
    /// at their own skill. What makes skill matter in RimWorld is the player, who sets the best doctor or two to
    /// Doctor priority 1 and takes it off everyone else. A god game has no work tab, so the settlement makes that
    /// choice itself. The live map now matches the abstract one, too: <see cref="AbstractDiseaseResolver"/> has
    /// always assumed an unwatched citizen is tended by the best doctor on their roster.
    /// <para/>
    /// <b>The rule.</b> Among the citizens on the map who could doctor right now, the top
    /// <c>ceil(n / </c><see cref="CitizensPerHealer"/><c>)</c> by Medicine skill are healers. Routine tending
    /// (<c>DoctorTend</c>) is theirs. Everyone else takes a routine patient only when no healer is standing,
    /// so a band whose healers are all down still tends its own. Emergency tending (<c>DoctorTendEmergency</c>,
    /// bleeding that kills inside the day) and rescue stay open to anyone: there speed beats skill, and the lane
    /// that put emergency work above sleep showed what waiting costs.
    /// <para/>
    /// <b>Stateless.</b> Healers are not stored: the answer is recomputed from the map each time a doctor asks, as
    /// <see cref="Offices.OfficeManager"/> reads a seat from the roster rather than from a pointer. Nothing to save,
    /// nothing to drift after a load, and succession is automatic: when the healer dies, the next best is one.
    /// <para/>
    /// <b>Across the eras.</b> The rule does not change; what it selects for does. A band of twenty has three or
    /// four healers, the herb-wise few; a town of sixty has ten, a profession. Concentrating the work also
    /// concentrates the learning (<see cref="AI.JobDriver_TendPatient"/> now grants Medicine experience per tend),
    /// so a settlement grows its healers instead of spreading the craft thin. Medicine potency (herbal 0.6,
    /// industrial 1.0) and a medical bed's tend offset still multiply and add on top, in
    /// <see cref="TendUtility.CalculateBaseTendQuality"/>.
    /// </summary>
    public static class HealerUtility
    {
        /// <summary>
        /// Citizens each healer serves. Not sourced: RimWorld has no such number, because the player decides. Six
        /// is a colony of six to ten running one doctor and a backup, the usual arrangement. Pinned by tests as
        /// behaviour (the best doctor is a healer, the worst is not while better ones stand, the count grows with
        /// the town), not as the literal.
        /// </summary>
        public const int CitizensPerHealer = 6;

        /// <summary>Whether <paramref name="pawn"/> could doctor right now: humanlike, of a faction, standing, sane,
        /// Doctor active and hands that work. Sleeping counts: a healer asleep will wake, and a poor tend now holds
        /// the wound for hours before a good one may replace it.</summary>
        public static bool CanDoctorNow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.faction == null) return false;
            if (!pawn.RaceProps.Humanlike || pawn.InMentalState) return false;
            if (pawn.workSettings == null || !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor)) return false;
            return pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        /// <summary>How many healers a settlement of <paramref name="able"/> able doctors keeps: at least one.</summary>
        public static int HealerCount(int able) => able <= 0 ? 0 : (able + CitizensPerHealer - 1) / CitizensPerHealer;

        /// <summary>
        /// Whether <paramref name="doctor"/> is one of the healers of their faction on their map, leaving
        /// <paramref name="patient"/> out of the count (a healer who is the patient cannot treat themself, so the
        /// next best steps up). Ranked by Medicine level, ties to the lower thing id so the answer never depends on
        /// list order. A doctor who cannot doctor now is not a healer.
        /// </summary>
        public static bool IsHealer(Pawn doctor, Pawn? patient = null)
        {
            if (!CanDoctorNow(doctor)) return false;
            IReadOnlyList<Pawn> pawns = doctor.Map!.mapPawns.AllPawnsSpawned;
            int mine = MedicineLevel(doctor);
            int able = 0;
            int above = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (ReferenceEquals(other, patient) || !ReferenceEquals(other.faction, doctor.faction)) continue;
                if (!CanDoctorNow(other)) continue;
                able++;
                if (ReferenceEquals(other, doctor)) continue;
                int theirs = MedicineLevel(other);
                if (theirs > mine || (theirs == mine && other.thingIDNumber < doctor.thingIDNumber)) above++;
            }
            return above < HealerCount(able);
        }

        /// <summary>
        /// Whether <paramref name="doctor"/> should take a routine tend of <paramref name="patient"/>: they are a
        /// healer, or no healer of their faction stands on the map besides the patient. The second needs no scan
        /// of its own: an able doctor who is not a healer was outranked by at least one healer, so one stands; and
        /// a doctor <see cref="CanDoctorNow"/> refuses is left to the work giver's own checks, as before.
        /// </summary>
        public static bool ShouldTakeRoutineTend(Pawn doctor, Pawn patient) =>
            !CanDoctorNow(doctor) || IsHealer(doctor, patient);

        private static int MedicineLevel(Pawn pawn) => pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
    }
}
