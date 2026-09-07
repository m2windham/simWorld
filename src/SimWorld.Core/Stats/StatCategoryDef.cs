using SimWorld.Defs;

namespace SimWorld.Stats
{
    /// <summary>
    /// A display grouping for stats (RimWorld: <c>RimWorld.StatCategoryDef</c>), e.g. "BasicsPawn". Trimmed to
    /// a bare tag: no stats UI exists to group by it yet (see <see cref="StatWorker.ShouldShowFor"/>), but
    /// content can still name a real category so future UI work has authored data to read.
    /// </summary>
    public class StatCategoryDef : Def
    {
    }
}
