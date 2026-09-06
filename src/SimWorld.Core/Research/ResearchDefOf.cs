using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>Core research projects referenced directly from code.</summary>
    [DefOf]
    public static class ResearchProjectDefOf
    {
        public static ResearchProjectDef Fire = null!;
        public static ResearchProjectDef StoneTools = null!;
        public static ResearchProjectDef Agriculture = null!;
        public static ResearchProjectDef Writing = null!;
    }

    /// <summary>The one research tab the seed content ships.</summary>
    [DefOf]
    public static class ResearchTabDefOf
    {
        public static ResearchTabDef Main = null!;
    }
}
