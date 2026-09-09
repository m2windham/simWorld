using SimWorld.Defs;

namespace SimWorld.Factions
{
    [DefOf]
    public static class FactionDefOf
    {
        public static FactionDef PlayerCivilization = null!;
    }

    /// <summary>The one <see cref="PawnGroupKindDef"/> anything actually generates a squad as today.</summary>
    [DefOf]
    public static class PawnGroupKindDefOf
    {
        public static PawnGroupKindDef Combat = null!;
    }
}
