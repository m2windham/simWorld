using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>A tab of the research tree UI, grouping projects (RimWorld: <c>RimWorld.ResearchTabDef</c>).</summary>
    public class ResearchTabDef : Def
    {
        /// <summary>Order tabs appear in, lowest first.</summary>
        public int listOrder;

        public string? generalTitle;

        public string? generalDescription;
    }
}
