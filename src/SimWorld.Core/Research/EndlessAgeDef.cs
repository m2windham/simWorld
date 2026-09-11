using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// The register the endless tail is named and shaped in: the vocabulary every generated age draws its
    /// theme word from, and how wide an age is.
    ///
    /// <para/><b>An age is not an <see cref="EraDef"/>, and deliberately so.</b> The era ladder is a finite
    /// authored artefact that ends at Exotic — <c>docs/spec/simworld-spec.md</c> §10 says generated projects
    /// carry no era and are invisible to <see cref="EraDef.Projects"/>, so that nothing minted at run time can
    /// change what an authored era's <see cref="EraDef.SpineProjects"/> is or hold
    /// <see cref="EraDef.IsComplete"/> open. An age keeps that promise: it is a grouping <i>inside</i> endless
    /// research, used for naming, for cost, and for the shape of the generated DAG, and it never becomes a Def
    /// the rest of the simulation can see. What it gives the player is what an era gives them — a named span
    /// of history their civilization lived through, announced when they reach it
    /// (<see cref="EndlessAgeUtility"/>) — without giving the ladder a ninth rung.
    ///
    /// <para/><b>Why the tail never repeats a name.</b> Every generated label is
    /// <c>"{theme} {substrate} {form}"</c>. The theme is a property of the age and nothing else, and
    /// <see cref="ThemeFor"/> is injective on the age index: the first <see cref="themes"/><c>.Count</c> ages
    /// take one theme each, and past that the theme compounds with <see cref="prefixes"/> the way real
    /// terminology does ("post-resonant", "meta-post-resonant"), which is a base-N numeral in words and so
    /// never runs out and never repeats. Two projects can therefore only collide if they are in the
    /// <i>same</i> age, and inside one age the substrate lists are disjoint between tracks, the form lists are
    /// disjoint between foundation and leaf, and the leaves of one track draw from a permutation. So distinct
    /// labels are a property of the construction rather than of a uniqueness check over everything already
    /// minted — which also means the <i>n</i>-th generated project can be named without generating the first
    /// <i>n</i>-1.
    /// </summary>
    public class EndlessAgeDef : Def
    {
        /// <summary>One word per age, in content order; <see cref="ThemeFor"/> permutes them per game so two
        /// civilizations do not walk the same named ages in the same order.</summary>
        public List<string> themes = new List<string>();

        /// <summary>Compounding particles for ages past the end of <see cref="themes"/> ("post", "meta",
        /// "trans"). This is what makes the register unbounded instead of merely long.</summary>
        public List<string> prefixes = new List<string>();

        /// <summary>Age label, with <c>{theme}</c> substituted.</summary>
        public string ageLabelFormat = "the {theme} age";

        /// <summary>Age description for the letter and the chronicle, with <c>{theme}</c> and <c>{age}</c>
        /// (the whole label) substituted.</summary>
        public string ageDescription = "";

        /// <summary>Description of a generated foundation, with <c>{age}</c>, <c>{track}</c> and
        /// <c>{subject}</c> (the project's own label) substituted.</summary>
        public string foundationDescription = "";

        /// <summary>Description of a generated applied project; same substitutions.</summary>
        public string appliedDescription = "";

        /// <summary>
        /// Projects minted per track per age: one foundation and the rest applied work hanging off it.
        /// <para/>
        /// This is what makes an endless age a shape rather than a queue. One-in-<c>projectsPerTrack</c> of an
        /// age is structural and the rest is optional, which is the same minority-spine shape the authored
        /// tree was restructured into for exactly the same reason — so two civilizations can leave an age
        /// having done visibly different things in it (<c>docs/research/tech-reachability.md</c> §11).
        /// Unsourced; the tests pin the ratio as a band.
        /// </summary>
        public int projectsPerTrack = 4;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (themes.Count == 0) yield return "themes is empty — an age needs a word of its own.";
            if (prefixes.Count == 0) yield return "prefixes is empty — without one the register runs out after " + themes.Count + " ages.";
            if (projectsPerTrack < 2) yield return "projectsPerTrack must be at least 2, or an age has no optional work in it at all.";
            if (!ageLabelFormat.Contains("{theme}")) yield return "ageLabelFormat must contain {theme}, or every age is called the same thing.";
        }

        /// <summary>
        /// The theme word for an age. Injective on <paramref name="ageIndex"/> for a given
        /// <paramref name="seed"/> — see the type remarks — and a pure function of the two, so the age a
        /// civilization is about to reach can be named without minting anything.
        /// </summary>
        public string ThemeFor(int ageIndex, int seed)
        {
            if (ageIndex < 0) throw new ArgumentOutOfRangeException(nameof(ageIndex), ageIndex, "Ages start at 0.");
            if (themes.Count == 0) return "unnamed";

            int cycle = ageIndex / themes.Count;
            int within = ageIndex % themes.Count;
            string theme = themes[EndlessSequence.Permute(within, themes.Count, MurmurHash.Combine(seed, cycle, ThemeSalt))];
            return cycle == 0 ? theme : PrefixChain(cycle) + theme;
        }

        /// <summary>The age's own label, e.g. "the resonant age".</summary>
        public string LabelFor(int ageIndex, int seed) => ageLabelFormat.Replace("{theme}", ThemeFor(ageIndex, seed));

        /// <summary>The age's description, or null when content gave it none.</summary>
        public string? DescriptionFor(int ageIndex, int seed)
        {
            if (string.IsNullOrEmpty(ageDescription)) return null;
            return ageDescription
                .Replace("{theme}", ThemeFor(ageIndex, seed))
                .Replace("{age}", LabelFor(ageIndex, seed))
                .Replace("{n}", (ageIndex + 1).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The compounding particles for cycle <paramref name="cycle"/> (≥ 1), rendered most significant
        /// first: cycle 1 is the first prefix, and once those run out they stack. This is the base-N numeral
        /// that makes the register unbounded; it is written in words because "post-resonant" is a discipline
        /// and "resonant IV" is a counter, which is the whole complaint this rewrite answers.
        /// </summary>
        private string PrefixChain(int cycle)
        {
            int radix = prefixes.Count;
            int value = cycle - 1;
            var digits = new List<int>();
            do
            {
                digits.Add(value % radix);
                value /= radix;
            }
            while (value > 0);

            var builder = new StringBuilder();
            for (int i = digits.Count - 1; i >= 0; i--)
            {
                builder.Append(prefixes[digits[i]]).Append('-');
            }
            return builder.ToString();
        }

        /// <summary>Salt so the theme permutation never shares a draw with anything else seeded from the same
        /// game seed.</summary>
        private const int ThemeSalt = 0x7A6E;
    }

    /// <summary>
    /// The one piece of arithmetic the endless tail is built on: a permutation of <c>[0, n)</c> that is a pure
    /// function of a seed.
    ///
    /// <para/>Deliberately not a draw from the ambient <see cref="Rand"/>. Everything the tail generates has to
    /// be identical for a given game however many other systems drew from the shared stream in between, and
    /// has to be re-derivable from a save that stores nothing but a seed and a depth. So this follows the
    /// pattern <see cref="RandomStream.RangeSeeded(int,int,int)"/> exists for — a value that is a function of
    /// (identity, index) and advances no stream — and adds the property a naming scheme needs on top of it:
    /// the result is a <i>bijection</i>, so no two indices ever land on the same word.
    /// </summary>
    internal static class EndlessSequence
    {
        /// <summary>
        /// Maps <paramref name="x"/> in <c>[0, n)</c> to another value in <c>[0, n)</c>, injectively for a
        /// fixed <paramref name="n"/> and <paramref name="seed"/>. An affine map <c>ax + b (mod n)</c> is a
        /// bijection exactly when <c>a</c> is coprime to <c>n</c>, so <c>a</c> is chosen by walking up from a
        /// seeded start until it is.
        /// </summary>
        internal static int Permute(int x, int n, int seed)
        {
            if (n <= 1) return 0;
            int a = CoprimeTo(n, seed);
            int b = RandomStream.RangeSeeded(0, n, MurmurHash.Combine(seed, OffsetSalt));
            return (int)((((long)a * x) + b) % n);
        }

        /// <summary>A value in <c>[1, n)</c> coprime to <paramref name="n"/>, chosen from a seeded start.
        /// 1 always qualifies, so the walk terminates.</summary>
        private static int CoprimeTo(int n, int seed)
        {
            int start = RandomStream.RangeSeeded(1, n, MurmurHash.Combine(seed, MultiplierSalt));
            for (int i = 0; i < n - 1; i++)
            {
                int candidate = 1 + (((start - 1) + i) % (n - 1));
                if (Gcd(candidate, n) == 1) return candidate;
            }
            return 1;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        private const int MultiplierSalt = 0x4D55;
        private const int OffsetSalt = 0x0FF5;
    }
}
