using SimWorld.Defs;

namespace SimWorld.Designations
{
    /// <summary>What a <see cref="Designation"/> is aimed at (RimWorld: <c>Verse.TargetType</c>).</summary>
    public enum TargetType
    {
        Thing,
        Cell,
    }

    /// <summary>
    /// A kind of mark on the map that asks for work (RimWorld: <c>Verse.DesignationDef</c>). Only the fields this
    /// port reads are ported: the target type, and whether a cell designation goes when the building on its cell
    /// does. RimWorld's icon, sound and designator fields belong to the player's UI, which lives in the host.
    /// </summary>
    public class DesignationDef : Def
    {
        public TargetType targetType;

        /// <summary>A cell designation is removed when a building standing on its cell despawns, which is how a
        /// Mine designation disappears once its rock is mined (RimWorld: <c>Building.DeSpawn</c> calls
        /// <c>DesignationManager.Notify_BuildingDespawned</c>).</summary>
        public bool removeIfBuildingDespawned;
    }

    [DefOf]
    public static class DesignationDefOf
    {
        public static DesignationDef Mine = null!;
    }
}
