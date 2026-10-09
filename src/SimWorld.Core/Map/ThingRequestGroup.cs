namespace SimWorld.Map
{
    /// <summary>
    /// Broad category a caller can ask <see cref="ListerThings.ThingsInGroup"/> for (RimWorld:
    /// <c>Verse.ThingRequestGroup</c>). A group is a list <see cref="ListerThings"/> keeps current as Things
    /// spawn and leave, so a work giver that wants "the benches" or "the research benches" reads a short list
    /// instead of walking every Thing of a broad kind and discarding nearly all of them. Which defs a group
    /// holds is <see cref="ThingRequestGroupUtility.Includes"/> (RimWorld: <c>ThingListGroupHelper.Includes</c>).
    /// <para/>
    /// Members are appended after the originals so no existing value moves. RimWorld's own groups this port
    /// has not needed yet (<c>Corpse</c>, <c>Medicine</c>, <c>Weapon</c> ...) are added when a caller does.
    /// </summary>
    public enum ThingRequestGroup
    {
        Undefined,
        Everything,
        Pawn,
        Building,
        Item,
        Plant,
        HaulableEver,
        BuildingArtificial,
        Filth,

        /// <summary>Blueprints awaiting materials (system 16: Building).</summary>
        Blueprint,

        /// <summary>Frames under construction (system 16: Building).</summary>
        BuildingFrame,

        /// <summary>Anything that can hold a bill stack: a def that lists recipes or carries a
        /// <see cref="Things.CompProperties_BillGiver"/> (RimWorld: <c>ThingRequestGroup.PotentialBillGiver</c>,
        /// <c>!def.AllRecipes.NullOrEmpty()</c>). <see cref="Crafting.WorkGiver_DoBill"/> asks for this instead of
        /// every building.</summary>
        PotentialBillGiver,

        /// <summary>The research bench (RimWorld: <c>ThingRequestGroup.ResearchBench</c>, every def whose class
        /// is <c>Building_ResearchBench</c>). <see cref="AI.WorkGiver_Research"/> asks for this instead of every
        /// building.</summary>
        ResearchBench,
    }
}
