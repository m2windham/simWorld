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

        /// <summary>A body (RimWorld: <c>ThingRequestGroup.Corpse</c>, every def whose class is <c>Corpse</c>).
        /// The hauler of the dead and the settlement's gravedigger ask for this instead of walking every item
        /// on the map to find the few that are bodies.</summary>
        Corpse,

        /// <summary>A Thing that is a place to haul to other than a stockpile cell — a grave (RimWorld keeps
        /// these in <c>HaulDestinationManager</c>; this port asks the lister, which already indexes by group).</summary>
        HaulDestination,
    }
}
