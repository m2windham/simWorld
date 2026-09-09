using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>A pawn's whole priority tree, loaded from content (RimWorld: <c>Verse.AI.ThinkTreeDef</c>).</summary>
    public class ThinkTreeDef : Def
    {
        public ThinkNode thinkRoot = null!;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (thinkRoot == null) yield return "thinkRoot is required.";
        }
    }

    [DefOf]
    public static class ThinkTreeDefOf
    {
        public static ThinkTreeDef Humanlike = null!;

        /// <summary>A real second tree (system: ai.animals) — not the humanlike one with branches skipped;
        /// see <see cref="Pawn_JobTracker"/>'s own doc for how a pawn's race picks between the two.</summary>
        public static ThinkTreeDef Animal = null!;
    }
}
