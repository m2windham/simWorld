using System.Threading;

using SimWorld.Defs;

namespace SimWorld.Health
{
    /// <summary>
    /// How many tends this process has done, by what they used: no medicine, herbal, industrial, and how many were
    /// given in a hospital bed. A diagnostic for <c>tools/bench</c> (era-unlocks: what a settlement uses as well as
    /// what it builds), read and never saved, like <see cref="Combat.Verb_LaunchProjectile.LastShot"/>. Nothing in
    /// the simulation reads it, so it cannot change a run.
    /// </summary>
    public static class TendTally
    {
        private static int none;
        private static int herbal;
        private static int industrial;
        private static int other;
        private static int inHospitalBed;

        public static int None => none;
        public static int Herbal => herbal;
        public static int Industrial => industrial;
        public static int Other => other;
        public static int InHospitalBed => inHospitalBed;

        /// <summary>Counts one tend that used <paramref name="medicine"/> (null: none) in <paramref name="bed"/>.</summary>
        public static void Record(ThingDef? medicine, ThingDef? bed)
        {
            if (medicine == null) Interlocked.Increment(ref none);
            else if (ReferenceEquals(medicine, MedicineDefOf.MedicineHerbal)) Interlocked.Increment(ref herbal);
            else if (ReferenceEquals(medicine, MedicineDefOf.MedicineIndustrial)) Interlocked.Increment(ref industrial);
            else Interlocked.Increment(ref other);
            if (MedicalBeds.IsHospitalBed(bed)) Interlocked.Increment(ref inHospitalBed);
        }
    }
}
