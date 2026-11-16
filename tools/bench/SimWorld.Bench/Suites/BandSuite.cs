using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Who the host's default game founds with: the band of <c>--layout-seed</c> (the host's own seed by
    /// default), each citizen's names, backstories and traits, then the spread of first names, adulthoods and
    /// traits over the band and over <c>--runs</c> further seeds. Deterministic per seed.
    /// </summary>
    internal static class BandSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("Band: who the founders are (" + opt.LayoutSeed + ")");
            List<Pawn> band = Found(opt.LayoutSeed);
            foreach (Pawn p in band)
            {
                var name = (NameTriple)p.Name!;
                Console.WriteLine("  " + name.ToStringShort.PadRight(10) + " " + name.ToStringFull.PadRight(26) + " "
                    + (p.story.childhood?.defName ?? "-") + " / " + (p.story.adulthood?.defName ?? "-") + "; "
                    + string.Join(", ", p.story.traits.allTraits.Select(t => t.def.defName + (t.degree != 0 ? "(" + t.degree + ")" : ""))));
            }
            Console.WriteLine();
            Spread("this band", new[] { band });

            var bands = new List<List<Pawn>>();
            for (int i = 0; i < opt.Runs; i++) bands.Add(Found("band-" + i.ToString(CultureInfo.InvariantCulture)));
            Spread(opt.Runs + " more seeds", bands);
        }

        private static List<Pawn> Found(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            return game.World!.worldObjects.OfType<Settlement>().First().Citizens.ToList();
        }

        private static void Spread(string label, IReadOnlyList<List<Pawn>> bands)
        {
            int pawns = bands.Sum(b => b.Count);
            int dupBands = bands.Count(b => b.GroupBy(p => p.Name!.ToStringShort).Any(g => g.Count() > 1));
            double dupNames = bands.Average(b => b.Count - b.Select(p => p.Name!.ToStringShort).Distinct().Count());
            Console.WriteLine("  " + label + ": " + pawns + " pawns; bands with a repeated short name " + dupBands + "/" + bands.Count
                + ", repeats per band " + dupNames.ToString("0.00", CultureInfo.InvariantCulture));
            Console.WriteLine("    adulthood: " + Count(bands.SelectMany(b => b).Select(p => p.story.adulthood?.defName ?? "-"), pawns));
            Console.WriteLine("    childhood: " + Count(bands.SelectMany(b => b).Select(p => p.story.childhood?.defName ?? "-"), pawns));
            Console.WriteLine("    traits (share of pawns): " + Count(bands.SelectMany(b => b).SelectMany(p => p.story.traits.allTraits.Select(t => t.def.defName)), pawns));
            if (bands.Count == 1) Console.WriteLine("    short names: " + Count(bands[0].Select(p => p.Name!.ToStringShort), pawns));
        }

        private static string Count(IEnumerable<string> values, int of) => string.Join(", ", values.GroupBy(v => v)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " " + g.Count() + (of > 20 ? " (" + (100.0 * g.Count() / of).ToString("0", CultureInfo.InvariantCulture) + "%)" : "")));
    }
}
