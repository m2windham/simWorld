using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>The core generatable pawn kinds, bound by defName after content loads.</summary>
    [DefOf]
    public static class PawnKindDefOf
    {
        public static PawnKindDef Colonist = null!;
        public static PawnKindDef Villager = null!;
        public static PawnKindDef Tribesperson = null!;
        public static PawnKindDef Husky = null!;
    }

    /// <summary>The life stages other systems reference by name, bound by defName after content loads.</summary>
    [DefOf]
    public static class LifeStageDefOf
    {
        public static LifeStageDef HumanlikeAdult = null!;
        public static LifeStageDef HumanlikeChild = null!;
    }
}
