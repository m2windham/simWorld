using System;

namespace SimWorld.Thoughts
{
    /// <summary>Builds a thought of a def, of the class the def names (RimWorld: <c>RimWorld.ThoughtMaker</c>).
    /// <see cref="MemoryThoughtHandler.TryGainMemory(ThoughtDef, Pawns.Pawn?)"/> does the same inline; a giver
    /// that has to set something on the memory before the pawn gains it (a target) needs it in hand first.</summary>
    public static class ThoughtMaker
    {
        public static Thought_Memory MakeMemory(ThoughtDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!def.IsMemory) throw new ArgumentException(def.defName + " is not a memory thought.", nameof(def));
            var memory = (Thought_Memory)Activator.CreateInstance(def.ThoughtClass)!;
            memory.def = def;
            return memory;
        }
    }
}
