using System;

using SimWorld.Factions;
using SimWorld.Sim;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Director
{
    /// <summary>
    /// One reading of <b>relative standing</b> — how a civilization's strength compares to the rival
    /// civilizations it shares a world with — at the tick <see cref="StandingReader"/> took it. Values only:
    /// nothing here is a handle, a <c>Def</c>, an offer or a command, and nothing in this file decides
    /// anything. It answers a question and stops.
    ///
    /// <para/><b>Parity is the origin, and the scale is logarithmic on purpose.</b> A rival twice our size and
    /// a rival half our size are the same distance from parity in opposite directions, and only a log ratio
    /// says that. A plain difference (<c>own - theirs</c>) grows without bound as a world's populations grow
    /// and would read a 100-vs-200 world as calmer than a 10,000-vs-10,100 one, which is backwards. So every
    /// gap here is <c>log2(own / theirs)</c>: <c>+1</c> is "twice their strength", <c>-1</c> is "half",
    /// <c>0</c> is parity.
    ///
    /// <para/><b>Two extremes, because "furthest from parity" is ambiguous in a world of several rivals.</b>
    /// <see cref="GapToWeakest"/> and <see cref="GapToStrongest"/> bracket every rival (the first is always
    /// the greater), and <see cref="Gap"/> is whichever of the two is further from parity — the headline
    /// reading. Carrying both is what lets a caller tell "ahead of everybody" from "behind everybody" rather
    /// than only "far from somebody".
    /// </summary>
    public readonly struct StandingReading
    {
        private readonly string? weakestRivalName;
        private readonly string? strongestRivalName;

        internal StandingReading(
            int ownStrength,
            int rivalCount,
            int weakestRivalStrength,
            int strongestRivalStrength,
            string? weakestRivalName,
            string? strongestRivalName)
        {
            OwnStrength = ownStrength;
            RivalCount = rivalCount;
            WeakestRivalStrength = weakestRivalStrength;
            StrongestRivalStrength = strongestRivalStrength;
            this.weakestRivalName = weakestRivalName;
            this.strongestRivalName = strongestRivalName;
        }

        /// <summary>A reading with nothing to compare — see <see cref="IsDefined"/>.</summary>
        public static StandingReading Undefined => default;

        /// <summary>The subject civilization's own strength, by <see cref="DiplomacyAI.StrengthOf"/>.</summary>
        public int OwnStrength { get; }

        /// <summary>How many rival civilizations this reading had to compare against: every civilization the
        /// diplomacy roster considers real (not hidden, not defeated) other than the subject, and holding at
        /// least one person. Zero means the world has settled into one civilization — see
        /// <see cref="IsDefined"/>.</summary>
        public int RivalCount { get; }

        public int WeakestRivalStrength { get; }

        public int StrongestRivalStrength { get; }

        /// <summary>Names, for a report to read back. Display text, never a handle: nothing can be looked up
        /// by these and they are not stable identifiers. Empty when there was no such rival — including on a
        /// <see cref="Undefined"/> reading, which is <c>default</c> and so has no backing string at all.</summary>
        public string WeakestRivalName => weakestRivalName ?? "";

        public string StrongestRivalName => strongestRivalName ?? "";

        /// <summary>
        /// Whether this reading means anything. False in exactly two cases, and both are real states of the
        /// world rather than errors: the subject has nobody left (<see cref="OwnStrength"/> is zero), or no
        /// rival civilization with anybody in it exists (<see cref="RivalCount"/> is zero). Relative standing
        /// is a comparison, and a comparison with one side missing is not a small gap — it is no reading at
        /// all, which is why every gap below is zero here rather than some large number a caller might read
        /// as parity's opposite.
        /// </summary>
        public bool IsDefined => OwnStrength > 0 && RivalCount > 0;

        /// <summary>How far above parity the subject stands against its <i>weakest</i> rival, in log2 strength
        /// ratio. Always the greater of the two gaps. Zero when <see cref="IsDefined"/> is false.</summary>
        public float GapToWeakest =>
            IsDefined ? StandingReader.Log2Ratio(OwnStrength, WeakestRivalStrength) : 0f;

        /// <summary>How far above (positive) or below (negative) parity the subject stands against its
        /// <i>strongest</i> rival. Always the lesser of the two gaps. Zero when <see cref="IsDefined"/> is
        /// false.</summary>
        public float GapToStrongest =>
            IsDefined ? StandingReader.Log2Ratio(OwnStrength, StrongestRivalStrength) : 0f;

        /// <summary>
        /// The headline: the distance from parity to whichever rival the subject is furthest from parity with,
        /// signed — positive means that rival is the weaker side, negative means the subject is. This is the
        /// reading "how far from parity are we, and in which direction" asks for.
        /// <para/>
        /// Ties go to <see cref="GapToWeakest"/>, which only matters in the one symmetric case (exactly as far
        /// above the weakest as below the strongest) and picks the same side every time so the series never
        /// flips on a coin.
        /// </summary>
        public float Gap
        {
            get
            {
                float up = GapToWeakest;
                float down = GapToStrongest;
                return Math.Abs(down) > Math.Abs(up) ? down : up;
            }
        }

        /// <summary>The headline gap's size, with the direction dropped.</summary>
        public float Distance => Math.Abs(Gap);

        /// <summary>Whether the subject is the stronger side of the headline comparison. Meaningless when
        /// <see cref="IsDefined"/> is false, where it reads true only because zero is not negative.</summary>
        public bool Ahead => Gap >= 0f;
    }

    /// <summary>
    /// <b>An instrument, not a mechanism.</b> Reads one tension — relative standing — off the live world and
    /// returns values. It offers nothing, commands nothing, schedules nothing and holds no state; calling it
    /// twice on the same tick returns the same answer and changes nothing. It exists to answer the question
    /// <c>docs/design/goal-renewal.md</c> §8 step 1 asks before anything is built on top: <i>does this tension
    /// actually move over a long run, or does it flatten the way §2 shows <see cref="MomentCurator"/>'s three
    /// rules must?</i>
    ///
    /// <para/><b>Why relative standing and not novelty.</b> §3's argument is that a renewal layer has to read
    /// something "always present to some degree and always changing" rather than something that fires once.
    /// Standing qualifies on the first half by construction — two civilizations that both exist always stand
    /// in some ratio to each other, and there is no "already happened" to exhaust. Whether it qualifies on the
    /// second half is an empirical question about this simulation, and the honest answer needs a measured
    /// series rather than this paragraph. <c>TensionSuite</c> in <c>tools/bench</c> is that measurement, and
    /// its findings — including where the reading has a real defect — are recorded in the commit that added
    /// this file.
    ///
    /// <para/><b>Strength is <see cref="DiplomacyAI.StrengthOf"/>, reused rather than reinvented.</b> That
    /// method's own doc is explicit that total population is a stand-in because no aggregate military ledger
    /// exists at civilization scale. <b>This reader inherits that limitation whole and does not fix it.</b> A
    /// civilization of many poor farmers reads as stronger than a smaller one with better weapons, because
    /// nothing in this codebase can yet tell the difference. A second, differently-derived strength number
    /// living here would be worse than the limitation: the world would then act on one reading of strength and
    /// report another. When a real ledger exists, <see cref="DiplomacyAI.StrengthOf"/> is the single call site
    /// that changes and this reader follows it for free.
    ///
    /// <para/><b>Which civilizations count as rivals.</b> Exactly the roster
    /// <see cref="FactionManager.GetFactions"/> hands <see cref="DiplomacyAI.Tick"/> with its own defaults —
    /// visible, undefeated — minus the subject itself, minus any with nobody left in them. A rival at zero
    /// strength is skipped for the same reason <see cref="DiplomacyAI"/> skips it: there is nothing there to
    /// be measurably stronger than, and a ratio against zero is not a large number, it is undefined.
    ///
    /// <para/><b>Determinism.</b> Nothing here rolls anything. This is a pure function of world state, so the
    /// same world at the same tick reads the same every time by construction — a stronger guarantee than a
    /// seeded stream, and the reason this file has no <see cref="RandomStream"/> in it. It also holds no state
    /// of its own, so there is nothing to Scribe: a reading is recomputed from state that is itself already
    /// saved.
    /// </summary>
    public static class StandingReader
    {
        /// <summary>
        /// Reads relative standing for the player's civilization out of the ambient game. Returns
        /// <see cref="StandingReading.Undefined"/> when there is no world or no player civilization yet, which
        /// is an ordinary state before a game exists rather than a failure.
        /// </summary>
        public static StandingReading ReadForPlayer()
        {
            CoreWorld? world = Find.World;
            if (world == null) return StandingReading.Undefined;

            FactionManager factions = Find.FactionManager;
            Faction? player = factions.OfPlayer;
            if (player == null) return StandingReading.Undefined;

            return ReadFor(player, factions, world);
        }

        /// <summary>
        /// Reads relative standing for <paramref name="subject"/> against every other civilization on the
        /// roster. See the class doc for which civilizations count and why the scale is logarithmic.
        /// </summary>
        public static StandingReading ReadFor(Faction subject, FactionManager factions, CoreWorld world)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (factions == null) throw new ArgumentNullException(nameof(factions));
            if (world == null) throw new ArgumentNullException(nameof(world));

            int ownStrength = DiplomacyAI.StrengthOf(subject, world);

            int rivalCount = 0;
            int weakest = 0;
            int strongest = 0;
            string? weakestName = null;
            string? strongestName = null;

            foreach (Faction other in factions.GetFactions())
            {
                if (ReferenceEquals(other, subject)) continue;

                int strength = DiplomacyAI.StrengthOf(other, world);
                if (strength <= 0) continue; // nothing there to stand in a ratio to — DiplomacyAI skips these too.

                rivalCount++;
                if (rivalCount == 1 || strength < weakest)
                {
                    weakest = strength;
                    weakestName = other.name;
                }

                if (strength > strongest)
                {
                    strongest = strength;
                    strongestName = other.name;
                }
            }

            return new StandingReading(ownStrength, rivalCount, weakest, strongest, weakestName, strongestName);
        }

        /// <summary>Distance from parity on a log2 scale: <c>+1</c> is twice, <c>-1</c> is half, <c>0</c> is
        /// equal. Callers guarantee both arguments are positive — <see cref="ReadFor"/> drops zero-strength
        /// rivals and <see cref="StandingReading.IsDefined"/> gates the subject's own side — so this never has
        /// to answer for a ratio that has no logarithm.</summary>
        internal static float Log2Ratio(int own, int other)
        {
            if (own <= 0 || other <= 0) return 0f;
            if (own == other) return 0f; // exact, rather than a float log's near-zero.
            return (float)((Math.Log(own) - Math.Log(other)) / Ln2);
        }

        private static readonly double Ln2 = Math.Log(2.0);
    }
}
