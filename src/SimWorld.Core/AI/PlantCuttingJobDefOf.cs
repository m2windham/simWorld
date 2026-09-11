using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>The JobDef <see cref="WorkGiver_PlantsCut"/> issues (RimWorld: <c>JobDefOf.CutPlant</c>),
    /// bound by defName in its own <c>[DefOf]</c> class the way <c>Building.BuildingJobDefOf</c> already is
    /// — see <see cref="RepairJobDefOf"/>'s own remark on why each of these gets a file of its own.</summary>
    [DefOf]
    public static class PlantCuttingJobDefOf
    {
        /// <summary>Clears one standing plant out of the way, yielding whatever it had grown
        /// (system: work — <see cref="WorkGiver_PlantsCut"/>/<see cref="JobDriver_PlantWork"/>).</summary>
        public static JobDef CutPlant = null!;
    }
}
