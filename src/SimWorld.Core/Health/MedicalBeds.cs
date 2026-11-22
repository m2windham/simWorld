using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Things;

namespace SimWorld.Health
{
    /// <summary>Defs medicine-by-era reads by name. Its own <c>[DefOf]</c> class, per CLAUDE.md.</summary>
    [DefOf]
    public static class MedicalEraDefOf
    {
        /// <summary>RimWorld's hospital bed (<c>Buildings_Medical.xml</c>), gated on <see cref="Medicine"/>.</summary>
        public static ThingDef HospitalBed = null!;

        /// <summary>RimWorld's drug lab, where <see cref="Make_MedicineIndustrial"/> is worked; gated on
        /// <see cref="Pharmacology"/>.</summary>
        public static ThingDef DrugLab = null!;

        /// <summary>Two herbal doses distilled into one industrial dose (<c>Recipes_Medicine.xml</c>).</summary>
        public static RecipeDef Make_MedicineIndustrial = null!;

        /// <summary>Classical: the first hospital beds.</summary>
        public static ResearchProjectDef Medicine = null!;

        /// <summary>Medieval: a hospital, so more of its beds.</summary>
        public static ResearchProjectDef Hospitals = null!;

        /// <summary>Medieval: the apothecary's bench and the medicine it makes.</summary>
        public static ResearchProjectDef Pharmacology = null!;
    }

    /// <summary>
    /// Beds for the sick (RimWorld: the hospital bed, and the <c>Medical</c> flag on <c>Building_Bed</c> that
    /// keeps a bed for patients). This port's beds are all ordinary <see cref="ConstructionThingDefOf.Bed"/>s
    /// and every bed-reading path named that one def; a hospital bed is a second bed def, so the paths a patient
    /// takes read <see cref="IsBed"/> instead, and the sleeping paths keep reading <see cref="ConstructionThingDefOf.Bed"/>
    /// alone. That is RimWorld's medical flag in effect: nobody sleeps the night in a hospital bed, and a
    /// patient goes to one before an ordinary bed.
    /// </summary>
    public static class MedicalBeds
    {
        /// <summary>Citizens per hospital bed once <see cref="MedicalEraDefOf.Medicine"/> is known: a sick-house
        /// with a few cots. Unsourced (RimWorld's player sizes the hospital); pinned by a trend test, not by
        /// the number.</summary>
        public const int CitizensPerBedSickHouse = 10;

        /// <summary>Citizens per hospital bed once <see cref="MedicalEraDefOf.Hospitals"/> is known: "treats
        /// more people than any single healer could". Unsourced, pinned by the same trend test.</summary>
        public const int CitizensPerBedHospital = 5;

        /// <summary>A bed of either kind: what a patient may lie in.</summary>
        public static bool IsBed(ThingDef? def) =>
            def != null && (ReferenceEquals(def, ConstructionThingDefOf.Bed) || IsHospitalBed(def));

        /// <summary>A bed kept for the sick.</summary>
        public static bool IsHospitalBed(ThingDef? def) =>
            def != null && MedicalEraDefOf.HospitalBed != null && ReferenceEquals(def, MedicalEraDefOf.HospitalBed);

        /// <summary>
        /// Hospital beds the settlement wants for <paramref name="citizens"/> living people: none until it knows
        /// <see cref="MedicalEraDefOf.Medicine"/>, then one per <see cref="CitizensPerBedSickHouse"/>, then one
        /// per <see cref="CitizensPerBedHospital"/> once it knows <see cref="MedicalEraDefOf.Hospitals"/>.
        /// Rounded up, so a band with any people has at least one.
        /// </summary>
        public static int HospitalBedsWanted(int citizens)
        {
            if (citizens <= 0 || !MedicalEraDefOf.HospitalBed.IsResearchFinished) return 0;
            int per = MedicalEraDefOf.Hospitals.IsFinished ? CitizensPerBedHospital : CitizensPerBedSickHouse;
            return (citizens + per - 1) / per;
        }

        /// <summary>
        /// The nearest hospital bed <paramref name="pawn"/> can reserve and reach, or null (RimWorld:
        /// <c>RestUtility.FindPatientBedFor</c> trying medical beds first). The caller falls back to an
        /// ordinary bed, as RimWorld's does.
        /// </summary>
        public static Thing? FindHospitalBedFor(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || MedicalEraDefOf.HospitalBed == null) return null;

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(MedicalEraDefOf.HospitalBed);
            for (int i = 0; i < beds.Count; i++)
            {
                Thing bed = beds[i];
                if (!bed.Spawned || bed.IsBurning()) continue;
                if (!map.reservationManager.CanReserve(pawn, bed)) continue;
                int distSq = (bed.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!Reachability.CanReach(pawn, bed, PathEndMode.OnCell)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }
    }
}
