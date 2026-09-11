using SimWorld.AI;
using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// The <see cref="JobDef"/> and <see cref="ThingDef"/> the research work type binds by name — kept apart
    /// from <see cref="ResearchProjectDefOf"/>/<see cref="ResearchTabDefOf"/> above and, more importantly,
    /// apart from <c>AI.JobDefOf</c> in <c>AI/JobDef.cs</c>: that file is a shared hotspot every work-type
    /// lane's <c>JobDef</c> lands in, and four lanes are editing <c>WorkGivers.xml</c>-adjacent code in
    /// parallel worktrees this pass — a new <c>[DefOf]</c> class in a file only this module touches binds
    /// exactly the same way (<see cref="Defs.DefOfHelper"/> scans every <see cref="DefOfAttribute"/> class in
    /// the assembly, not a fixed list) without adding a merge collision to a file three other lanes are also
    /// touching this same round.
    /// </summary>
    [DefOf]
    public static class ResearchWorkDefOf
    {
        /// <summary>One work tick spent advancing <see cref="ResearchManager.CurrentProj"/> at a research
        /// bench (RimWorld: <c>JobDefOf.Research</c>).</summary>
        public static JobDef Research = null!;

        /// <summary>The single-tier bench this port requires for research work — see its own content
        /// comment (<c>Buildings_Research.xml</c>) for why it carries no research prerequisite of its own.</summary>
        public static ThingDef ResearchBench = null!;
    }
}
