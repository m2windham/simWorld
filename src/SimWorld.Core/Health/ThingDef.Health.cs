using SimWorld.Health;

namespace SimWorld.Defs
{
    /// <summary>
    /// Health-layer half of <see cref="ThingDef"/> (system: Health — medicine), in its own file the way
    /// <c>Building/ThingDef.Plant.cs</c> and every other per-module slice of this partial class already are.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Whether this item is medicine at all (RimWorld: <c>Verse.ThingDef.IsMedicine</c> — "has a
        /// <see cref="HealthStatDefOf.MedicalPotency"/> statBases entry"), the same reading
        /// <see cref="MedicineUtility.FindBestMedicine"/> uses to recognise a stockpiled stack with no
        /// hardcoded list of medicine defNames.</summary>
        public bool IsMedicine => StatBaseDefined(HealthStatDefOf.MedicalPotency);
    }
}
