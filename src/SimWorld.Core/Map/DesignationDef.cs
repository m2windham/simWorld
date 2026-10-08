using SimWorld.Defs;

namespace SimWorld.Map
{
    /// <summary>What a <see cref="Designation"/> is aimed at (RimWorld: <c>Verse.TargetType</c>).</summary>
    public enum TargetType : byte
    {
        /// <summary>The designation sits on one Thing and follows it.</summary>
        Thing,

        /// <summary>The designation sits on a cell, whatever stands in it — a mine mark outlives nothing but
        /// the rock it was drawn over.</summary>
        Cell,
    }

    /// <summary>
    /// A kind of mark somebody can put on the map for citizens to act on (RimWorld: <c>Verse.DesignationDef</c>).
    /// Mining's <c>Mine</c> is the first one shipped — see <see cref="DesignationDefOf"/>.
    ///
    /// <para/>Ported field for field as far as this port has a reader. <c>texturePath</c> and the
    /// <c>iconMat</c> it resolves to are how RimWorld draws the mark; this core is engine-free and the host
    /// draws from a <c>defName</c>, so there is nothing here for either to be read by.
    /// </summary>
    public class DesignationDef : Def
    {
        /// <summary>Whether the mark is indexed by <see cref="Designation.target"/>'s Thing or by its cell.
        /// <see cref="DesignationManager"/> refuses to look a cell-targeted def up by Thing and vice versa,
        /// exactly as RimWorld's does.</summary>
        public TargetType targetType;

        /// <summary>Whether <see cref="DesignationManager.Notify_BuildingDespawned"/> takes this mark away
        /// when the edifice standing in its cell leaves the map. This is the whole of how a
        /// <c>Mine</c> mark ends: the rock is mined out, it despawns, and its mark goes with it.</summary>
        public bool removeIfBuildingDespawned;

        /// <summary>Whether <see cref="DesignationManager.RemoveAllDesignationsOn"/> removes this mark when
        /// asked to cancel in the ordinary way (RimWorld: <c>DesignationDef.designateCancelable</c>).</summary>
        public bool designateCancelable = true;
    }
}
