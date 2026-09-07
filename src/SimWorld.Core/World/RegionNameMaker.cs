using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Names a newly partitioned <see cref="WorldRegion"/> (spec §5b.1). RimWorld has no region concept to
    /// name; this reuses the same content-driven bank mechanism <c>PawnBioAndNameGenerator</c> draws pawn and
    /// household names from — <see cref="NameBankDef"/> pools, gated on <see cref="NameSlot.RegionPrefix"/>/
    /// <see cref="NameSlot.RegionSuffix"/> — rather than a second, code-side prefix/suffix bank the way
    /// <c>Factions.FactionNameMaker</c> names factions.
    /// </summary>
    public static class RegionNameMaker
    {
        private const int MaxAttempts = 50;

        /// <summary>Draws "Prefixsuffix" (e.g. "Ashmere") unique among <paramref name="existingNames"/>; falls
        /// back to a numbered name if the banks are empty or every combination collides.</summary>
        public static string MakeRegionName(RandomStream rand, ICollection<string> existingNames)
        {
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (existingNames == null) throw new ArgumentNullException(nameof(existingNames));

            List<string> prefixes = Pool(NameSlot.RegionPrefix);
            List<string> suffixes = Pool(NameSlot.RegionSuffix);
            if (prefixes.Count == 0 || suffixes.Count == 0)
            {
                return UniqueFallback("Region", existingNames);
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string candidate = rand.Element(prefixes) + rand.Element(suffixes);
                if (!existingNames.Contains(candidate)) return candidate;
            }
            return UniqueFallback(rand.Element(prefixes) + rand.Element(suffixes), existingNames);
        }

        private static string UniqueFallback(string baseName, ICollection<string> existingNames)
        {
            string candidate = baseName;
            int n = 2;
            while (existingNames.Contains(candidate))
            {
                candidate = baseName + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                n++;
            }
            return candidate;
        }

        private static List<string> Pool(NameSlot slot) =>
            DefDatabase<NameBankDef>.AllDefsListForReading
                .Where(bank => bank.slot == slot)
                .SelectMany(bank => bank.names)
                .ToList();
    }
}
