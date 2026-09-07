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
    }
}
