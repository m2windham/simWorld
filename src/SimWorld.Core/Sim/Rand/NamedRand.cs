using System;

using SimWorld.World;

namespace SimWorld.Sim
{
    /// <summary>
    /// A private random stream, named, derived from the world's own seed — the sanctioned way for a system to
    /// roll dice without disturbing anybody else's.
    ///
    /// <para/><b>Why this has to exist before the first ablatable defect does.</b> Measuring what a defect
    /// costs is a subtraction: run the simulation, switch the defect on, run it again, attribute the
    /// difference. That only works if switching it on changes nothing else. A defect drawing from the ambient
    /// <see cref="Rand.Current"/> fails that at its first roll: every subsequent draw in the whole game shifts
    /// by one, so the weather, the births, the raids and the diseases all take different values, and the
    /// measured delta is mostly the reshuffle rather than the defect. The result looks enormous and means
    /// nothing. Retrofitting this after the first defect is written is how a project ends up with a drawer
    /// full of numbers it cannot explain.
    ///
    /// <para/><b>Draw whether or not you act.</b> A stream keeps its own position, so a defect that consumes
    /// its roll only when enabled advances at a different rate than one that always consumes it — and the two
    /// runs diverge on that stream even though the defect was the only difference. Roll first, then decide
    /// what to do with the answer. The stream being private is what makes that affordable: nobody else is
    /// queued behind you, so a wasted draw costs nothing.
    ///
    /// <para/><b>The convention already existed; it was just unwritten.</b> <c>MapGen.GenStep</c>,
    /// <c>World.Gen.WorldGenStep</c>, <c>World.EmergenceManager</c> and <c>Sim.Game</c>'s founding roll all
    /// derive a stream this way, with three different separators between them ("_" before a defName,
    /// "_Emergence", "|founding"). Three spellings of one idea is how two systems eventually choose names that
    /// hash into the same stream and quietly share dice. This fixes the spelling in one place.
    /// </summary>
    public static class NamedRand
    {
        /// <summary>
        /// Separates the world seed from the stream's name. A vertical bar cannot appear in a defName and is
        /// not something a caller would put in a stream name, so "Fire" + "Storm" and "FireStorm" cannot
        /// collide into one stream however the names are spelled.
        /// </summary>
        public const char Separator = '|';

        /// <summary>
        /// A fresh stream for <paramref name="name"/>, seeded off the current world. <b>Does not touch
        /// <see cref="Rand.Current"/></b> — that is the whole point, and a test pins it.
        ///
        /// <para/>Fresh each call, so a caller that needs a stream to advance across ticks holds the one it
        /// was given rather than asking again. Asking again replays the sequence from the start: right for a
        /// one-shot decision, wrong for a recurring one.
        /// </summary>
        public static RandomStream For(string name) => For(WorldSeed(), name);

        /// <summary>The seed-explicit form, for a caller that has a seed but no world yet — world generation
        /// itself, or a test.</summary>
        public static RandomStream For(string seedString, string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return new RandomStream(GenText.StableStringHash((seedString ?? string.Empty) + Separator + name));
        }

        /// <summary>The world's seed, or empty before there is a world. Empty is a real answer rather than a
        /// failure: a stream derived before world generation is still stable for a given name, it simply is
        /// not tied to a particular planet.</summary>
        private static string WorldSeed() => Find.World?.info?.seedString ?? string.Empty;
    }
}
