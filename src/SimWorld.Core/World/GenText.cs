using System;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Text helpers for world generation (RimWorld: parts of <c>Verse.GenText</c>). <see cref="StableStringHash"/>
    /// is the seed source for <see cref="WorldInfo"/> and every <c>WorldGenStep</c>: deliberately not
    /// <c>string.GetHashCode()</c>, which .NET randomizes per process and must never drive a save-stable seed.
    /// </summary>
    public static class GenText
    {
        /// <summary>Folds every character through <see cref="MurmurHash"/>; identical input gives identical output on any machine, any run.</summary>
        public static int StableStringHash(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            uint h = 0u;
            for (int i = 0; i < text.Length; i++)
            {
                h = unchecked((uint)MurmurHash.GetInt(h, text[i]));
            }
            return unchecked((int)h);
        }
    }
}
