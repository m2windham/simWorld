using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// Names a newly generated faction (RimWorld: <c>RimWorld.NameGenerator</c> reading a per-faction
    /// symbol-set XML). This port keeps it simple: a small prefix/suffix bank in code, keyed by
    /// <see cref="TechLevel"/>, retried until the result is unique among already-named factions.
    /// <see cref="FactionDef.settlementNamePrefixes"/> overrides the prefix bank when the def sets one.
    /// </summary>
    public static class FactionNameMaker
    {
        private const int MaxAttempts = 50;

        private static readonly Dictionary<TechLevel, string[]> PrefixBanks = new Dictionary<TechLevel, string[]>
        {
            [TechLevel.Neolithic] = new[] { "Ash", "Stone", "Red", "Wolf", "Bone", "River", "Thorn", "Storm" },
            [TechLevel.Medieval] = new[] { "Iron", "Black", "Grey", "Silver", "Crimson", "Raven", "Steel", "Wolf" },
            [TechLevel.Industrial] = new[] { "New", "North", "East", "West", "South", "Fort", "Port", "Grand" },
        };

        private static readonly string[] DefaultPrefixes = { "New", "Fort", "North", "South", "East", "West" };

        private static readonly Dictionary<TechLevel, string[]> SuffixBanks = new Dictionary<TechLevel, string[]>
        {
            [TechLevel.Neolithic] = new[] { "clan", "tribe", "kin", "band", "folk", "horde" },
            [TechLevel.Medieval] = new[] { "hold", "reach", "march", "watch", "vale", "keep" },
            [TechLevel.Industrial] = new[] { "haven", "union", "league", "combine", "federation", "state" },
        };

        private static readonly string[] DefaultSuffixes = { "nation", "people", "settlement", "domain" };

        /// <summary>
        /// Draws a "Prefix suffix" name unique among <paramref name="existingNames"/>; falls back to a
        /// numbered def label if the bank runs dry. Takes the name set directly (rather than reading a
        /// <see cref="FactionManager"/>) so uniqueness is scoped to whatever batch the caller is naming —
        /// typically just the factions being generated for one new world — and never depends on state left
        /// over from an unrelated earlier generation on the same <see cref="Sim.Find.FactionManager"/>,
        /// which would otherwise make two same-seed generations diverge.
        /// </summary>
        public static string MakeFactionName(FactionDef def, RandomStream rand, ICollection<string> existingNames)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (rand == null) throw new System.ArgumentNullException(nameof(rand));
            if (existingNames == null) throw new System.ArgumentNullException(nameof(existingNames));

            string[] prefixes = def.settlementNamePrefixes != null && def.settlementNamePrefixes.Count > 0
                ? def.settlementNamePrefixes.ToArray()
                : PrefixBanks.TryGetValue(def.techLevel, out string[]? p) ? p : DefaultPrefixes;
            string[] suffixes = SuffixBanks.TryGetValue(def.techLevel, out string[]? s) ? s : DefaultSuffixes;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string candidate = rand.Element(prefixes) + " " + rand.Element(suffixes);
                if (!existingNames.Contains(candidate)) return candidate;
            }

            string baseName = def.LabelCap;
            string fallback = baseName;
            int n = 2;
            while (existingNames.Contains(fallback))
            {
                fallback = baseName + " " + n;
                n++;
            }
            return fallback;
        }
    }
}
