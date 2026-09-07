namespace SimWorld.Pawns
{
    /// <summary>A pawn's sex (RimWorld: <c>Verse.Gender</c>). Drives name-bank selection, trait/backstory
    /// commonality (<see cref="TraitDef.commonalityFemale"/>) and, later, pronoun text and appearance.</summary>
    public enum Gender
    {
        None,
        Male,
        Female,
    }
}
